// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Chippi.Core.Diagnostics;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// The dock's pins: a program's path or a shortcut's. The first start brings the taskbar's pins over (its shortcuts in
/// User Pinned\TaskBar). A shortcut is read for the program it runs, so its running windows show under it.
/// </summary>
internal static class DockPins
{
    private static readonly ConcurrentDictionary<string, string?> Targets = new(StringComparer.OrdinalIgnoreCase);

    public static string TaskbarFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    /// <summary>The taskbar's pinned shortcuts, by name. Windows keeps their order elsewhere; File Explorer goes first.</summary>
    public static IReadOnlyList<string> FromTaskbar()
    {
        try
        {
            if (!Directory.Exists(TaskbarFolder))
                return [];
            return Directory.EnumerateFiles(TaskbarFolder, "*.lnk")
                .OrderBy(p => Path.GetFileNameWithoutExtension(p).Contains("Explorer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Mac-style desktop: reading the taskbar's pins failed", ex);
            return [];
        }
    }

    /// <summary>The program a pin runs: the path itself, or a shortcut's target; null when it can't be read.</summary>
    public static string? Program(string pin)
    {
        if (!pin.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return Path.IsPathFullyQualified(pin) ? pin : null;
        return Targets.GetOrAdd(pin, static link => ReadShortcut(link));
    }

    /// <summary>What the dock calls a pin: a shortcut's own name, or the program's.</summary>
    public static string Name(string pin) => pin.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(pin)
        : AppNames.Name(pin);

    private static string? ReadShortcut(string link)
    {
        object? created = null;
        try
        {
            created = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!);
            ((IPersistFile)created!).Load(link, 0);
            var buffer = new StringBuilder(1024);
            ((IShellLinkW)created).GetPath(buffer, buffer.Capacity, 0, 0);
            string target = Environment.ExpandEnvironmentVariables(buffer.ToString());
            return target.Length > 0 && Path.IsPathFullyQualified(target) ? target : null;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (created is not null)
                Marshal.ReleaseComObject(created);
        }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int length, nint data, uint flags);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    }
}
