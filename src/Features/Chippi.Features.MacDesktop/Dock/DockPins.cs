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
/// The dock's pins: a program's path, a shortcut's, or a Store app's <c>shell:AppsFolder\ID</c>. The first start brings the
/// taskbar's pins over (its shortcuts in User Pinned\TaskBar), in the taskbar's order. A shortcut is read for the program it
/// runs and its AppUserModelID, so its running windows show under it.
/// </summary>
internal static class DockPins
{
    private const string AppsFolder = @"shell:AppsFolder\";

    private static readonly ConcurrentDictionary<string, (string? Program, string? AppId)> Links = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> AppNamesById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pin as the dock matches windows to it.</summary>
    public static DockPin Read(string pin) => new(pin, Program(pin), Name(pin), AppId(pin));

    public static string TaskbarFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    /// <summary>The taskbar's pinned shortcuts, in the order the taskbar shows them (its Taskband Favorites value).</summary>
    public static IReadOnlyList<string> FromTaskbar()
    {
        try
        {
            if (!Directory.Exists(TaskbarFolder))
                return [];
            var links = Directory.EnumerateFiles(TaskbarFolder, "*.lnk").ToArray();
            byte[]? favorites = null;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
                favorites = key?.GetValue("Favorites") as byte[];
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Warn("Mac-style desktop: reading the taskbar's order failed", ex);
            }
            return DockApps.TaskbarOrder(links, favorites);
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
            return !pin.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(pin) ? pin : null;
        return Links.GetOrAdd(pin, static link => ReadShortcut(link)).Program;
    }

    /// <summary>The AppUserModelID a pin's windows carry: a Store app's ID, or the one its shortcut sets; null for none.</summary>
    public static string? AppId(string pin)
    {
        if (pin.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase))
            return pin[AppsFolder.Length..];
        return pin.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Links.GetOrAdd(pin, static link => ReadShortcut(link)).AppId : null;
    }

    /// <summary>What the dock calls a pin: a shortcut's own name, or the program's.</summary>
    public static string Name(string pin)
    {
        if (pin.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase))
            return AppNamesById.GetOrAdd(pin, static p => ShellNames.DisplayName(p) ?? p[AppsFolder.Length..]);
        return pin.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(pin)
            : AppNames.Name(pin);
    }

    private static (string? Program, string? AppId) ReadShortcut(string link)
    {
        object? created = null;
        try
        {
            created = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!);
            ((IPersistFile)created!).Load(link, 0);
            var buffer = new StringBuilder(1024);
            ((IShellLinkW)created).GetPath(buffer, buffer.Capacity, 0, 0);
            string target = Environment.ExpandEnvironmentVariables(buffer.ToString());
            string? appId = created is ShellProperties.IPropertyStore store ? ShellProperties.AppId(store) : null;
            return (target.Length > 0 && Path.IsPathFullyQualified(target) ? target : null, appId);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException)
        {
            return (null, null);
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
