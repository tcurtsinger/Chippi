// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>One window Alt+Tab lists.</summary>
/// <param name="Handle">The window to close.</param>
/// <param name="ProcessId">The app's process: for a UWP app, its own rather than the frame host's.</param>
/// <param name="WindowProcessId">The process that owns <paramref name="Handle"/>.</param>
/// <param name="Path">The app's program file.</param>
/// <param name="Content">A UWP app's own window inside the frame, or null.</param>
internal readonly record struct AltTabWindow(HWND Handle, uint ProcessId, uint WindowProcessId, string Path, HWND Content);

/// <summary>The open apps as Alt+Tab has them, the ones that stopped responding, and quitting or ending them.</summary>
internal sealed partial class AltTabApps
{
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every window Alt+Tab lists, front first, leaving out Chippi's own.</summary>
    public static List<AltTabWindow> List()
    {
        uint self = (uint)Environment.ProcessId;
        var shell = PInvoke.GetShellWindow();
        var paths = new Dictionary<uint, string?>();
        var found = new List<AltTabWindow>();
        foreach (var top in Win32Windows.TopLevel())
        {
            var hwnd = Win32Windows.ClassIs(top, "Ghost") ? HungWindow(top) : top;
            if (hwnd.IsNull || hwnd == shell || !IsAltTab(hwnd, ghosted: hwnd != top))
                continue;
            uint windowPid = Win32Windows.ProcessId(hwnd);
            uint pid = windowPid;
            var content = HWND.Null;
            if (Win32Windows.ClassIs(hwnd, "ApplicationFrameWindow"))
            {
                // A minimized UWP app's own window leaves its frame; with no way to tell which app it is, it's left out.
                content = PInvoke.FindWindowEx(hwnd, HWND.Null, "Windows.UI.Core.CoreWindow", null);
                pid = content.IsNull ? 0 : Win32Windows.ProcessId(content);
                if (pid == 0 || pid == windowPid)
                    continue;
            }
            if (pid == self || windowPid == self)
                continue;
            if (!paths.TryGetValue(pid, out var path))
                paths[pid] = path = ImagePath(pid);
            if (path is not null && !found.Exists(w => w.Handle == hwnd))
                found.Add(new AltTabWindow(hwnd, pid, windowPid, path, content));
        }
        return found;
    }

    /// <summary>One app per program file, in Alt+Tab's order; <paramref name="frontProcessId"/> marks the front one.</summary>
    public IReadOnlyList<OpenApp> Apps(IReadOnlyList<AltTabWindow> windows, uint frontProcessId, IReadOnlySet<uint>? frozen = null)
    {
        var paths = new List<string>();
        foreach (var window in windows)
        {
            if (!paths.Contains(window.Path, StringComparer.OrdinalIgnoreCase))
                paths.Add(window.Path);
        }
        // A UWP app in front reports the frame host's process; then the frontmost app that host shows is the one.
        string? front = frontProcessId == 0 ? null
            : windows.FirstOrDefault(w => w.ProcessId == frontProcessId).Path
              ?? windows.FirstOrDefault(w => w.WindowProcessId == frontProcessId).Path;
        return [.. paths.Select(p => new OpenApp(p, Name(p), string.Equals(p, front, StringComparison.OrdinalIgnoreCase),
            frozen is not null && windows.Any(w => frozen.Contains(w.ProcessId) && string.Equals(w.Path, p, StringComparison.OrdinalIgnoreCase))))];
    }

    /// <summary>
    /// The processes with an Alt+Tab window that has stopped responding. A UWP app is judged by its own window, and
    /// neither the frame host (it holds every UWP app) nor the shell's explorer.exe is ever one.
    /// </summary>
    public static HashSet<uint> Frozen(IReadOnlyList<AltTabWindow> windows)
    {
        uint shell = ExplorerShell.ProcessId();
        var frozen = new HashSet<uint>();
        foreach (var window in windows)
        {
            if (window.ProcessId == shell
                || string.Equals(Path.GetFileName(window.Path), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (PInvoke.IsHungAppWindow(window.Content.IsNull ? window.Handle : window.Content))
                frozen.Add(window.ProcessId);
        }
        return frozen;
    }

    /// <summary>Asks each of the app's windows to close, as its close button does.</summary>
    public static void Quit(string path)
    {
        var windows = List().Where(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
        int asked = windows.Count(w => PInvoke.PostMessage(w.Handle, PInvoke.WM_CLOSE, 0, 0));
        if (windows.Count > 0 && asked == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Quit failed");
    }

    /// <summary>Ends the app at <paramref name="path"/> if it has stopped responding; throws when it couldn't be ended (an elevated app).</summary>
    public static void End(string path)
    {
        var windows = List();
        var mine = Frozen(windows).Where(pid => windows.Exists(w => w.ProcessId == pid && string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)));
        int failed = 0;
        foreach (uint pid in mine)
        {
            using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, pid);
            if (process.IsInvalid || !PInvoke.TerminateProcess(process, 1))
                failed++;
        }
        if (failed > 0)
            throw new InvalidOperationException("End failed: the app couldn't be ended");
    }

    /// <summary>Ends every app that has stopped responding; when one couldn't be ended (an elevated app), throws after the rest.</summary>
    public static void EndFrozen()
    {
        int failed = 0;
        foreach (uint pid in Frozen(List()))
        {
            using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, pid);
            if (process.IsInvalid || !PInvoke.TerminateProcess(process, 1))
                failed++;
        }
        if (failed > 0)
            throw new InvalidOperationException($"End frozen apps failed: {failed} couldn't be ended");
    }

    /// <summary>Visible, its own app (unowned, or marked as an app window), not a tool window, not cloaked, with a title.</summary>
    private static unsafe bool IsAltTab(HWND hwnd, bool ghosted)
    {
        if (!ghosted && !PInvoke.IsWindowVisible(hwnd))
            return false;
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((style & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0)
            return false;
        if ((style & WINDOW_EX_STYLE.WS_EX_APPWINDOW) == 0
            && (!PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER).IsNull || (style & WINDOW_EX_STYLE.WS_EX_NOACTIVATE) != 0))
            return false;
        uint cloaked = 0;
        if (!ghosted && PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Succeeded && cloaked != 0)
            return false;
        if (Win32Windows.ClassIs(hwnd, "Windows.UI.Core.CoreWindow"))
            return false; // Start, Search and the like; an app's own sits inside its frame
        Span<char> title = stackalloc char[2];
        return PInvoke.InternalGetWindowText(hwnd, title) > 0;
    }

    private static string? ImagePath(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
            return null;
        Span<char> buffer = stackalloc char[1024];
        uint size = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
            ? buffer[..(int)size].ToString()
            : null;
    }

    /// <summary>The file's description ("Google Chrome"), else its name without ".exe".</summary>
    private string Name(string path) => _names.GetOrAdd(path, static p =>
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(p).FileDescription?.Trim() is { Length: > 0 } description)
                return description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return Path.GetFileNameWithoutExtension(p);
    });

    /// <summary>The hung window a ghost stands in for while Windows shows it as "Not Responding".</summary>
    private static HWND HungWindow(HWND ghost)
    {
        try
        {
            return new HWND(HungWindowFromGhostWindow(ghost));
        }
        catch (EntryPointNotFoundException)
        {
            return HWND.Null;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint HungWindowFromGhostWindow(nint ghost);
}
