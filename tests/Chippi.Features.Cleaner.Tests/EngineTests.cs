// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;
using Chippi.Features.Cleaner;

namespace Chippi.Features.Cleaner.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly Fixture _f = new();
    public void Dispose() => _f.Dispose();

    private static CleanerRow Row(CleanerEngine e, string id) => e.Rows.Single(r => r.Category.Id == id);

    [Fact]
    public void Defaults_tick_user_temp_and_thumbnails_only_and_the_catalog_is_the_mockups()
    {
        using var e = _f.Engine();
        Assert.Equal(["User temp files", "Thumbnail and icon caches", "App caches", "Windows temp", "Windows Update cache", "Delivery Optimization cache", "Crash dumps and logs"],
            e.Rows.Select(r => r.Category.Name));
        Assert.Equal(["temp", "thumb"], e.Rows.Where(r => r.Ticked).Select(r => r.Category.Id));
        Assert.Equal(["wtemp", "wu", "do", "dump"], CleanerCatalog.All.Where(c => c.NeedsAdmin).Select(c => c.Id));
        Assert.Equal(CleanerTag.CheckFirst, CleanerCatalog.Find("app").Tag);
        Assert.False(e.AnyAdminTicked);
    }

    [Fact]
    public async Task Age_rule_deletes_only_files_whose_write_and_creation_are_both_over_a_day_old()
    {
        string old = _f.File(_f.Temp("old.tmp"), 100, 3);
        string fresh = _f.File(_f.Temp("fresh.tmp"), 100, 0);
        string newWrite = _f.File(_f.Temp("new-write.tmp"), 100, 3);
        File.SetLastWriteTimeUtc(newWrite, DateTime.UtcNow);
        string newCreation = _f.File(_f.Temp("new-creation.tmp"), 100, 3);
        File.SetCreationTimeUtc(newCreation, DateTime.UtcNow);
        using var e = _f.Engine();
        var plan = (await e.PrepareCleanAsync())!;
        Assert.Equal(100, plan.Bytes);
        var result = await e.CleanAsync(plan);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh) && File.Exists(newWrite) && File.Exists(newCreation));
        Assert.Equal(100, result.FreedBytes);
    }

    [Fact]
    public async Task Measure_deletes_nothing_and_sums_bytes_and_files_off_the_calling_thread()
    {
        _f.File(_f.Temp("a.tmp"), 300);
        _f.File(_f.Temp(@"sub\b.tmp"), 700);
        _f.File(_f.Local("Microsoft", "Windows", "Explorer", "thumbcache_256.db"), 2000, 0);
        int testThread = Environment.CurrentManagedThreadId;
        var threads = new List<int>();
        using var e = new CleanerEngine(_f.Roots, null, null, null, () => { threads.Add(Environment.CurrentManagedThreadId); return new HashSet<string>(); });
        await e.MeasureAsync();
        var temp = Row(e, "temp");
        Assert.Equal((CleanerRowState.Measured, 1000L, 2), (temp.State, temp.Bytes, temp.Files));
        Assert.Equal(2000, Row(e, "thumb").Bytes);
        Assert.True(File.Exists(_f.Temp("a.tmp")) && File.Exists(_f.Temp(@"sub\b.tmp")));
        Assert.NotEmpty(threads);
        Assert.DoesNotContain(testThread, threads);
        Assert.Equal("3 KB", e.TotalText);   // ticked: temp + thumbnails
    }

    [Fact]
    public async Task A_file_held_open_is_skipped_and_counted_never_forced()
    {
        string held = _f.File(_f.Temp("held.tmp"), 500);
        string other = _f.File(_f.Temp("other.tmp"), 200);
        using var e = _f.Engine();
        var plan = (await e.PrepareCleanAsync())!;
        using var hold = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await e.CleanAsync(plan);
        Assert.True(File.Exists(held));
        Assert.False(File.Exists(other));
        Assert.Equal(200, result.FreedBytes);
        Assert.Equal(1, result.InUseFiles);
        Assert.Equal(1, Row(e, "temp").InUseFiles);
    }

    [Fact]
    public async Task A_junction_inside_is_removed_as_a_link_and_its_target_is_untouched()
    {
        string target = Path.GetDirectoryName(_f.File(_f.Outside(@"keep\precious.txt"), 50, 5))!;
        string link = _f.Junction(_f.Roaming("discord", "Cache", "link"), target);
        _f.File(_f.Roaming("discord", "Cache", "real.bin"), 80, 0);
        var tick = new CleanerSettings { Ticks = new() { ["app"] = true } };
        using var e = _f.Engine(settings: tick);
        await e.MeasureAsync();
        Assert.Equal(80, Row(e, "app").Bytes);   // the link's target is not counted
        var plan = (await e.PrepareCleanAsync(["app"]))!;
        var result = await e.CleanAsync(plan);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "precious.txt")));
        Assert.False(File.Exists(_f.Roaming("discord", "Cache", "real.bin")));
        Assert.Equal(80, result.FreedBytes);
    }

    [Fact]
    public async Task A_young_junction_in_user_temp_is_left_alone_and_a_root_that_is_a_link_is_unavailable()
    {
        string target = Path.GetDirectoryName(_f.File(_f.Outside(@"t\x.txt"), 50, 5))!;
        string link = _f.Junction(_f.Temp("young-link"), target);
        _f.File(_f.Temp("old.tmp"), 10, 3);
        using var e = _f.Engine();
        await e.CleanAsync((await e.PrepareCleanAsync())!);
        Assert.True(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "x.txt")));

        // The category root itself a link: never followed, Unavailable for that category only.
        string linkedTemp = Path.Combine(_f.Base, "linked-temp");
        _f.Junction(linkedTemp, target);
        var roots = _f.Roots with { Temp = linkedTemp };
        _f.File(_f.Local("Microsoft", "Windows", "Explorer", "thumbcache_32.db"), 40, 0);
        using var linked = new CleanerEngine(roots, null, null, null, () => new HashSet<string>());
        await linked.MeasureAsync();
        Assert.Equal(CleanerRowState.Unavailable, Row(linked, "temp").State);
        Assert.Equal("Unavailable", Row(linked, "temp").Text);
        Assert.Equal(CleanerRowState.Measured, Row(linked, "thumb").State);
        Assert.True(File.Exists(Path.Combine(target, "x.txt")));
    }

    [Fact]
    public async Task A_missing_root_makes_only_its_category_unavailable()
    {
        _f.File(_f.Local("Microsoft", "Windows", "Explorer", "iconcache_16.db"), 30, 0);
        using var e = _f.Engine();   // no temp folder at all
        var plan = (await e.PrepareCleanAsync())!;
        Assert.Equal(CleanerRowState.Unavailable, Row(e, "temp").State);
        Assert.Equal([("thumb", 30L)], plan.Items.Select(i => (i.Category.Id, i.Bytes)));
        var result = await e.CleanAsync(plan);
        Assert.Equal(30, result.FreedBytes);
    }

    [Fact]
    public async Task Thumbnail_category_matches_only_the_cache_database_files_in_the_explorer_folder()
    {
        string explorer = _f.Local("Microsoft", "Windows", "Explorer");
        string thumb = _f.File(Path.Combine(explorer, "thumbcache_idx.db"), 10, 0);
        string icon = _f.File(Path.Combine(explorer, "iconcache_idx.db"), 10, 0);
        string settings = _f.File(Path.Combine(explorer, "ExplorerStartupLog.etl"), 10, 5);
        string nested = _f.File(Path.Combine(explorer, "sub", "thumbcache_1.db"), 10, 0);
        string notDb = _f.File(Path.Combine(explorer, "thumbcache_1.dat"), 10, 0);
        using var e = _f.Engine();
        await e.CleanAsync((await e.PrepareCleanAsync(["thumb"]))!);
        Assert.False(File.Exists(thumb) || File.Exists(icon));
        Assert.True(File.Exists(settings) && File.Exists(nested) && File.Exists(notDb));
    }

    [Fact]
    public async Task Cookies_logins_history_and_storage_are_never_matched_even_inside_a_browser_profile()
    {
        string profile = _f.Local("Google", "Chrome", "User Data", "Default");
        string[] keep =
        [
            _f.File(Path.Combine(profile, "Cookies"), 11, 9), _f.File(Path.Combine(profile, "Login Data"), 12, 9),
            _f.File(Path.Combine(profile, "History"), 13, 9), _f.File(Path.Combine(profile, "Web Data"), 14, 9),
            _f.File(Path.Combine(profile, "Local Storage", "leveldb", "000003.log"), 15, 9), _f.File(Path.Combine(profile, "IndexedDB", "x.leveldb", "a"), 16, 9),
            _f.File(Path.Combine(profile, "Extensions", "ext", "m.json"), 17, 9),
            _f.File(Path.Combine(profile, "Cache", "Cache_Data", "History"), 18, 9),   // a second guard behind the fixed folder list
            _f.File(Path.Combine(_f.Local("Google", "Chrome", "User Data"), "Local State"), 19, 9),
            _f.File(Path.Combine(_f.Local("Google", "Chrome", "User Data"), "System Profile", "Cache", "x"), 20, 9),
        ];
        string[] go =
        [
            _f.File(Path.Combine(profile, "Cache", "Cache_Data", "f_000001"), 100, 0), _f.File(Path.Combine(profile, "Code Cache", "js", "a_0"), 200, 0),
            _f.File(Path.Combine(profile, "GPUCache", "data_0"), 300, 0),
            _f.File(Path.Combine(_f.Local("Google", "Chrome", "User Data"), "Profile 2", "Cache", "Cache_Data", "f_1"), 400, 0),
        ];
        string ff = _f.Local("Mozilla", "Firefox", "Profiles", "abc.default");
        string ffKeep = _f.File(Path.Combine(ff, "cookies.sqlite"), 5, 9), ffKeep2 = _f.File(Path.Combine(ff, "places.sqlite"), 5, 9);
        string ffGo = _f.File(Path.Combine(ff, "cache2", "entries", "AA"), 600, 0);
        using var e = _f.Engine(settings: new() { Ticks = new() { ["app"] = true } });
        await e.MeasureAsync();
        Assert.Equal(1600L, Row(e, "app").Bytes);
        var result = await e.CleanAsync((await e.PrepareCleanAsync(["app"]))!);
        Assert.Equal(1600, result.FreedBytes);
        Assert.All(keep.Append(ffKeep).Append(ffKeep2), p => Assert.True(File.Exists(p), p));
        Assert.All(go.Append(ffGo), p => Assert.False(File.Exists(p), p));
        Assert.True(Directory.Exists(Path.Combine(profile, "Cache")));   // a cache folder's own folder stays
    }

    [Fact]
    public async Task Discord_and_Slack_caches_only_and_never_their_other_data()
    {
        string discordKeep = _f.File(_f.Roaming("discord", "Local Storage", "leveldb", "a.log"), 9, 9);
        string slackKeep = _f.File(_f.Roaming("Slack", "storage", "root-state.json"), 9, 9);
        string d = _f.File(_f.Roaming("discord", "Cache", "Cache_Data", "x"), 50, 0), s = _f.File(_f.Roaming("Slack", "Code Cache", "js", "y"), 70, 0);
        using var e = _f.Engine();
        var result = await e.CleanAsync((await e.PrepareCleanAsync(["app"]))!);
        Assert.Equal(120, result.FreedBytes);
        Assert.True(File.Exists(discordKeep) && File.Exists(slackKeep));
        Assert.False(File.Exists(d) || File.Exists(s));
    }

    [Fact]
    public async Task A_running_app_makes_its_cache_in_use_and_it_is_never_forced()
    {
        string chrome = _f.File(Path.Combine(_f.Local("Google", "Chrome", "User Data", "Default"), "Cache", "a"), 100, 0);
        string edge = _f.File(Path.Combine(_f.Local("Microsoft", "Edge", "User Data", "Default"), "Cache", "b"), 200, 0);
        _f.Running.Add("chrome");
        using var e = _f.Engine();
        await e.MeasureAsync();
        var row = Row(e, "app");
        Assert.Equal((CleanerRowState.Measured, 200L), (row.State, row.Bytes));
        Assert.Equal(["Chrome"], row.SkippedApps);
        var result = await e.CleanAsync((await e.PrepareCleanAsync(["app"]))!);
        Assert.True(File.Exists(chrome));
        Assert.False(File.Exists(edge));
        Assert.Equal(200, result.FreedBytes);

        // Every installed browser running: the row reads In use and nothing is in the plan.
        string edge2 = _f.File(Path.Combine(_f.Local("Microsoft", "Edge", "User Data", "Default"), "Cache", "b"), 200, 0);
        _f.Running.Add("msedge");
        var plan = (await e.PrepareCleanAsync(["app"]))!;
        Assert.Equal(CleanerRowState.InUse, Row(e, "app").State);
        Assert.Equal("In use", Row(e, "app").Text);
        Assert.Empty(plan.Items);
        Assert.True(File.Exists(edge2));
    }

    [Fact]
    public async Task Windows_update_shows_a_lower_bound_with_a_plus_and_the_unlistable_ones_show_no_size()
    {
        _f.File(_f.Windows("SoftwareDistribution", "Download", "pkg", "a.cab"), 1500, 3);
        _f.File(_f.Local("CrashDumps", "app.dmp"), 2500, 3);
        _f.File(_f.Local("CrashDumps", "new.dmp"), 999, 0);
        using var e = _f.Engine();
        await e.MeasureAsync();
        var wu = Row(e, "wu");
        Assert.Equal((CleanerRowState.Measured, 1500L, true, "2 KB+"), (wu.State, wu.Bytes, wu.LowerBound, wu.Text));
        var dump = Row(e, "dump");
        Assert.Equal((2500L, true, "3 KB+"), (dump.Bytes, dump.LowerBound, dump.Text));
        foreach (var id in new[] { "wtemp", "do" })
        {
            Assert.Equal(CleanerRowState.Unmeasured, Row(e, id).State);
            Assert.Null(Row(e, id).Bytes);
            Assert.Equal("—", Row(e, id).Text);
        }
        Assert.True(File.Exists(_f.Windows("SoftwareDistribution", "Download", "pkg", "a.cab")));   // measure never deletes
    }

    [Fact]
    public void Size_text_follows_the_mockup()
    {
        Assert.Equal("820 MB", CleanerText.Size(820_000_000));
        Assert.Equal("1.0 GB", CleanerText.Size(1_030_000_000));
        Assert.Equal("1.6 GB+", CleanerText.Size(1_600_000_000, true));
        Assert.Equal("0 B", CleanerText.Size(0));
        Assert.Equal("—", CleanerText.Size(null));
        Assert.Equal("184 MB", CleanerText.Size(184_000_000));
    }

    [Fact]
    public async Task Ticks_are_one_value_remembered_and_a_failed_save_rolls_back()
    {
        var saved = new List<CleanerSettings>();
        using (var e = _f.Engine(save: saved.Add))
        {
            Assert.True(e.SetTicked("wu", true));
            Assert.True(e.SetTicked("temp", false));
            Assert.True(e.IsTicked("wu"));
            Assert.False(e.IsTicked("temp"));
            Assert.True(e.AnyAdminTicked);
            Assert.Equal(new Dictionary<string, bool> { ["wu"] = true, ["temp"] = false }, saved[^1].Ticks);
        }
        using var again = _f.Engine(settings: saved[^1]);
        Assert.Equal(["thumb", "wu"], again.Rows.Where(r => r.Ticked).Select(r => r.Category.Id));

        using var failing = _f.Engine(save: _ => throw new IOException());
        Assert.False(failing.SetTicked("app", true));
        Assert.False(failing.IsTicked("app"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Last_sizes_are_remembered_and_shown_as_from_earlier_while_a_fresh_measure_runs()
    {
        _f.File(_f.Temp("a.tmp"), 4000);
        CleanerSettings? saved = null;
        using (var first = _f.Engine(save: s => saved = s))
        {
            await first.MeasureAsync();
            Assert.Equal(new CleanerSavedSize(4000, false), saved!.Sizes["temp"]);
        }
        var gate = new TaskCompletionSource();
        var enter = new TaskCompletionSource();
        using var e = new CleanerEngine(_f.Roots, null, saved, null, () => { enter.TrySetResult(); gate.Task.Wait(); return new HashSet<string>(); });
        var opening = Row(e, "temp");
        Assert.Equal((4000L, true), (opening.Bytes, opening.FromEarlier));
        Assert.True(e.HasSizes);
        var measuring = e.MeasureAsync();
        await enter.Task;
        var during = Row(e, "temp");
        Assert.Equal((CleanerRowState.Measuring, 4000L, true), (during.State, during.Bytes, during.FromEarlier));
        Assert.True(e.IsMeasuring);
        gate.SetResult();
        await measuring;
        Assert.False(Row(e, "temp").FromEarlier);
    }

    [Fact]
    public async Task The_confirm_asks_the_ticked_size_and_cleaning_reports_what_was_freed_when_files_changed_in_between()
    {
        string grows = _f.File(_f.Temp("grows.tmp"), 1000);
        string goes = _f.File(_f.Temp("goes.tmp"), 3000);
        _f.File(_f.Temp("stays.tmp"), 500, 0);
        using var e = _f.Engine();
        var plan = (await e.PrepareCleanAsync())!;
        Assert.Equal(4000, plan.Bytes);
        Assert.Equal("Clean 4 KB?", plan.Question);
        File.AppendAllText(grows, new string('x', 250));   // 1250 bytes now
        File.SetLastWriteTimeUtc(grows, DateTime.UtcNow - TimeSpan.FromDays(2));
        File.Delete(goes);
        var result = await e.CleanAsync(plan);
        Assert.Equal(1250, result.FreedBytes);
        Assert.Equal("1 KB freed", result.Freed);
    }

    [Fact]
    public async Task Nothing_ticked_asks_nothing_and_an_empty_plan_is_empty()
    {
        using var e = _f.Engine(settings: new() { Ticks = new() { ["temp"] = false, ["thumb"] = false } });
        Assert.Null(await e.PrepareCleanAsync());
        Directory.CreateDirectory(_f.Temp());
        using var empty = _f.Engine();
        var plan = (await empty.PrepareCleanAsync())!;
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public async Task A_cancelled_clean_stops_between_files_and_reports_cancelled()
    {
        string a = _f.File(_f.Temp("a.tmp"), 100);
        using var e = _f.Engine();
        var plan = (await e.PrepareCleanAsync())!;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await e.CleanAsync(plan, ct: cts.Token);
        Assert.True(result.Cancelled);
        Assert.True(File.Exists(a));
        Assert.Equal(CleanerRowState.Cancelled, Row(e, "temp").State);
        Assert.False(e.IsCleaning);
    }

    [Fact]
    public async Task Clean_with_no_admin_category_ticked_never_touches_the_helper()
    {
        _f.File(_f.Temp("a.tmp"), 100);
        var admin = new FakeAdmin();
        using var e = _f.Engine(admin);
        await e.CleanAsync((await e.PrepareCleanAsync())!);
        Assert.Empty(admin.Calls);
    }

    [Fact]
    public async Task With_an_admin_category_ticked_the_helper_measures_first_then_the_question_shows_the_full_size_then_one_session_cleans()
    {
        string tmp = _f.File(_f.Temp("a.tmp"), 1000);
        _f.File(_f.Windows("SoftwareDistribution", "Download", "a.cab"), 500, 3);
        var admin = new FakeAdmin
        {
            Measure = c => new(c, AdminCategoryState.Measured, c == AdminCategory.WindowsUpdateDownloads ? 1_700_000_000 : 1_400_000_000, 9),
            Clean = c => new(c, AdminCategoryState.Cleaned, c == AdminCategory.WindowsUpdateDownloads ? 1_650_000_000 : 1_400_000_000, 9, 2),
        };
        var order = new List<string>();
        admin.OnClean = () => order.Add(File.Exists(tmp) ? "admin before standard" : "standard before admin");
        using var e = _f.Engine(admin, new() { Ticks = new() { ["wtemp"] = true, ["wu"] = true } });
        Assert.True(e.AnyAdminTicked);
        var plan = (await e.PrepareCleanAsync())!;
        Assert.Equal(["measure WindowsTemp,WindowsUpdateDownloads"], admin.Calls);   // the one prompt, before any question
        Assert.Equal(1000L + 1_400_000_000 + 1_700_000_000, plan.Bytes);   // the helper's full size replaces the lower bound
        Assert.False(plan.LowerBound);
        Assert.Equal("Clean 3.1 GB?", plan.Question);
        Assert.True(plan.NeedsAdmin);
        Assert.True(File.Exists(tmp));

        var result = await e.CleanAsync(plan);
        Assert.Equal(["measure WindowsTemp,WindowsUpdateDownloads", "clean WindowsTemp,WindowsUpdateDownloads"], admin.Calls);   // one session: no second measure or prompt
        Assert.Equal(["admin before standard"], order);
        Assert.False(File.Exists(tmp));
        Assert.Equal(1000L + 1_400_000_000 + 1_650_000_000, result.FreedBytes);
        Assert.Equal(4, result.InUseFiles);
        Assert.Equal(CleanerRowState.Cleaned, Row(e, "wu").State);
    }

    [Fact]
    public async Task Cancel_at_the_question_deletes_nothing_and_ends_the_helper_session()
    {
        string tmp = _f.File(_f.Temp("a.tmp"), 1000);
        var admin = new FakeAdmin();
        using var e = _f.Engine(admin, new() { Ticks = new() { ["wtemp"] = true } });
        var plan = (await e.PrepareCleanAsync())!;
        Assert.NotEmpty(plan.Items);
        await e.CancelPlanAsync();
        Assert.Equal(["measure WindowsTemp", "end"], admin.Calls);
        Assert.True(File.Exists(tmp));
    }

    [Fact]
    public async Task A_declined_prompt_marks_admin_categories_Needs_admin_and_the_standard_ones_still_run()
    {
        string tmp = _f.File(_f.Temp("a.tmp"), 1000);
        var admin = new FakeAdmin { Measure = c => new(c, AdminCategoryState.NeedsAdmin) };
        using var e = _f.Engine(admin, new() { Ticks = new() { ["wtemp"] = true, ["wu"] = true } });
        var plan = (await e.PrepareCleanAsync())!;
        Assert.True(plan.AdminDeclined);
        Assert.Equal(["temp"], plan.Items.Select(i => i.Category.Id));   // thumb is empty so Unavailable; wu/wtemp not in the plan
        Assert.Equal("Needs admin", Row(e, "wtemp").Text);
        var result = await e.CleanAsync(plan);
        Assert.False(File.Exists(tmp));
        Assert.DoesNotContain(admin.Calls, c => c.StartsWith("clean"));
        Assert.Equal(2, result.NeedsAdmin);
        Assert.Equal("1 KB freed", result.Freed);
        Assert.Equal("Needs admin", Row(e, "wu").Text);
    }

    [Fact]
    public async Task Without_a_helper_admin_categories_read_Needs_admin()
    {
        _f.File(_f.Temp("a.tmp"), 10);
        using var e = _f.Engine(null, new() { Ticks = new() { ["do"] = true } });
        var plan = (await e.PrepareCleanAsync())!;
        Assert.True(plan.AdminDeclined);
        Assert.Equal(CleanerRowState.NeedsAdmin, Row(e, "do").State);
    }

    [Fact]
    public async Task A_helper_that_fails_to_measure_marks_the_admin_rows_Failed_with_Admin_helper_failed()
    {
        _f.File(_f.Temp("a.tmp"), 10);
        var admin = new FakeAdmin { MeasureThrows = new IOException("pipe") };
        using var e = _f.Engine(admin, new() { Ticks = new() { ["wu"] = true, ["dump"] = true } });
        var plan = (await e.PrepareCleanAsync())!;
        Assert.True(plan.HelperFailed);
        Assert.Equal("Admin helper failed", e.Meta);
        Assert.Equal(["Failed", "Failed"], new[] { Row(e, "wu").Text, Row(e, "dump").Text });
        Assert.Equal(["temp"], plan.Items.Select(i => i.Category.Id));
    }

    [Fact]
    public async Task A_helper_that_fails_mid_clean_leaves_the_remaining_admin_rows_Failed_and_standard_ones_are_cleaned()
    {
        string tmp = _f.File(_f.Temp("a.tmp"), 1000);
        var admin = new FakeAdmin { BeforeClean = c => { if (c == AdminCategory.WindowsUpdateDownloads) throw new IOException("pipe"); } };
        using var e = _f.Engine(admin, new() { Ticks = new() { ["wtemp"] = true, ["wu"] = true, ["dump"] = true } });
        var plan = (await e.PrepareCleanAsync())!;
        var result = await e.CleanAsync(plan);
        Assert.True(result.HelperFailed);
        Assert.Equal("Admin helper failed", result.Meta);
        Assert.Equal(CleanerRowState.Cleaned, Row(e, "wtemp").State);   // finished before the failure
        Assert.Equal(CleanerRowState.Failed, Row(e, "wu").State);
        Assert.Equal(CleanerRowState.Failed, Row(e, "dump").State);
        Assert.False(File.Exists(tmp));
        Assert.Equal(2, result.Failed);
        Assert.Equal("Clean failed", new CleanerRunResult([], 0, 0, true, false).BarTitle);
    }

    [Fact]
    public async Task Retry_runs_one_category_through_the_same_flow()
    {
        var admin = new FakeAdmin();
        using var e = _f.Engine(admin, new() { Ticks = new() { ["wu"] = true, ["temp"] = false, ["thumb"] = false } });
        var plan = (await e.PrepareCleanAsync(["wu"]))!;
        Assert.Equal(["wu"], plan.Items.Select(i => i.Category.Id));
        Assert.Equal(["measure WindowsUpdateDownloads"], admin.Calls);
    }

    [Fact]
    public async Task Crash_dumps_clean_the_user_folders_with_the_helpers_system_part_and_sum_both()
    {
        string user = _f.File(_f.Local("CrashDumps", "a.dmp"), 700, 3);
        string young = _f.File(_f.Local("CrashDumps", "b.dmp"), 300, 0);
        var admin = new FakeAdmin { Measure = c => new(c, AdminCategoryState.Measured, 5000, 2), Clean = c => new(c, AdminCategoryState.Cleaned, 4800, 2, 1) };
        using var e = _f.Engine(admin, new() { Ticks = new() { ["dump"] = true, ["temp"] = false, ["thumb"] = false } });
        var plan = (await e.PrepareCleanAsync())!;
        Assert.Equal(5700, plan.Bytes);
        Assert.False(plan.LowerBound);
        var result = await e.CleanAsync(plan);
        Assert.False(File.Exists(user));
        Assert.True(File.Exists(young));
        Assert.Equal(5500, result.FreedBytes);
        Assert.Equal(1, result.InUseFiles);
    }

    [Fact]
    public void A_run_result_words_follow_the_mockup()
    {
        var wu = CleanerCatalog.Find("wu");
        CleanerRow R(CleanerRowState s) => new(wu, true, s);
        Assert.Equal("1.0 GB freed", new CleanerRunResult([], 1_000_000_000, 0, false, false).BarTitle);
        var declined = new CleanerRunResult([R(CleanerRowState.NeedsAdmin), R(CleanerRowState.NeedsAdmin)], 1_000_000_000, 0, false, false);
        Assert.Equal(("1.0 GB freed", "2 need admin", true), (declined.BarTitle, declined.BarDetail, declined.BarCaution));
        var failed = new CleanerRunResult([R(CleanerRowState.Failed)], 0, 0, false, false);
        Assert.Equal(("Clean failed", "Windows Update cache", "1 failed"), (failed.BarTitle, failed.BarDetail, failed.Meta));
        Assert.Equal("Cleaner", new CleanerRunResult([], 5, 0, false, false).BarDetail);
    }

    [Fact]
    public async Task Ten_thousand_small_files_clean_and_the_times_are_reported()
    {
        string dir = _f.Temp("many");
        Directory.CreateDirectory(dir);
        var when = DateTime.UtcNow - TimeSpan.FromDays(3);
        var bytes = new byte[100];
        for (int i = 0; i < 10_000; i++)
        {
            string p = Path.Combine(dir, (i % 100) + "", i + ".tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
            File.SetCreationTimeUtc(p, when);
            File.SetLastWriteTimeUtc(p, when);
        }
        using var e = _f.Engine();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await e.MeasureAsync();
        long measureMs = sw.ElapsedMilliseconds;
        Assert.Equal(1_000_000, Row(e, "temp").Bytes);
        Assert.Equal(10_000, Row(e, "temp").Files);
        var plan = (await e.PrepareCleanAsync())!;
        sw.Restart();
        var result = await e.CleanAsync(plan);
        long cleanMs = sw.ElapsedMilliseconds;
        Assert.Equal(1_000_000, result.FreedBytes);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_f.Temp()));   // every emptied subfolder went, the root stayed
        string report = $"10,000 files (1 MB): measure {measureMs} ms, clean {cleanMs} ms";
        Console.WriteLine(report);
        if (Environment.GetEnvironmentVariable("CHIPPI_CLEANER_TIMINGS") is { Length: > 0 } path) File.AppendAllText(path, report + Environment.NewLine);
        Assert.True(cleanMs < 60_000);
    }
}
