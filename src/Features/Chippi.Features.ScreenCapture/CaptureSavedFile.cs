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
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return null;
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(file, out var info)
                ? info.IndexHigh != 0 || info.IndexLow != 0 ? new(info.Volume, info.IndexHigh, info.IndexLow) : null : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static bool Recycle(RecentCapture capture)
    {
        if (capture.SavedFile is not { } path || capture.SavedIdentity is not { } expected || Identity(path) != expected)
            return false;
        object? operation = null;
        ShellItem? item = null;
        try
        {
            operation = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), throwOnError: true)!);
            var files = (FileOperation)operation!;
            // Recycle only, no fallback to permanent deletion, and no error/elevation UI.
            // https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags
            Marshal.ThrowExceptionForHR(files.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x400 | 0x10 | 0x04));
            var iid = typeof(ShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out item));
            if (Identity(path) != expected) return false;
            Marshal.ThrowExceptionForHR(files.DeleteItem(item, 0));
            Marshal.ThrowExceptionForHR(files.PerformOperations());
            Marshal.ThrowExceptionForHR(files.GetAnyOperationsAborted(out bool aborted));
            return !aborted && !File.Exists(path);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            if (item != null) Marshal.FinalReleaseComObject(item);
            if (operation != null) Marshal.FinalReleaseComObject(operation);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
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
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out ShellItem item);
}