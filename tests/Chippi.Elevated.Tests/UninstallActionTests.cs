// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

public sealed class UninstallActionTests
{
    private const string Msi = "{3F2504E0-4F89-11D3-9A0C-0305E82C3301}";
    private const string AppExe = @"C:\Program Files\Fixture App\unins000.exe";
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public FakeRegistry Registry = new();
        public FakeRecords Records = new();
        public FakeProbe Probe = new();
        public FakeHost Host = new();
        public FakeLauncher Launcher;
        public StepClock Clock = new(Start);
        public UninstallActions Actions;
        public Rig() { Launcher = new(Probe, Registry); Actions = new(Registry, Records, Probe, Launcher, Host, Clock); }
        public Task<AdminReply> Run(string key, int view = 64) =>
            Actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: view, KeyName: key), CancellationToken.None);
        public Rig WithApp(string key, UninstallKey entry, int view = 64) { Registry.Keys[(view, key)] = entry; return this; }
    }

    private static UninstallKey Entry(string? quiet = null, string? uninstall = @"""C:\Program Files\Fixture App\unins000.exe"" /SILENT", int? windowsInstaller = null,
        bool link = false, int? systemComponent = null, string? parent = null) =>
        new(link, systemComponent, parent, windowsInstaller, "Fixture App", "Fixture Co", @"C:\Program Files\Fixture App", quiet, uninstall);

    private static Rig Trusted(string key = "FixtureApp", UninstallKey? entry = null)
    {
        var rig = new Rig().WithApp(key, entry ?? Entry());
        rig.Probe.Trusting(AppExe);
        rig.Host.Files.Add(AppExe);
        return rig;
    }

    // ---- the key ----

    [Theory]
    [InlineData("link")] [InlineData("system")] [InlineData("parent")]
    public async Task A_link_a_system_component_and_an_update_are_refused_and_nothing_runs_or_is_recorded(string kind)
    {
        var entry = kind switch { "link" => Entry(link: true), "system" => Entry(systemComponent: 1), _ => Entry(parent: "Parent") };
        var rig = Trusted(entry: entry);
        var reply = await rig.Run("FixtureApp");
        Assert.Equal(Messages.UninstallRefused, reply.State);
        Assert.Null(reply.RecordId);
        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Records.All);
        Assert.Empty(rig.Probe.Opened);
    }

    [Fact]
    public async Task A_missing_key_and_a_key_with_no_command_are_refused()
    {
        var rig = Trusted();
        Assert.Equal(UninstallActions.NotFoundReason, (await rig.Run("Nothing")).Reason);
        rig.Registry.Keys[(64, "NoCommand")] = Entry(uninstall: null);
        Assert.Equal(UninstallActions.NoCommandReason, (await rig.Run("NoCommand")).Reason);
        rig.Registry.Keys[(64, "Blank")] = Entry(quiet: "  ", uninstall: " ");
        Assert.Equal(UninstallActions.NoCommandReason, (await rig.Run("Blank")).Reason);
        Assert.Empty(rig.Launcher.Started);
    }

    [Fact]
    public async Task The_view_picks_the_key()
    {
        var rig = Trusted();
        Assert.Equal(Messages.UninstallRefused, (await rig.Run("FixtureApp", view: 32)).State);   // only in the 64-bit view
        Assert.Equal(Messages.UninstallDone, (await rig.Run("FixtureApp", view: 64)).State);
    }

    // ---- the command ----

    [Fact]
    public async Task A_Windows_Installer_entry_runs_msiexec_from_System32_with_the_line_the_helper_builds()
    {
        string msiexec = @"C:\Windows\System32\msiexec.exe";
        var rig = new Rig().WithApp(Msi, Entry(uninstall: @"C:\evil.exe /x", windowsInstaller: 1));
        rig.Probe.Trusting(msiexec);
        var reply = await rig.Run(Msi);
        Assert.Equal(Messages.UninstallDone, reply.State);
        var started = Assert.Single(rig.Launcher.Started);
        Assert.Equal(msiexec, started.Application);
        Assert.Equal($"\"{msiexec}\" /x {Msi} /passive /norestart", started.CommandLine);
        Assert.Equal(Msi, Assert.Single(rig.Records.All).ProductCode);
    }

    [Fact]
    public async Task Only_an_MSI_flag_with_a_product_code_name_takes_the_msiexec_path()
    {
        var rig = Trusted();
        rig.Registry.Keys[(64, Msi)] = Entry(windowsInstaller: 0);                        // a product-code name without the flag
        rig.Registry.Keys[(64, "NotAGuid")] = Entry(windowsInstaller: 1);                 // the flag without a product-code name
        await rig.Run(Msi);
        await rig.Run("NotAGuid");
        Assert.All(rig.Launcher.Started, s => Assert.Equal(AppExe, s.Application));
    }

    [Fact]
    public async Task The_quiet_string_wins_over_the_plain_one()
    {
        var rig = Trusted(entry: Entry(quiet: @"""C:\Program Files\Fixture App\unins000.exe"" /VERYSILENT", uninstall: @"""C:\Program Files\Fixture App\unins000.exe"""));
        await rig.Run("FixtureApp");
        Assert.Equal($"\"{AppExe}\" /VERYSILENT", Assert.Single(rig.Launcher.Started).CommandLine);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Fixture App\unins000.exe"" /SILENT /x", @"C:\Program Files\Fixture App\unins000.exe", @"""C:\Program Files\Fixture App\unins000.exe"" /SILENT /x")]
    [InlineData(@"C:\Program Files\Fixture App\unins000.exe /SILENT", @"C:\Program Files\Fixture App\unins000.exe", @"""C:\Program Files\Fixture App\unins000.exe"" /SILENT")]
    [InlineData(@"C:\Program Files\Fixture App\unins000.exe", @"C:\Program Files\Fixture App\unins000.exe", @"""C:\Program Files\Fixture App\unins000.exe""")]
    [InlineData(@"%ProgramFiles%\Fixture App\unins000.exe /S", @"C:\Program Files\Fixture App\unins000.exe", @"""C:\Program Files\Fixture App\unins000.exe"" /S")]
    [InlineData(@"""%ProgramFiles%\Fixture App\unins000.exe"" /S", @"C:\Program Files\Fixture App\unins000.exe", @"""C:\Program Files\Fixture App\unins000.exe"" /S")]
    public void Quoted_unquoted_with_spaces_and_environment_paths_resolve(string text, string application, string commandLine)
    {
        var host = new FakeHost();
        host.Files.Add(@"C:\Program Files\Fixture App\unins000.exe");
        Assert.Equal((application, commandLine), UninstallCommand.Parse(text, host.Expand, host.FileExists));
    }

    [Fact]
    public void An_unquoted_path_is_resolved_left_to_right_at_each_exe_boundary()
    {
        var host = new FakeHost();
        // "C:\Tools\a.exe" exists and so does "C:\Tools\a.exe b\c.exe": the leftmost existing boundary wins, the rest are arguments.
        host.Files.Add(@"C:\Tools\a.exe"); host.Files.Add(@"C:\Tools\a.exe b\c.exe");
        Assert.Equal((@"C:\Tools\a.exe", @"""C:\Tools\a.exe"" b\c.exe /x"), UninstallCommand.Parse(@"C:\Tools\a.exe b\c.exe /x", host.Expand, host.FileExists));
        // With only the longer one present, the first boundary is skipped because that file doesn't exist.
        host.Files.Remove(@"C:\Tools\a.exe");
        Assert.Equal((@"C:\Tools\a.exe b\c.exe", @"""C:\Tools\a.exe b\c.exe"" /x"), UninstallCommand.Parse(@"C:\Tools\a.exe b\c.exe /x", host.Expand, host.FileExists));
        // ".exe" inside a longer word is no boundary.
        host.Files.Add(@"C:\Tools\x.exe2.exe");
        Assert.Equal(@"C:\Tools\x.exe2.exe", UninstallCommand.Parse(@"C:\Tools\x.exe2.exe", host.Expand, host.FileExists)!.Value.Application);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    [InlineData(@"""C:\Tools\a.exe /x")]            // no closing quote
    [InlineData(@"""C:\Tools\a.bat"" /x")]          // not an exe
    [InlineData(@"C:\Tools\missing.exe /x")]        // nothing on disk to resolve to
    [InlineData("msiexec /x {X}")]                  // no path at all
    public void Commands_that_cant_be_resolved_to_an_exe_are_refused(string? text) =>
        Assert.Null(UninstallCommand.Parse(text, new FakeHost().Expand, new FakeHost().FileExists));

    [Fact]
    public async Task A_command_that_resolves_to_nothing_is_a_refusal_and_runs_nothing()
    {
        var rig = Trusted(entry: Entry(uninstall: @"C:\somewhere\else.exe /x"));
        var reply = await rig.Run("FixtureApp");
        Assert.Equal((Messages.UninstallRefused, UninstallActions.CommandReason), (reply.State, reply.Reason));
        Assert.Empty(rig.Launcher.Started);
    }

    // ---- path rules ----

    [Theory]
    [InlineData(@"C:\Program Files\App\u.exe", true)]
    [InlineData(@"relative\u.exe", false)] [InlineData(@"u.exe", false)] [InlineData(@"C:u.exe", false)]
    [InlineData(@"C:\Program Files\..\u.exe", false)] [InlineData(@"C:\Program Files\.\u.exe", false)]
    [InlineData(@"\\?\C:\u.exe", false)] [InlineData(@"\\.\C:\u.exe", false)]
    [InlineData(@"\\server\share\u.exe", false)] [InlineData(@"C:\u.exe:stream", false)] [InlineData(@"C:\PROGRA~1\u.exe", false)]
    [InlineData("C:/Program Files/u.exe", false)] [InlineData(@"C:\Program Files\\u.exe", false)] [InlineData(@"C:\dir.\u.exe", false)]
    [InlineData(@"C:\a|b\u.exe", false)]
    public void Path_rules(string path, bool allowed) => Assert.Equal(allowed, PathRules.Refusal(path) is null);

    [Fact]
    public void Containment_compares_whole_segments_case_insensitively()
    {
        Assert.True(PathRules.Contains(@"C:\Program Files\App", @"C:\Program Files\app\bin\x.dll"));
        Assert.False(PathRules.Contains(@"C:\Program Files\App", @"C:\Program Files\AppData"));
        Assert.False(PathRules.Contains(@"C:\Program Files\App", @"C:\Program Files\App"));
        Assert.True(PathRules.Contains(@"C:\", @"C:\Program Files"));
    }

    [Fact]
    public async Task An_unsafe_path_in_the_command_is_refused_before_the_file_is_opened()
    {
        var rig = new Rig().WithApp("K", Entry(uninstall: @"""C:\Program Files\..\Windows\evil.exe"""));
        var reply = await rig.Run("K");
        Assert.Equal(Messages.UninstallRefused, reply.State);
        Assert.Empty(rig.Probe.Opened);
        Assert.Empty(rig.Launcher.Started);
    }

    // ---- the execution check ----

    private const uint FileWriteData = 0x2, FileAppendData = 0x4, FileDeleteChild = 0x40, Delete = 0x10000, WriteDac = 0x40000, WriteOwner = 0x80000, Read = 0x120089;

    private static (FakeProbe Probe, string File, string Folder, string Parent) Chain()
    {
        var probe = new FakeProbe().Trusting(AppExe);
        return (probe, AppExe, @"C:\Program Files\Fixture App", @"C:\Program Files");
    }

    [Theory]
    [InlineData(FileWriteData)] [InlineData(FileAppendData)] [InlineData(WriteDac)] [InlineData(WriteOwner)] [InlineData(Delete)]
    public void A_file_the_standard_token_can_rewrite_take_over_or_swap_is_refused(uint right)
    {
        var (probe, file, _, _) = Chain();
        probe.Nodes[file] = new(Granted: right | Read);
        var refusal = Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
        Assert.Contains("file", refusal.Message);
        Assert.Equal(0, probe.Live);   // a refusal releases everything it held
    }

    [Fact]
    public void A_file_the_standard_token_can_only_read_and_run_passes_and_holds_every_node_until_disposed()
    {
        var (probe, file, _, _) = Chain();
        probe.Nodes[file] = new(Granted: Read);
        using (ExecutionCheck.Open(file, probe))
            Assert.Equal(new[] { file, @"C:\Program Files\Fixture App", @"C:\Program Files", @"C:\" }.Length, probe.Live);
        Assert.Equal(0, probe.Live);
    }

    [Theory]
    [InlineData(FileWriteData)]     // a planted DLL beside the uninstaller (add file)
    [InlineData(FileAppendData)]    // add subdirectory
    [InlineData(FileDeleteChild)] [InlineData(WriteDac)] [InlineData(WriteOwner)] [InlineData(Delete)]
    public void A_folder_that_lets_the_standard_token_plant_or_swap_beside_the_file_is_refused(uint right)
    {
        var (probe, file, folder, _) = Chain();
        probe.Nodes[folder] = new(Granted: right | Read);
        var refusal = Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
        Assert.Contains("folder", refusal.Message);
        Assert.Equal(0, probe.Live);
    }

    [Theory]
    [InlineData(FileDeleteChild, true)] [InlineData(Delete, true)] [InlineData(WriteDac, true)] [InlineData(WriteOwner, true)]
    [InlineData(FileAppendData, false)]   // add-subdirectory: Windows lets every user add folders to C:\, and a new folder can't take an existing name
    [InlineData(FileWriteData, false)]    // add-file on a folder above is no way to change what is below it
    public void A_folder_above_refuses_what_could_rename_or_replace_it_but_not_what_only_adds_new_names(uint right, bool refused)
    {
        var (probe, file, _, parent) = Chain();
        probe.Nodes[parent] = new(Granted: right | Read);
        if (refused) Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
        else ExecutionCheck.Open(file, probe).Dispose();
    }

    [Fact]
    public void The_volume_root_may_grant_delete_but_nothing_that_edits_it()
    {
        var (probe, file, _, _) = Chain();
        probe.Nodes[@"C:\"] = new(Granted: Read | Delete | FileAppendData);
        ExecutionCheck.Open(file, probe).Dispose();
        probe.Nodes[@"C:\"] = new(Granted: Read | FileDeleteChild);
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void An_owner_that_is_not_Administrators_SYSTEM_or_TrustedInstaller_is_refused_anywhere_on_the_way(int level)
    {
        var (probe, file, folder, parent) = Chain();
        string path = new[] { file, folder, parent, @"C:\" }[level];
        probe.Nodes[path] = new(Owner: FakeProbe.User);
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
        probe.Nodes[path] = new(Owner: FakeProbe.System);
        ExecutionCheck.Open(file, probe).Dispose();
        probe.Nodes[path] = new(Owner: "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
        ExecutionCheck.Open(file, probe).Dispose();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void A_reparse_point_or_a_moved_path_anywhere_on_the_way_is_refused(int level)
    {
        var (probe, file, folder, parent) = Chain();
        string path = new[] { file, folder, parent, @"C:\" }[level];
        probe.Nodes[path] = new(Reparse: true);
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
        probe.Nodes[path] = new(Matches: false);
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
        Assert.Equal(0, probe.Live);
    }

    [Fact]
    public void A_file_or_folder_that_cant_be_opened_is_refused()
    {
        var (probe, file, folder, _) = Chain();
        probe.Nodes.Remove(folder);
        Assert.Throws<ExecutionRefusedException>(() => ExecutionCheck.Open(file, probe));
    }

    [Fact]
    public async Task A_refused_execution_check_runs_and_records_nothing()
    {
        var rig = Trusted();
        rig.Probe.Nodes[AppExe] = new(Granted: FileWriteData);
        var reply = await rig.Run("FixtureApp");
        Assert.Equal(Messages.UninstallRefused, reply.State);
        Assert.Null(reply.RecordId);
        Assert.DoesNotContain(@"C:\", reply.Reason);   // a refusal names no path
        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Records.All);
    }

    // ---- the run ----

    [Fact]
    public async Task Uninstall_writes_the_record_first_and_returns_its_id()
    {
        var rig = Trusted();
        UninstallRecord? atStart = null;
        rig.Launcher.AtStart = () => atStart = rig.Records.All.SingleOrDefault();
        rig.Launcher.RemoveKeyWhenDone = (64, "FixtureApp");
        var reply = await rig.Run("FixtureApp");
        Assert.Equal(Messages.UninstallDone, reply.State);
        Assert.Equal((0, true), (reply.ExitCode, reply.KeyGone));
        Assert.NotNull(atStart);   // written before the uninstaller started
        var record = Assert.Single(rig.Records.All);
        Assert.Equal(record.Id, reply.RecordId);
        Assert.Equal(new UninstallRecord(record.Id, 64, "FixtureApp", "Fixture App", "Fixture Co", @"C:\Program Files\Fixture App", null,
            @"C:\Program Files\Fixture App", Start.UtcDateTime), record);
    }

    [Fact]
    public async Task The_file_and_its_folders_are_held_while_the_process_starts_and_released_after()
    {
        var rig = Trusted();
        await rig.Run("FixtureApp");
        Assert.Equal(4, rig.Launcher.HeldAtStart);   // file, its folder, Program Files and the root
        Assert.Equal(0, rig.Launcher.HeldWhileRunning);
        Assert.Equal(AppExe, Assert.Single(rig.Launcher.Started).Application);
        Assert.Equal(@"C:\Program Files\Fixture App", rig.Launcher.Started[0].Folder);
    }

    [Theory]
    [InlineData(0, Messages.UninstallDone)] [InlineData(3010, Messages.UninstallRestart)] [InlineData(1, Messages.UninstallFailed)] [InlineData(1602, Messages.UninstallFailed)]
    public async Task The_exit_code_decides_the_state_and_the_key_says_whether_it_is_gone(int exit, string state)
    {
        var rig = Trusted();
        rig.Launcher.ExitCode = exit;
        var reply = await rig.Run("FixtureApp");
        Assert.Equal((state, exit, false), (reply.State, reply.ExitCode, reply.KeyGone));   // the key is still there
        Assert.NotNull(reply.RecordId);
    }

    [Fact]
    public async Task A_process_that_wont_start_is_a_failure_with_its_record_kept()
    {
        var rig = Trusted();
        rig.Launcher.StartError = new System.ComponentModel.Win32Exception(5);
        var reply = await rig.Run("FixtureApp");
        Assert.Equal((Messages.UninstallFailed, UninstallActions.StartFailedReason), (reply.State, reply.Reason));
        Assert.Equal(rig.Records.All.Single().Id, reply.RecordId);
        Assert.Equal(0, rig.Probe.Live);
    }

    [Fact]
    public async Task A_records_key_the_helper_cant_trust_refuses_the_uninstall_before_it_runs()
    {
        var rig = Trusted();
        rig.Records.Usable = false;   // a registry link, or a key the standard token could write
        var reply = await rig.Run("FixtureApp");
        Assert.Equal((Messages.UninstallRefused, UninstallActions.RecordsReason), (reply.State, reply.Reason));
        Assert.Empty(rig.Launcher.Started);
        Assert.Equal(0, rig.Probe.Live);
    }

    [Fact]
    public async Task A_record_that_cant_be_written_stops_the_uninstall()
    {
        var rig = Trusted();
        var records = new ThrowingRecords();
        var actions = new UninstallActions(rig.Registry, records, rig.Probe, rig.Launcher, rig.Host, rig.Clock);
        var reply = await actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 64, KeyName: "FixtureApp"), CancellationToken.None);
        Assert.Equal((Messages.UninstallFailed, UninstallActions.RecordFailedReason), (reply.State, reply.Reason));
        Assert.Empty(rig.Launcher.Started);
    }

    [Fact]
    public async Task An_unexpected_exception_answers_the_request_and_never_leaks_its_text()
    {
        var rig = Trusted();
        rig.Launcher.AtStart = () => throw new NotSupportedException(@"secret C:\path");
        var reply = await rig.Run("FixtureApp");
        Assert.Equal((Messages.UninstallFailed, UninstallActions.FailedReason), (reply.State, reply.Reason));
    }

    // ---- records ----

    private static UninstallRecord Old(DateTimeOffset when, string name = "x") =>
        new(Guid.NewGuid(), 64, name, name, null, null, null, null, when.UtcDateTime);

    [Fact]
    public void A_record_lasts_thirty_days_and_goes_at_thirty_one_on_the_injected_clock()
    {
        var rig = new Rig();
        var record = Old(Start);
        rig.Records.All.Add(record);
        rig.Clock.Now = Start.AddDays(30);
        rig.Actions.Maintain();
        Assert.Single(rig.Records.All);
        rig.Clock.Now = Start.AddDays(31);
        rig.Actions.Maintain();
        Assert.Empty(rig.Records.All);
    }

    [Fact]
    public void A_record_that_cant_be_read_expires_with_the_rest()
    {
        var rig = new Rig();
        rig.Records.All.Add(Old(DateTimeOffset.MinValue));
        rig.Actions.Maintain();
        Assert.Empty(rig.Records.All);
    }

    [Fact]
    public async Task The_fifty_first_record_drops_the_oldest()
    {
        var rig = Trusted();
        for (int i = 0; i < 50; i++) rig.Records.All.Add(Old(Start.AddHours(-100 + i), "r" + i));   // r0 is the oldest, all inside 30 days
        var reply = await rig.Run("FixtureApp");
        Assert.Equal(50, rig.Records.All.Count);
        Assert.DoesNotContain(rig.Records.All, r => r.KeyName == "r0");
        Assert.Contains(rig.Records.All, r => r.KeyName == "r1");
        Assert.Contains(rig.Records.All, r => r.Id == reply.RecordId);
    }

    [Fact]
    public async Task Expired_records_go_before_a_new_one_is_counted()
    {
        var rig = Trusted();
        for (int i = 0; i < 50; i++) rig.Records.All.Add(Old(Start.AddDays(-40), "e" + i));
        await rig.Run("FixtureApp");
        Assert.Single(rig.Records.All);
    }

    [Fact]
    public void Maintain_does_nothing_when_the_records_key_cant_be_trusted()
    {
        var rig = new Rig();
        rig.Records.All.Add(Old(Start.AddDays(-90)));
        rig.Records.Usable = false;
        rig.Actions.Maintain();
        Assert.Single(rig.Records.All);   // never touches a key it refuses to use
    }

    [Fact]
    public async Task A_request_for_another_verb_or_a_bad_key_name_is_refused_by_the_actions_too()
    {
        var rig = Trusted();
        var reply = await rig.Actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 64, KeyName: @"A\B"), CancellationToken.None);
        Assert.Equal(Messages.UninstallRefused, reply.State);
        reply = await rig.Actions.RunAsync(new AdminRequest(Guid.NewGuid(), "UpdatePackage", "a.b", "1.0"), CancellationToken.None);
        Assert.Equal(Messages.UninstallRefused, reply.State);
        Assert.Empty(rig.Launcher.Started);
    }

    private sealed class ThrowingRecords : IRecordStore
    {
        public bool Prepare() => true;
        public IReadOnlyList<UninstallRecord> List() => [];
        public UninstallRecord? Get(Guid id) => null;
        public void Write(UninstallRecord record) => throw new IOException("disk full");
        public void Delete(Guid id) { }
    }
}
