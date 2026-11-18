// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Runtime.InteropServices;

namespace Chippi.Elevated;

/// <summary>What a delete by handle did.</summary>
public enum DeleteOutcome { Deleted, Missing, InUse }

/// <summary>
/// Handle-based file access for the category clean. A folder being walked is held open without FILE_SHARE_DELETE, so it can't be renamed,
/// removed or swapped for a junction until the walk leaves it; every delete opens the entry itself with FILE_FLAG_OPEN_REPARSE_POINT (a link
/// is removed as a link, never followed) and no sharing, so anything else holding the file open makes it "in use" and a file opened with
/// FILE_SHARE_DELETE is not deleted from under its owner.
/// </summary>
public static unsafe class SafeFs
{
    private const uint Delete = 0x10000, ReadAttributes = 0x80, ListDirectory = 0x1;
    private const uint ShareRead = 1, ShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const uint ReparseAttr = 0x400, DirectoryAttr = 0x10;
    private const int FileDispositionInfoEx = 21;
    private const uint DispositionDelete = 0x1, DispositionIgnoreReadOnly = 0x10;
    private static readonly IntPtr Invalid = new(-1);

    /// <summary>Holds a real (non-link) folder open, or null when it is missing, a reparse point, not a folder or can't be opened.</summary>
    public static IDisposable? OpenFolder(string path)
    {
        IntPtr h = Open(path, ListDirectory | ReadAttributes, ShareRead | ShareWrite, BackupSemantics | OpenReparsePoint);
        if (h == Invalid) return null;
        if (!GetFileInformationByHandle(h, out var info) || (info.Attributes & ReparseAttr) != 0 || (info.Attributes & DirectoryAttr) == 0)
        {
            CloseHandle(h);
            return null;
        }
        return new Held(h);
    }

    /// <summary>Deletes the file, folder or link at <paramref name="path"/> itself, read-only or not, and never anything it points to.</summary>
    public static DeleteOutcome DeleteEntry(string path)
    {
        IntPtr h = Open(path, Delete | ReadAttributes, 0, BackupSemantics | OpenReparsePoint);
        if (h == Invalid)
        {
            int err = Marshal.GetLastWin32Error();
            return err is 2 or 3 ? DeleteOutcome.Missing : DeleteOutcome.InUse;
        }
        try
        {
            uint flags = DispositionDelete | DispositionIgnoreReadOnly;
            return SetFileInformationByHandle(h, FileDispositionInfoEx, &flags, sizeof(uint)) ? DeleteOutcome.Deleted : DeleteOutcome.InUse;
        }
        finally { CloseHandle(h); }
    }

    private static IntPtr Open(string path, uint access, uint share, uint flags)
    {
        string full = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
        fixed (char* p = full) return CreateFileW(p, access, share, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
    }

    private sealed class Held(IntPtr handle) : IDisposable
    {
        private IntPtr _handle = handle;
        public void Dispose() { var h = Interlocked.Exchange(ref _handle, IntPtr.Zero); if (h != IntPtr.Zero) CloseHandle(h); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;   // FILETIMEs are two DWORDs: a long would pad the struct
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFileW(char* name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(IntPtr handle, out ByHandleFileInformation info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(IntPtr handle, int infoClass, void* info, int size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
