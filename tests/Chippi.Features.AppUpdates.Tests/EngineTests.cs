// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.Json;
using Chippi.Core.Admin;

namespace Chippi.Features.AppUpdates.Tests;

public sealed class EngineTests
{
    [Fact]
    public async Task Discovery_is_read_only_filters_unknown_and_own_package_and_retains_cache_on_failure()
    {
        var source = new Source { Packages = [App("B"), App("A"), App("Unknown") with { InstalledVersion = "Unknown" }, App("ChippiApp")] };
        var admin = new Admin(); var engine = new AppUpdatesEngine(source, admin, new Store());
        await engine.CheckAsync();
        Assert.Equal(new[] { "A", "B" }, engine.Updates.Select(p => p.Id)); Assert.Empty(source.Installed); Assert.Empty(admin.Batches);
        Assert.NotNull(engine.LastChecked);
        source.Error = new InvalidOperationException(); await engine.CheckAsync();
        Assert.Equal("Check failed", engine.Error); Assert.Equal(2, engine.Updates.Count); Assert.False(engine.IsChecking);
    }

    [Fact]
    public async Task Missing_winget_has_its_specific_recovery_state()
    {
        var source = new Source { Error = new WingetUnavailableException("Unavailable") };
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync();
        Assert.Equal("App updates need winget", engine.Error); Assert.Null(engine.LastChecked);
    }

    [Fact]
    public async Task An_unavailable_projection_interface_is_a_page_error_not_an_unhandled_fault()
    {
        var source = new Source { Error = new InvalidCastException("No such interface") };
        var engine = new AppUpdatesEngine(source, null, new Store());
        await engine.CheckAsync(); Assert.Equal("Check failed", engine.Error); Assert.False(engine.IsChecking);
    }

    [Fact]
    public async Task A_failed_source_shows_its_warning_and_logs_why_on_partial_and_total_failure()
    {
        var source = new StatusSource
        {
            Packages = [App("A")], Warning = "Check failed · Microsoft Store",
            SourceErrors = ["msstore: InvalidOperationException 0x80131509 Package source unavailable (SourceAgreementsNotAccepted, 0x8A150046)."]
        };
        var logged = new List<string>();
        var engine = new AppUpdatesEngine(source, null, new Store(), onSourceError: logged.Add);
        await engine.CheckAsync();
        Assert.Equal("Check failed · Microsoft Store", engine.Error); Assert.Single(engine.Updates);
        Assert.Contains("SourceAgreementsNotAccepted", Assert.Single(logged));
        source.SourceErrors = ["winget: InvalidOperationException 0x80131509 Package check failed.", "msstore: COMException 0x8A15000F Source unavailable."];
        source.Error = new InvalidOperationException("Package check failed."); await engine.CheckAsync();
        Assert.Equal("Check failed", engine.Error); Assert.Equal(3, logged.Count); Assert.StartsWith("winget:", logged[1]);
        source.SourceErrors = []; source.Error = new WingetUnavailableException("Unavailable"); await engine.CheckAsync();
        Assert.Equal(3, logged.Count);
    }

    [Fact]
    public async Task Concurrent_checks_join_one_read_and_a_later_check_reads_again()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<PackageUpdate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Read = _ => gate.Task };
        var engine = new AppUpdatesEngine(source, null, new Store());
        var first = engine.CheckAsync(); var second = engine.CheckAsync();
        Assert.Same(first, second); Assert.Equal(1, source.Checks); Assert.True(engine.IsChecking);
        gate.SetResult([App("A")]); await first; source.Read = null;
        await engine.CheckAsync(); Assert.Equal(2, source.Checks);
    }

    [Fact]
    public async Task Skip_version_expires_on_a_new_version_always_skip_survives_and_restore_is_persistent()
    {
        var first = App("First"); var second = App("Second");
        var source = new Source { Packages = [first, second] }; var store = new Store();
        var engine = new AppUpdatesEngine(source, null, store); await engine.CheckAsync();
        Assert.True(engine.Skip(first, false)); Assert.True(engine.Skip(second, true)); Assert.Empty(engine.Updates);
        source.Packages = [first with { AvailableVersion = "3" }, second with { AvailableVersion = "3" }]; await engine.CheckAsync();
        Assert.Single(engine.Updates); Assert.Equal("First", engine.Updates[0].Id);
        Assert.True(engine.Restore(engine.Preferences.Skipped.Single(s => s.Id == "Second")));
        Assert.Equal(2, engine.Updates.Count); Assert.Single(store.State.Skipped);
        var restarted = new AppUpdatesEngine(source, null, store); await restarted.CheckAsync(); Assert.Equal(2, restarted.Updates.Count);
    }

    [Fact]
    public async Task Opting_into_unknown_versions_allows_a_confirmed_machine_update()
    {
        var package = App("Unknown", UpdateScope.Machine) with { InstalledVersion = "Unknown" };
        var source = new Source { Packages = [package] }; var admin = new Admin();
        var engine = new AppUpdatesEngine(source, admin, new Store()); await engine.CheckAsync(); Assert.Empty(engine.Updates);
        Assert.True(engine.SetShowUnknownVersions(true)); await engine.CheckAsync();
        await engine.RunAsync(engine.CaptureBatch());
        Assert.Single(admin.Batches); Assert.Equal("Unknown", Assert.Single(admin.Batches[0])); Assert.Equal(UpdateState.Updated, engine.Rows[0].State);
    }

    [Fact]
    public async Task Completed_unknown_version_does_not_reappear_after_a_check_and_preference_toggle()
    {
        var package = App("Unknown") with { InstalledVersion = "Unknown" };
        var source = new Source { Packages = [package] };
        var engine = new AppUpdatesEngine(source, null, new Store());
        await engine.CheckAsync(); Assert.True(engine.SetShowUnknownVersions(true));
        await engine.RunAsync(engine.CaptureBatch());
        Assert.True(engine.SetShowUnknownVersions(true));
        Assert.Equal(UpdateState.Updated, Assert.Single(engine.Rows).State);
        await engine.CheckAsync(); Assert.Empty(engine.Rows); Assert.Empty(engine.Updates);
        Assert.True(engine.SetShowUnknownVersions(false)); Assert.True(engine.SetShowUnknownVersions(true));
        Assert.Empty(engine.Rows); Assert.Empty(engine.Updates); Assert.Empty(engine.CaptureBatch().Packages);
        Assert.Single(source.Installed);
    }

    [Fact]
    public async Task Save_failure_keeps_the_previous_setting_and_list()
    {
        var source = new Source { Packages = [App("A")] }; var store = new Store(); var engine = new AppUpdatesEngine(source, null, store);
        await engine.CheckAsync(); store.Fail = true;
        Assert.False(engine.Skip(engine.Updates[0], true)); Assert.Empty(engine.Preferences.Skipped); Assert.Single(engine.Updates); Assert.Equal("Save failed", engine.Error);
    }

    [Fact]
    public async Task Batch_is_an_exact_frozen_snapshot_and_a_failed_app_does_not_stop_later_apps()
    {
        var source = new Source { Packages = [App("A"), App("B")] };
        source.Install = (p, _, _) => Task.FromResult(new PackageInstallResult(p.Id == "A" ? UpdateState.Failed : UpdateState.Updated));
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync(); var batch = engine.CaptureBatch();
        source.Packages = [.. source.Packages, App("New")]; await engine.CheckAsync(); await engine.RunAsync(batch);
        Assert.Equal(new[] { "A", "B" }, source.Installed.Select(p => p.Id)); Assert.Equal(2, engine.Total);
        Assert.Equal(UpdateState.Failed, engine.Rows[0].State); Assert.Equal(UpdateState.Updated, engine.Rows[1].State); Assert.Equal(1, engine.Completed);
    }

    [Fact]
    public async Task Single_updates_keep_twenty_rows_and_only_remaining_versions_are_actionable()
    {
        var source = new Source { Packages = Enumerable.Range(0, 20).Select(i => App($"App{i:00}")).ToArray() };
        source.Install = (package, _, _) => Task.FromResult(new PackageInstallResult(package.Id == "App01" ? UpdateState.Failed : UpdateState.Updated));
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync();
        var first = engine.Updates.Single(p => p.Id == "App00");
        await engine.RunAsync(engine.CaptureBatch([first]));
        Assert.Equal(20, engine.Rows.Count); Assert.Equal(19, engine.Updates.Count);
        Assert.Equal(UpdateState.Updated, engine.Rows.Single(r => r.Package.Id == "App00").State);
        Assert.Equal(19, engine.Rows.Count(r => r.State == UpdateState.Available));
        Assert.Equal(1, engine.Total); Assert.Equal(1, engine.Completed); Assert.Single(engine.BatchRows);

        var second = engine.Updates.Single(p => p.Id == "App01");
        await engine.RunAsync(engine.CaptureBatch([second]));
        Assert.Equal(20, engine.Rows.Count); Assert.Equal(19, engine.Updates.Count);
        Assert.Equal(UpdateState.Failed, engine.Rows.Single(r => r.Package.Id == "App01").State);
        Assert.Equal(UpdateState.Updated, engine.Rows.Single(r => r.Package.Id == "App00").State);
        Assert.Equal(1, engine.Total); Assert.Equal(0, engine.Completed);
        Assert.Equal("App01", Assert.Single(engine.BatchRows).Package.Id);
        var next = engine.CaptureBatch(); Assert.Equal(19, next.Packages.Count);
        Assert.DoesNotContain(next.Packages, p => p.Id == "App00"); Assert.Contains(next.Packages, p => p.Id == "App01");

        source.Packages = source.Packages.Select(p => p.Id == "App00" ? p with { InstalledVersion = "2", AvailableVersion = "3" } : p).ToArray();
        await engine.CheckAsync();
        Assert.Equal(20, engine.Updates.Count); Assert.Contains(engine.CaptureBatch().Packages, p => p.Id == "App00" && p.AvailableVersion == "3");
        Assert.Empty(engine.BatchRows); Assert.Equal(0, engine.Total);
    }

    [Fact]
    public async Task Restart_required_version_is_not_selected_again_but_a_newer_version_is()
    {
        var package = App("A"); var source = new Source { Packages = [package] };
        source.Install = (_, _, _) => Task.FromResult(new PackageInstallResult(UpdateState.RestartRequired));
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync(); await engine.RunAsync(engine.CaptureBatch());
        Assert.Empty(engine.Updates); Assert.Empty(engine.CaptureBatch().Packages); Assert.Equal(1, engine.Completed);
        Assert.Equal(UpdateState.RestartRequired, Assert.Single(engine.Rows).State);
        source.Packages = [package with { InstalledVersion = "2", AvailableVersion = "3" }]; await engine.CheckAsync();
        Assert.Equal("3", Assert.Single(engine.CaptureBatch().Packages).AvailableVersion);
    }

    [Fact]
    public async Task Machine_results_are_saved_before_the_next_installer_finishes_and_final_replies_are_deduplicated()
    {
        var a = App("A", UpdateScope.Machine); var b = App("B", UpdateScope.Machine); var c = App("C", UpdateScope.Machine); var d = App("D");
        var source = new Source { Packages = [a, b, c, d] }; var store = new Store();
        var group = new TaskCompletionSource<IReadOnlyList<AdminUpdateResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<AdminUpdateProgress>? callback = null;
        var admin = new Admin
        {
            Run = (_, progress, _) =>
            {
                callback = progress;
                progress!.Report(new(a.Id, 1, 3)); progress.Report(new(a.Id, 1, 3, new(a.Id, "Done", 0)));
                progress.Report(new(b.Id, 2, 3)); return group.Task;
            }
        };
        var engine = new AppUpdatesEngine(source, admin, store); await engine.CheckAsync();
        using var stop = new CancellationTokenSource();
        var running = engine.RunAsync(engine.CaptureBatch([a, b, c]), stop.Token);
        Assert.False(running.IsCompleted); Assert.True(engine.IsRunning);
        Assert.Equal("A", Assert.Single(store.State.History).Id);
        Assert.Equal(UpdateState.Updated, store.State.History[0].State);
        Assert.Equal(1, engine.Completed); Assert.Equal(3, engine.Total);
        Assert.Equal(UpdateState.Updating, engine.BatchRows.Single(r => r.Package.Id == "B").State);
        Assert.Equal(UpdateState.Available, engine.Rows.Single(r => r.Package.Id == "D").State);

        stop.Cancel();
        callback!.Report(new(a.Id, 1, 3));
        callback.Report(new(a.Id, 1, 3, new(a.Id, "Failed", -1)));
        Assert.Equal(UpdateState.Updated, engine.BatchRows.Single(r => r.Package.Id == "A").State);
        callback.Report(new(b.Id, 2, 3, new(b.Id, "Done", 0)));
        Assert.Equal(2, store.State.History.Count);
        group.SetResult([new(a.Id, "Done", 0), new(b.Id, "Done", 0), new(c.Id, "Cancelled")]); await running;
        Assert.Equal(3, store.State.History.Count);
        Assert.All(new[] { "A", "B", "C" }, id => Assert.Single(store.State.History, h => h.Id == id));
        Assert.Equal(2, engine.Completed); Assert.Equal(3, engine.Total);

        var userFinish = new TaskCompletionSource<PackageInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Install = (_, _, _) => userFinish.Task;
        var next = engine.RunAsync(engine.CaptureBatch([d]));
        callback.Report(new(b.Id, 2, 3, new(b.Id, "Failed", -1)));
        Assert.Equal(1, engine.Total); Assert.Equal(0, engine.Completed); Assert.Equal("D", Assert.Single(engine.BatchRows).Package.Id);
        Assert.Equal(UpdateState.Updated, engine.Rows.Single(r => r.Package.Id == "B").State); Assert.Equal(3, store.State.History.Count);
        userFinish.SetResult(new(UpdateState.Updated)); await next;
        Assert.Equal(4, store.State.History.Count); Assert.Equal(1, engine.Completed);
    }

    [Fact]
    public async Task Machine_updates_send_the_reviewed_version_and_flag_only_unknown_installed_versions()
    {
        var known = App("Known", UpdateScope.Machine) with { AvailableVersion = "5.1" };
        var unknown = App("Unknown", UpdateScope.Machine) with { InstalledVersion = "Unknown", AvailableVersion = "6.0" };
        var source = new Source { Packages = [known, unknown] }; var admin = new Admin();
        var engine = new AppUpdatesEngine(source, admin, new Store()); engine.SetShowUnknownVersions(true); await engine.CheckAsync();
        await engine.RunAsync(engine.CaptureBatch());
        Assert.Equal(new[] { ("Known", "5.1", false), ("Unknown", "6.0", true) }, admin.Items.Single().Select(i => (i.PackageId, i.ExpectedVersion, i.IncludeUnknown)));
    }

    [Fact]
    public async Task A_helper_that_throws_anything_fails_every_machine_row_instead_of_leaving_them_queued()
    {
        var a = App("A", UpdateScope.Machine); var b = App("B", UpdateScope.Machine);
        var source = new Source { Packages = [a, b] };
        var admin = new Admin { Run = (_, _, _) => throw new ArgumentException("Invalid package batch.") };
        var engine = new AppUpdatesEngine(source, admin, new Store()); await engine.CheckAsync();
        await engine.RunAsync(engine.CaptureBatch());
        Assert.False(engine.IsRunning); Assert.All(engine.Rows, r => Assert.Equal(UpdateState.Failed, r.State));
    }

    [Fact]
    public async Task An_id_the_helper_would_refuse_fails_its_own_row_and_the_rest_still_run()
    {
        var odd = App("Bad Id!", UpdateScope.Machine); var fine = App("Fine", UpdateScope.Machine);
        var source = new Source { Packages = [odd, fine] }; var admin = new Admin();
        var engine = new AppUpdatesEngine(source, admin, new Store()); await engine.CheckAsync();
        await engine.RunAsync(engine.CaptureBatch());
        Assert.Equal(new[] { "Fine" }, admin.Batches.Single());
        Assert.Equal(UpdateState.Failed, engine.Rows.Single(r => r.Package.Id == "Bad Id!").State);
        Assert.Equal(UpdateState.Updated, engine.Rows.Single(r => r.Package.Id == "Fine").State);
    }

    [Fact]
    public async Task One_machine_session_covers_the_whole_group_and_store_stays_out_of_the_helper()
    {
        var user = App("User"); var store = App("Store", UpdateScope.Unknown) with { Source = "msstore" };
        var machine = App("Machine", UpdateScope.Machine); var unknown = App("Unknown", UpdateScope.Unknown);
        var source = new Source { Packages = [user, machine, store, unknown] }; var admin = new Admin();
        var engine = new AppUpdatesEngine(source, admin, new Store()); await engine.CheckAsync(); var batch = engine.CaptureBatch();
        Assert.Equal(new[] { "Store", "User", "Machine", "Unknown" }, batch.Packages.Select(p => p.Id));
        await engine.RunAsync(batch);
        Assert.Equal(new[] { "Store", "User" }, source.Installed.Select(p => p.Id)); Assert.Single(admin.Batches);
        Assert.Equal(new[] { "Machine", "Unknown" }, admin.Batches[0]); Assert.Equal(4, engine.Completed);
    }

    [Fact]
    public async Task Active_installer_finishes_after_cancellation_but_no_remaining_package_starts()
    {
        var finish = new TaskCompletionSource<PackageInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Packages = [App("A"), App("B")] };
        source.Install = (_, _, _) => finish.Task;
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync(); using var cancelled = new CancellationTokenSource();
        var running = engine.RunAsync(engine.CaptureBatch(), cancelled.Token); cancelled.Cancel();
        Assert.False(running.IsCompleted); Assert.Single(source.Installed);
        finish.SetResult(new(UpdateState.Updated)); await running;
        Assert.Single(source.Installed); Assert.Equal(UpdateState.Updated, engine.Rows[0].State); Assert.Equal(UpdateState.Cancelled, engine.Rows[1].State);
    }

    [Fact]
    public async Task A_second_run_joins_the_active_batch_and_checks_do_not_overwrite_it()
    {
        var finish = new TaskCompletionSource<PackageInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Packages = [App("A")], Install = (_, _, _) => finish.Task };
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync(); var batch = engine.CaptureBatch();
        var first = engine.RunAsync(batch); var second = engine.RunAsync(batch); Assert.Same(first, second);
        await engine.CheckAsync(); Assert.Equal(1, source.Checks); Assert.Equal(UpdateState.Updating, engine.Rows[0].State);
        finish.SetResult(new(UpdateState.Updated)); await first; Assert.Single(source.Installed);
    }

    [Fact]
    public async Task A_read_started_before_the_batch_cannot_erase_its_finished_results()
    {
        var source = new Source { Packages = [App("A")] };
        var engine = new AppUpdatesEngine(source, null, new Store()); await engine.CheckAsync();
        var read = new TaskCompletionSource<IReadOnlyList<PackageUpdate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Read = _ => read.Task; var checking = engine.CheckAsync();
        await engine.RunAsync(engine.CaptureBatch()); Assert.Equal(UpdateState.Updated, engine.Rows[0].State);
        read.SetResult([App("A")]); await checking;
        Assert.Equal(UpdateState.Updated, engine.Rows[0].State);
    }

    [Fact]
    public async Task Admin_results_arrive_live_and_decline_is_not_reported_as_success()
    {
        var finish = new TaskCompletionSource<IReadOnlyList<AdminUpdateResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var package = App("A", UpdateScope.Machine); var source = new Source { Packages = [package] };
        var admin = new Admin { Run = (ids, progress, _) => { progress?.Report(new(ids[0], 1, 1, new(ids[0], "NeedsAdmin"))); return finish.Task; } };
        var engine = new AppUpdatesEngine(source, admin, new Store()); await engine.CheckAsync(); var run = engine.RunAsync(engine.CaptureBatch());
        Assert.Equal(UpdateState.NeedsAdmin, engine.Rows[0].State); Assert.True(engine.IsRunning); Assert.Equal(0, engine.Completed);
        finish.SetResult([new(package.Id, "NeedsAdmin")]); await run; Assert.Equal(UpdateState.NeedsAdmin, engine.Rows[0].State);
    }

    [Theory]
    [InlineData("Done", 0, UpdateState.Updated)]
    [InlineData("Failed", -1978334967, UpdateState.RestartRequired)]
    [InlineData("Failed", -1978334966, UpdateState.Failed)]
    [InlineData("Failed", -1978334965, UpdateState.RestartRequired)]
    [InlineData("Failed", -1978334964, UpdateState.Cancelled)]
    [InlineData("Failed", -1978335189, UpdateState.Skipped)]
    public void Admin_hresult_states_are_not_confused_with_ordinary_failure(string state, int code, UpdateState expected)
    { Assert.Equal(expected, AppUpdatesEngine.MapAdminResult(new("A", state, code)).State); }

    [Fact]
    public async Task History_keeps_last_fifty_results_and_never_includes_install_paths_or_command_output()
    {
        var source = new Source { Packages = Enumerable.Range(0, 55).Select(i => App($"A{i:00}") with { InstallLocation = @"C:\do-not-store", IconPath = @"C:\private\app.exe" }).ToArray() };
        var store = new Store(); var engine = new AppUpdatesEngine(source, null, store); await engine.CheckAsync();
        await engine.RunAsync(engine.CaptureBatch());
        Assert.Equal(50, store.State.History.Count); string json = JsonSerializer.Serialize(store.State);
        Assert.DoesNotContain("do-not-store", json); Assert.DoesNotContain("app.exe", json); Assert.DoesNotContain("InstallLocation", json);
    }

    [Fact]
    public async Task History_save_failure_preserves_the_successful_install_result()
    {
        var source = new Source { Packages = [App("A")] }; var store = new Store(); var engine = new AppUpdatesEngine(source, null, store);
        await engine.CheckAsync(); store.Fail = true; await engine.RunAsync(engine.CaptureBatch());
        Assert.Equal(UpdateState.Updated, engine.Rows[0].State); Assert.Equal("History save failed", engine.Error); Assert.Single(engine.Preferences.History);
    }

    [Fact]
    public async Task Bad_persisted_state_is_not_silently_overwritten()
    {
        var store = new Store { BadLoad = true }; var source = new Source { Packages = [App("A")] };
        var engine = new AppUpdatesEngine(source, null, store); await engine.CheckAsync();
        Assert.False(engine.SetShowUnknownVersions(true)); Assert.Equal("Settings read failed", engine.Error); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void File_store_roundtrips_owned_data_and_refuses_corrupt_json()
    {
        string dir = Path.Combine(Path.GetTempPath(), "chippi-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonAppUpdatesStore(dir); Assert.Empty(store.Load().History);
            store.Save(new() { ShowUnknownVersions = true, Skipped = [new("winget/A", "A", "A", "2")] });
            Assert.True(store.Load().ShowUnknownVersions); Assert.Single(store.Load().Skipped);
            File.WriteAllText(Path.Combine(dir, "app-updates.json"), "broken"); Assert.Throws<JsonException>(() => store.Load());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static PackageUpdate App(string id, UpdateScope scope = UpdateScope.User) => new(id, id, "1", "2", "winget", scope);
    private sealed class Store : IAppUpdatesStore
    {
        public AppUpdatesPreferences State = new(); public bool Fail, BadLoad; public int Saves;
        public AppUpdatesPreferences Load() => BadLoad ? throw new JsonException() : State;
        public void Save(AppUpdatesPreferences state) { if (Fail) throw new IOException(); Saves++; State = state; }
    }
    private sealed class Source : IPackageSource
    {
        public IReadOnlyList<PackageUpdate> Packages = []; public Exception? Error; public int Checks;
        public List<PackageUpdate> Installed { get; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<PackageUpdate>>>? Read;
        public Func<PackageUpdate, IProgress<PackageInstallProgress>?, CancellationToken, Task<PackageInstallResult>>? Install;
        public Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default)
        { Checks++; if (Error is not null) throw Error; return Read?.Invoke(ct) ?? Task.FromResult(Packages); }
        public Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default)
        { Installed.Add(package); return Install?.Invoke(package, progress, ct) ?? Task.FromResult(new PackageInstallResult(UpdateState.Updated)); }
    }
    private sealed class StatusSource : IPackageSource, IPackageSourceStatus
    {
        public IReadOnlyList<PackageUpdate> Packages = []; public Exception? Error;
        public string? Warning { get; init; }
        public IReadOnlyList<string> SourceErrors { get; set; } = [];
        public Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default)
            => Error is not null ? throw Error : Task.FromResult(Packages);
        public Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new PackageInstallResult(UpdateState.Updated));
    }
    private sealed class Admin : IAdminHelper
    {
        public List<IReadOnlyList<string>> Batches { get; } = [];
        public List<IReadOnlyList<AdminUpdateItem>> Items { get; } = [];
        public Func<IReadOnlyList<string>, IProgress<AdminUpdateProgress>?, CancellationToken, Task<IReadOnlyList<AdminUpdateResult>>>? Run;
        public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default)
        { var ids = items.Select(i => i.PackageId).ToArray(); Items.Add(items); Batches.Add(ids); return Run?.Invoke(ids, progress, ct) ?? Task.FromResult<IReadOnlyList<AdminUpdateResult>>(ids.Select(id => new AdminUpdateResult(id, "Done", 0)).ToArray()); }
    }
}
