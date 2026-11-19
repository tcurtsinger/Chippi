// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using Chippi.Core.Admin;
using Chippi.Features.Cleaner;
using Chippi.Features.PortManager;
using Chippi.Features.Uninstaller;
using Chippi.UI.Admin;

/// <summary>
/// Port manager, Cleaner and Uninstaller against tools-fixture.ps1's throwaway listeners, service, folders and apps, through each feature's own
/// engine and the real helper, as the medium-integrity user. Every check is recorded and the run goes on, so one Sandbox run reports them all.
/// </summary>
static class ToolsCheck
{
    private static readonly List<string> Failures = [];
    private static readonly Dictionary<string, object?> Report = [];

    private static void Expect(bool ok, string what) { if (!ok) Failures.Add(what); }

    private static async Task<(T Value, double Ms)> Timed<T>(Func<Task<T>> run)
    {
        var watch = Stopwatch.StartNew();
        var value = await run();
        return (value, watch.Elapsed.TotalMilliseconds);
    }

    public static async Task<bool> RunAsync(AdminHelper helper, Func<int> launches)
    {
        foreach (var (name, step) in new (string, Func<AdminHelper, Func<int>, Task>)[] { ("ports", PortsAsync), ("cleaner", CleanerAsync), ("uninstaller", UninstallerAsync) })
        {
            try { await step(helper, launches); }
            catch (Exception ex) { Failures.Add(name + " threw: " + ex); }
            await WaitHelperGoneAsync(name);
        }
        Report["helperLaunches"] = launches();
        Report["failures"] = Failures;
        Report["passed"] = Failures.Count == 0;
        File.WriteAllText(@"C:\results\tools.json", JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true }));
        return Failures.Count == 0;
    }

    /// <summary>Each tool's flow ends its session; the next one starts a new helper, as days later would.</summary>
    private static async Task WaitHelperGoneAsync(string after)
    {
        var end = DateTime.UtcNow.AddSeconds(70);
        while (Process.GetProcessesByName("Chippi.Elevated").Length > 0)
        {
            if (DateTime.UtcNow > end) { Failures.Add($"{after}: helper still running 70 s after the flow ended"); return; }
            await Task.Delay(100);
        }
    }

    // ---- Port manager ----

    private static async Task PortsAsync(AdminHelper helper, Func<int> launches)
    {
        var system = new WindowsPortSystem();
        var scanner = new PortScanner(system);
        var ender = new PortEnder(system, helper);
        PortRow? Row(int port) => scanner.Scan(e => e.Protocol == PortProtocol.Tcp && e.LocalPort == port && e.IsHeld).FirstOrDefault();
        var ports = new Dictionary<string, object?>();
        Report["ports"] = ports;
        int before = launches();

        var window = Row(47011); var hidden = Row(47012); var service = Row(47013);
        ports["rows"] = new[] { window, hidden, service }.Select(r => r is null ? null : new { r.Endpoint.LocalPort, r.Holder.Name, kind = r.Holder.Kind.ToString(), verb = r.Verb.ToString(), r.ServiceName }).ToList();
        Expect(window?.Holder.Kind == HolderKind.Elevated && window.Verb == PortVerb.Free, "ports: 47011 not an elevated holder with Free");
        Expect(hidden?.Holder.Kind == HolderKind.Elevated && hidden.Verb == PortVerb.Free, "ports: 47012 not an elevated holder with Free");
        Expect(service?.Holder.Kind == HolderKind.Service && service.Verb == PortVerb.Stop && service.ServiceName == "ChippiFixtureSvc", "ports: 47013 not attributed to ChippiFixtureSvc with Stop");

        if (window is not null)
        {
            var (r, ms) = await Timed(() => ender.FreeAsync(window));
            ports["closeWindow"] = new { state = r.State.ToString(), r.Reason, ms };
            Expect(r.State == PortEndState.Freed, "ports: Free (close) on the window listener: " + r.Text);
        }
        if (hidden is not null)
        {
            var (r, ms) = await Timed(() => ender.FreeAsync(hidden));
            ports["endHidden"] = new { state = r.State.ToString(), r.Reason, ms };
            Expect(r.State == PortEndState.Freed, "ports: Free (end) on the windowless listener: " + r.Text);
        }
        if (service is not null)
        {
            var (r, ms) = await Timed(() => ender.StopAsync(service));
            ports["stopService"] = new { state = r.State.ToString(), r.Reason, ms };
            Expect(r.State == PortEndState.Freed, "ports: Stop on ChippiFixtureSvc: " + r.Text);
        }
        Expect(Row(47011) is null && Row(47012) is null && Row(47013) is null, "ports: a fixture port is still held");

        // Refusals, straight to the helper: a core service, a service that doesn't own the endpoint, and System.
        var refusals = new Dictionary<string, object?>();
        ports["refusals"] = refusals;
        async Task Refused(string key, Func<Task<AdminPortResult>> call, object row, Func<AdminPortState, bool> ok)
        {
            try
            {
                var (r, ms) = await Timed(call);
                refusals[key] = new { row, state = r.State.ToString(), r.Reason, ms };
                Expect(ok(r.State), $"ports: {key} not refused: {r.State}");
            }
            catch (Exception ex) { refusals[key] = ex.Message; Failures.Add($"ports: {key} threw {ex.Message}"); }
        }
        if (scanner.Scan(e => e.Protocol == PortProtocol.Tcp && e.LocalPort == 135 && e.IsHeld).FirstOrDefault() is { } rpc)
        {
            var row = new { holder = rpc.Holder.Name, kind = rpc.Holder.Kind.ToString(), rpc.Holder.StartTime, rpc.ServiceName };
            await Refused("coreService", () => helper.StopServiceAsync("RpcSs", Endpoint(rpc)), row, s => s == AdminPortState.Kept);
            await Refused("otherService", () => helper.StopServiceAsync("ChippiFixtureSvc", Endpoint(rpc)), row, s => s is AdminPortState.Kept or AdminPortState.Changed);
        }
        else refusals["coreService"] = "port 135 not listening";
        if (scanner.Scan(e => e.Protocol == PortProtocol.Tcp && e.Pid == 4 && e.IsHeld).FirstOrDefault() is { } sys)
            // System has no start time a user can send; any value must be refused before the helper compares it.
            await Refused("system", () => helper.EndProcessAsync(Endpoint(sys) with { StartTime = 1 }), new { sys.Endpoint.LocalPort }, s => s == AdminPortState.Kept);
        else refusals["system"] = "no System listener";
        await ender.EndAdminSessionAsync();
        ports["helperLaunches"] = launches() - before;
        Expect(launches() - before == 1, "ports: expected one helper session for every port action");
    }

    private static AdminEndpoint Endpoint(PortRow row) =>
        new(AdminProtocol.Tcp, row.Endpoint.LocalAddress.ToString(), row.Endpoint.LocalPort, row.Holder.Pid, row.Holder.StartTime);

    // ---- Cleaner ----

    private static async Task CleanerAsync(AdminHelper helper, Func<int> launches)
    {
        var cleaner = new Dictionary<string, object?>();
        Report["cleaner"] = cleaner;
        int before = launches();
        using var engine = new CleanerEngine(CleanerRoots.ForWindows(), helper);
        foreach (var c in CleanerCatalog.All) engine.SetTicked(c.Id, c.Id is not (CleanerCatalog.Apps or CleanerCatalog.Thumbnails));
        var watch = Stopwatch.StartNew();
        await engine.MeasureAsync();
        cleaner["measureMs"] = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var plan = await engine.PrepareCleanAsync();
        cleaner["prepareMs"] = watch.Elapsed.TotalMilliseconds;
        if (plan is null) { Failures.Add("cleaner: no plan"); return; }
        cleaner["plan"] = plan.Items.Select(i => new { i.Category.Id, i.Bytes, i.Files, i.LowerBound }).ToList();
        cleaner["adminDeclined"] = plan.AdminDeclined;
        cleaner["helperFailedAtPlan"] = plan.HelperFailed;
        Expect(!plan.AdminDeclined && !plan.HelperFailed, "cleaner: the helper measure failed");
        foreach (var id in new[] { CleanerCatalog.WindowsTemp, CleanerCatalog.WindowsUpdate, CleanerCatalog.DeliveryOptimization, CleanerCatalog.Dumps, CleanerCatalog.Temp })
            Expect(plan.Items.FirstOrDefault(i => i.Category.Id == id) is { Bytes: > 0 }, $"cleaner: {id} measured nothing");
        watch.Restart();
        var run = await engine.CleanAsync(plan);
        cleaner["cleanMs"] = watch.Elapsed.TotalMilliseconds;
        cleaner["freed"] = run.FreedBytes;
        cleaner["inUse"] = run.InUseFiles;
        cleaner["rows"] = run.Rows.Select(r => new { r.Category.Id, state = r.State.ToString(), r.Bytes, r.Files, r.InUseFiles }).ToList();
        Expect(!run.HelperFailed && !run.Cancelled, "cleaner: the clean failed or stopped");
        Expect(run.InUseFiles >= 1, "cleaner: the held file wasn't counted in use");
        foreach (var id in new[] { CleanerCatalog.WindowsTemp, CleanerCatalog.WindowsUpdate, CleanerCatalog.DeliveryOptimization, CleanerCatalog.Dumps, CleanerCatalog.Temp })
            Expect(run.Rows.FirstOrDefault(r => r.Category.Id == id)?.State == CleanerRowState.Cleaned, $"cleaner: {id} not Cleaned");
        cleaner["helperLaunches"] = launches() - before;
        Expect(launches() - before == 1, "cleaner: expected one helper session for measure and clean");
    }

    // ---- Uninstaller ----

    private static async Task UninstallerAsync(AdminHelper helper, Func<int> launches)
    {
        var un = new Dictionary<string, object?>();
        Report["uninstaller"] = un;
        string data = @"C:\results\uninstaller";
        Directory.CreateDirectory(data);
        using var engine = new UninstallerEngine(UninstallerSystem.ForWindows(), helper, new RecordStore(Path.Combine(data, "records.json")), LeftoverRoots.ForWindows(),
            Path.Combine(data, "backup"));
        var watch = Stopwatch.StartNew();
        await engine.RefreshAsync();
        un["listMs"] = watch.Elapsed.TotalMilliseconds;
        un["apps"] = engine.Apps.Count;
        AppEntry? App(string key) => engine.Apps.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        string[] keys = ["ChippiFixtureNotes", "ChippiFixtureRelaunch", "ChippiFixtureWritable", "Mozilla Firefox 999.0 (x64 en-US)", "ChippiFixtureUser"];
        var apps = keys.Select(App).ToList();
        un["found"] = keys.Zip(apps, (k, a) => new { k, source = a?.Source.ToString(), canUninstall = a?.CanUninstall }).ToList();
        if (apps.Any(a => a is null)) { Failures.Add("uninstaller: a fixture app is missing from the list"); return; }
        Expect(apps[4]!.Source == AppSource.User && apps.Take(4).All(a => a!.IsMachine), "uninstaller: fixture sources wrong");

        int before = launches();
        watch.Restart();
        var outcomes = await engine.UninstallAsync([.. apps!]);
        un["uninstallMs"] = watch.Elapsed.TotalMilliseconds;
        un["outcomes"] = outcomes.Select(o => new { o.App.Name, state = o.Status.State.ToString(), o.RecordId }).ToList();
        AppRunState? State(string key) => outcomes.FirstOrDefault(o => o.App.Key == key)?.Status.State;
        Expect(State("ChippiFixtureNotes") == AppRunState.Uninstalled, "uninstaller: Notes not Uninstalled");
        Expect(State("ChippiFixtureRelaunch") == AppRunState.Uninstalled, "uninstaller: the relaunching uninstaller not Uninstalled");
        Expect(State("ChippiFixtureWritable") == AppRunState.Refused, "uninstaller: the user-writable uninstaller not Refused");
        Expect(State("Mozilla Firefox 999.0 (x64 en-US)") == AppRunState.Uninstalled, "uninstaller: Firefox not Uninstalled");
        Expect(State("ChippiFixtureUser") == AppRunState.Uninstalled, "uninstaller: the user app not Uninstalled");
        un["uninstallLaunches"] = launches() - before;
        Expect(launches() - before == 1, "uninstaller: expected one helper session for the machine queue");
        await engine.EndAdminSessionAsync();
        await WaitHelperGoneAsync("uninstall");

        // Leftovers in a new session, after the uninstall session has ended (days later would be the same).
        var leftovers = new Dictionary<string, object?>();
        un["leftovers"] = leftovers;
        foreach (string name in new[] { "Fixture Notes", "Mozilla Firefox", "Fixture User Tool" })
        {
            var record = engine.Recent.FirstOrDefault(r => r.Name == name);
            if (record is null) { Failures.Add($"uninstaller: no record for {name}"); continue; }
            watch.Restart();
            var scan = await engine.ScanAsync(record);
            double scanMs = watch.Elapsed.TotalMilliseconds;
            if (scan is null) { Failures.Add($"uninstaller: no scan for {name}"); continue; }
            var ticked = scan.Items.Where(i => i.Class != LeftoverClass.Shared && !i.NoRecycleBin).Select(i => i.Id).ToList();
            int launchesBefore = launches();
            watch.Restart();
            var run = await engine.RemoveAsync(scan, ticked);
            double removeMs = watch.Elapsed.TotalMilliseconds;
            await engine.EndAdminSessionAsync();
            leftovers[name] = new
            {
                hasRecord = scan.HasRecord, scanMs, removeMs, run.BackupFolder, launches = launches() - launchesBefore,
                items = scan.Items.Select(i => new { i.Path, kind = i.Kind.ToString(), klass = i.Class.ToString(), i.Reason, i.SharedWith, i.NeedsAdmin, i.IsLink, i.NoRecycleBin }).ToList(),
                results = run.Results.Select(r => new { r.Item.Path, state = r.State.ToString(), r.Reason }).ToList(),
            };
            Expect(scan.HasRecord, $"uninstaller: {name} has no record");
            Expect(run.Results.All(r => r.State == LeftoverState.Removed), $"uninstaller: {name}: not every ticked leftover Removed");
            if (name == "Mozilla Firefox")
            {
                Expect(scan.Items.Where(i => i.Path.Contains(@"\ProgramData\Mozilla", StringComparison.OrdinalIgnoreCase) && !i.Path.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
                    .All(i => i.Class == LeftoverClass.Shared), "uninstaller: the shared Mozilla folder offered for Firefox");
                Expect(!scan.Items.Any(i => i.Path.Contains("Thunderbird", StringComparison.OrdinalIgnoreCase) && i.Class != LeftoverClass.Shared), "uninstaller: a Thunderbird item offered for Firefox");
            }
            if (name == "Fixture User Tool") Expect(launches() == launchesBefore, "uninstaller: the user app's leftovers used the helper");
            await WaitHelperGoneAsync("leftovers " + name);
        }

        // Dismiss: the helper drops its record of the relaunching app.
        if (engine.Recent.FirstOrDefault(r => r.Name == "Fixture Relaunch") is { } relaunch)
        {
            await engine.DismissAsync(relaunch);
            un["dismissed"] = engine.Recent.All(r => r.Id != relaunch.Id);
            Expect(engine.Recent.All(r => r.Id != relaunch.Id), "uninstaller: Dismiss kept the record");
        }
        else Failures.Add("uninstaller: no record for Fixture Relaunch");
        await engine.EndAdminSessionAsync();
    }
}
