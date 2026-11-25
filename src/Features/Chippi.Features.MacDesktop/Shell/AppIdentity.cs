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
    /// <summary>The desktop, or nothing Chippi can name: Explorer's name, no menus.</summary>
    public static AppIdentity Desktop { get; } = new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "File Explorer");

    public bool Is(string path) => string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Names a window's app from its program: the file's description ("Notepad++"), else its name.</summary>
internal static class AppNames
{
    private static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

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
        return ImagePath(pid) is { } path ? (pid, new AppIdentity(path, Name(path))) : null;
    }

    public static string Name(string path) => Names.GetOrAdd(path, static exe =>
    {
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
