// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated;

/// <summary>Checks the standard peer's actual access, keeping checked paths open until launch.</summary>
public sealed class TrustedExecutable : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    public static TrustedExecutable Open(string path, SafeAccessTokenHandle standardToken)
    {
        var guard = new TrustedExecutable();
        try
        {
            string full = Path.GetFullPath(path);
            if (!Path.IsPathFullyQualified(path) || !string.Equals(full, path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(@"\\", StringComparison.Ordinal) || path.AsSpan(2).Contains(':')
                || path.Contains('~') || path.Split('\\', '/').Any(p => p is "." or ".."))
                throw new UnauthorizedAccessException("Unsafe executable path.");
            guard.Check(path, 2 | 4 | 0x10000 | 0x40000 | 0x80000, standardToken, file: true);
            var folder = Path.GetDirectoryName(path);
            bool immediate = true;
            while (!string.IsNullOrEmpty(folder))
            {
                guard.Check(folder, DirectoryDeniedRights(folder, immediate), standardToken, file: false);
                immediate = false;
                folder = Directory.GetParent(folder)?.FullName;
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }

    private void Check(string path, uint deniedRights, SafeAccessTokenHandle token, bool file)
    {
        string label = Path.GetFileName(path);
        if (label.Length == 0) label = "volume root";
        // FILE_READ_DATA (FILE_LIST_DIRECTORY) is needed beside READ_CONTROL and FILE_READ_ATTRIBUTES: a handle that asks only for attribute-type rights is skipped by
        // the sharing check, so it would not stop a rename or delete.
        var handle = CreateFile(path, 0x20081, file ? 1u : 3u, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
        _handles.Add(handle);
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception();
        if ((info.Attributes & 0x400) != 0) throw new UnauthorizedAccessException("Reparse point refused.");
        var final = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, final, final.Capacity, 0);
        if (length == 0 || length >= final.Capacity) throw new Win32Exception();
        if (!string.Equals(final.ToString()[4..].TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path changed.");
        uint error = GetSecurityInfo(handle, 1, 7, out var owner, out _, out _, out _, out var descriptor);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            string sid = new SecurityIdentifier(owner).Value;
            if (sid is not ("S-1-5-18" or "S-1-5-32-544" or TrustedInstaller))
                throw new UnauthorizedAccessException("Untrusted file owner (" + label + ").");
            try { CheckStandardAccess(descriptor, token, deniedRights); }
            catch (UnauthorizedAccessException refusal) { throw new UnauthorizedAccessException(refusal.Message + " (" + label + ")", refusal); }
        }
        finally { LocalFree(descriptor); }
    }
    internal static uint DirectoryDeniedRights(string path, bool immediate)
    {
        // A verified drive-volume root cannot be renamed; DELETE there cannot replace an ancestor.
        // Owner, reparse/final-path, DELETE_CHILD and ACL-control checks still apply to that root.
        bool volumeRoot = path.Length == 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        return (immediate ? 2u | 4u : 0) | 0x40 | (volumeRoot ? 0 : 0x10000u) | 0x40000 | 0x80000;
    }
    internal static void CheckStandardAccess(nint descriptor, SafeAccessTokenHandle token, uint deniedRights)
    {
        var mapping = new GenericMapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200A0, All = 0x1F01FF };
        uint size = 4096;
        var privileges = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!AccessCheck(descriptor, token, 0x02000000, ref mapping, privileges, ref size, out uint granted, out _))
                throw new Win32Exception();
            // MAXIMUM_ALLOWED may successfully grant zero rights on a protected ancestor.
            if ((granted & deniedRights) != 0) throw new UnauthorizedAccessException($"Executable writable by standard user (rights 0x{granted & deniedRights:X}).");
        }
        finally { Marshal.FreeHGlobal(privileges); }
    }
    public void Dispose() { foreach (var handle in _handles) handle.Dispose(); _handles.Clear(); }
    [StructLayout(LayoutKind.Sequential)] private struct GenericMapping { public uint Read, Write, Execute, All; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int count, uint flags);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AccessCheck(nint descriptor, SafeAccessTokenHandle token, uint desired, ref GenericMapping mapping,
        nint privileges, ref uint length, out uint granted, [MarshalAs(UnmanagedType.Bool)] out bool allowed);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
