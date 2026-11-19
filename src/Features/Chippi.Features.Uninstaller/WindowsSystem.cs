// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Windows.Management.Deployment;

namespace Chippi.Features.Uninstaller;

/// <summary>The real Uninstall keys, opened read-only. Nothing here writes.</summary>
public sealed class WindowsAppRegistry : IAppRegistry
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly string[] Names =
    [
        "DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "InstallDate", "EstimatedSize", "DisplayIcon", "UninstallString",
        "QuietUninstallString", "SystemComponent", "ParentKeyName", "WindowsInstaller", "NoRemove", "ReleaseType", "ParentDisplayName",
    ];

    private static RegistryKey? Open(AppSource source) => source switch
    {
        AppSource.Machine64 => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(UninstallPath, false),
        AppSource.Machine32 => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(UninstallPath, false),
        AppSource.User => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64).OpenSubKey(UninstallPath, false),
        _ => null,
    };

    public IReadOnlyList<RawApp> Read(AppSource source)
    {
        var list = new List<RawApp>();
        try
        {
            using var root = Open(source);
            if (root is null) return list;
            foreach (string name in root.GetSubKeyNames())
            {
                if (ReadKey(root, name) is { } raw) list.Add(raw);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return list;
    }

    public RawApp? ReadOne(AppSource source, string key)
    {
        try
        {
            using var root = Open(source);
            return root is null ? null : ReadKey(root, key);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    private static RawApp? ReadKey(RegistryKey root, string name)
    {
        try
        {
            using var key = root.OpenSubKey(name, false);
            if (key is null) return null;
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in Names)
            {
                object? v = key.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                values[value] = v is string or int or long ? v : null;
            }
            return new RawApp(name, values);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
    }
}

/// <summary>The Store and MSIX packages of the current user (PackageManager, no admin), and their removal for the current user.</summary>
public sealed class WindowsPackages : IPackageSource, IPackageRemover
{
    public IReadOnlyList<RawPackage> List()
    {
        var list = new List<RawPackage>();
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                try
                {
                    var id = package.Id;
                    var v = id.Version;
                    string? location = null;
                    try { location = package.InstalledPath; } catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException) { }
                    list.Add(new RawPackage(id.FullName, id.FamilyName, id.Name, package.DisplayName ?? "", package.PublisherDisplayName ?? "",
                        $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}", location, package.InstalledDate.UtcDateTime,
                        package.IsFramework, package.IsResourcePackage, package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System));
                }
                catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or InvalidOperationException) { }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { }
        return list;
    }

    public async Task<bool> RemoveAsync(string fullName, CancellationToken ct)
    {
        try
        {
            var result = await new PackageManager().RemovePackageAsync(fullName, RemovalOptions.None).AsTask(ct).ConfigureAwait(false);
            return result.ExtendedErrorCode is null || result.ExtendedErrorCode.HResult == 0;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }
}

/// <summary>Programs whose image file is inside a folder, read with limited process access. Never ends one.</summary>
public sealed class WindowsAppProcesses : IAppProcesses
{
    private const uint QueryLimited = 0x1000;

    public IReadOnlyList<string> Images()
    {
        var images = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                if (ImagePath(process.Id) is { } path) images.Add(path);
            }
        }
        return images;
    }

    private static string? ImagePath(int pid)
    {
        using var handle = OpenProcess(QueryLimited, false, (uint)pid);
        if (handle.IsInvalid) return null;
        var builder = new StringBuilder(1024);
        uint size = (uint)builder.Capacity;
        return QueryFullProcessImageName(handle, 0, builder, ref size) ? builder.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
}

/// <summary>Starts an HKCU app's uninstaller as the standard user. Never elevated: a program that needs it fails to start.</summary>
public sealed class WindowsUserProcess : IUserProcess
{
    public async Task<int?> RunAsync(string application, string arguments, TimeSpan limit, CancellationToken ct)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(application) { Arguments = arguments, UseShellExecute = false, CreateNoWindow = false });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException) { return null; }
        if (process is null) return null;
        using (process)
        {
            using var timeout = new CancellationTokenSource(limit);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }   // a wait that ended is not a verdict: the entry's re-read decides
            return process.ExitCode;
        }
    }
}

/// <summary>The Recycle Bin through IFileOperation: recycle only, no fallback to permanent deletion, no error UI.</summary>
public sealed class WindowsRecycler : IRecycler
{
    private const int SharingViolation = unchecked((int)0x80070020);

    public bool CanRecycle(string path, long bytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(path)!);
            // A fixed drive's bin holds 10% of it by default; a network or removable drive has none.
            return drive.DriveType == DriveType.Fixed && bytes <= drive.TotalSize / 10;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return false; }
    }

    public RecycleResult Recycle(string path)
    {
        object? operation = null;
        ShellItem? item = null;
        try
        {
            operation = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), throwOnError: true)!);
            var files = (FileOperation)operation!;
            // FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE | FOFX_ADDUNDORECORD | FOF_NOERRORUI | FOF_NOCONFIRMATION | FOF_SILENT: never permanent.
            Marshal.ThrowExceptionForHR(files.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x400 | 0x10 | 0x04));
            var iid = typeof(ShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out item));
            Marshal.ThrowExceptionForHR(files.DeleteItem(item, 0));
            int hr = files.PerformOperations();
            if (hr == SharingViolation) return RecycleResult.InUse;
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(files.GetAnyOperationsAborted(out bool aborted));
            return aborted || Exists(path) ? RecycleResult.Failed : RecycleResult.Recycled;
        }
        catch (COMException ex) { return ex.HResult == SharingViolation ? RecycleResult.InUse : RecycleResult.Failed; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return RecycleResult.Failed; }
        finally
        {
            if (item != null) Marshal.FinalReleaseComObject(item);
            if (operation != null) Marshal.FinalReleaseComObject(operation);
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ShellItem { }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface FileOperation
    {
        // Unused slots keep the native IFileOperation vtable order; only the typed methods below are called.
        void Advise();
        void Unadvise();
        [PreserveSig] int SetOperationFlags(uint flags);
        void SetProgressMessage();
        void SetProgressDialog();
        void SetProperties();
        void SetOwnerWindow();
        void ApplyPropertiesToItem();
        void ApplyPropertiesToItems();
        void RenameItem();
        void RenameItems();
        void MoveItem();
        void MoveItems();
        void CopyItem();
        void CopyItems();
        [PreserveSig] int DeleteItem(ShellItem item, nint sink);
        void DeleteItems();
        void NewItem();
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out ShellItem item);
}

/// <summary>Reads where a .lnk points: IShellLink's first method on the Shell's link object, loaded read-only.</summary>
public sealed class WindowsShortcutReader : IShortcutReader
{
    public string? Target(string lnkPath)
    {
        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!);
            ((IPersistFile)link!).Load(lnkPath, 0);
            var builder = new StringBuilder(1024);
            ((ShellLink)link).GetPath(builder, builder.Capacity, 0, 0);
            return builder.Length == 0 ? null : builder.ToString();
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException) { return null; }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint data, uint flags);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    }
}

/// <summary>HKCU and HKLM\Software as the standard user: reads, and deletes of keys the user's token may delete.</summary>
public sealed class WindowsLeftoverRegistry : ILeftoverRegistry
{
    private static RegistryKey Base(RegHive hive) => hive switch
    {
        RegHive.User => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
        RegHive.Machine32 => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32),
        _ => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
    };

    public bool Exists(RegHive hive, string path)
    {
        try
        {
            using var root = Base(hive);
            using var key = root.OpenSubKey(path, false);
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return false; }
    }

    public IReadOnlyList<string> SubKeys(RegHive hive, string path)
    {
        try
        {
            using var root = Base(hive);
            using var key = root.OpenSubKey(path, false);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return []; }
    }

    public bool DeleteTree(RegHive hive, string path)
    {
        try
        {
            using var root = Base(hive);
            root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return false; }
    }
}
