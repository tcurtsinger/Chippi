// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Windows.Threading;
using Chippi.Features.RadialMenu.Pc;

namespace Chippi.Features.RadialMenu.Input;

/// <summary>What the wheel hears from the mouse and Esc, on the UI thread.</summary>
public interface IWheelInputSink
{
    /// <summary>A trigger was held 250ms without moving: open at <paramref name="at"/>, the button still held.</summary>
    void Opened(PixelPoint at, MouseButton button);

    void Moved(PixelPoint at);

    void Released(PixelPoint at);

    void Clicked(PixelPoint at);

    void Dismissed();
}

/// <summary>The mouse and Esc while the Radial menu may open. The real one hooks them; tests use a fake, so no test takes the owner's mouse.</summary>
public interface IWheelInput : IDisposable
{
    /// <summary>The buttons whose hold opens the wheel. With none, the hooks run only while the wheel shows.</summary>
    void Watch(bool middle, bool back, bool forward);

    /// <summary>While true (Cleaning mode), no press opens the wheel.</summary>
    Func<bool> Paused { set; }

    /// <summary>
    /// The wheel opened from its shortcut: its clicks and Esc go to <see cref="IWheelInputSink"/> until <see cref="Closed"/>.
    /// With <paramref name="virtualPointer"/> (a game hides the pointer), the pointer the wheel hears starts at
    /// <paramref name="at"/> and moves as the mouse does.
    /// </summary>
    void OpenedByShortcut(bool virtualPointer, PixelPoint at);

    /// <summary>The wheel opened from a hold at a hidden pointer, so it hears a pointer that starts at <paramref name="at"/>.</summary>
    void UseVirtualPointer(PixelPoint at);

    /// <summary>The wheel has closed.</summary>
    void Closed();
}

/// <summary>
/// Low-level mouse and keyboard hooks on a thread of their own that does nothing else, so they always answer in time
/// (Windows passes input on, and may drop a hook unannounced, when the thread that owns it is busy). Input Chippi sends is
/// passed on untouched, as is everything else unless <see cref="Gesture"/> keeps it.
/// </summary>
internal sealed class WheelHook(IWheelInputSink sink, Dispatcher ui) : IWheelInput
{
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Dispatcher _ui = ui;
    private bool _middle, _back, _forward, _shown;
    private Func<bool> _paused = () => false;
    private Thread? _thread;
    private uint _threadId;
    private Native.HookProc? _mouseProc, _keyProc;
    private nint _mouseHook, _keyHook;
    private Timer? _timer;
    private bool _virtual;
    private PixelPoint _pointer;
    private bool _movePosted;
    private Task _replays = Task.CompletedTask;

    // The gesture asks which buttons open the wheel through this, under the lock.
    private Gesture Gesture => _gestureWithWatch ??= new Gesture(Watched, Slop());
    private Gesture? _gestureWithWatch;

    public Func<bool> Paused { set => _paused = value; }

    private bool Watched(MouseButton button) => !_paused() && button switch
    {
        MouseButton.Middle => _middle,
        MouseButton.Back => _back,
        MouseButton.Forward => _forward,
        _ => false,
    };

    /// <summary>How far a press may wander and still be a hold: Windows' own drag distance (4 at 100%).</summary>
    private static int Slop() => Math.Max(Native.GetSystemMetrics(68), Native.GetSystemMetrics(69)); // SM_CXDRAG, SM_CYDRAG

    public void Watch(bool middle, bool back, bool forward)
    {
        lock (_gate)
        {
            _middle = middle;
            _back = back;
            _forward = forward;
        }
        Update();
    }

    public void OpenedByShortcut(bool virtualPointer, PixelPoint at)
    {
        lock (_gate)
        {
            _shown = true;
            Gesture.OpenedByShortcut();
            _virtual = virtualPointer;
            _pointer = at;
        }
        Update();
    }

    public void UseVirtualPointer(PixelPoint at)
    {
        lock (_gate)
        {
            _virtual = true;
            _pointer = at;
        }
    }

    public void Closed()
    {
        lock (_gate)
        {
            _shown = false;
            _virtual = false;
            Gesture.Closed();
        }
        Update();
    }

    // The hooks run while a button opens the wheel, while it shows, and until the last kept key or button is up.
    private bool Wanted => _middle || _back || _forward || _shown || !Gesture.IsIdle;

    private void Update()
    {
        bool wanted;
        lock (_gate)
            wanted = Wanted;
        if (wanted && _thread is null)
            Start();
        else if (!wanted && _thread is not null)
            Stop();
    }

    private void Start()
    {
        var ready = new ManualResetEventSlim();
        Exception? failed = null;
        _mouseProc = OnMouse;
        _keyProc = OnKey;
        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            nint module = Native.GetModuleHandle(null);
            _mouseHook = Native.SetWindowsHookEx(Native.HookMouse, _mouseProc, module, 0);
            _keyHook = _mouseHook == 0 ? 0 : Native.SetWindowsHookEx(Native.HookKeyboard, _keyProc, module, 0);
            if (_mouseHook == 0 || _keyHook == 0)
            {
                failed = new Win32Exception();
                if (_mouseHook != 0)
                    Native.UnhookWindowsHookEx(_mouseHook);
                ready.Set();
                return;
            }
            ready.Set();
            // Windows calls the hooks from inside GetMessage; the loop ends on WM_QUIT.
            while (Native.GetMessage(out _, 0, 0, 0) > 0)
            {
            }
            Native.UnhookWindowsHookEx(_keyHook);
            Native.UnhookWindowsHookEx(_mouseHook);
        })
        { IsBackground = true, Name = "Radial menu input", Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait();
        ready.Dispose();
        if (failed is not null)
        {
            _thread = null;
            throw failed;
        }
    }

    private void Stop()
    {
        if (_threadId != 0)
            Native.PostThreadMessage(_threadId, Native.WmQuit, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        _timer?.Dispose();
        _timer = null;
    }

    private unsafe nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return Native.CallNextHookEx(0, code, wParam, lParam);
        var info = (Native.MouseHookInfo*)lParam;
        // Chippi's own replays go straight on. Input other programs send (a remapped key, Remote Desktop) counts as a hand's.
        if ((info->Flags & Native.InjectedMouse) != 0 && info->ExtraInfo == Native.Ours)
            return Native.CallNextHookEx(0, code, wParam, lParam);
        uint message = (uint)wParam;
        GestureStep step;
        lock (_gate)
        {
            var at = new PixelPoint(info->At.X, info->At.Y);
            if (_virtual)
            {
                // A game that hides the pointer may hold it in place: follow the mouse's own movement instead.
                Native.GetCursorPos(out var was);
                _pointer = new PixelPoint(_pointer.X + at.X - was.X, _pointer.Y + at.Y - was.Y);
                at = _pointer;
            }
            step = message switch
            {
                Native.WmMouseMove => Gesture.Move(at),
                Native.WmLButtonDown => Gesture.Down(MouseButton.Left, at),
                Native.WmLButtonUp => Gesture.Up(MouseButton.Left, at),
                Native.WmRButtonDown => Gesture.Down(MouseButton.Right, at),
                Native.WmRButtonUp => Gesture.Up(MouseButton.Right, at),
                Native.WmMButtonDown => Gesture.Down(MouseButton.Middle, at),
                Native.WmMButtonUp => Gesture.Up(MouseButton.Middle, at),
                Native.WmXButtonDown => Gesture.Down(XButton(info->MouseData), at),
                Native.WmXButtonUp => Gesture.Up(XButton(info->MouseData), at),
                _ => new GestureStep(false),
            };
            if (step.Effect == GestureEffect.Move)
            {
                // Moves come up to a thousand a second: the UI hears the latest, once per post.
                _pointer = step.At;
                if (_movePosted)
                    step = step with { Effect = GestureEffect.None };
                else
                    _movePosted = true;
            }
            else if (step.Keep && !Wanted)
                _ui.BeginInvoke(Update); // the last kept up after the wheel opened from its shortcut: the hooks can go
        }
        Act(step);
        return step.Keep ? 1 : Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private static MouseButton XButton(uint data) => (data >> 16) == 1 ? MouseButton.Back : MouseButton.Forward;

    private unsafe nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return Native.CallNextHookEx(0, code, wParam, lParam);
        var info = (Native.KeyHookInfo*)lParam;
        if ((info->Flags & Native.InjectedKey) != 0 && info->ExtraInfo == Native.Ours)
            return Native.CallNextHookEx(0, code, wParam, lParam);
        uint message = (uint)wParam;
        GestureStep step;
        lock (_gate)
        {
            step = Gesture.Key(info->Vk, message is Native.WmKeyDown or Native.WmSysKeyDown);
            if (step.Keep && step.Effect == GestureEffect.None && !Wanted)
                _ui.BeginInvoke(Update);
        }
        Act(step);
        return step.Keep ? 1 : Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private void Act(GestureStep step)
    {
        switch (step.Effect)
        {
            case GestureEffect.StartHold:
                int press;
                lock (_gate)
                    press = Gesture.Press;
                _timer?.Dispose();
                _timer = new Timer(_ => Elapsed(press), null, Hold, Timeout.InfiniteTimeSpan);
                break;
            case GestureEffect.ReplayDown or GestureEffect.ReplayUp or GestureEffect.ReplayClick:
                // Sent from a worker, one after another: a drag's up goes out only after its replayed down. (Input sent
                // from inside a hook comes back through it only once the hook returns anyway.)
                var replay = step;
                lock (_gate)
                    _replays = _replays.ContinueWith(_ => Replay(replay.Button, replay.Effect), TaskScheduler.Default);
                break;
            case GestureEffect.Open:
                _ui.BeginInvoke(() => sink.Opened(step.At, step.Button));
                break;
            case GestureEffect.Move:
                _ui.BeginInvoke(() =>
                {
                    PixelPoint latest;
                    lock (_gate)
                    {
                        latest = _pointer;
                        _movePosted = false;
                    }
                    sink.Moved(latest);
                });
                break;
            case GestureEffect.Release:
                _ui.BeginInvoke(() => sink.Released(step.At));
                break;
            case GestureEffect.Click:
                _ui.BeginInvoke(() => sink.Clicked(step.At));
                break;
            case GestureEffect.Dismiss:
                _ui.BeginInvoke(sink.Dismissed);
                break;
        }
    }

    private void Elapsed(int press)
    {
        GestureStep step;
        lock (_gate)
        {
            step = Gesture.Elapsed(press);
            if (step.Effect == GestureEffect.Open)
                _shown = true;
        }
        Act(step);
    }

    private static void Replay(MouseButton button, GestureEffect effect)
    {
        (uint down, uint up, uint data) = button switch
        {
            MouseButton.Middle => (Native.MouseMiddleDown, Native.MouseMiddleUp, 0u),
            MouseButton.Back => (Native.MouseXDown, Native.MouseXUp, 1u),
            _ => (Native.MouseXDown, Native.MouseXUp, 2u),
        };
        if (effect == GestureEffect.ReplayClick)
            Native.Send([Native.Button(down, data), Native.Button(up, data)]);
        else
            Native.Send([Native.Button(effect == GestureEffect.ReplayDown ? down : up, data)]);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _middle = _back = _forward = _shown = false;
        }
        Stop();
    }
}
