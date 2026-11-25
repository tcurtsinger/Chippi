// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>An app with a window in front or in the dock: its program and the name people know it by.</summary>
public sealed record AppIdentity(string Path, string Name)
{
    /// <summary>The desktop itself, with no window in front: "Desktop", no menus. Its program is Explorer's.</summary>
    public static AppIdentity Desktop { get; } = new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "Desktop");

    public bool Is(string path) => string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Names a window's app: what the Start menu calls it (<see cref="AppCatalog"/>), else its program's description
/// ("Notepad++"), else the program's name.
/// </summary>
internal static class AppNames
{
    private static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private static volatile AppCatalog _catalog = AppCatalog.Empty;
    private static int _reading;

    /// <summary>Raised (on the thread that read it) when the Start menu's names have been read: names given before may be better now.</summary>
    public static event Action? CatalogRead;

    /// <summary>The Start menu's names, as last read.</summary>
    public static AppCatalog Catalog
    {
        get => _catalog;
        set
        {
            _catalog = value;
            Names.Clear();
        }
    }

    /// <summary>Reads the Start menu's names on a thread of its own (it takes a second), once at a time.</summary>
    public static void ReadCatalog()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1)
            return;
        var thread = new Thread(() =>
        {
            try
            {
                var catalog = AppCatalog.Read();
                if (catalog.Count > 0)
                    Catalog = catalog;
                CatalogRead?.Invoke();
            }
            finally { Interlocked.Exchange(ref _reading, 0); }
        }) { IsBackground = true, Name = "Chippi app names" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>The app that owns <paramref name="window"/>, looking through a UWP frame to the app inside; null when unreadable.</summary>
    public static (uint ProcessId, AppIdentity App)? Of(nint window)
    {
        if (window == 0)
            return null;
        Native.GetWindowThreadProcessId(window, out uint pid);
        if (Native.ClassOf(window) == "ApplicationFrameWindow")
        {
            nint content = Native.FindWindowEx(window, 0, "Windows.UI.Core.CoreWindow", null);
            if (content != 0)
                Native.GetWindowThreadProcessId(content, out pid);
        }
        if (ImagePath(pid) is not { } path)
            return null;
        // A window with a taskbar identity of its own (a Store app, a web app in a browser's window) is named by it.
        string? id = ShellProperties.AppId(new Chippi.Core.Actions.ListedWindow(window, pid, "", "", path, false, 0));
        return (pid, new AppIdentity(path, id is not null && _catalog.NameOf(null, id) is { } known ? known : Name(path)));
    }

    /// <summary>The name for a window of <paramref name="path"/> with the taskbar identity <paramref name="appId"/>, for the dock's running apps.</summary>
    public static string Name(string path, string? appId) => appId is not null && _catalog.NameOf(null, appId) is { } known ? known : Name(path);

    public static string Name(string path) => Names.GetOrAdd(path, static exe =>
    {
        // Windows 11 calls it File Explorer; the program still describes itself as "Windows Explorer".
        if (AppIdentity.Desktop.Is(exe))
            return "File Explorer";
        if (_catalog.NameOf(exe, null) is { } listed)
            return listed;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            string? described = info.FileDescription?.Trim();
            // Some descriptions are sentences ("Notepad++ : a free (GNU) source code editor"): keep the product's name.
            if (!string.IsNullOrEmpty(described) && described.Length <= 32 && !described.Contains(" : ", StringComparison.Ordinal))
                return described;
            if (!string.IsNullOrWhiteSpace(info.ProductName) && info.ProductName.Trim().Length <= 32)
                return info.ProductName.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        string name = System.IO.Path.GetFileNameWithoutExtension(exe);
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : exe;
    });

    public static string? ImagePath(uint pid)
    {
        const uint QueryLimitedInformation = 0x1000;
        nint process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process == 0)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
