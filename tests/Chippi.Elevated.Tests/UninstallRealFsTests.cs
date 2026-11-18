// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated.Tests;

/// <summary>
/// The real file system on a temp tree, with no admin needed and nothing that depends on running unelevated (CI is an admin without UAC):
/// links, holds and the owner check as Windows reports them. Nothing here starts a process or touches the registry.
/// </summary>
public sealed class UninstallRealFsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-uninstall-fs-" + Guid.NewGuid().ToString("N"));
    private readonly SafeAccessTokenHandle _token;
    private readonly NativeFsProbe _probe;

    public UninstallRealFsTests()
    {
        Directory.CreateDirectory(_root);
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(DuplicateToken(identity.AccessToken, 2, out _token));
        _probe = new NativeFsProbe(_token);
    }

    public void Dispose()
    {
        try { foreach (var junction in Directory.EnumerateDirectories(_root)) if (new DirectoryInfo(junction).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(junction); } catch (IOException) { }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        _token.Dispose();
    }

    [Fact]
    public void A_plain_file_and_folder_report_themselves_and_a_user_made_one_is_refused_by_the_execution_check()
    {
        string exe = Path.Combine(_root, "fixture.exe"); File.WriteAllText(exe, "not executable");
        using (var file = _probe.Open(exe, file: true))
        {
            Assert.NotNull(file);
            Assert.False(file.IsReparsePoint);
            Assert.True(file.PathMatches);
            Assert.NotEmpty(file.OwnerSid);
        }
        using (var folder = _probe.Open(_root, file: false)) { Assert.NotNull(folder); Assert.False(folder.IsReparsePoint); Assert.True(folder.PathMatches); }
        // A temp folder is writable by whoever made it: refused whether Windows says the owner is a user or the token can write.
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(exe, _probe));
        Assert.Null(_probe.Open(Path.Combine(_root, "missing.exe"), file: true));
    }

    [Fact]
    public void A_junction_is_a_reparse_point_and_is_refused_on_the_way_to_a_file()
    {
        string alias = Path.Combine(_root, "alias");
        using var junction = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{alias}\" \"{Environment.SystemDirectory}\"",
        })!;
        junction.WaitForExit(); Assert.Equal(0, junction.ExitCode);
        using (var node = _probe.Open(alias, file: false)) { Assert.NotNull(node); Assert.True(node.IsReparsePoint); }
        // Through the junction the final path is System32's, not the requested one.
        using (var through = _probe.Open(Path.Combine(alias, "kernel32.dll"), file: true)) { Assert.NotNull(through); Assert.False(through.PathMatches); }
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(Path.Combine(alias, "kernel32.dll"), _probe));
        Directory.Delete(alias);   // the junction entry only, never its target
        Assert.True(File.Exists(Path.Combine(Environment.SystemDirectory, "kernel32.dll")));
    }

    [Fact]
    public void A_held_folder_cant_be_renamed_and_a_held_file_cant_be_written_or_deleted()
    {
        string folder = Path.Combine(_root, "held"); Directory.CreateDirectory(folder);
        string exe = Path.Combine(folder, "u.exe"); File.WriteAllText(exe, "x");
        using (_probe.Open(folder, file: false))
            Assert.ThrowsAny<IOException>(() => Directory.Move(folder, Path.Combine(_root, "moved")));
        Directory.Move(folder, Path.Combine(_root, "moved"));   // released: free again
        exe = Path.Combine(_root, "moved", "u.exe");
        using (_probe.Open(exe, file: true))
        {
            Assert.ThrowsAny<IOException>(() => File.Delete(exe));
            Assert.ThrowsAny<IOException>(() => File.AppendAllText(exe, "y"));
            Assert.Equal("x", File.ReadAllText(exe));   // reading is shared
        }
        File.Delete(exe);
    }

    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateToken(SafeAccessTokenHandle token, int level, out SafeAccessTokenHandle duplicate);
}
