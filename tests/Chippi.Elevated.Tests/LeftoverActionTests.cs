// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;
using static Chippi.Elevated.Tests.LeftoverRig;

namespace Chippi.Elevated.Tests;

public sealed class LeftoverActionTests
{
    private const string Kept = Messages.ItemKept;
    private static LeftoverResult Only(AdminReply reply) => Assert.Single(reply.Results!);

    // ---- the record is the identity ----

    [Fact]
    public async Task A_safe_item_inside_the_install_location_goes_to_the_recycle_bin()
    {
        var rig = new LeftoverRig();
        string folder = rig.Exists(@"C:\Program Files\Mozilla Firefox\browser");
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\updater.ini");
        var reply = await rig.Run(Folder(folder), File(file));
        Assert.Equal(Messages.LeftoversDone, reply.State);
        Assert.All(reply.Results!, r => Assert.Equal(Messages.ItemRemoved, r.State));
        Assert.Equal([folder, file], rig.System.Recycled);
        Assert.Equal(0, rig.Probe.Live);   // every hold is released
    }

    [Fact]
    public async Task The_install_folder_itself_and_the_uninstallers_folder_are_the_records()
    {
        var rig = new LeftoverRig();
        rig.Exists(LeftoverRig.Firefox);
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Folder(LeftoverRig.Firefox))).State);
        var other = new LeftoverRig(installLocation: @"C:\Program Files\Fixture", uninstallerFolder: @"C:\Program Files (x86)\Fixture Helper");
        string helper = other.Exists(@"C:\Program Files (x86)\Fixture Helper\tool.dat");
        Assert.Equal(Messages.ItemRemoved, Only(await other.Run(File(helper))).State);
    }

    [Fact]
    public async Task Firefox_with_thunderbird_installed_refuses_thunderbirds_folder_and_the_shared_mozilla_folder()
    {
        var rig = new LeftoverRig();
        rig.Apps.Hklm("Mozilla Thunderbird 128 (x64 en-US)", "Mozilla Thunderbird", @"C:\Program Files\Mozilla\Thunderbird");
        string thunderbird = rig.Exists(@"C:\Program Files\Mozilla\Thunderbird");
        string inside = rig.Exists(@"C:\Program Files\Mozilla\Thunderbird\thunderbird.exe");
        string shared = rig.Exists(@"C:\Program Files\Mozilla");
        var reply = await rig.Run(Folder(thunderbird), File(inside), Folder(shared, Messages.ClassCheckFirst));
        Assert.All(reply.Results!, r => Assert.Equal((Kept, "Kept: shared with Mozilla Thunderbird"), (r.State, r.Reason)));
        Assert.Empty(rig.System.Recycled);
    }

    [Fact]
    public async Task An_item_that_holds_another_apps_folder_is_refused_even_when_it_is_not_in_the_install_location()
    {
        var rig = new LeftoverRig();
        rig.Apps.Hklm("Vendor.Suite", "Vendor Suite", @"C:\Tools\Vendor\Suite");
        string vendor = rig.Exists(@"C:\Tools\Vendor");
        var reply = await rig.Run(Folder(vendor, Messages.ClassCheckFirst));
        Assert.Equal("Kept: shared with Vendor Suite", Only(reply).Reason);
    }

    [Fact]
    public async Task Another_apps_install_folder_comes_from_its_install_location_its_uninstaller_or_its_icon_and_from_the_owners_own_apps()
    {
        var rig = new LeftoverRig();
        rig.Apps.Hklm("A", "App A", null, uninstall: @"""C:\Tools\A\unins000.exe"" /S")
            .Hklm("B", "App B", null, view: 32, icon: @"C:\Tools\B\b.exe,0")
            .Hkcu("C", "App C", @"C:\Tools\C")
            .Hklm("D", "App D", null, uninstall: @"%ProgramFiles%\D\unins.exe /S");
        foreach (var (folder, name) in new[] { (@"C:\Tools\A", "App A"), (@"C:\Tools\B", "App B"), (@"C:\Tools\C", "App C"), (@"C:\Program Files\D", "App D") })
        {
            rig.Exists(folder);
            Assert.Equal($"Kept: shared with {name}", Only(await rig.Run(Folder(folder, Messages.ClassCheckFirst))).Reason);
        }
    }

    [Fact]
    public async Task The_app_being_removed_is_not_another_app_even_if_its_own_key_is_still_there()
    {
        var rig = new LeftoverRig();
        rig.Apps.Hklm(rig.Record.KeyName, "Mozilla Firefox", LeftoverRig.Firefox);
        string folder = rig.Exists(@"C:\Program Files\Mozilla Firefox\browser");
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Folder(folder))).State);
        // The same key name in the other view is another app.
        rig.Apps.Hklm(rig.Record.KeyName, "Firefox 32", LeftoverRig.Firefox, view: 32);
        Assert.Equal("Kept: shared with Firefox 32", Only(await rig.Run(Folder(folder))).Reason);
    }

    [Fact]
    public async Task A_safe_item_outside_the_install_location_is_refused_but_a_ticked_check_first_one_is_taken()
    {
        var rig = new LeftoverRig();
        string data = rig.Exists(@"C:\ProgramData\Mozilla Firefox");
        Assert.Equal("Kept: not this app's", Only(await rig.Run(Folder(data))).Reason);
        Assert.Empty(rig.System.Recycled);
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Folder(data, Messages.ClassCheckFirst))).State);
        Assert.Equal([data], rig.System.Recycled);
    }

    [Fact]
    public async Task A_check_first_item_the_owner_did_not_tick_is_refused_and_so_is_an_unticked_safe_one()
    {
        var rig = new LeftoverRig();
        string data = rig.Exists(@"C:\ProgramData\Mozilla Firefox");
        string inside = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        var reply = await rig.Run(Folder(data, Messages.ClassCheckFirst, ticked: false), File(inside, ticked: false));
        Assert.All(reply.Results!, r => Assert.Equal((Kept, "Kept: not selected"), (r.State, r.Reason)));
        Assert.Empty(rig.System.Recycled);
        Assert.DoesNotContain(rig.Probe.Opened, p => p.Contains("Mozilla"));
    }

    [Fact]
    public async Task An_install_location_that_is_a_protected_root_or_holds_one_covers_nothing()
    {
        foreach (string location in new[] { @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\Test", @"C:\ProgramData" })
        {
            var rig = new LeftoverRig(installLocation: location, uninstallerFolder: null);
            string child = rig.Exists(location + @"\Something");
            Assert.Equal("Kept: not this app's", Only(await rig.Run(Folder(child))).Reason);
        }
        // Nor does a missing one.
        var none = new LeftoverRig(installLocation: null, uninstallerFolder: null);
        Assert.Equal("Kept: not this app's", Only(await none.Run(Folder(none.Exists(@"C:\Program Files\Mozilla Firefox\x")))).Reason);
    }

    [Fact]
    public async Task No_record_an_unknown_id_an_expired_record_and_a_dropped_one_keep_every_item()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        var keyItem = Key(@"Software\Mozilla Firefox");
        rig.Keys.Has(64, @"Software\Mozilla Firefox");

        void AllKept(AdminReply reply)
        {
            Assert.Equal(Messages.LeftoversDone, reply.State);
            Assert.Equal(2, reply.Results!.Length);
            Assert.All(reply.Results, r => Assert.Equal((Kept, "Kept: no uninstall record"), (r.State, r.Reason)));
            Assert.Empty(rig.System.Recycled); Assert.Empty(rig.Keys.Deleted); Assert.Empty(rig.Launcher.Started);
        }

        AllKept(await rig.RunFor(Guid.NewGuid(), File(file), keyItem));          // unknown id

        rig.Clock.Now = Start + TimeSpan.FromDays(30);                           // 30 days is still the record's life
        Assert.Equal(Messages.ItemRemoved, (await rig.Run(File(file))).Results![0].State);
        rig.System.Existing.Add(file);
        rig.System.Recycled.Clear();
        rig.Clock.Now = Start + TimeSpan.FromDays(31);                           // 31 days: expired
        AllKept(await rig.Run(File(file), keyItem));

        rig.Clock.Now = Start;
        Assert.Equal(Messages.RecordDropped, (await rig.Drop()).State);
        AllKept(await rig.Run(File(file), keyItem));                             // dropped
    }

    [Fact]
    public async Task An_unusable_records_key_keeps_every_item()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        rig.Records.Usable = false;
        var reply = await rig.Run(File(file));
        Assert.Equal(("Kept: no uninstall record", Kept), (Only(reply).Reason, Only(reply).State));
        Assert.Equal(Messages.LeftoversFailed, (await rig.Drop()).State);
        Assert.Empty(rig.System.Recycled);
    }

    [Fact]
    public async Task A_record_written_by_one_session_is_read_back_by_another_helper_days_later()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        rig.Clock.Now = Start + TimeSpan.FromDays(9);
        var later = new LeftoverActions(rig.Records, rig.Apps, rig.Probe, rig.Keys, rig.System, rig.Launcher, rig.Clock);   // a new helper process over the same store
        var reply = await later.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.DeleteLeftovers, RecordId: rig.Record.Id, Items: [File(file)]), CancellationToken.None);
        Assert.Equal(Messages.ItemRemoved, Only(reply).State);
    }

    [Fact]
    public async Task Drop_deletes_the_record_and_is_idempotent()
    {
        var rig = new LeftoverRig();
        var other = new UninstallRecord(Guid.NewGuid(), 64, "Other", null, null, null, null, null, Start.UtcDateTime);
        rig.Records.All.Add(other);
        Assert.Equal(Messages.RecordDropped, (await rig.Drop()).State);
        Assert.Equal([other.Id], rig.Records.All.Select(r => r.Id));
        Assert.Equal(Messages.RecordDropped, (await rig.Drop()).State);
        Assert.Equal(Messages.RecordDropped, (await rig.Drop(Guid.NewGuid())).State);
        Assert.Single(rig.Records.All);
    }

    // ---- the path rules ----

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Firefox\..\Mozilla Firefox\a.dll")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\..")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\.")]
    [InlineData(@"C:\PROGRA~1\Mozilla Firefox\a.dll")]
    [InlineData(@"C:\Program Files\MOZILL~1\a.dll")]
    [InlineData(@"\\?\C:\Program Files\Mozilla Firefox\a.dll")]
    [InlineData(@"\\.\C:\Program Files\Mozilla Firefox\a.dll")]
    [InlineData(@"\\server\share\Mozilla Firefox\a.dll")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\a.dll:hidden")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\a.dll::$DATA")]
    [InlineData(@"Mozilla Firefox\a.dll")]
    [InlineData(@"\Program Files\Mozilla Firefox\a.dll")]
    [InlineData(@"C:Program Files\Mozilla Firefox\a.dll")]
    [InlineData(@"C:\")]
    public async Task An_unsafe_path_is_refused_before_anything_is_opened(string path)
    {
        var rig = new LeftoverRig();
        rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        int before = rig.Probe.Opened.Count;
        var item = new LeftoverItem(Messages.KindFile, path, null, Messages.ClassSafe, true);
        var reply = await rig.Run(item);
        Assert.Equal((Kept, "Kept: protected folder"), (Only(reply).State, Only(reply).Reason));
        Assert.Equal(before, rig.Probe.Opened.Count);
        Assert.Empty(rig.System.Recycled);
    }

    [Fact]
    public async Task Containment_is_whole_segments_so_app_does_not_cover_appdata()
    {
        var rig = new LeftoverRig(installLocation: @"C:\Program Files\App", uninstallerFolder: null);
        string appData = rig.Exists(@"C:\Program Files\AppData");
        string appDataFile = rig.Exists(@"C:\Program Files\AppData\x.dat");
        string appFile = rig.Exists(@"C:\Program Files\App\x.dat");
        var reply = await rig.Run(Folder(appData), File(appDataFile), File(appFile));
        Assert.Equal(["Kept: not this app's", "Kept: not this app's", null], reply.Results!.Select(r => r.Reason));
        Assert.Equal(Messages.ItemRemoved, reply.Results![2].State);
        // An install location that only starts with the same letters doesn't make another app's folder shared either.
        var apps = new LeftoverRig(installLocation: @"C:\Tools\Mine", uninstallerFolder: null);
        apps.Apps.Hklm("Other", "Other", @"C:\Tools\Min");
        string mine = apps.Exists(@"C:\Tools\Mine\a.dat");
        Assert.Equal(Messages.ItemRemoved, Only(await apps.Run(File(mine))).State);
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\Program Files\Common Files")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\Test")]
    [InlineData(@"C:\Users\Test\AppData\Local")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Users\Test\AppData\Local\Chippi")]
    [InlineData(@"C:\Users\Test\AppData\Local\ChippiApp\app-1.0\Chippi.exe")]
    [InlineData(@"C:\Users\Test\AppData\Local\Chippi\leftovers-backup")]
    public async Task The_fixed_deny_list_is_refused_whatever_class_and_ticks_say(string path)
    {
        var rig = new LeftoverRig();
        rig.Exists(path);
        foreach (var cls in new[] { Messages.ClassSafe, Messages.ClassCheckFirst })
        {
            var reply = await rig.Run(new LeftoverItem(Messages.KindFolder, path, null, cls, true));
            Assert.Equal((Kept, "Kept: protected folder"), (Only(reply).State, Only(reply).Reason));
        }
        Assert.Empty(rig.System.Recycled);
    }

    [Fact]
    public async Task A_root_folders_children_are_not_denied_but_a_folder_that_holds_a_protected_folder_is()
    {
        var rig = new LeftoverRig();
        // Common Files' own children belong to apps; Program Files itself and everything that holds it don't.
        string child = rig.Exists(@"C:\Program Files\Common Files\Mozilla Firefox Shared");
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Folder(child, Messages.ClassCheckFirst))).State);
        string appData = rig.Exists(@"C:\Users\Test\AppData");
        Assert.Equal("Kept: protected folder", Only(await rig.Run(Folder(appData, Messages.ClassCheckFirst))).Reason);
        string chippiHolder = rig.Exists(@"C:\Users\Test\AppData\Local");
        Assert.Equal("Kept: protected folder", Only(await rig.Run(Folder(chippiHolder, Messages.ClassCheckFirst))).Reason);
    }

    // ---- links and holds ----

    [Fact]
    public async Task The_folders_above_are_held_while_the_item_goes_and_released_after_it()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\browser\omni.ja");
        int heldAtRecycle = -1;
        rig.System.OnRecycle = _ => heldAtRecycle = rig.Probe.Live;
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(File(file))).State);
        // C:\, Program Files, Mozilla Firefox, browser: four folders held, the item's own handle already closed.
        Assert.Equal(4, heldAtRecycle);
        Assert.Equal(0, rig.Probe.Live);
    }

    [Fact]
    public async Task A_reparse_point_as_the_item_or_above_it_or_a_path_that_isnt_the_requested_one_is_refused()
    {
        var rig = new LeftoverRig();
        string link = rig.Exists(@"C:\Program Files\Mozilla Firefox\link");
        rig.Probe.Nodes[link] = new(Reparse: true);
        string aboveLink = @"C:\Program Files\Mozilla Firefox\link\x.dat";
        rig.Probe.Trusting(aboveLink); rig.Probe.Nodes[@"C:\Program Files\Mozilla Firefox\link"] = new(Reparse: true); rig.System.Existing.Add(aboveLink);
        string moved = rig.Exists(@"C:\Program Files\Mozilla Firefox\moved.dat");
        rig.Probe.Nodes[moved] = new(Matches: false);
        string folderMoved = rig.Exists(@"C:\Program Files\Mozilla Firefox\sub\deep.dat");
        rig.Probe.Nodes[@"C:\Program Files\Mozilla Firefox\sub"] = new(Matches: false);
        var reply = await rig.Run(Folder(link), File(aboveLink), File(moved), File(folderMoved));
        Assert.All(reply.Results!, r => Assert.Equal((Kept, "Kept: changed since the scan"), (r.State, r.Reason)));
        Assert.Empty(rig.System.Recycled);
        Assert.Equal(0, rig.Probe.Live);
    }

    [Fact]
    public async Task An_item_already_gone_is_removed_and_one_that_cant_be_opened_but_is_there_is_failed()
    {
        var rig = new LeftoverRig();
        // The folder above is gone, so the item is: nothing to do.
        var gone = await rig.Run(File(@"C:\Program Files\Mozilla Firefox\never.dat"));
        Assert.Equal(Messages.ItemRemoved, Only(gone).State);
        // The path exists but Windows won't open it.
        rig.System.Existing.Add(@"C:\Program Files\Mozilla Firefox\locked.dat");
        rig.Probe.Trusting(@"C:\Program Files\Mozilla Firefox");
        Assert.Equal(Messages.ItemFailed, Only(await rig.Run(File(@"C:\Program Files\Mozilla Firefox\locked.dat"))).State);
        Assert.Empty(rig.System.Recycled);
    }

    // ---- what the owner could delete himself, and what a user owns ----

    [Fact]
    public async Task An_item_the_standard_token_could_delete_is_yours_and_is_not_touched()
    {
        var rig = new LeftoverRig();
        string a = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dat");
        rig.Probe.Nodes[a] = new(Granted: ExecutionCheck.Delete);                                 // DELETE on the item
        string b = rig.Exists(@"C:\Program Files\Mozilla Firefox\sub\b.dat");
        rig.Probe.Nodes[@"C:\Program Files\Mozilla Firefox\sub"] = new(Granted: 0x40);            // FILE_DELETE_CHILD on its folder
        string c = rig.Exists(@"C:\Program Files\Mozilla Firefox\c.dat");
        rig.Probe.Nodes[c] = new(Owner: FakeProbe.User, Granted: ExecutionCheck.Delete);          // a user's own file is his to delete
        var reply = await rig.Run(File(a), File(b), File(c));
        Assert.All(reply.Results!, r => Assert.Equal((Messages.ItemYours, null), (r.State, r.Reason)));
        Assert.Empty(rig.System.Recycled);
    }

    [Fact]
    public async Task An_admin_only_item_owned_by_a_standard_user_was_planted_and_is_refused()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\planted.dll");
        rig.Probe.Nodes[file] = new(Owner: FakeProbe.User);
        var reply = await rig.Run(File(file));
        Assert.Equal((Kept, "Kept: protected folder"), (Only(reply).State, Only(reply).Reason));
        foreach (string owner in new[] { FakeProbe.System, FakeProbe.Admins, "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" })
        {
            rig.Probe.Nodes[file] = new(Owner: owner);
            rig.System.Existing.Add(file);
            Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(File(file))).State);
        }
    }

    [Fact]
    public async Task Recycle_outcomes_map_to_in_use_and_failed_and_a_vanished_item_counts_as_removed()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        rig.System.Outcome = RecycleOutcome.InUse;
        Assert.Equal(Messages.ItemInUse, Only(await rig.Run(File(file))).State);
        rig.System.Outcome = RecycleOutcome.Failed;
        Assert.Equal(Messages.ItemFailed, Only(await rig.Run(File(file))).State);
        rig.System.Existing.Remove(file);
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(File(file))).State);   // gone by the time it answered
    }

    [Fact]
    public async Task One_items_exception_fails_that_item_and_the_batch_goes_on()
    {
        var rig = new LeftoverRig();
        string a = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dat");
        string b = rig.Exists(@"C:\Program Files\Mozilla Firefox\b.dat");
        rig.System.OnRecycle = p => { if (p == a) throw new InvalidOperationException(@"C:\secret\path"); };
        var reply = await rig.Run(File(a), File(b));
        Assert.Equal([Messages.ItemFailed, Messages.ItemRemoved], reply.Results!.Select(r => r.State));
        Assert.All(reply.Results!, r => Assert.Null(r.Reason));
        Assert.Equal(0, rig.Probe.Live);
    }

    // ---- registry keys ----

    [Fact]
    public async Task A_key_the_record_names_is_exported_first_then_deleted()
    {
        var rig = new LeftoverRig();
        string key = @"Software\Mozilla\Mozilla Firefox";
        rig.Keys.Has(64, key);
        var order = new List<string>();
        rig.Launcher.AtStart += () => order.Add("export");
        rig.Keys.OnDelete = _ => order.Add("delete");
        var reply = await rig.Run(Key(key));
        Assert.Equal(Messages.ItemRemoved, Only(reply).State);
        Assert.Equal(["export", "delete"], order);
        Assert.Equal([(64, key)], rig.Keys.Deleted);
        var started = Assert.Single(rig.Launcher.Started);
        Assert.Equal(@"C:\Windows\System32\reg.exe", started.Application);
        Assert.StartsWith(@"""C:\Windows\System32\reg.exe"" export ""HKLM\Software\Mozilla\Mozilla Firefox"" ""C:\Users\Test\AppData\Local\Chippi\leftovers-backup\", started.CommandLine);
        Assert.EndsWith(@".reg"" /y /reg:64", started.CommandLine);
        Assert.Contains(rig.Record.Id.ToString("N"), started.CommandLine);
        Assert.Equal(0, rig.Probe.Live);   // reg.exe's file and folders, and the backup folder's levels, all released
    }

    [Fact]
    public async Task The_32_bit_view_exports_with_its_own_flag_and_each_view_is_named()
    {
        var rig = new LeftoverRig(view: 32);
        string key = @"Software\Mozilla\Mozilla Firefox";
        rig.Keys.Has(32, key);
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Key(key, view: 32))).State);
        Assert.EndsWith("/reg:32", rig.Launcher.Started[0].CommandLine);
        Assert.Equal([(32, key)], rig.Keys.Deleted);
        // The key isn't in the 64-bit view, so the same name there is already gone.
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Key(key, view: 64))).State);
        Assert.Single(rig.Keys.Deleted);
    }

    [Fact]
    public async Task The_records_own_uninstall_key_is_named_by_the_record_in_its_own_view_only()
    {
        var rig = new LeftoverRig();
        string own = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mozilla Firefox 130.0 (x64 en-US)";
        rig.Keys.Has(64, own).Has(64, own + @"\Child").Has(32, own);
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Key(own))).State);
        Assert.Equal("Kept: not this app's", Only(await rig.Run(Key(own, view: 32))).Reason);   // the record's key isn't in the other view
        rig.Keys.Has(64, own);
        Assert.Equal(Messages.ItemRemoved, Only(await rig.Run(Key(own + @"\Child"))).State);
    }

    [Theory]
    [InlineData(@"System\CurrentControlSet\Services\Foo")]
    [InlineData(@"SYSTEM\Mozilla")]
    [InlineData(@"Hardware\Mozilla Firefox")]
    [InlineData(@"Software")]
    [InlineData(@"Software\Microsoft")]
    [InlineData(@"Software\Microsoft\Windows")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(@"Software\Microsoft\Cryptography")]
    [InlineData(@"Software\Classes\Mozilla Firefox")]
    [InlineData(@"Software\Policies\Mozilla Firefox")]
    [InlineData(@"Software\Chippi\Elevated\UninstallRecords")]
    [InlineData(@"Software\Chippi")]
    [InlineData(@"Software\Wow6432Node\Mozilla Firefox")]
    [InlineData(@"Software\Mozilla Firefox\..\..\System")]
    [InlineData(@"Software\Mozilla Firefox\\x")]
    [InlineData(@"Software\Mozilla ""Firefox""")]
    [InlineData(@"HKEY_LOCAL_MACHINE\Software\Mozilla Firefox")]
    public async Task A_key_outside_hklm_software_or_on_the_deny_list_is_refused_and_never_probed(string path)
    {
        var rig = new LeftoverRig();
        rig.Keys.Has(64, path);
        var reply = await rig.Run(Key(path));
        Assert.Equal(Kept, Only(reply).State);
        Assert.True(Only(reply).Reason is "Kept: protected folder" or "Kept: shared with Mozilla Thunderbird", Only(reply).Reason);
        Assert.Empty(rig.Keys.Probed); Assert.Empty(rig.Keys.Deleted); Assert.Empty(rig.Launcher.Started);
    }

    [Fact]
    public async Task A_key_the_record_does_not_name_is_refused_and_a_key_holding_another_apps_uninstall_key_is_shared()
    {
        var rig = new LeftoverRig();
        rig.Apps.Hklm("Mozilla Thunderbird 128", "Mozilla Thunderbird", @"C:\Program Files\Mozilla Thunderbird");
        rig.Keys.Has(64, @"Software\Vendor\Other").Has(64, @"Software\Mozilla").Has(64, @"Software\Mozilla Firefox 130");
        var reply = await rig.Run(Key(@"Software\Vendor\Other"), Key(@"Software\Mozilla"), Key(@"Software\Mozilla Firefox 130"), Key(@"Software\Mozilla Firefox\Extra\Deeper"));
        Assert.All(reply.Results!.Take(3), r => Assert.Equal((Kept, "Kept: not this app's"), (r.State, r.Reason)));   // Software\Mozilla is the publisher's, not the record's
        Assert.Equal(Messages.ItemRemoved, reply.Results![3].State);                                                     // under Software\Mozilla Firefox, which the record names (and is gone)
        Assert.Empty(rig.Keys.Deleted);

        // The Uninstall folder holds every app's key.
        string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        var shared = await rig.Run(Key(uninstall), Key(uninstall + @"\Mozilla Thunderbird 128"));
        Assert.Equal(Messages.ItemKept, shared.Results![1].State);
        Assert.Equal("Kept: shared with Mozilla Thunderbird", shared.Results[1].Reason);
        Assert.Equal("Kept: protected folder", shared.Results[0].Reason);   // denied outright, as a root
    }

    [Fact]
    public async Task A_publisher_and_display_name_make_the_vendor_key_the_record_names_and_other_apps_overlapping_it_make_it_shared()
    {
        var rig = new LeftoverRig();
        string vendor = @"Software\Mozilla\Mozilla Firefox";
        rig.Keys.Has(64, vendor).Has(32, vendor);
        Assert.All((await rig.Run(Key(vendor), Key(vendor, view: 32))).Results!, r => Assert.Equal(Messages.ItemRemoved, r.State));
        rig.Apps.Hklm("Mozilla Thunderbird", "Mozilla Thunderbird", @"C:\Program Files\Mozilla Thunderbird");
        // Another app's uninstall key never hides inside the vendor key, but its own key is its own.
        rig.Apps.Hklm("Mozilla Firefox", "x", null);
        rig.Keys.Has(64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mozilla Firefox");
        Assert.Equal("Kept: shared with x", Only(await rig.Run(Key(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mozilla Firefox"))).Reason);
    }

    [Fact]
    public async Task A_key_with_a_link_under_it_is_refused_and_a_key_the_standard_token_could_delete_is_yours()
    {
        var rig = new LeftoverRig();
        string linked = @"Software\Mozilla\Mozilla Firefox";
        string yours = @"Software\Mozilla Firefox";
        rig.Keys.Has(64, linked, unsafeTree: true).Has(64, yours, standardCanDelete: true);
        var reply = await rig.Run(Key(linked), Key(yours));
        Assert.Equal(("Kept: changed since the scan", Messages.ItemYours), (reply.Results![0].Reason, reply.Results[1].State));
        Assert.Empty(rig.Keys.Deleted); Assert.Empty(rig.Launcher.Started);
    }

    [Fact]
    public async Task A_key_that_cant_be_backed_up_is_not_deleted()
    {
        string key = @"Software\Mozilla\Mozilla Firefox";
        // reg.exe fails.
        var rig = new LeftoverRig(); rig.Keys.Has(64, key); rig.Launcher.ExitCode = 1;
        var failed = Only(await rig.Run(Key(key)));
        Assert.Equal((Messages.ItemFailed, "Backup failed"), (failed.State, failed.Reason));
        Assert.Empty(rig.Keys.Deleted);
        // reg.exe says 0 but wrote nothing.
        var empty = new LeftoverRig(); empty.Keys.Has(64, key); empty.Launcher.AtStart = null;
        Assert.Equal("Backup failed", Only(await empty.Run(Key(key))).Reason);
        Assert.Empty(empty.Keys.Deleted);
        // reg.exe won't start.
        var nostart = new LeftoverRig(); nostart.Keys.Has(64, key); nostart.Launcher.StartError = new System.ComponentModel.Win32Exception(5);
        Assert.Equal("Backup failed", Only(await nostart.Run(Key(key))).Reason);
        // reg.exe fails the execution check: a user could write it.
        var untrusted = new LeftoverRig(); untrusted.Keys.Has(64, key); untrusted.Probe.Nodes[@"C:\Windows\System32\reg.exe"] = new(Granted: ExecutionCheck.FileWriteData);
        Assert.Equal("Backup failed", Only(await untrusted.Run(Key(key))).Reason);
        Assert.Empty(untrusted.Launcher.Started); Assert.Empty(untrusted.Keys.Deleted);
        Assert.Equal(0, untrusted.Probe.Live);
    }

    [Fact]
    public async Task A_backup_folder_that_is_a_link_or_cant_be_made_stops_the_export()
    {
        string key = @"Software\Mozilla\Mozilla Firefox";
        var linked = new LeftoverRig(); linked.Keys.Has(64, key);
        linked.Probe.Nodes[@"C:\Users\Test\AppData\Local\Chippi"] = new(Reparse: true);
        Assert.Equal("Backup failed", Only(await linked.Run(Key(key))).Reason);
        Assert.Empty(linked.Launcher.Started); Assert.Empty(linked.Keys.Deleted); Assert.Equal(0, linked.Probe.Live);
        var moved = new LeftoverRig(); moved.Keys.Has(64, key);
        moved.Probe.Nodes[@"C:\Users\Test\AppData"] = new(Matches: false);
        Assert.Equal("Backup failed", Only(await moved.Run(Key(key))).Reason);
        var cant = new LeftoverRig(); cant.Keys.Has(64, key); cant.System.CanCreate = false;
        Assert.Equal("Backup failed", Only(await cant.Run(Key(key))).Reason);
        Assert.Empty(cant.Keys.Deleted);
    }

    [Fact]
    public async Task The_backup_folder_is_created_once_level_by_level_and_a_delete_failure_is_failed()
    {
        var rig = new LeftoverRig();
        string a = @"Software\Mozilla Firefox", b = @"Software\Mozilla\Mozilla Firefox";
        rig.Keys.Has(64, a).Has(64, b);
        rig.Keys.DeleteResult = false;
        rig.Probe.Trusting(@"C:\Users\Test\AppData\Local\Chippi");
        var reply = await rig.Run(Key(a), Key(b));
        Assert.All(reply.Results!, r => Assert.Equal(Messages.ItemFailed, r.State));
        Assert.Equal([@"C:\Users\Test\AppData\Local\Chippi\leftovers-backup"], rig.System.Created);   // only the levels that didn't exist, once
        Assert.Equal(2, rig.Launcher.Started.Count);
        Assert.NotEqual(rig.Launcher.Started[0].CommandLine, rig.Launcher.Started[1].CommandLine);
    }

    [Fact]
    public async Task A_batch_mixes_files_and_keys_and_keeps_the_order_of_the_request()
    {
        var rig = new LeftoverRig();
        string file = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll");
        rig.Keys.Has(64, @"Software\Mozilla Firefox");
        var reply = await rig.Run(Key(@"Software\Mozilla Firefox"), File(@"C:\Elsewhere\x.dat"), File(file), Key(@"Software\Nope"));
        Assert.Equal([Messages.ItemRemoved, Kept, Messages.ItemRemoved, Kept], reply.Results!.Select(r => r.State));
        Assert.Equal("Kept: not this app's", reply.Results![1].Reason);
        Assert.Equal("Kept: not this app's", reply.Results[3].Reason);
    }

    // ---- bounds ----

    [Fact]
    public async Task Two_hundred_items_against_three_hundred_apps_read_the_apps_once_and_stay_fast()
    {
        var rig = new LeftoverRig();
        for (int i = 0; i < 300; i++) rig.Apps.Hklm($"App{i}", $"App {i}", $@"C:\Tools\App{i}", uninstall: $@"C:\Tools\App{i}\unins.exe");
        var items = new List<LeftoverItem>();
        for (int i = 0; i < 200; i++) items.Add(File(rig.Exists($@"C:\Program Files\Mozilla Firefox\dir{i % 10}\file{i}.dat")));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var reply = await rig.Run([.. items]);
        timer.Stop();
        Assert.All(reply.Results!, r => Assert.Equal(Messages.ItemRemoved, r.State));
        Assert.Equal(1, rig.Apps.Reads);
        Assert.True(timer.ElapsedMilliseconds < 3000, $"{timer.ElapsedMilliseconds} ms");
        Assert.Equal(0, rig.Probe.Live);
    }

    [Fact]
    public async Task A_request_the_protocol_refuses_is_failed_without_touching_anything()
    {
        var rig = new LeftoverRig();
        var empty = await rig.Actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.DeleteLeftovers, RecordId: rig.Record.Id, Items: []), CancellationToken.None);
        Assert.Equal((Messages.LeftoversFailed, "Invalid request"), (empty.State, empty.Reason));
        var wrongVerb = await rig.Actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 64, KeyName: "x"), CancellationToken.None);
        Assert.Equal(Messages.LeftoversFailed, wrongVerb.State);
        Assert.Empty(rig.Probe.Opened);
    }
}
