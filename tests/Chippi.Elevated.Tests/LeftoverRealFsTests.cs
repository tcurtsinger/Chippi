// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Chippi.Elevated.Protocol;
using Microsoft.Win32.SafeHandles;
using static Chippi.Elevated.Tests.LeftoverRig;

namespace Chippi.Elevated.Tests;

/// <summary>
/// DeleteLeftovers' file checks against the real file system on a temp tree: real junctions and real holds through <see cref="NativeFsProbe"/>. Only what a
/// user could never fake in CI (the owner and the token's rights) is stood in for. The Recycle Bin is a fake that touches nothing, and no registry or process is involved.
/// </summary>
public sealed class LeftoverRealFsTests : IDisposable
{
    private readonly string _root;
    private readonly SafeAccessTokenHandle _token;
    private readonly NativeFsProbe _real;

    public LeftoverRealFsTests()
    {
        // GetTempPath can come back as an 8.3 path (RUNNER~1), which the path rules refuse by design.
        string temp = Path.Combine(Path.GetTempPath(), "chippi-leftovers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        _root = LongPath(temp);
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(DuplicateToken(identity.AccessToken, 2, out _token));
        _real = new NativeFsProbe(_token);
        Assert.Null(PathRules.Refusal(_root));
    }

    public void Dispose()
    {
        try { foreach (var dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList()) Directory.Delete(dir); } catch (IOException) { }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        _token.Dispose();
    }

    /// <summary>The real probe, but the item reads as an admin-owned file the standard token can't delete: the one thing a CI machine can't be.</summary>
    private sealed class AdminOnly(IFsProbe inner) : IFsProbe
    {
        public IHeldNode? Open(string path, bool file) => inner.Open(path, file) is { } node ? new Node(node) : null;
        private sealed class Node(IHeldNode node) : IHeldNode
        {
            public bool IsReparsePoint => node.IsReparsePoint;
            public bool PathMatches => node.PathMatches;
            public string OwnerSid => FakeProbe.Admins;
            public uint StandardGranted => 0;
            public void Dispose() => node.Dispose();
        }
    }

    private (LeftoverActions Actions, FakeLeftoverSystem System, UninstallRecord Record) Rig(string installLocation)
    {
        var fakeProbe = new FakeProbe();
        var system = new FakeLeftoverSystem(fakeProbe);
        system.Trees.Add(Path.Combine(_root, "System32"));
        var record = new UninstallRecord(Guid.NewGuid(), 64, "Fixture", "Fixture", "Co", installLocation, null, null, Start.UtcDateTime);
        var records = new FakeRecords(); records.All.Add(record);
        var actions = new LeftoverActions(records, new FakeApps(), new AdminOnly(_real), new FakeKeys(), system, new FakeLauncher(fakeProbe), new StepClock(Start));
        return (actions, system, record);
    }

    private static Task<AdminReply> Run(LeftoverActions actions, UninstallRecord record, params LeftoverItem[] items) =>
        actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.DeleteLeftovers, RecordId: record.Id, Items: items), CancellationToken.None);

    private static void Junction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task A_junction_as_the_item_or_above_it_pointing_at_a_system32_stand_in_is_refused_and_nothing_is_touched()
    {
        string app = Path.Combine(_root, "app"); Directory.CreateDirectory(app);
        string system32 = Path.Combine(_root, "System32"); Directory.CreateDirectory(system32);
        string core = Path.Combine(system32, "core.dll"); System.IO.File.WriteAllText(core, "keep");
        string link = Path.Combine(app, "link");
        Junction(link, system32);
        var (actions, system, record) = Rig(app);

        var reply = await Run(actions, record, Folder(link), File(Path.Combine(link, "core.dll")), Folder(link, Messages.ClassCheckFirst));
        Assert.All(reply.Results!, r => Assert.Equal((Messages.ItemKept, "Kept: changed since the scan"), (r.State, r.Reason)));
        Assert.Empty(system.Recycled);
        Assert.True(System.IO.File.Exists(core));
        Assert.Equal("keep", System.IO.File.ReadAllText(core));
    }

    [Fact]
    public async Task A_real_item_in_real_folders_goes_to_the_recycler_with_its_folders_held_and_its_own_handle_closed()
    {
        string app = Path.Combine(_root, "app"); string data = Path.Combine(app, "data"); Directory.CreateDirectory(data);
        string item = Path.Combine(data, "x.dat"); System.IO.File.WriteAllText(item, "x");
        var (actions, system, record) = Rig(app);
        system.Existing.Add(item);
        Exception? renameError = null; Exception? deleteError = null;
        system.OnRecycle = path =>
        {
            Assert.Equal(item, path);
            // The folders above are held without FILE_SHARE_DELETE: they can't be renamed or swapped for a junction while the item goes.
            renameError = Record.Exception(() => Directory.Move(data, Path.Combine(app, "moved")));
            renameError ??= Record.Exception(() => Directory.Move(app, Path.Combine(_root, "moved-app")));
            // The item's own handle is closed, or the recycler couldn't delete it.
            deleteError = Record.Exception(() => System.IO.File.Delete(item));
        };
        var reply = await Run(actions, record, File(item));
        Assert.Equal(Messages.ItemRemoved, Assert.Single(reply.Results!).State);
        Assert.IsAssignableFrom<IOException>(renameError);
        Assert.Null(deleteError);
        // Released afterwards.
        Directory.Move(data, Path.Combine(app, "moved"));
    }

    [Fact]
    public async Task A_folder_item_is_not_held_by_the_helper_when_the_recycler_runs()
    {
        string app = Path.Combine(_root, "app"); string data = Path.Combine(app, "data"); Directory.CreateDirectory(data);
        System.IO.File.WriteAllText(Path.Combine(data, "x.dat"), "x");
        var (actions, system, record) = Rig(app);
        system.Existing.Add(data);
        Exception? deleteError = null;
        system.OnRecycle = _ => deleteError = Record.Exception(() => Directory.Delete(data, recursive: true));
        var reply = await Run(actions, record, Folder(data));
        Assert.Equal(Messages.ItemRemoved, Assert.Single(reply.Results!).State);
        Assert.Null(deleteError);
        Assert.False(Directory.Exists(data));
    }

    [Fact]
    public async Task A_path_that_doesnt_exist_is_already_gone()
    {
        string app = Path.Combine(_root, "app"); Directory.CreateDirectory(app);
        var (actions, system, record) = Rig(app);
        var reply = await Run(actions, record, File(Path.Combine(app, "nothing.dat")), Folder(Path.Combine(app, "no", "deeper")));
        Assert.All(reply.Results!, r => Assert.Equal(Messages.ItemRemoved, r.State));
        Assert.Empty(system.Recycled);
    }

    private static string LongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetLongPathName(path, buffer, buffer.Capacity);
        return length == 0 || length >= buffer.Capacity ? path : buffer.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, int length);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateToken(SafeAccessTokenHandle token, int level, out SafeAccessTokenHandle duplicate);
}
