// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Input;
using Chippi.Features.RadialMenu.Pc;
using HubButton = Chippi.Core.Input.MouseButton;

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
/// The Radial menu's filter in the input hub, among the triggers (architecture §2): the hub's hooks, on their own thread,
/// bring it every mouse event and key while a button opens the wheel or it shows. Everything passes on unless
/// <see cref="Gesture"/> keeps it; the hub keeps a kept press's up for it.
/// </summary>
internal sealed class WheelHook : IWheelInput, IInputFilter
{
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly IWheelInputSink _sink;
    private readonly Dispatcher _ui;
    private readonly IInputRegistration _registration;
    private bool _middle, _back, _forward, _shown;
    private Func<bool> _paused = () => false;
    private Timer? _timer;
    private bool _virtual;
    private PixelPoint _pointer;
    private bool _movePosted;
    private Task _replays = Task.CompletedTask;

    public WheelHook(IWheelInputSink sink, IInputHub hub, Dispatcher ui)
    {
        _sink = sink;
        _ui = ui;
        _registration = hub.Add(this);
    }

    // The gesture asks which buttons open the wheel through this, under the lock.
    private Gesture Gesture => _gestureWithWatch ??= new Gesture(Watched, Slop());
    private Gesture? _gestureWithWatch;

    public Func<bool> Paused { set => _paused = value; }

    public InputOrder Order => InputOrder.Triggers;

    // The Radial menu has its own switch for fullscreen apps (wave 1, PR 3); the hub's pause never stops it.
    public bool Pauses => false;

    // The hooks run while a button opens the wheel and while it shows; the hub keeps them until a kept press's up.
    public InputDevices Devices
    {
        get
        {
            lock (_gate)
                return _middle || _back || _forward || _shown ? InputDevices.Both : InputDevices.None;
        }
    }

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
        _registration.Refresh();
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
        _registration.Refresh();
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
        _registration.Refresh();
    }

    public bool Mouse(in MouseInput mouse)
    {
        GestureStep step;
        lock (_gate)
        {
            var at = new PixelPoint(mouse.X, mouse.Y);
            if (_virtual)
            {
                // A game that hides the pointer may hold it in place: follow the mouse's own movement instead.
                Native.GetCursorPos(out var was);
                _pointer = new PixelPoint(_pointer.X + at.X - was.X, _pointer.Y + at.Y - was.Y);
                at = _pointer;
            }
            step = (mouse.Kind, Button(mouse.Button)) switch
            {
                (MouseKind.Move, _) => Gesture.Move(at),
                (MouseKind.Down, { } button) => Gesture.Down(button, at),
                (MouseKind.Up, { } button) => Gesture.Up(button, at),
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
        }
        Act(step);
        return step.Keep;
    }

    private static MouseButton? Button(HubButton button) => button switch
    {
        HubButton.Left => MouseButton.Left,
        HubButton.Right => MouseButton.Right,
        HubButton.Middle => MouseButton.Middle,
        HubButton.Back => MouseButton.Back,
        HubButton.Forward => MouseButton.Forward,
        _ => null,
    };

    public bool Key(in KeyInput key)
    {
        GestureStep step;
        lock (_gate)
            step = Gesture.Key(key.Vk, key.Down);
        Act(step);
        return step.Keep;
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
                _ui.BeginInvoke(() => _sink.Opened(step.At, step.Button));
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
                    _sink.Moved(latest);
                });
                break;
            case GestureEffect.Release:
                _ui.BeginInvoke(() => _sink.Released(step.At));
                break;
            case GestureEffect.Click:
                _ui.BeginInvoke(() => _sink.Clicked(step.At));
                break;
            case GestureEffect.Dismiss:
                _ui.BeginInvoke(_sink.Dismissed);
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
            _middle = _back = _forward = _shown = false;
        _registration.Dispose();
        _timer?.Dispose();
        _timer = null;
    }
}
