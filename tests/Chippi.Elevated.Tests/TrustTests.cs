// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Chippi.Elevated.Protocol;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated.Tests;

public sealed class TrustTests
{
    [Fact]
    public void Peer_path_and_elevation_must_match_the_process()
    {
        bool elevated = PeerIdentity.CurrentIsElevated();
        using var actual = PeerIdentity.Open((uint)Environment.ProcessId, Environment.ProcessPath!, elevated);
        Assert.Throws<UnauthorizedAccessException>(() => PeerIdentity.Open((uint)Environment.ProcessId, @"C:\wrong\Chippi.exe", elevated));
        Assert.Throws<UnauthorizedAccessException>(() => PeerIdentity.Open((uint)Environment.ProcessId, Environment.ProcessPath!, !elevated));
    }

    [Theory]
    [InlineData(@"relative.exe")] [InlineData(@"C:\folder\..\app.exe")] [InlineData(@"\\server\app.exe")]
    [InlineData(@"\\?\C:\app.exe")] [InlineData(@"C:\app.exe:stream")] [InlineData(@"C:\PROGRA~1\app.exe")]
    public void Unsafe_paths_are_refused_without_opening_a_target(string path)
    {
        using var token = new SafeAccessTokenHandle(0);
        Assert.Throws<UnauthorizedAccessException>(() => TrustedExecutable.Open(path, token));
    }

    [Fact]
    public void User_owned_executables_and_junction_paths_are_refused_without_execution()
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-admin-trust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string exe = Path.Combine(root, "fixture.exe"); File.WriteAllText(exe, "not executable");
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(DuplicateToken(identity.AccessToken, 2, out var token));
        using (token)
        {
            Assert.Throws<UnauthorizedAccessException>(() => TrustedExecutable.Open(exe, token));
            string alias = Path.Combine(root, "alias");
            using var junction = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                Arguments = $"/c mklink /J \"{alias}\" \"{Environment.SystemDirectory}\"", RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            junction.WaitForExit(); Assert.Equal(0, junction.ExitCode);
            Assert.Throws<UnauthorizedAccessException>(() => TrustedExecutable.Open(Path.Combine(alias, "kernel32.dll"), token));
            // Remove only the junction entry, never traverse its target.
            Directory.Delete(alias);
        }
        File.Delete(exe); Directory.Delete(root);
    }

    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateToken(SafeAccessTokenHandle token, int level, out SafeAccessTokenHandle duplicate);
}
