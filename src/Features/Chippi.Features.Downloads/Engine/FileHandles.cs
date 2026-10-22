// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Features.Downloads.Engine;

/// <summary>
/// File operations bound to one open handle, so the file that was checked is the file that is
/// renamed or deleted. The handle holds DELETE access and does not share it: while it is open
/// nobody else can rename, delete or replace the file at that path (Karl's review of c6199fe).
/// </summary>
internal static class FileHandles
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint Delete = 0x00010000;
    private const uint OpenExisting = 3;
    private const uint CreateNew = 1;
    private const uint SequentialScan = 0x08000000;
    private const int FileRenameInfoClass = 3;
    private const int FileDispositionInfoClass = 4;
    private const int ErrorNotSameDevice = 17;

    /// <summary>Opens a file to move it: others may still read it, but not write, rename or delete it.</summary>
    public static SafeFileHandle OpenToMove(string path) =>
        Open(path, GenericRead | Delete, FileShare.Read, OpenExisting);

    /// <summary>Creates a new file, failing if anything is already there. Not shared at all.</summary>
    public static SafeFileHandle CreateExclusive(string path) =>
        Open(path, GenericRead | GenericWrite | Delete, FileShare.None, CreateNew);

    /// <summary>Renames the open file to <paramref name="destination"/>, never replacing anything. False when it is on another volume.</summary>
    public static bool TryRename(SafeFileHandle handle, string destination)
    {
        // FILE_RENAME_INFO: a flags word, the root directory handle, the name length, then the name.
        int rootOffset = IntPtr.Size;
        int lengthOffset = rootOffset + IntPtr.Size;
        int nameOffset = lengthOffset + sizeof(uint);
        byte[] name = System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(destination));
        var info = new byte[nameOffset + name.Length + sizeof(char)];
        BitConverter.TryWriteBytes(info.AsSpan(lengthOffset), (uint)name.Length);
        name.CopyTo(info, nameOffset);
        if (SetFileInformationByHandle(handle, FileRenameInfoClass, info, (uint)info.Length))
            return true;
        int error = Marshal.GetLastPInvokeError();
        if (error == ErrorNotSameDevice)
            return false;
        throw Failure(error, destination);
    }

    /// <summary>Deletes the open file when the handle closes.</summary>
    public static void DeleteOnClose(SafeFileHandle handle)
    {
        var info = new byte[sizeof(int)];
        info[0] = 1;
        if (!SetFileInformationByHandle(handle, FileDispositionInfoClass, info, (uint)info.Length))
            throw Failure(Marshal.GetLastPInvokeError(), null);
    }

    public static string Sha256(SafeFileHandle handle)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16];
        long offset = 0;
        int read;
        while ((read = RandomAccess.Read(handle, buffer, offset)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            offset += read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static void Copy(SafeFileHandle from, SafeFileHandle to)
    {
        var buffer = new byte[1 << 16];
        long offset = 0;
        int read;
        while ((read = RandomAccess.Read(from, buffer, offset)) > 0)
        {
            RandomAccess.Write(to, buffer.AsSpan(0, read), offset);
            offset += read;
        }
        RandomAccess.FlushToDisk(to);
    }

    private static SafeFileHandle Open(string path, uint access, FileShare share, uint disposition)
    {
        var handle = CreateFileW(Path.GetFullPath(path), access, share, IntPtr.Zero, disposition, SequentialScan, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Failure(error, path);
        }
        return handle;
    }

    private static Exception Failure(int error, string? path)
    {
        string message = new Win32Exception(error).Message;
        return error switch
        {
            2 or 3 => new FileNotFoundException(message, path),
            5 => new UnauthorizedAccessException(message),
            _ => new IOException(message, unchecked((int)0x80070000) | error),
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, byte[] info, uint size);
}
