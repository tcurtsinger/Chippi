// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chippi.Core.Actions;

namespace Chippi.UI.Actions;

/// <summary>Reads registered launch metadata. Never reads process command lines or logs targets and arguments.</summary>
public static class RegisteredAppTargets
{
    public sealed record Target(string Identity, string Executable, string Arguments, string PackageFullName);

    public static IReadOnlyList<Target> ReadAll()
    {
        object? shell = null, folder = null, items = null;
        var targets = new List<Target>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) return targets;
            items = ((dynamic)folder).Items();
            int count = ((dynamic)items).Count;
            for (int i = 0; i < count; i++)
            {
                object item = ((dynamic)items).Item(i);
                try
                {
                    string Property(string name) => ((dynamic)item).ExtendedProperty(name) as string ?? "";
                    string identity = Property("System.AppUserModel.ID");
                    if (identity.Length > 0) targets.Add(new(identity, Property("System.Link.TargetParsingPath"),
                        Property("System.Link.Arguments"), Property("System.AppUserModel.PackageFullName")));
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            return targets;
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }

    public static string? ProcessIdentity(uint pid)
    {
        nint process = OpenProcessForApp(0x1000, false, pid);
        if (process == 0) return null;
        try
        {
            uint length = 1024;
            var identity = new StringBuilder((int)length);
            return GetApplicationUserModelId(process, ref length, identity) == 0 ? identity.ToString() : null;
        }
        finally { CloseAppProcess(process); }
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess")]
    private static extern nint OpenProcessForApp(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "CloseHandle")]
    private static extern bool CloseAppProcess(nint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder identity);

    public static Target? Resolve(string identity)
    {
        object? shell = null, folder = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) return null;
            item = ((dynamic)folder).ParseName(identity);
            if (item is null) return null;
            string Property(string name) => ((dynamic)item).ExtendedProperty(name) as string ?? "";
            string actual = Property("System.AppUserModel.ID");
            if (!string.Equals(actual, identity, StringComparison.OrdinalIgnoreCase)) return null;
            return new(actual, Property("System.Link.TargetParsingPath"), Property("System.Link.Arguments"),
                Property("System.AppUserModel.PackageFullName"));
        }
        catch (COMException) { return null; }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>Checks Start menu shortcuts with this identity, including the explicit Run as administrator flag.</summary>
    public static bool ShortcutNeedsAdmin(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        file.ReadExactly(header);
        if (BitConverter.ToUInt32(header[..4]) != 0x4c
            || new Guid(header[4..20]) != new Guid("00021401-0000-0000-C000-000000000046"))
            throw new ActionFailure("App target unavailable");
        return (BitConverter.ToUInt32(header[20..24]) & 0x2000) != 0;
    }

    public static bool NeedsAdminShortcut(Target target)
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            foreach (var root in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
            {
                string directory = Environment.GetFolderPath(root);
                if (!Directory.Exists(directory)) continue;
                foreach (string path in Directory.EnumerateFiles(directory, "*.lnk", new EnumerationOptions
                    { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    if (!ShortcutNeedsAdmin(path)) continue;
                    object? folder = null, item = null;
                    try
                    {
                        folder = ((dynamic)shell!).NameSpace(Path.GetDirectoryName(path));
                        if (folder is null) continue;
                        item = ((dynamic)folder).ParseName(Path.GetFileName(path));
                        if (item is null) continue;
                        string? appId = ((dynamic)item).ExtendedProperty("System.AppUserModel.ID") as string;
                        if (!string.Equals(appId, target.Identity, StringComparison.OrdinalIgnoreCase))
                        {
                            // The shell generates IDs for links without an explicit AppUserModelID; the source link
                            // need not expose that generated ID. Match its actual launch metadata as well.
                            string? executable = ((dynamic)item).ExtendedProperty("System.Link.TargetParsingPath") as string;
                            string arguments = ((dynamic)item).ExtendedProperty("System.Link.Arguments") as string ?? "";
                            if (!string.Equals(executable, target.Executable, StringComparison.OrdinalIgnoreCase)
                                || !string.Equals(arguments, target.Arguments, StringComparison.Ordinal)) continue;
                        }
                        if (ShortcutNeedsAdmin(path)) return true;
                    }
                    finally
                    {
                        if (item is not null) Marshal.ReleaseComObject(item);
                        if (folder is not null) Marshal.ReleaseComObject(folder);
                    }
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        { throw new ActionFailure("App target unavailable"); }
        finally { if (shell is not null) Marshal.ReleaseComObject(shell); }
    }
}

public sealed partial class WindowsActionPc
{
    public LaunchedApp? LaunchRegisteredApp(string parsingName)
    {
        var target = RegisteredAppTargets.Resolve(parsingName) ?? throw new ActionFailure("Not found");
        if (RegisteredAppTargets.NeedsAdminShortcut(target)) throw new ActionFailure("Needs admin");
        if (target.PackageFullName.Length == 0)
            return LaunchExecutable(new(target.Executable, target.Arguments));
        var path = new StringBuilder(32768);
        uint length = (uint)path.Capacity;
        if (GetStagedPackagePathByFullName(target.PackageFullName, ref length, path) != 0)
            throw new ActionFailure("Not found");
        try
        {
            using var xml = XmlReader.Create(Path.Combine(path.ToString(), "AppxManifest.xml"),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var manifest = XDocument.Load(xml);
            // Packages need allowElevation to elevate. Refuse these before any activation call.
            if (manifest.Descendants().Any(e => e.Name.LocalName == "Capability"
                && string.Equals((string?)e.Attribute("Name"), "allowElevation", StringComparison.OrdinalIgnoreCase)))
                throw new ActionFailure("Needs admin");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        { throw new ActionFailure("App target unavailable"); }
        IApplicationActivationManager? manager = null;
        try
        {
            manager = (IApplicationActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), throwOnError: true)!)!;
            int result = manager.ActivateApplication(parsingName, null, 2, out uint pid); // AO_NOERRORUI
            if (result == unchecked((int)0x800702e4)) throw new ActionFailure("Needs admin");
            if (result < 0) throw new ActionFailure("Launching failed");
            try { using var process = Process.GetProcessById((int)pid); return ReadLaunchedApp(process); }
            catch (ArgumentException) { return null; }
        }
        catch (COMException) { throw new ActionFailure("Launching failed"); }
        finally { if (manager is not null) Marshal.ReleaseComObject(manager); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetStagedPackagePathByFullName(string fullName, ref uint length, StringBuilder path);

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
        [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appId, nint items,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
        [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appId, nint items, out uint processId);
    }
}
