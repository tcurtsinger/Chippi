// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated;

/// <summary>Every app's Uninstall entry: both HKLM views and the owner's HKCU, read from the owner's hive under HKU\&lt;sid&gt; (the helper's own HKCU is not the owner's).</summary>
public sealed class NativeInstalledApps(string ownerSid) : IInstalledApps
{
    private const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<InstalledApp> List()
    {
        var apps = new List<InstalledApp>();
        foreach (int view in new[] { 64, 32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view == 64 ? RegistryView.Registry64 : RegistryView.Registry32);
                Read(root, Uninstall, "HKLM", view, apps);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        try
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            Read(users, ownerSid + "\\" + Uninstall, "HKCU", 64, apps);
            Read(users, ownerSid + @"\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU", 32, apps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return apps;
    }

    private static void Read(RegistryKey root, string path, string hive, int view, List<InstalledApp> apps)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return;
        foreach (var name in key.GetSubKeyNames())
        {
            try
            {
                using var app = key.OpenSubKey(name);
                if (app is null) continue;
                apps.Add(new InstalledApp(hive, view, name, Str(app, "DisplayName"), Str(app, "InstallLocation"), Str(app, "UninstallString"), Str(app, "DisplayIcon")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }

    private static string? Str(RegistryKey key, string name) => key.GetValue(name) is string value ? value : null;
}

/// <summary>Probes and deletes HKLM keys without following a registry link, in the named view.</summary>
public sealed class NativeLeftoverRegistry(SafeAccessTokenHandle standardToken) : ILeftoverRegistry
{
    private const uint Delete = 0x10000, ReadControl = 0x20000, QueryValue = 0x1, EnumerateSubKeys = 0x8;
    private const int MaxKeys = 10000, MaxDepth = 64;

    private static uint Access(int view, uint rights) => rights | (view == 64 ? NativeRegistry.Wow64Key : NativeRegistry.Wow32Key);

    public KeyProbe Probe(int view, string path)
    {
        var handle = NativeRegistry.Open(path, Access(view, NativeRegistry.KeyRead), out bool link);
        if (handle == IntPtr.Zero) { bool there = ExistsButUnreadable(view, path); return new KeyProbe(there, there, false); }
        try
        {
            if (link) return new KeyProbe(true, true, false);
            bool canDelete = StandardCanDelete(handle);
            int count = 0;
            bool unsafeTree = !Walk(view, path, handle, 0, ref count);
            return new KeyProbe(true, unsafeTree, canDelete);
        }
        finally { NativeRegistry.Close(handle); }
    }

    /// <summary>A key that can't be opened for reading is either missing (error 2) or one the helper can't vouch for; only a missing one is "gone".</summary>
    private static bool ExistsButUnreadable(int view, string path)
    {
        int error = NativeRegistry.RegOpenKeyExW(NativeRegistry.LocalMachine, path, NativeRegistry.OptionOpenLink, Access(view, 0x1), out var probe);
        if (error == 0) { NativeRegistry.Close(probe); return true; }
        return error != 2;
    }

    /// <summary>False when a key under this one is a link, can't be opened, or the tree is bigger than the helper will vouch for.</summary>
    private static bool Walk(int view, string path, IntPtr handle, int depth, ref int count)
    {
        if (depth > MaxDepth) return false;
        string[] names;
        using (var key = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: false), view == 64 ? RegistryView.Registry64 : RegistryView.Registry32))
            names = key.GetSubKeyNames();
        foreach (var name in names)
        {
            if (++count > MaxKeys) return false;
            string child = path + "\\" + name;
            var childHandle = NativeRegistry.Open(child, Access(view, NativeRegistry.KeyRead), out bool link);
            if (childHandle == IntPtr.Zero) return false;
            try { if (link || !Walk(view, child, childHandle, depth + 1, ref count)) return false; }
            finally { NativeRegistry.Close(childHandle); }
        }
        return true;
    }

    /// <summary>Whether AccessCheck grants the standard token DELETE on the key: then it is the owner's to delete, not the helper's.</summary>
    private unsafe bool StandardCanDelete(IntPtr handle)
    {
        if (NativeRegistry.GetSecurityInfo(handle, 4, 7, out _, out _, out _, out _, out var descriptor) != 0) return true;   // can't tell: leave it to the owner's own delete
        try
        {
            var mapping = new NativeRegistry.GenericMapping { Read = 0x20019, Write = 0x20006, Execute = 0x20019, All = 0xF003F };
            return (NativeRegistry.Granted(descriptor, standardToken, mapping) & Delete) != 0;
        }
        finally { NativeRegistry.LocalFree(descriptor); }
    }

    public bool DeleteTree(int view, string path)
    {
        int cut = path.LastIndexOf('\\');
        if (cut <= 0) return false;
        var parent = NativeRegistry.Open(path[..cut], Access(view, 0xF003F), out bool link);
        if (parent == IntPtr.Zero) return false;
        try { return !link && RegDeleteTreeW(parent, path[(cut + 1)..]) == 0; }
        finally { NativeRegistry.Close(parent); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegDeleteTreeW(IntPtr key, string subKey);
}

/// <summary>
/// The owner's machine for DeleteLeftovers. Every path under the owner's profile comes from HKLM's ProfileList entry for the verified owner's SID,
/// never from the helper's own environment and never from anything Chippi sends.
/// </summary>
public sealed class NativeLeftoverSystem : ILeftoverSystem
{
    public NativeLeftoverSystem(string ownerProfile)
    {
        string drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        string local = Path.Combine(ownerProfile, "AppData", "Local"), roaming = Path.Combine(ownerProfile, "AppData", "Roaming");
        var roots = new List<string>
        {
            Path.Combine(drive, "Users"), Path.Combine(drive, "Users", "Public"), ownerProfile, Path.Combine(ownerProfile, "AppData"), local, roaming,
            Path.Combine(ownerProfile, "AppData", "LocalLow"), Path.Combine(ownerProfile, "Desktop"), Path.Combine(ownerProfile, "Documents"),
            Path.Combine(ownerProfile, "Downloads"), Path.Combine(ownerProfile, "Pictures"), Path.Combine(ownerProfile, "Music"), Path.Combine(ownerProfile, "Videos"),
        };
        foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramFiles(Arm)", "CommonProgramFiles", "CommonProgramFiles(x86)",
            "CommonProgramW6432", "ProgramData" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 3 } value) roots.Add(value.TrimEnd('\\'));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\'));
        ProtectedRoots = [.. roots.Where(r => r.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase)];
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        ProtectedTrees =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\'),
            Path.Combine(local, "Chippi"), Path.Combine(local, "ChippiApp"), Path.Combine(local, "Chippi-dev"),
            Path.Combine(roaming, "Chippi"), Path.Combine(roaming, "Chippi-dev"), Path.Combine(programData, "Chippi"),
            AppContext.BaseDirectory.TrimEnd('\\'),
        ];
        BackupFolder = Path.Combine(local, "Chippi", "leftovers-backup");
    }

    public string Expand(string text) => Environment.ExpandEnvironmentVariables(text);
    public string SystemDirectory => Environment.SystemDirectory;
    public IReadOnlyList<string> ProtectedRoots { get; }
    public IReadOnlyList<string> ProtectedTrees { get; }
    public string BackupFolder { get; }

    public bool Exists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return true; }
    }

    public bool CreateFolder(string path)
    {
        try { Directory.CreateDirectory(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public unsafe RecycleOutcome Recycle(string path)
    {
        // pFrom is a list of names, each ended by a null and the list by a second one.
        int bytes = (path.Length + 2) * sizeof(char);
        var from = Marshal.AllocHGlobal(bytes);
        try
        {
            var span = new Span<char>((void*)from, path.Length + 2);
            span.Clear();
            path.AsSpan().CopyTo(span);
            var operation = new ShFileOperation { Function = 3, From = from, Flags = 0x4 | 0x40 | 0x400 | 0x4000 };   // SILENT | ALLOWUNDO | NOERRORUI | WANTNUKEWARNING
            int result = SHFileOperationW(ref operation);
            if (result == 0 && operation.Aborted == 0) return RecycleOutcome.Removed;
            return result is 0x20 or 0x21 or 0x78 or 0x71 ? RecycleOutcome.InUse : RecycleOutcome.Failed;   // sharing or lock violation, access denied
        }
        finally { Marshal.FreeHGlobal(from); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOperation
    {
        public IntPtr Window; public uint Function; public IntPtr From; public IntPtr To; public ushort Flags; public int Aborted; public IntPtr Mappings; public IntPtr Title;
    }
    [DllImport("shell32.dll")] private static extern int SHFileOperationW(ref ShFileOperation operation);

    /// <summary>The owner's profile folder from HKLM's ProfileList for this SID (a REG_EXPAND_SZ the profile service wrote), or null.</summary>
    public static string? ProfileFolder(string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
        return key?.GetValue("ProfileImagePath") is string path && path.Length > 3 && Path.IsPathFullyQualified(path) ? path.TrimEnd('\\') : null;
    }

    public static string? SidOf(SafeAccessTokenHandle token)
    {
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        return identity.User?.Value;
    }
}
