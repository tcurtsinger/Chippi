// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Chippi.Core.Input;
using Microsoft.Extensions.Logging;

namespace Chippi.Input;

/// <summary>
/// The real hub: the hooks, a message-only window for its timer and the session's lock, and the desktop-switch event, all
/// on one thread that does nothing else, so the hooks always answer in time (Windows passes input on, and may drop a hook
/// unannounced, when the thread that owns it is busy). Calls from other threads wait their turn on it. Owned by the shell
/// and disposed at exit, after the modules stop.
/// </summary>
public sealed class InputHub : IInputHub, IDisposable
{
    private const uint WmApp = 0x8000, WmRun = WmApp + 1, WmUpdate = WmApp + 2, WmTimer = 0x0113, WmSessionChange = 0x02B1;
    private const uint WmKeyDown = 0x0100, WmSysKeyDown = 0x0104;
    private const nint SessionLock = 7;
    private const uint DesktopSwitch = 0x0020;

    private readonly ILogger? _log;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Lock _gate = new();
    private readonly HookTimings? _timings;
    private HubCore? _core;
    private Thread? _thread;
    private int _threadId;
    private nint _window, _keyHook, _mouseHook, _desktopHook;
    private bool _disposed, _faulted;

    // Kept alive while Windows holds them.
    private readonly Native.HookProc _keyProc, _mouseProc;
    private readonly Native.WndProc _windowProc;
    private readonly Native.WinEventProc _desktopProc;

    public InputHub(ILogger? log = null)
    {
        _log = log;
        _keyProc = OnKey;
        _mouseProc = OnMouse;
        _windowProc = OnMessage;
        _desktopProc = (_, _, _, _, _, _, _) => _core?.SessionChanged();
        if (Environment.GetEnvironmentVariable("CHIPPI_INPUT_TIMINGS") is { Length: > 0 } report)
            _timings = new HookTimings(report);
    }

    public IInputRegistration Add(IInputFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var entry = Invoke(core => core.Add(filter));
        return new Registration(this, entry);
    }

    public bool Paused
    {
        get => Invoke(core => core.Paused);
        set => Invoke(core => core.Paused = value);
    }

    /// <summary>The hooks in now (the Sandbox checks read it).</summary>
    public InputDevices Installed => Invoke(core => core.Installed);

    /// <summary>Times the watchdog put a deaf hook back.</summary>
    public int Reinstalls => Invoke(core => core.Reinstalls);

#if DEBUG
    /// <summary>Takes a hook off behind the hub's back, as Windows does to a slow one (the watchdog's Sandbox check).</summary>
    public void DropHook(InputDevices device) => Invoke(_ =>
    {
        ref nint hook = ref device == InputDevices.Keyboard ? ref _keyHook : ref _mouseHook;
        Native.UnhookWindowsHookEx(hook);
        return 0;
    });
#endif

    private sealed class Registration(InputHub hub, HubCore.Entry entry) : IInputRegistration
    {
        public void Refresh() => hub.Invoke(core => core.Refresh(entry));

        public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers)
        {
            ushort[] copy = modifiers.ToArray();
            hub.Invoke(core => core.HoldModifiers(entry, heldBy, copy));
        }

        public void Mask() => hub.Invoke(core => core.Mask());

        public void Dispose()
        {
            if (!hub._disposed)
                hub.Invoke(core => core.Remove(entry));
        }
    }

    private void Invoke(Action<HubCore> action) => Invoke(core =>
    {
        action(core);
        return 0;
    });

    private T Invoke<T>(Func<HubCore, T> call)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is null)
                Start();
        }
        if (Environment.CurrentManagedThreadId == _threadId)
            return call(_core!);
        T result = default!;
        Exception? failed = null;
        using var done = new ManualResetEventSlim();
        _work.Enqueue(() =>
        {
            try { result = call(_core!); }
            catch (Exception ex) { failed = ex; }
            finally { done.Set(); }
        });
        Native.PostMessage(_window, WmRun, 0, 0);
        done.Wait();
        if (failed is not null)
            throw failed;
        return result;
    }

    private void Start()
    {
        var ready = new ManualResetEventSlim();
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                _core = new HubCore(new Hooks(this), new Sender(), new WindowsSystem(), _log);
                _core.RequestUpdate = () => Native.PostMessage(_window, WmUpdate, 0, 0);
                _window = Native.CreateMessageWindow(_windowProc);
                Native.SetTimer(_window, 1, 1000, 0);
                Native.WTSRegisterSessionNotification(_window, 0);
                _desktopHook = Native.SetWinEventHook(DesktopSwitch, DesktopSwitch, 0, _desktopProc, 0, 0, 0);
                _threadId = Environment.CurrentManagedThreadId;
            }
            catch (Exception ex) { failed = ex; }
            ready.Set();
            if (failed is not null)
                return;
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
                Native.DispatchMessage(in message);
        })
        { IsBackground = true, Name = "Input hub", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait();
        ready.Dispose();
        if (failed is not null)
            throw failed;
        _thread = thread;
    }

    private nint OnMessage(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmRun:
                while (_work.TryDequeue(out var work))
                    work();
                return 0;
            case WmUpdate:
                Guard(() => _core!.UpdateHooks());
                return 0;
            case WmTimer:
                Guard(() => _core!.Tick());
                _timings?.Tick();
                return 0;
            case WmSessionChange when wParam == SessionLock:
                Guard(() => _core!.SessionChanged());
                return 0;
        }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log?.LogWarning(ex, "Input hub failed"); }
    }

    private unsafe nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _core is { } core)
        {
            long start = Stopwatch.GetTimestamp();
            var info = (Native.KeyHookInfo*)lParam;
            bool keep = false;
            try { keep = core.Key(info->Vk, info->Scan, info->Flags, info->ExtraInfo, (uint)wParam is WmKeyDown or WmSysKeyDown); }
            catch (Exception ex) { Faulted(ex); }
            _timings?.Add(Stopwatch.GetTimestamp() - start, keyboard: true);
            if (keep)
                return 1;
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private unsafe nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _core is { } core)
        {
            long start = Stopwatch.GetTimestamp();
            var info = (Native.MouseHookInfo*)lParam;
            bool keep = false;
            try { keep = core.Mouse((uint)wParam, info->X, info->Y, info->MouseData, info->Flags, info->ExtraInfo); }
            catch (Exception ex) { Faulted(ex); }
            _timings?.Add(Stopwatch.GetTimestamp() - start, keyboard: false);
            if (keep)
                return 1;
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    // A filter that throws passes that event on; the log hears about it once, not on every mouse move.
    private void Faulted(Exception ex)
    {
        if (_faulted)
            return;
        _faulted = true;
        _log?.LogError(ex, "Input hub: a filter failed; its event was passed on");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_thread is null)
            {
                _disposed = true;
                return;
            }
        }
        Invoke(core =>
        {
            core.Stop();
            if (_desktopHook != 0)
                Native.UnhookWinEvent(_desktopHook);
            Native.WTSUnRegisterSessionNotification(_window);
            Native.KillTimer(_window, 1);
            Native.DestroyMessageWindow(_window);
            Native.PostQuitMessage(0);
        });
        lock (_gate)
            _disposed = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _timings?.Write();
    }

    private sealed class Hooks(InputHub hub) : IHookSource
    {
        public void Install(InputDevices device)
        {
            bool keyboard = device == InputDevices.Keyboard;
            nint hook = Native.SetWindowsHookEx(keyboard ? Native.HookKeyboard : Native.HookMouse, keyboard ? hub._keyProc : hub._mouseProc,
                Native.GetModuleHandle(null), 0);
            if (hook == 0)
                throw new Win32Exception();
            // The new hook goes in before the old comes off, so nothing slips past between them.
            ref nint slot = ref keyboard ? ref hub._keyHook : ref hub._mouseHook;
            if (slot != 0)
                Native.UnhookWindowsHookEx(slot);
            slot = hook;
        }

        public void Remove(InputDevices device)
        {
            ref nint slot = ref device == InputDevices.Keyboard ? ref hub._keyHook : ref hub._mouseHook;
            if (slot != 0)
                Native.UnhookWindowsHookEx(slot);
            slot = 0;
        }
    }

    private sealed class Sender : IInputSender
    {
        public void Send(ReadOnlySpan<SentKey> keys) => Native.SendKeys(keys);
    }

    private sealed class WindowsSystem : IInputSystem
    {
        public long Now => Stopwatch.GetTimestamp();

        public long LastInput
        {
            get
            {
                var info = new Native.LastInputInfo { Size = (uint)Marshal.SizeOf<Native.LastInputInfo>() };
                if (!Native.GetLastInputInfo(ref info))
                    return 0;
                long now = Stopwatch.GetTimestamp();
                uint ago = unchecked((uint)Environment.TickCount - info.Time);
                return now - ago * Stopwatch.Frequency / 1000;
            }
        }

        public (int X, int Y) Cursor => Native.GetCursorPos(out var at) ? (at.X, at.Y) : default;

        public bool IsDown(uint vk) => (Native.GetAsyncKeyState((int)vk) & 0x8000) != 0;
    }
}
