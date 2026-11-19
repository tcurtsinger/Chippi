// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;
using Chippi.Features.Uninstaller;

namespace Chippi.Features.Uninstaller.Tests;

public sealed class RemovalTests : IDisposable
{
    private readonly LeftoverTests _scans = new();
    public void Dispose() => _scans.Dispose();

    // The leftover fixture is built by LeftoverTests (its own temp tree); removals run through its engine's fakes.
    private Fixture F => _scans.Fx;

    private static string[] Ids(LeftoverScan scan, params string[] paths) =>
        [.. scan.Items.Where(i => paths.Contains(i.Path, StringComparer.OrdinalIgnoreCase)).Select(i => i.Id)];

    private static string[] Removable(LeftoverScan scan) => [.. scan.Removable.Select(i => i.Id)];

    private async Task<(LeftoverScan Scan, UninstallerEngine Engine)> Prepare(bool helperRecord = true)
    {
        var record = _scans.Mozilla(helperRecord);
        var engine = F.Engine();
        return ((await engine.ScanAsync(record))!, engine);
    }

    [Fact]
    public async Task Ticked_admin_items_go_to_the_helper_with_its_record_id_and_the_rest_to_the_standard_path()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        var run = await engine.RemoveAsync(scan, Removable(scan));
        var (id, sent) = Assert.Single(F.Admin.LeftoverCalls);
        Assert.Equal(scan.Record.HelperRecordId, id);
        Assert.Equal(
            [(AdminLeftoverKind.Folder, F.ProgramFiles("Mozilla Firefox"), AdminLeftoverClass.Safe, (AdminRegistryView?)null),
             (AdminLeftoverKind.RegistryKey, @"Software\Mozilla\Firefox", AdminLeftoverClass.CheckFirst, AdminRegistryView.Registry64)],
            sent.Select(i => (i.Kind, i.Path, i.Class, i.View)));
        Assert.All(sent, i => Assert.True(i.Ticked));
        // The owner's own folders and keys never went to the helper: the Recycle Bin and reg.exe took them.
        Assert.Contains(F.Roaming("Mozilla", "Firefox"), F.Recycler.Recycled);
        Assert.Contains(F.Local("Mozilla", "Firefox"), F.Recycler.Recycled);
        Assert.DoesNotContain(F.ProgramFiles("Mozilla Firefox"), F.Recycler.Recycled);
        Assert.All(run.Results, r => Assert.True(r.Ok));
        Assert.Equal(["leftovers 2", "drop", "end"], F.Admin.Calls);   // all removed: the helper's record is dropped and its session ends
        Assert.Contains(F.Admin.Dropped, d => d == scan.Record.HelperRecordId);
    }

    [Fact]
    public async Task Shared_and_unticked_items_are_never_sent_and_never_removed()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        string shared = F.Data("Mozilla");
        var run = await engine.RemoveAsync(scan, [.. Ids(scan, F.Roaming("Mozilla", "Firefox"), shared)]);
        Assert.Empty(F.Admin.LeftoverCalls);
        Assert.True(Directory.Exists(shared));
        Assert.True(Directory.Exists(F.ProgramFiles("Mozilla Firefox")));   // not ticked
        Assert.Equal(["Kept: shared with " + LeftoverTests.ThunderbirdName, "Removed"], run.Results.Select(r => r.Text).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(shared, F.Recycler.Recycled);
        Assert.DoesNotContain("drop", F.Admin.Calls);   // not everything went: the record stays for later
    }

    [Fact]
    public async Task What_the_helper_returns_is_shown_in_its_words_and_Yours_goes_to_the_standard_path()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        F.Admin.Leftover = i => i.Kind == AdminLeftoverKind.Folder
            ? new AdminLeftoverResult(i, AdminLeftoverState.Yours)
            : new AdminLeftoverResult(i, AdminLeftoverState.Kept, "Kept: changed since the scan");
        var run = await engine.RemoveAsync(scan, Ids(scan, F.ProgramFiles("Mozilla Firefox"), @"HKLM\Software\Mozilla\Firefox"));
        Assert.Contains(F.ProgramFiles("Mozilla Firefox"), F.Recycler.Recycled);   // Yours: the owner can delete it himself
        Assert.Equal(["Kept: changed since the scan", "Removed"], run.Results.Select(r => r.Text).Order(StringComparer.Ordinal));
        Assert.Equal(["Kept: changed since the scan"], run.Results.Where(r => !r.Ok).Select(r => r.Text));
        Assert.True(F.Keys.Exists(RegHive.Machine64, @"Software\Mozilla\Firefox\132.0.1 (x64 en-US)"));   // a kept key stays
    }

    [Theory]
    [InlineData(AdminLeftoverState.InUse, "In use")]
    [InlineData(AdminLeftoverState.Failed, "Remove failed")]
    [InlineData(AdminLeftoverState.NeedsAdmin, "Needs admin")]
    public async Task Each_helper_state_has_its_word(AdminLeftoverState state, string text)
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        F.Admin.Leftover = i => new AdminLeftoverResult(i, state);
        var run = await engine.RemoveAsync(scan, Ids(scan, F.ProgramFiles("Mozilla Firefox")));
        Assert.Equal(text, Assert.Single(run.Results).Text);
        Assert.Contains("end", F.Admin.Calls);
    }

    [Fact]
    public async Task A_registry_key_is_exported_before_it_is_deleted_and_the_backup_is_kept()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        var run = await engine.RemoveAsync(scan, Ids(scan, @"HKCU\Software\Mozilla\Firefox"));
        Assert.Equal(["export HKCU\\Software\\Mozilla\\Firefox", "delete User Software\\Mozilla\\Firefox"], F.Log);
        Assert.Equal(F.BackupFolder, run.BackupFolder);
        string file = Assert.Single(Directory.GetFiles(F.BackupFolder));
        Assert.StartsWith("Mozilla Firefox (x64 en-US)-2026-11-18", Path.GetFileName(file));
        Assert.EndsWith(".reg", file);
        Assert.False(F.Keys.Exists(RegHive.User, @"Software\Mozilla\Firefox"));
        Assert.Equal("Removed", Assert.Single(run.Results).Text);
    }

    [Fact]
    public async Task A_failed_export_keeps_the_key_and_a_failed_delete_is_Remove_failed()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        F.User.ExportOk = false;
        var run = await engine.RemoveAsync(scan, Ids(scan, @"HKCU\Software\Mozilla\Firefox"));
        Assert.Equal("Remove failed", Assert.Single(run.Results).Text);
        Assert.DoesNotContain(F.Log, l => l.StartsWith("delete", StringComparison.Ordinal));
        Assert.True(F.Keys.Exists(RegHive.User, @"Software\Mozilla\Firefox"));
        Assert.Null(run.BackupFolder);

        F.User.ExportOk = true;
        F.Keys.DeleteOk = false;
        var again = await engine.RemoveAsync(scan, Ids(scan, @"HKCU\Software\Mozilla\Firefox"));
        Assert.Equal("Remove failed", Assert.Single(again.Results).Text);
        Assert.True(F.Keys.Exists(RegHive.User, @"Software\Mozilla\Firefox"));
    }

    [Fact]
    public async Task A_folder_too_big_for_the_bin_is_Remove_failed_and_never_deleted_for_good_and_a_held_one_is_In_use()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        F.Recycler.TooBig.Add(F.Roaming("Mozilla", "Firefox"));
        F.Recycler.Held.Add(F.Local("Mozilla", "Firefox"));
        var run = await engine.RemoveAsync(scan, Ids(scan, F.Roaming("Mozilla", "Firefox"), F.Local("Mozilla", "Firefox")));
        Assert.Equal(["In use", "Remove failed"], run.Results.Select(r => r.Text).Order(StringComparer.Ordinal));
        Assert.True(Directory.Exists(F.Roaming("Mozilla", "Firefox")));
        Assert.True(Directory.Exists(F.Local("Mozilla", "Firefox")));
        Assert.Empty(F.Recycler.Recycled);
    }

    [Fact]
    public async Task A_link_inside_a_folder_is_removed_as_a_link_before_the_bin_and_its_target_is_untouched()
    {
        var record = _scans.Mozilla();
        string keep = F.WriteFile(F.Outside("precious", "keep.bin"), 1000);
        string inner = F.Junction(Path.Combine(F.Roaming("Mozilla", "Firefox"), "profile-link"), F.Outside("precious"));
        using var engine = F.Engine();
        var scan = (await engine.ScanAsync(record))!;
        var run = await engine.RemoveAsync(scan, Ids(scan, F.Roaming("Mozilla", "Firefox")));
        Assert.Equal("Removed", Assert.Single(run.Results).Text);   // the fake bin throws if a link was left inside
        Assert.True(File.Exists(keep));
        Assert.False(Directory.Exists(inner));
    }

    [Fact]
    public async Task A_link_that_is_itself_an_item_is_removed_as_a_link_and_never_goes_to_the_bin()
    {
        var record = _scans.Mozilla();
        string keep = F.WriteFile(F.Outside("precious", "keep.bin"), 1000);
        string link = F.Junction(F.Roaming("Firefox"), F.Outside("precious"));
        using var engine = F.Engine();
        var scan = (await engine.ScanAsync(record))!;
        var run = await engine.RemoveAsync(scan, Ids(scan, link));
        Assert.Equal("Removed", Assert.Single(run.Results).Text);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(keep));
        Assert.Empty(F.Recycler.Recycled);
    }

    [Fact]
    public async Task Without_the_helpers_record_nothing_admin_is_sent_and_a_machine_key_is_kept()
    {
        var (scan, engine) = await Prepare(helperRecord: false);
        using var _ = engine;
        var run = await engine.RemoveAsync(scan, Ids(scan, @"HKLM\Software\Mozilla\Firefox", F.Roaming("Mozilla", "Firefox")));
        Assert.Empty(F.Admin.LeftoverCalls);
        Assert.Equal(["Kept: no uninstall record", "Removed"], run.Results.Select(r => r.Text).Order(StringComparer.Ordinal));
        Assert.True(F.Keys.Exists(RegHive.Machine64, @"Software\Mozilla\Firefox\132.0.1 (x64 en-US)"));
    }

    [Fact]
    public async Task Removing_everything_marks_the_record_Cleaned_and_a_dismissed_record_is_forgotten_with_the_helpers()
    {
        var (scan, engine) = await Prepare();
        using var _ = engine;
        F.Records().Put(scan.Record);
        await engine.RemoveAsync(scan, Removable(scan));
        Assert.True(engine.Recent.Single().Cleaned);

        await engine.DismissAsync(scan.Record);
        Assert.Empty(engine.Recent);
        Assert.Equal(2, F.Admin.Dropped.Count);
    }
}
