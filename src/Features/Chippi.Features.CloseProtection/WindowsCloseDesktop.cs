// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Chippi.Clipboard.Windows;
using Microsoft.Win32;

namespace Chippi.Features.CloseProtection;

// A separate message pump owns WinEvents and process/token queries. The keyboard hook only reads
// its immutable cache and verifies the current HWND/PID, so stale metadata never targets a new app.
public sealed class WindowsCloseDesktop : ICloseDesktop
{
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly WindowsForeground _foreground = new();
    private readonly WinEvent _callback;
    private Thread? _thread;
    private Dispatcher? _events;
    private CloseTarget? _current;
    private bool _disposed;
    public event Action? ForegroundChanged;
    public event Action? DesktopChanged;

    public WindowsCloseDesktop() => _callback = OnEvent;

    public CloseTarget? Current
    {
        get
        {
            var cached = Volatile.Read(ref _current);
            return cached is not null && cached.Window == GetForegroundWindow()
                && GetWindowThreadProcessId(cached.Window, out uint pid) != 0 && pid == cached.ProcessId ? cached : null;
        }
    }

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            nint focus = 0, destroyed = 0, desktop = 0;
            try
            {
                _events = Dispatcher.CurrentDispatcher;
                focus = SetWinEventHook(3, 3, 0, _callback, 0, 0, 0);
                destroyed = SetWinEventHook(0x8001, 0x8001, 0, _callback, 0, 0, 0);
                desktop = SetWinEventHook(0x20, 0x20, 0, _callback, 0, 0, 0);
                if (focus == 0 || destroyed == 0 || desktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                Refresh();
                SystemEvents.SessionSwitch += SessionSwitch;
            }
            catch (Exception ex) { error = ex; }
            finally { ready.Set(); }
            try { if (error is null) Dispatcher.Run(); }
            finally
            {
                SystemEvents.SessionSwitch -= SessionSwitch;
                foreach (nint hook in new[] { focus, destroyed, desktop })
                    if (hook != 0) UnhookWinEvent(hook);
            }
        }) { IsBackground = true, Name = "Close protection foreground" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        if (error is not null) throw error;
    }

    private void OnEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed) return;
        if (kind == 0x8001 && (objectId != 0 || childId != 0 || Volatile.Read(ref _current)?.Window != window)) return;
        Volatile.Write(ref _current, null);
        if (kind == 0x20) DesktopChanged?.Invoke();
        else ForegroundChanged?.Invoke();
        Refresh();
    }

    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Volatile.Write(ref _current, null);
        DesktopChanged?.Invoke();
        _events?.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        nint hwnd = GetForegroundWindow();
        var target = WindowsForeground.Of(hwnd);
        if (target is null) return;
        var cls = new StringBuilder(128);
        GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW") return;
        try
        {
            using var process = Process.GetProcessById((int)target.ProcessId);
            string? path = process.MainModule?.FileName;
            if (path is null) return;
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool excluded = file.Equals("mstsc", StringComparison.OrdinalIgnoreCase)
                || file.Equals("vmconnect", StringComparison.OrdinalIgnoreCase)
                || file.Equals("VirtualBoxVM", StringComparison.OrdinalIgnoreCase)
                || file.Equals("vmware", StringComparison.OrdinalIgnoreCase);
            var snapshot = new CloseTarget(hwnd, target.ProcessId, process.StartTime.ToUniversalTime().Ticks,
                path, FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } name ? name : file,
                !excluded && _foreground.CanSendInput(target));
            if (!_disposed && GetForegroundWindow() == hwnd) Volatile.Write(ref _current, snapshot);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or System.IO.IOException)
        { /* Inaccessible or exited apps retain their normal keys. */ }
    }

    public Task<IReadOnlyList<CloseApp>> OpenAppsAsync() => Task.Run<IReadOnlyList<CloseApp>>(() =>
    {
        var apps = new Dictionary<string, CloseApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == 0 || process.Id == Environment.ProcessId) continue;
                    if (process.MainModule?.FileName is not { } path) continue;
                    string name = FileVersionInfo.GetVersionInfo(path).FileDescription
                        ?? System.IO.Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(path);
                    apps.TryAdd(path, new CloseApp(path, name));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or System.IO.IOException) { }
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    });

    public CloseModifiers Modifiers()
    {
        var modifiers = CloseModifiers.None;
        if (Down(0x12)) modifiers |= CloseModifiers.Alt;
        if (Down(0x11)) modifiers |= CloseModifiers.Control;
        if (Down(0x10)) modifiers |= CloseModifiers.Shift;
        if (Down(0x5B) || Down(0x5C)) modifiers |= CloseModifiers.Windows;
        return modifiers;
    }
    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    public void Post(Action work) { if (!_ui.HasShutdownStarted) _ui.BeginInvoke(work); }
    public void Beep() => MessageBeep(0);
    public void Dispose()
    {
        _disposed = true;
        Volatile.Write(ref _current, null);
        _events?.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private delegate void WinEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
}
