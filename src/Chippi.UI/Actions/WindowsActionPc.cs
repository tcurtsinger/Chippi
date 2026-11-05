// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Chippi.Core.Actions;
using Native = Chippi.UI.Actions.ActionNative;

namespace Chippi.UI.Actions;

/// <summary>
/// The real PC side of every feature's actions, which the shell hands out as <see cref="Core.Modules.ModuleContext.Actions"/>.
/// Nothing in it runs in tests or dev runs (docs/phase-5.5-handoff.md §3): tests pass a fake.
/// </summary>
public sealed partial class WindowsActionPc : IActionPc
{
    // The terminals where Ctrl+C stops what runs: Windows Terminal, the console host, ConEmu, mintty and PuTTY.
    private static readonly HashSet<string> Terminals = new(StringComparer.Ordinal)
        { "CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass", "VirtualConsoleClass", "mintty", "PuTTY" };

    private static readonly TimeSpan HeldKeysTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Asks the shell for the app each time: about 1 to 10ms, 50 the first time, so read a list of them off the UI thread.</summary>
    public bool? HasApp(string parsingName) => Native.Parses(@"shell:AppsFolder\" + parsingName);

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public nint WindowUnderPointer() => Native.GetCursorPos(out var at) ? WindowAt(at.X, at.Y) : 0;

    /// <summary>The top-level window under the point (physical pixels), or 0 for the desktop, the taskbar or one of Chippi's.</summary>
    public static nint WindowAt(int x, int y) => AppWindow(Native.GetAncestor(Native.WindowFromPoint(new Native.Point { X = x, Y = y }), 2)); // GA_ROOT

    public nint WindowInFront() => AppWindow(Native.GetForegroundWindow());

    /// <summary>The window, or 0 for the desktop, the taskbar or one of Chippi's.</summary>
    private static nint AppWindow(nint window)
    {
        if (window == 0 || window == Native.GetShellWindow())
            return 0;
        if (Native.ClassOf(window) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return 0;
        Native.GetWindowThreadProcessId(window, out uint pid);
        return pid == (uint)Environment.ProcessId ? 0 : window;
    }

    public void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new ActionFailure("Not found"); }
    }

    public void Window(nint window, WindowAction action)
    {
        if (window == 0)
            throw new ActionFailure("No window");
        if (!Native.IsWindow(window))
            throw new ActionFailure("Window closed");
        if (IsSnapAction(action))
        {
            var target = CaptureWindow(window) ?? throw new ActionFailure("Window closed");
            SnapWindow(target, action);
            return;
        }
        switch (action)
        {
            case WindowAction.Maximize:
                Command(window, Native.IsZoomed(window) ? Native.ScRestore : Native.ScMaximize);
                break;
            case WindowAction.Minimize:
                Command(window, Native.ScMinimize);
                break;
            case WindowAction.Close:
                Command(window, Native.ScClose);
                break;
            case WindowAction.KeepOnTop:
                bool onTop = (Native.GetWindowLongPtr(window, -20) & 0x8) != 0; // GWL_EXSTYLE, WS_EX_TOPMOST
                // SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE
                if (!Native.SetWindowPos(window, onTop ? -2 : -1, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010))
                    throw new ActionFailure("Access denied");
                break;

        }
    }

    private static void Command(nint window, nint command)
    {
        if (!Native.PostMessage(window, Native.WmSysCommand, command, 0))
            throw new ActionFailure("Access denied");
    }

    /// <summary>Puts the window's visible edges on <paramref name="target"/>: Windows 11 frames carry invisible resize borders outside them.</summary>
    private static void Place(nint window, Native.Rect target)
    {
        Native.GetWindowRect(window, out var outer);
        bool framed = Visible(window, out var visible);
        if ((Native.GetWindowLongPtr(window, -16) & 0x00040000) == 0) // no WS_THICKFRAME: preserve fixed-size windows
        {
            int width = visible.Width, height = visible.Height;
            target.Left += (target.Width - width) / 2;
            target.Top += (target.Height - height) / 2;
            target.Right = target.Left + width;
            target.Bottom = target.Top + height;
        }
        int left = framed ? visible.Left - outer.Left : 0, top = framed ? visible.Top - outer.Top : 0;
        int right = framed ? outer.Right - visible.Right : 0, bottom = framed ? outer.Bottom - visible.Bottom : 0;
        // SWP_NOZORDER | SWP_NOACTIVATE
        if (!Native.SetWindowPos(window, 0, target.Left - left, target.Top - top, target.Width + left + right, target.Height + top + bottom, 0x0004 | 0x0010))
            throw new ActionFailure("Access denied");
    }

    private static bool Visible(nint window, out Native.Rect visible)
    {
        if (Native.DwmGetWindowAttribute(window, 9, out visible, System.Runtime.InteropServices.Marshal.SizeOf<Native.Rect>()) == 0) // DWMWA_EXTENDED_FRAME_BOUNDS
            return true;
        Native.GetWindowRect(window, out visible);
        return false;
    }

    public void Media(MediaAction action)
    {
        ushort vk = action switch
        {
            MediaAction.PlayPause => 0xB3,
            MediaAction.Next => 0xB0,
            MediaAction.Previous => 0xB1,
            MediaAction.Mute => 0xAD,
            MediaAction.VolumeUp => 0xAF,
            _ => 0xAE,
        };
        if (Native.Send([Native.Key(vk, up: false, Native.KeyExtended), Native.Key(vk, up: true, Native.KeyExtended)]) != 2)
            throw new ActionFailure("Input blocked");
    }

    public async Task PressAsync(KeyCombo keys)
    {
        await HeldKeysReleasedAsync();
        var inputs = new List<Native.Input>();
        foreach (uint vk in keys.ModifierKeys)
            inputs.Add(Native.Key((ushort)vk, up: false, KeyCombo.IsExtended(vk) ? Native.KeyExtended : 0));
        uint flags = KeyCombo.IsExtended(keys.Key) ? Native.KeyExtended : 0;
        inputs.Add(Native.Key((ushort)keys.Key, up: false, flags));
        inputs.Add(Native.Key((ushort)keys.Key, up: true, flags));
        foreach (uint vk in keys.ModifierKeys.Reverse())
            inputs.Add(Native.Key((ushort)vk, up: true, KeyCombo.IsExtended(vk) ? Native.KeyExtended : 0));
        if (Native.Send([.. inputs]) != inputs.Count)
            throw new ActionFailure("Input blocked");
    }

    public async Task TypeAsync(string text)
    {
        await HeldKeysReleasedAsync();
        var inputs = new List<Native.Input>();
        foreach (char unit in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (unit == '\r')
                continue;
            if (unit is '\n' or '\t')
            {
                ushort vk = unit == '\n' ? (ushort)0x0D : (ushort)0x09;
                inputs.Add(Native.Key(vk, up: false));
                inputs.Add(Native.Key(vk, up: true));
                continue;
            }
            inputs.Add(Native.Unit(unit, up: false));
            inputs.Add(Native.Unit(unit, up: true));
        }
        // In batches, so a long text doesn't outrun a slow app's queue.
        for (int i = 0; i < inputs.Count; i += 128)
        {
            var batch = inputs.GetRange(i, Math.Min(128, inputs.Count - i));
            if (Native.Send([.. batch]) != batch.Count)
                throw new ActionFailure("Input blocked");
            if (i + 128 < inputs.Count)
                await Task.Delay(10);
        }
    }

    /// <summary>Waits, up to a second, until Ctrl, Alt, Shift and Win are up: the action's keys mustn't mix with a shortcut's.</summary>
    private static async Task HeldKeysReleasedAsync()
    {
        var until = DateTime.UtcNow + HeldKeysTimeout;
        while ((Held(0x11) || Held(0x12) || Held(0x10) || Held(0x5B) || Held(0x5C)) && DateTime.UtcNow < until)
            await Task.Delay(15);

        static bool Held(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public void Run(CommandShell shell, string line)
    {
        var start = shell == CommandShell.Cmd
            ? new ProcessStartInfo("cmd.exe") { Arguments = "/d /s /c \"" + line + "\"" }
            : new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", line } };
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try { Process.Start(start)?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new ActionFailure("Shell not found"); }
    }

    public Task<(bool Terminal, string? Text)> TerminalSelectionAsync()
    {
        nint front = Native.GetForegroundWindow();
        if (front == 0 || !Terminals.Contains(Native.ClassOf(front)))
            return Task.FromResult<(bool, string?)>((false, null));
        // UI Automation calls into the terminal; off the UI thread, so a slow one never stalls Chippi.
        return Task.Run<(bool, string?)>(() =>
        {
            try
            {
                if (AutomationElement.FocusedElement is { } focused && focused.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
                {
                    var ranges = ((TextPattern)pattern).GetSelection();
                    string text = string.Concat(ranges.Select(r => r.GetText(64 * 1024)));
                    return (true, string.IsNullOrWhiteSpace(text) ? null : text);
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            return (true, null);
        });
    }
}
