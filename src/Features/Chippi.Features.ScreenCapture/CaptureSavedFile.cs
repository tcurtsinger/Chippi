// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Features.ScreenCapture;

public sealed record CaptureFileIdentity(uint Volume, uint High, uint Low);

internal static class CaptureSavedFile
{
    public static CaptureFileIdentity? Identity(string path)
    {
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(file, out var info)
                ? new(info.Volume, info.IndexHigh, info.IndexLow) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static bool Recycle(RecentCapture capture)
    {
        if (capture.SavedFile is not { } path || capture.SavedIdentity is not { } expected || Identity(path) != expected)
            return false;
        // Recheck the file identity immediately before asking the shell to recycle it. Old records without an identity refuse.
        var operation = new FileOperation { Function = 3, From = path + '\0' + '\0', Flags = 0x40 | 0x10 | 0x04 | 0x400 };
        return SHFileOperation(ref operation) == 0 && !operation.Aborted && !File.Exists(path);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public nint Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public nint Mappings;
        public string? Title;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref FileOperation operation);
}
