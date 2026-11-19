// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;
using Chippi.Features.Uninstaller;

namespace Chippi.Features.Uninstaller.Tests;

/// <summary>The engine's edges from the review: what a shortcut may claim, two views of one key, a vanished item, where removal runs, retries, "Running" and stale entries.</summary>
public sealed class HardeningTests : IDisposable
{
    private readonly LeftoverTests _scans = new();
    private Fixture F => _scans.Fx;
    public void Dispose() => _scans.Dispose();

    private AppRecord Record(string name, string? location, params string[] folders) =>
        new(Guid.NewGuid(), name, "Maker", "1.0", AppSource.User, name.Replace(" ", ""), location, null, folders, null, F.Now.UtcDateTime);

    private string Shortcut(string name, string target)
    {
        string lnk = F.WriteFile(Path.Combine(F.Roots.StartMenuPrograms[0], name + ".lnk"), 1);
        F.Shortcuts.Targets[lnk] = target;
        return lnk;
    }

    // ---- shortcut ownership ----

    [Fact]
    public async Task A_sloppy_install_location_or_an_uninstaller_in_System32_never_makes_other_programs_shortcuts_the_apps_own()
    {
        string other = F.Folder(F.ProgramFiles("Other App"));
        string lnk = Shortcut("Other", F.WriteFile(Path.Combine(other, "other.exe")));
        string system = Shortcut("Tool", F.WriteFile(F.Windows("System32", "tool.exe")));
        using var engine = F.Engine();
        // InstallLocation = C:\Program Files, and an uninstaller that lives in System32.
        var scan = (await engine.ScanAsync(Record("Fixture Thing", F.ProgramFiles(), F.Windows("System32"))))!;
        Assert.DoesNotContain(scan.Items, i => i.Path == lnk || i.Path == system);
    }

    [Fact]
    public async Task A_folder_that_holds_another_installed_app_is_not_the_apps_own_for_its_shortcuts_but_its_own_folder_still_is()
    {
        string suite = F.Folder(F.ProgramFiles("Suite"));
        string neighbour = F.Folder(Path.Combine(suite, "Neighbour"));
        F.Registry.Add(AppSource.Machine64, "Neighbour", "Neighbour", "Other Maker", "1.0", neighbour, null);
        string theirs = Shortcut("Neighbour", F.WriteFile(Path.Combine(neighbour, "n.exe")));
        string own = F.Folder(Path.Combine(suite, "Fixture Thing"));
        string ours = Shortcut("Fixture Thing", F.WriteFile(Path.Combine(own, "f.exe")));
        using var engine = F.Engine();
        var parent = (await engine.ScanAsync(Record("Fixture Thing", suite)))!;
        Assert.DoesNotContain(parent.Items, i => i.Path == theirs);   // Suite holds Neighbour: not Fixture Thing's alone
        var child = (await engine.ScanAsync(Record("Fixture Thing", own)))!;
        Assert.Equal(LeftoverClass.Safe, child.Items.Single(i => i.Path == ours).Class);
        Assert.DoesNotContain(child.Items, i => i.Path == theirs);
    }

    // ---- ids ----

    [Fact]
    public async Task The_same_key_in_both_registry_views_is_two_items_with_their_own_ids()
    {
        F.Keys.Add(RegHive.Machine64, @"Software\Foo");
        F.Keys.Add(RegHive.Machine32, @"Software\Foo");
        using var engine = F.Engine();
        var scan = (await engine.ScanAsync(Record("Foo", null)))!;
        var keys = scan.Items.Where(i => i.Kind == LeftoverKind.Key && i.Hive is RegHive.Machine64 or RegHive.Machine32).ToList();
        Assert.Equal([@"HKLM\Software\Foo", @"HKLM\Software\WOW6432Node\Foo"], keys.Select(k => k.Path).Order(StringComparer.Ordinal));
        Assert.Equal(2, keys.Select(k => k.Id).Distinct().Count());
        Assert.Equal([RegHive.Machine64, RegHive.Machine32], keys.Select(k => k.Hive!.Value).Order());
    }

    // ---- removal ----

    [Fact]
    public async Task An_item_that_vanished_after_the_scan_reads_Removed_and_a_missing_path_is_no_link()
    {
        var record = _scans.Mozilla();
        using var engine = F.Engine();
        var scan = (await engine.ScanAsync(record))!;
        string gone = F.Roaming("Mozilla", "Firefox");
        Directory.Delete(gone, recursive: true);
        Assert.False(LeftoverRemover.IsLink(gone));
        var run = await engine.RemoveAsync(scan, [.. scan.Removable.Select(i => i.Id)]);
        Assert.True(run.Results.Single(r => r.Item.Path == gone).Ok);
        Assert.DoesNotContain(gone, F.Recycler.Recycled);
        Assert.All(run.Results, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task Files_and_the_Recycle_Bin_run_on_a_worker_of_their_own_and_not_on_the_callers_thread()
    {
        var record = _scans.Mozilla();
        using var engine = F.Engine();
        var scan = (await engine.ScanAsync(record))!;
        int caller = Environment.CurrentManagedThreadId;
        await engine.RemoveAsync(scan, [.. scan.Removable.Select(i => i.Id)]);
        Assert.NotEmpty(F.Recycler.Threads);
        Assert.All(F.Recycler.Threads, t => Assert.NotEqual(caller, t.Thread));
        Assert.All(F.Recycler.Threads, t => Assert.Equal(ApartmentState.STA, t.Apartment));
        Assert.Single(F.Recycler.Threads.Select(t => t.Thread).Distinct());   // one worker for the whole loop, whatever the order of the items
    }

    [Fact]
    public async Task A_retry_of_the_declined_items_adds_to_the_first_run_and_completes_the_record()
    {
        var record = _scans.Mozilla();
        using var engine = F.Engine();
        var scan = (await engine.ScanAsync(record))!;
        bool decline = true;
        F.Admin.Leftover = item => new AdminLeftoverResult(item, decline ? AdminLeftoverState.NeedsAdmin : AdminLeftoverState.Removed);
        var first = await engine.RemoveAsync(scan, [.. scan.Removable.Select(i => i.Id)]);
        var declined = first.Results.Where(r => !r.Ok).ToList();
        Assert.NotEmpty(declined);
        Assert.All(declined, r => Assert.Equal(LeftoverState.NeedsAdmin, r.State));
        Assert.NotNull(first.BackupFolder);   // the HKCU key's .reg
        Assert.DoesNotContain(engine.Recent, r => r.Id == record.Id && r.Cleaned);

        decline = false;
        var second = await engine.RemoveAsync(scan, [.. declined.Select(r => r.Item.Id)], previous: first);
        Assert.Equal(first.Results.Count, second.Results.Count);   // the first run's removals are still in it
        Assert.All(second.Results, r => Assert.True(r.Ok));
        Assert.Equal(first.BackupFolder, second.BackupFolder);
        Assert.Contains(engine.Recent, r => r.Id == record.Id && r.Cleaned);
        Assert.Contains(F.Admin.Dropped, d => d == record.HelperRecordId);
    }

    // ---- running ----

    private async Task<(UninstallerEngine Engine, AppEntry App)> Listed(string name, string? location, string uninstaller, string? icon = null)
    {
        var engine = F.Engine();
        F.Registry.Add(AppSource.User, name, name, "Maker", "1.0", location, "\"" + uninstaller + "\"", null, icon is null ? null : new[] { ("DisplayIcon", (object)icon) });
        await engine.RefreshAsync();
        return (engine, engine.Apps.Single(a => a.Name == name));
    }

    [Fact]
    public async Task An_app_with_no_install_location_is_open_when_its_uninstallers_folder_is()
    {
        string dir = F.Folder(F.Local("Programs", "Blank"));
        string exe = F.WriteFile(Path.Combine(dir, "unins000.exe"));
        F.Processes.RunningFolders.Add(dir);
        var (engine, app) = await Listed("Blank", null, exe);
        using var _ = engine;
        Assert.True(engine.IsOpen(app));
        var outcome = Assert.Single(await engine.UninstallAsync([app]));
        Assert.Equal(AppRunState.Running, outcome.Status.State);
        Assert.Empty(F.User.Calls);   // the uninstaller never started against the open app
    }

    [Fact]
    public async Task An_app_with_no_install_location_is_open_when_its_icons_folder_is_and_a_Windows_icon_never_counts()
    {
        string dir = F.Folder(F.Local("Programs", "Iconic"));
        string exe = F.WriteFile(Path.Combine(dir, "unins000.exe"));
        string bin = F.Folder(F.Local("Programs", "IconicBin"));
        F.Processes.RunningFolders.Add(bin);
        var (engine, app) = await Listed("Iconic", null, exe, Path.Combine(bin, "iconic.exe") + ",0");
        using var _ = engine;
        Assert.True(engine.IsOpen(app));

        using var other = F.Engine();
        F.Registry.Add(AppSource.User, "Shelly", "Shelly", "Maker", "1.0", F.Folder(F.Local("Programs", "Shelly")), "\"" + exe + "\"", null, [("DisplayIcon", F.Windows("System32", "shell32.dll") + ",3")]);
        F.Processes.RunningFolders.Clear();
        F.Processes.RunningFolders.Add(F.Windows("System32"));
        await other.RefreshAsync();
        Assert.False(other.IsOpen(other.Apps.Single(a => a.Name == "Shelly")));   // the whole of System32 is not the app
    }

    [Fact]
    public async Task Every_search_row_shares_one_read_of_the_running_programs()
    {
        using var engine = F.Engine();
        for (int i = 0; i < 5; i++)
        {
            string dir = F.Folder(F.Local("Programs", "App " + i));
            F.Registry.Add(AppSource.User, "app" + i, "App " + i, "Maker", "1.0", dir, "\"" + F.WriteFile(Path.Combine(dir, "unins000.exe")) + "\"");
        }
        await engine.RefreshAsync();
        int before = F.Processes.Reads;
        foreach (var app in engine.Apps) Assert.False(engine.IsOpen(app));
        Assert.Equal(before + 1, F.Processes.Reads);
    }

    // ---- the entry as it reads now ----

    [Fact]
    public async Task What_runs_is_the_entry_read_now_not_the_one_the_list_cached_and_a_gone_entry_fails_the_row()
    {
        string dir = F.Folder(F.Local("Programs", "Mover"));
        string oldExe = F.WriteFile(Path.Combine(dir, "old", "unins000.exe"));
        string newExe = F.WriteFile(Path.Combine(dir, "new", "unins000.exe"));
        var (engine, app) = await Listed("Mover", dir, oldExe);
        using var _ = engine;
        F.Registry.Remove(AppSource.User, "Mover");
        F.Registry.Add(AppSource.User, "Mover", "Mover", "Maker", "2.0", dir, "\"" + newExe + "\"");   // the app updated and moved its uninstaller
        await engine.UninstallAsync([app]);
        Assert.Equal(newExe, Assert.Single(F.User.Calls).App);

        F.User.Calls.Clear();
        F.Registry.Remove(AppSource.User, "Mover");
        var gone = Assert.Single(await engine.UninstallAsync([app]));
        Assert.Equal(AppRunState.Failed, gone.Status.State);
        Assert.Empty(F.User.Calls);
    }
}
