// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using Chippi.Core.Actions;
using Chippi.Core.Config;
using Chippi.Core.Input;
using Chippi.Features.WindowLayouts;
using Chippi.UI.Actions;

namespace Chippi.App.Shell;

/// <summary>Adapts the shared PC, switcher list and display catalog. Never launches or moves outside IActionPc.</summary>
internal sealed class ArrangementDesktop(IActionPc pc, Func<PauseRules> rules, Func<bool> locked) : IArrangementDesktop
{
    private readonly WindowsWindowList _windows = new();
    private IReadOnlyList<RegisteredAppTargets.Target> _targets = [];
    private ActionWindow? _original;

    public Task PrepareAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<RegisteredAppTargets.Target>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(RegisteredAppTargets.ReadAll()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Arrangements app targets" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ApplyAsync();
        async Task ApplyAsync() { _targets = await completion.Task.ConfigureAwait(false); }
    }

    public void BeginInvocation(nint original) => _original = pc.CaptureWindow(original);
    public IReadOnlyList<SnapDisplay> Displays() => SnapDisplaySource.Connected();

    public IReadOnlyList<ArrangementLiveWindow> Windows()
    {
        var displays = Displays();
        var found = new List<ArrangementLiveWindow>();
        foreach (var listed in _windows.Read(includeOtherDesktops: true))
        {
            try
            {
                if (pc.CaptureWindow(listed.Handle) is not { } window || window.ProcessId != listed.ProcessId) continue;
                var display = displays.FirstOrDefault(d => d.Handle == listed.Monitor);
                if (display is null) continue;
                bool eligible = window.ClassName != "#32770";
                var placement = !window.Elevated && eligible ? pc.ReadPlacement(window) : null;
                var bounds = placement is { } p ? new ArrangementBounds(p.Left, p.Top, p.Width, p.Height) : new(0, 0, 1, 1);
                string? identity = RegisteredAppTargets.ProcessIdentity(listed.AppProcessId == 0 ? listed.ProcessId : listed.AppProcessId);
                var target = RegisteredTarget(identity, _targets);
                string appIdentity = target?.Identity ?? identity ?? listed.Executable;
                string launch = target?.Identity ?? (identity is not null ? "" : listed.Executable);
                bool minimizedFullscreen = listed.Minimized && placement is { Maximized: false }
                    && (GetWindowLong(listed.Handle, -16) & 0x00c00000) != 0x00c00000
                    && bounds.Left <= display.Bounds.X && bounds.Top <= display.Bounds.Y
                    && (long)bounds.Left + bounds.Width >= (long)display.Bounds.X + display.Bounds.Width
                    && (long)bounds.Top + bounds.Height >= (long)display.Bounds.Y + display.Bounds.Height;
                found.Add(new(new(window, appIdentity, bounds, listed.CurrentDesktop, eligible), launch, display.Id,
                    display.Name, (int)GetDpiForWindow(listed.Handle), placement,
                    minimizedFullscreen || FullscreenCheck.CoversMonitor(listed.Handle, listed.Monitor)));
            }
            catch (ActionFailure) { /* A closing or inaccessible window is never captured or moved. */ }
        }
        return found;
    }

    internal static RegisteredAppTargets.Target? RegisteredTarget(string? identity, IReadOnlyList<RegisteredAppTargets.Target> targets) =>
        string.IsNullOrEmpty(identity) ? null : targets.FirstOrDefault(t =>
            string.Equals(t.Identity, identity, StringComparison.OrdinalIgnoreCase));

    public string? StopReason(IReadOnlySet<ArrangementProcess> launched)
    {
        if (locked()) return "Session locked";
        nint front = pc.WindowInFront();
        var foreground = front == 0 ? _original : pc.CaptureWindow(front);
        if (foreground is null) return null;
        bool own = launched.Contains(new(foreground.ProcessId, foreground.ProcessStarted));
        var policy = rules();
        bool fullscreen = FullscreenCheck.IsFullscreen(foreground.Handle);
        if (!policy.Pauses(fullscreen, foreground.Executable)) return null;
        // Said as the run page and the bar say it: a fullscreen app in front, or one of Pause in these apps.
        return own && FullscreenCheck.CoversMonitor(foreground.Handle, MonitorFromWindow(foreground.Handle, 2))
            ? null : fullscreen ? "Fullscreen app" : "Paused app";
    }

    public void Beep() => MessageBeep(0);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);

    public ArrangementProcess? Launch(ArrangementRow row)
    {
        var process = Path.IsPathFullyQualified(row.Target)
            ? pc.LaunchExecutable(new(row.Target, row.Arguments, row.WorkingFolder))
            : pc.LaunchRegisteredApp(row.Target);
        return process is { } identity ? new(identity.ProcessId, identity.Started) : null;
    }

    public void Place(ActionWindow target, SavedWindowPlacement placement, nint monitor) => pc.PlaceSaved(target, placement, monitor);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
}
