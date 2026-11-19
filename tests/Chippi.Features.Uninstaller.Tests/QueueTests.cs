// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;
using Chippi.Features.Uninstaller;

namespace Chippi.Features.Uninstaller.Tests;

public sealed class QueueTests : IDisposable
{
    private readonly Fixture _f = new();
    public void Dispose() => _f.Dispose();

    private AppEntry Machine(string key, string name, AppSource source = AppSource.Machine64, string publisher = "Acme")
    {
        string folder = _f.ProgramFiles(name);
        _f.WriteFile(Path.Combine(folder, "app.dll"));
        string exe = _f.WriteFile(Path.Combine(folder, "unins000.exe"));
        _f.Registry.Add(source, key, name, publisher, "1.0", folder, "\"" + exe + "\"");
        return Read(source, key);
    }

    private AppEntry User(string key, string name, string publisher = "Acme")
    {
        string folder = _f.Local("Programs", name);
        _f.WriteFile(Path.Combine(folder, "app.dll"));
        string exe = _f.WriteFile(Path.Combine(folder, "Update.exe"));
        _f.Registry.Add(AppSource.User, key, name, publisher, "2.0", folder, "\"" + exe + "\" --uninstall");
        return Read(AppSource.User, key);
    }

    private AppEntry Read(AppSource source, string key) => new AppReader(_f.Registry, _f.Packages, _ => false).Registered(source, _f.Registry.ReadOne(source, key)!)!;

    private AppEntry Store(string name)
    {
        _f.Packages.Packages.Add(new RawPackage("Full_" + name, "Family_" + name, name, name, "Contoso", "1.0.0.0", @"C:\WindowsApps\" + name, new DateTime(2026, 11, 1), false, false, false));
        return new AppReader(_f.Registry, _f.Packages, _ => false).Read().Single(a => a.Name == name);
    }

    private void UserRunRemovesKey(string key) => _f.User.Run = (_, _) => { _f.Registry.Remove(AppSource.User, key); return 0; };

    [Fact]
    public async Task One_confirmed_queue_runs_the_machine_apps_in_one_helper_session_then_the_users_then_the_Store_each_in_order()
    {
        var slack = User("Slack", "Slack");
        var git = Machine("Git_is1", "Git");
        var calc = Store("Calculator");
        var winrar = Machine("WinRAR", "WinRAR", AppSource.Machine32);
        _f.User.Run = (_, _) => { _f.Registry.Remove(AppSource.User, "Slack"); return 0; };
        using var engine = _f.Engine();
        var outcomes = await engine.UninstallAsync([slack, git, calc, winrar]);
        Assert.Equal(["Git", "WinRAR", "Slack", "Calculator"], outcomes.Select(o => o.App.Name));
        Assert.All(outcomes, o => Assert.Equal(AppRunState.Uninstalled, o.Status.State));
        Assert.Equal(["uninstall 2"], _f.Admin.Calls);   // one session, one batch, the machine apps only
        Assert.Equal([("Git_is1", AdminRegistryView.Registry64), ("WinRAR", AdminRegistryView.Registry32)], _f.Admin.Uninstalled.Select(i => (i.KeyName, i.View)));
        Assert.Equal(["Full_Calculator"], _f.Packages.Removed);
        Assert.Single(_f.User.Calls);
        Assert.Equal("Uninstalled", engine.StatusOf(slack.Id).Text);
        Assert.Equal(4, engine.Recent.Count);
    }

    [Fact]
    public async Task A_user_app_runs_as_the_standard_user_from_its_own_command_and_never_through_the_helper()
    {
        var slack = User("Slack", "Slack");
        UserRunRemovesKey("Slack");
        using var engine = _f.Engine();
        var outcome = Assert.Single(await engine.UninstallAsync([slack]));
        Assert.Equal(AppRunState.Uninstalled, outcome.Status.State);
        Assert.DoesNotContain(_f.Admin.Calls, c => c.StartsWith("uninstall", StringComparison.Ordinal));
        Assert.EndsWith("Update.exe", _f.User.Calls.Single().App);
        Assert.Equal("--uninstall", _f.User.Calls.Single().Args);
    }

    [Fact]
    public async Task The_entry_is_read_again_after_the_run_and_still_there_means_Uninstall_failed_with_no_record()
    {
        var slack = User("Slack", "Slack");   // the fake uninstaller exits 0 and leaves the key
        using var engine = _f.Engine();
        var outcome = Assert.Single(await engine.UninstallAsync([slack]));
        Assert.Equal("Uninstall failed", outcome.Status.Text);
        Assert.Empty(engine.Recent);   // nothing to scan: the app is still installed
        Assert.Equal("Uninstall failed", engine.StatusOf(slack.Id).Text);
    }

    [Fact]
    public async Task A_nonzero_exit_with_the_entry_gone_is_uninstalled_and_with_the_entry_kept_is_failed()
    {
        var a = User("A", "AppA");
        var b = User("B", "AppB");
        _f.User.Run = (app, _) => { if (app.Contains("AppA")) _f.Registry.Remove(AppSource.User, "A"); return 1; };
        using var engine = _f.Engine();
        var outcomes = await engine.UninstallAsync([a, b]);
        Assert.Equal([AppRunState.Uninstalled, AppRunState.Failed], outcomes.Select(o => o.Status.State));
    }

    [Fact]
    public async Task Exit_3010_is_Restart_to_finish_and_the_scan_waits()
    {
        var slack = User("Slack", "Slack");
        _f.User.Run = (_, _) => { _f.Registry.Remove(AppSource.User, "Slack"); return 3010; };
        using var engine = _f.Engine();
        var outcome = Assert.Single(await engine.UninstallAsync([slack]));
        Assert.Equal("Restart to finish", outcome.Status.Text);
        Assert.True(outcome.Succeeded);
        var record = Assert.Single(engine.Recent);
        Assert.True(record.Restart);
        Assert.Null(await engine.ScanAsync(record));
    }

    [Fact]
    public async Task The_helper_reporting_3010_is_Restart_to_finish_and_keeps_its_record_id()
    {
        var git = Machine("Git_is1", "Git");
        var id = Guid.NewGuid();
        _f.Admin.Uninstall = item => new AdminUninstallResult(item, AdminUninstallState.RestartNeeded, 3010, false, id);
        using var engine = _f.Engine();
        var outcome = Assert.Single(await engine.UninstallAsync([git]));
        Assert.Equal("Restart to finish", outcome.Status.Text);
        Assert.Equal(id, engine.Recent.Single().HelperRecordId);
    }

    [Fact]
    public async Task An_app_that_is_open_is_Running_never_ended_and_the_rest_of_the_queue_goes_on()
    {
        var git = Machine("Git_is1", "Git");
        var other = Machine("Other", "Other");
        _f.Processes.RunningFolders.Add(_f.ProgramFiles("Git"));
        using var engine = _f.Engine();
        var outcomes = await engine.UninstallAsync([git, other]);
        Assert.Equal([AppRunState.Running, AppRunState.Uninstalled], outcomes.Select(o => o.Status.State));
        Assert.Equal("Running", outcomes[0].Status.Text);
        Assert.Equal("Close Git first", outcomes[0].Status.Detail);
        Assert.Equal(["Other"], _f.Admin.Uninstalled.Select(i => i.KeyName));
    }

    [Fact]
    public async Task A_declined_prompt_or_no_helper_is_Needs_admin_and_nothing_runs()
    {
        var git = Machine("Git_is1", "Git");
        _f.Admin.Uninstall = item => new AdminUninstallResult(item, AdminUninstallState.NeedsAdmin);
        using (var engine = _f.Engine())
        {
            var outcome = Assert.Single(await engine.UninstallAsync([git]));
            Assert.Equal("Needs admin", outcome.Status.Text);
            Assert.Empty(engine.Recent);
        }
        using var noHelper = _f.Engine(withAdmin: false);
        Assert.Equal("Needs admin", Assert.Single(await noHelper.UninstallAsync([git])).Status.Text);
    }

    [Fact]
    public async Task A_helper_failure_or_refusal_and_a_Store_failure_are_failed_or_refused()
    {
        var git = Machine("Git_is1", "Git");
        var rar = Machine("WinRAR", "WinRAR");
        var calc = Store("Calculator");
        _f.Admin.Uninstall = item => item.KeyName == "Git_is1"
            ? new AdminUninstallResult(item, AdminUninstallState.Failed, 1602, false, Guid.NewGuid())   // cancelled: the key is still there
            : new AdminUninstallResult(item, AdminUninstallState.Refused, null, false, null, "not allowed");
        _f.Packages.RemoveOk = false;
        using var engine = _f.Engine();
        var outcomes = await engine.UninstallAsync([git, rar, calc]);
        Assert.Equal(["Uninstall failed", "Refused", "Uninstall failed"], outcomes.Select(o => o.Status.Text));
        Assert.Empty(engine.Recent);
        Assert.Contains("end", _f.Admin.Calls);   // nothing succeeded: the helper's session ends at once
    }

    [Fact]
    public async Task Cancel_skips_the_apps_not_started_and_never_stops_the_one_running()
    {
        var a = User("A", "AppA");
        var b = User("B", "AppB");
        using var cts = new CancellationTokenSource();
        _f.User.Run = (app, _) => { cts.Cancel(); _f.Registry.Remove(AppSource.User, "A"); return 0; };
        using var engine = _f.Engine();
        var outcomes = await engine.UninstallAsync([a, b], cts.Token);
        Assert.Equal([AppRunState.Uninstalled, AppRunState.Cancelled], outcomes.Select(o => o.Status.State));
        Assert.Single(_f.User.Calls);   // the second never started
    }

    [Fact]
    public async Task Rows_without_a_verb_are_refused_and_never_run()
    {
        _f.Registry.Add(AppSource.Machine64, "dn", "Microsoft .NET Runtime 8.0.10", "Microsoft Corporation", uninstall: "\"C:\\x\\u.exe\"");
        var component = Read(AppSource.Machine64, "dn");
        using var engine = _f.Engine();
        var outcome = Assert.Single(await engine.UninstallAsync([component]));
        Assert.Equal(AppRunState.Refused, outcome.Status.State);
        Assert.DoesNotContain(_f.Admin.Calls, c => c.StartsWith("uninstall", StringComparison.Ordinal));
        Assert.Empty(_f.User.Calls);
    }

    [Fact]
    public async Task The_engine_reports_the_running_queue_for_the_quit_guard_and_the_bar()
    {
        var a = User("A", "AppA");
        var b = User("B", "AppB");
        using var engine = _f.Engine();
        var seen = new List<(bool Running, string? Name, int Index, int Count)>();
        _f.User.Run = (app, _) =>
        {
            seen.Add((engine.IsRunning, engine.CurrentName, engine.Index, engine.Count));
            _f.Registry.Remove(AppSource.User, app.Contains("AppA") ? "A" : "B");
            return 0;
        };
        Assert.False(engine.IsRunning);
        await engine.UninstallAsync([a, b]);
        Assert.Equal([(true, "AppA", 1, 2), (true, "AppB", 2, 2)], seen);
        Assert.False(engine.IsRunning);
        Assert.Null(engine.CurrentName);
    }

    [Fact]
    public async Task The_entry_is_kept_from_before_the_run_for_the_scan_after_the_uninstaller_deleted_it()
    {
        var slack = User("Slack", "Slack");
        UserRunRemovesKey("Slack");
        using var engine = _f.Engine();
        await engine.UninstallAsync([slack]);
        Assert.Null(_f.Registry.ReadOne(AppSource.User, "Slack"));
        var record = Assert.Single(engine.Recent);
        Assert.Equal(_f.Local("Programs", "Slack"), record.InstallLocation);
        Assert.Equal([_f.Local("Programs", "Slack")], record.Folders);
        Assert.True(record.HasRecord);
        var scan = await engine.ScanAsync(record);
        Assert.Equal(LeftoverClass.Safe, scan!.Items.Single(i => i.Path == _f.Local("Programs", "Slack")).Class);
    }

    [Fact]
    public async Task The_scan_is_not_offered_while_the_app_is_still_installed()
    {
        var slack = User("Slack", "Slack");
        UserRunRemovesKey("Slack");
        using var engine = _f.Engine();
        await engine.UninstallAsync([slack]);
        var record = engine.Recent.Single();
        _f.Registry.Add(AppSource.User, "Slack", "Slack", "Acme", "2.0", _f.Local("Programs", "Slack"), "\"x.exe\"");   // it came back
        Assert.Null(await engine.ScanAsync(record));
    }

    [Fact]
    public async Task The_log_holds_an_apps_name_only_never_a_path_a_key_name_or_a_command()
    {
        var git = Machine("Git_is1", "Git");
        var slack = User("Slack-key-0f3a", "Slack");
        var calc = Store("Calculator");
        _f.User.Run = (_, _) => { _f.Registry.Remove(AppSource.User, "Slack-key-0f3a"); return 0; };
        using var engine = _f.Engine();
        await engine.UninstallAsync([git, slack, calc]);
        var scan = await engine.ScanAsync(engine.Recent.First(r => r.Name == "Slack"));
        await engine.RemoveAsync(scan!, scan!.Items.Select(i => i.Id).ToList());
        string log = string.Join("\n", _f.Logger.Messages);
        Assert.Contains("Git", log);
        Assert.Contains("Slack", log);
        Assert.DoesNotContain("Git_is1", log);
        Assert.DoesNotContain("Slack-key-0f3a", log);
        Assert.DoesNotContain(@"\", log);
        Assert.DoesNotContain("--uninstall", log);
        Assert.DoesNotContain(".exe", log);
    }
}
