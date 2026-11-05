// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Chippi.Core.Input;

namespace Chippi.Features.SmoothScrolling;

/// <summary>
/// The real windows, and the frame timer: one thread that sleeps until a notch arrives, then ticks at the display's rate on a
/// high-resolution timer until the glide has settled. It sends wheel messages marked as Chippi's.
/// </summary>
internal sealed unsafe class WindowsScrollEnvironment : IScrollEnvironment, IDisposable
{
    private readonly ConcurrentDictionary<uint, string?> _paths = new();
    private readonly ConcurrentDictionary<uint, bool> _reachable = new();
    private readonly bool _elevated = Elevated(GetCurrentProcess()) == true;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Thread _thread;
    private readonly nint _timer;
    private readonly int _frameMs;
    private Func<long, bool>? _tick;
    private volatile bool _stop;
    private volatile bool _awake;

    public WindowsScrollEnvironment()
    {
        int hz = GetDeviceCaps(GetDC(0), 116 /* VREFRESH */);
        _frameMs = hz is > 1 ? Math.Clamp(1000 / hz, 4, 16) : 16;
        _timer = CreateWaitableTimerExW(0, null, 2 /* HIGH_RESOLUTION */, 0x1F0003);
        _thread = new Thread(Run) { IsBackground = true, Name = "Smooth scrolling frames" };
        _thread.Start();
    }

    /// <summary>Set once, before the first notch.</summary>
    public void Start(Func<long, bool> tick) => _tick = tick;

    public void Wake()
    {
        if (_awake)
            return;
        _awake = true;
        _wake.Release();
    }

    private void Run()
    {
        while (!_stop)
        {
            _wake.Wait();
            while (!_stop && _tick is { } tick && tick(Stopwatch.GetTimestamp()))
                Frame();
            _awake = false;
            // A notch that arrived after the last tick found _awake still true: look once more before sleeping.
            if (!_stop && _tick is { } again && again(Stopwatch.GetTimestamp()))
                Wake();
        }
    }

    private void Frame()
    {
        if (_timer != 0)
        {
            long due = -_frameMs * 10_000L;
            if (SetWaitableTimer(_timer, ref due, 0, 0, 0, false))
            {
                WaitForSingleObject(_timer, 100);
                return;
            }
        }
        Thread.Sleep(_frameMs);
    }

    public nint WindowAt(int x, int y) => WindowFromPoint(new Point { X = x, Y = y });

    public nint WindowUnderCursor() => GetCursorPos(out var at) ? WindowFromPoint(at) : 0;

    public bool IsWindow(nint window) => IsWindowNative(window);

    public string? AppPath(nint window)
    {
        GetWindowThreadProcessId(window, out uint id);
        if (id == 0)
            return null;
        if (_paths.TryGetValue(id, out var known))
            return known;
        if (_paths.Count > 256)
            _paths.Clear();
        return _paths[id] = Read(id);
    }

    private static string? Read(uint id)
    {
        nint process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, id);
        if (process == 0)
            return null;
        try
        {
            char* name = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, name, ref size) ? new string(name, 0, (int)size) : null;
        }
        finally { CloseHandle(process); }
    }

    /// <summary>Cached by process. A process whose token can't be read counts as out of reach too (a system one).</summary>
    public bool CanSend(nint window)
    {
        GetWindowThreadProcessId(window, out uint id);
        if (id == 0)
            return false;
        if (_reachable.TryGetValue(id, out bool known))
            return known;
        if (_reachable.Count > 256)
            _reachable.Clear();
        nint process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, id);
        bool reachable = process != 0;
        if (reachable)
        {
            try { reachable = _elevated || Elevated(process) is false; }
            finally { CloseHandle(process); }
        }
        return _reachable[id] = reachable;
    }

    /// <summary>True or false from the token; null when it can't be read.</summary>
    private static bool? Elevated(nint process)
    {
        if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out nint token))
            return null;
        try
        {
            int elevated = 0;
            return GetTokenInformation(token, 20 /* TokenElevation */, &elevated, 4, out _) ? elevated != 0 : null;
        }
        finally { CloseHandle(token); }
    }

    public bool ModifierDown() =>
        (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x10) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    public void Send(bool horizontal, int delta)
    {
        var input = new Input
        {
            Type = 0, // INPUT_MOUSE
            Mouse = new MouseInput { Data = unchecked((uint)delta), Flags = horizontal ? 0x1000u : 0x0800u, ExtraInfo = ChippiInput.Marker },
        };
        SendInput(1, &input, sizeof(Input));
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Release();
        _thread.Join(500);
        if (_timer != 0)
            CloseHandle(_timer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint Data, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseInput Mouse;
    }

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point at);
    [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool IsWindowNative(nint window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input* inputs, int size);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc, int index);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint token, int kind, int* data, int size, out int needed);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimer(nint timer, ref long due, int period, nint routine, nint arg, bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint ms);
}
