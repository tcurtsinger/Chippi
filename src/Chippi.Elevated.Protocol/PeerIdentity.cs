// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated.Protocol;

/// <summary>Holds the peer process open, preventing its PID being reused during this session.</summary>
public sealed class PeerIdentity : IDisposable
{
    private readonly SafeProcessHandle _process;
    private readonly SafeAccessTokenHandle? _standardToken;
    public SafeAccessTokenHandle StandardToken => _standardToken ?? throw new InvalidOperationException("Peer is elevated.");
    private PeerIdentity(SafeProcessHandle process, SafeAccessTokenHandle? token) { _process = process; _standardToken = token; }

    public static PeerIdentity Open(uint pid, string expectedPath, bool elevated)
    {
        var process = OpenProcess(0x1000, false, pid);
        try
        {
            if (process.IsInvalid) throw new Win32Exception();
            var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length)
                || !GetExitCodeProcess(process, out uint code) || code != 259
                || !ProcessIdToSessionId(pid, out uint session) || !ProcessIdToSessionId((uint)Environment.ProcessId, out uint ownSession)
                || session != ownSession || !string.Equals(path.ToString(), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Unexpected admin peer.");
            if (!OpenProcessToken(process, elevated ? 8u : 0xAu, out var token)) throw new Win32Exception();
            using (token)
            {
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                using var own = WindowsIdentity.GetCurrent();
                if (identity.User is null || own.User is null || !identity.User.Equals(own.User) || Elevated(token) != elevated)
                    throw new UnauthorizedAccessException("Unexpected admin identity.");
                if (elevated) return new PeerIdentity(process, null);
                if (!DuplicateToken(token, 2, out var duplicate)) throw new Win32Exception();
                return new PeerIdentity(process, duplicate);
            }
        }
        catch { process.Dispose(); throw; }
    }

    public static bool CurrentIsElevated()
    {
        // A filtered child can query its token even when its process DACL denies PROCESS_ALL_ACCESS.
        if (!OpenCurrentProcessToken(GetCurrentProcess(), 8, out var token)) throw new Win32Exception();
        using (token) return Elevated(token);
    }
    private static bool Elevated(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, 20, out int elevated, 4, out _)) throw new Win32Exception();
        return elevated != 0;
    }
    public void Dispose() { _standardToken?.Dispose(); _process.Dispose(); }
    public static bool RestrictDllSearch() => SetDefaultDllDirectories(0x800);

    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenCurrentProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int information, out int value, int length, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateToken(SafeAccessTokenHandle token, int level, out SafeAccessTokenHandle duplicate);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint pid, out uint session);
}
