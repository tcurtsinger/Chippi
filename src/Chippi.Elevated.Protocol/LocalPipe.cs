// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated.Protocol;

public static class LocalPipe
{
    public static NamedPipeServerStream Create(string name)
    {
        if (!Messages.PipeNameAllowed(name)) throw new ArgumentException("Invalid pipe name.", nameof(name));
        using var identity = WindowsIdentity.GetCurrent();
        var logon = LogonSid(identity.AccessToken);
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(A;;GA;;;{logon.Value})", 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe($"\\\\.\\pipe\\{name}", 0x40080003, 8, 1, Messages.MaxBytes, Messages.MaxBytes, 0, ref attributes);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { LocalFree(descriptor); }
    }

    public static uint ClientId(SafePipeHandle handle) => GetNamedPipeClientProcessId(handle, out uint id) ? id : throw new Win32Exception();
    public static uint ServerId(SafePipeHandle handle) => GetNamedPipeServerProcessId(handle, out uint id) ? id : throw new Win32Exception();

    private static SecurityIdentifier LogonSid(SafeAccessTokenHandle token)
    {
        // WindowsIdentity.Groups intentionally excludes the SE_GROUP_LOGON_ID group.
        _ = GetTokenInformation(token, 28, 0, 0, out int length);
        if (Marshal.GetLastWin32Error() != 122 || length is <= 0 or > 65536) throw new Win32Exception();
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, 28, buffer, length, out _) || Marshal.ReadInt32(buffer) != 1) throw new Win32Exception();
            var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer, nint.Size));
            if (!sid.Value.StartsWith("S-1-5-5-", StringComparison.Ordinal)) throw new InvalidOperationException("No interactive logon.");
            return sid;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public nint Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint length);
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint mode, uint pipeMode, uint instances, int outBuffer, int inBuffer, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int information, nint buffer, int length, out int returned);
}
