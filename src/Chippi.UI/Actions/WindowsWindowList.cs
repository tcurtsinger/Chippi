// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Chippi.Core.Actions;

namespace Chippi.UI.Actions;

/// <summary>The App switcher and arrangements share this single top-level window enumerator.</summary>
public sealed partial class WindowsWindowList(uint self)
{
    public WindowsWindowList() : this((uint)Environment.ProcessId) { }
    private readonly uint _self = self;
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }
    public IReadOnlyList<ListedWindow> Read(bool includeOtherDesktops = false)
    {
        nint shell = GetShellWindow();
        var paths = new Dictionary<uint, string?>();
        var found = new List<ListedWindow>();
        IVirtualDesktopManager? manager = null;
        try
        {
            if (includeOtherDesktops)
            {
                try { manager = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!)!; }
                catch (COMException) { }
            }
            foreach (nint top in TopLevel())
            {
                if (top == shell || !IsAltTab(top, includeOtherDesktops, manager, out bool currentDesktop))
                    continue;
                GetWindowThreadProcessId(top, out uint pid);
                uint appPid = pid;
                if (ClassIs(top, "ApplicationFrameWindow"))
                {
                    // A UWP app's frame: its own window inside names the app. Minimized, it leaves the frame; then the frame's title still does.
                    nint content = FindWindowEx(top, 0, "Windows.UI.Core.CoreWindow", null);
                    if (content != 0)
                        GetWindowThreadProcessId(content, out appPid);
                }
                if (pid == _self || appPid == _self)
                    continue;
                if (!paths.TryGetValue(appPid, out var path))
                    paths[appPid] = path = ImagePath(appPid);
                string title = Title(top);
                found.Add(new ListedWindow(top, pid, path is null ? title : Name(path), title, path ?? "", IsIconic(top),
                    MonitorFromWindow(top, MonitorDefaultToNearest), currentDesktop, appPid));
            }
            return found;
        }
        finally { if (manager is not null) Marshal.ReleaseComObject(manager); }
    }

    private static bool? IsOnDesktop(IVirtualDesktopManager? manager, nint window)
    {
        try { return manager is not null && manager.IsWindowOnCurrentVirtualDesktop(window, out bool current) == 0 ? current : null; }
        catch (COMException) { return null; }
    }

    // A shell cloak is retained only when Windows confirms another desktop. Unknown/current stays hidden.
    internal static bool CloakingExcludes(uint cloaked, bool includeOtherDesktops, bool? onCurrentDesktop) =>
        (cloaked & ~2u) != 0 || (cloaked != 0 && (!includeOtherDesktops || onCurrentDesktop != false));

    private static unsafe bool IsAltTab(nint hwnd, bool includeOtherDesktops, IVirtualDesktopManager? manager, out bool currentDesktop)
    {
        currentDesktop = true;
        if (!IsWindowVisible(hwnd))
            return false;
        int style = GetWindowLong(hwnd, GwlExStyle);
        if ((style & WsExToolWindow) != 0)
            return false;
        if ((style & WsExAppWindow) == 0 && (GetWindow(hwnd, GwOwner) != 0 || (style & WsExNoActivate) != 0))
            return false;
        uint cloaked = 0;
        bool readCloak = DwmGetWindowAttribute(hwnd, DwmwaCloaked, &cloaked, sizeof(uint)) == 0;
        bool? onCurrentDesktop = includeOtherDesktops ? IsOnDesktop(manager, hwnd) : true;
        currentDesktop = onCurrentDesktop ?? false;
        if (readCloak && CloakingExcludes(cloaked, includeOtherDesktops, onCurrentDesktop))
            return false;
        if (ClassIs(hwnd, "Windows.UI.Core.CoreWindow"))
            return false; // Start, Search and the like
        return GetWindowTextLength(hwnd) > 0;
    }

    /// <summary>Every top-level window, in z-order (the front one first).</summary>
    private static unsafe List<nint> TopLevel()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumWindows(&Add, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    [UnmanagedCallersOnly]
    private static int Add(nint hwnd, nint list)
    {
        ((List<nint>)GCHandle.FromIntPtr(list).Target!).Add(hwnd);
        return 1;
    }

    private static unsafe bool ClassIs(nint hwnd, string name)
    {
        char* buffer = stackalloc char[64];
        int length = GetClassName(hwnd, buffer, 64);
        return length > 0 && new ReadOnlySpan<char>(buffer, length).SequenceEqual(name);
    }

    private static unsafe string Title(nint hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            int read = GetWindowText(hwnd, text, buffer.Length);
            return new string(text, 0, Math.Max(0, read));
        }
    }

    private static unsafe string? ImagePath(uint pid)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
            return null;
        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
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

    private const int GwlExStyle = -20, WsExToolWindow = 0x80, WsExAppWindow = 0x40000, WsExNoActivate = 0x08000000;
    private const uint GwOwner = 4, DwmwaCloaked = 14, MonitorDefaultToNearest = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")] private static partial int GetWindowLong(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint GetWindow(nint hwnd, uint cmd);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static partial int GetWindowTextLength(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")] private static unsafe partial int GetWindowText(nint hwnd, char* text, int max);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static unsafe partial int GetClassName(nint hwnd, char* name, int max);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> proc, nint data);
    [LibraryImport("dwmapi.dll")] private static unsafe partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);
    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")] [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);
}