// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Chippi.UI.Admin;
using Chippi.Elevated.Protocol;
using Microsoft.Extensions.Logging;
using System.Security.Principal;
using Chippi.Features.AppUpdates;

if (Environment.UserName != "WDAGUtilityAccount" || args.Length != 1 || args[0] is not ("--sandbox" or "--preflight" or "--handshake" or "--guard" or "--app-updates" or "--tools")) return 2;
string report = @"C:\results\helper.json";
try
{
    if (args[0] == "--preflight")
    {
        using var identity = WindowsIdentity.GetCurrent();
        bool elevated = PeerIdentity.CurrentIsElevated();
        bool administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        File.WriteAllText(@"C:\results\token.json", JsonSerializer.Serialize(new { elevated, administrator }));
        if (elevated || administrator) throw new InvalidOperationException("Probe token must be a standard user token.");
        using var pipe = LocalPipe.Create("chippi-admin-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(@"C:\results\preflight.json", "{\"passed\":true,\"elevated\":false}");
        return 0;
    }
    if (PeerIdentity.CurrentIsElevated()) throw new InvalidOperationException("Probe must run at medium integrity.");
    if (args[0] == "--handshake")
    {
        var elapsed = Stopwatch.StartNew();
        await using var connection = await BrokerFactory.Create().OpenAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ready = await Messages.ReadReplyAsync(connection.Stream, timeout.Token);
        if (ready?.State != "Ready") throw new InvalidDataException("Helper greeting failed.");
        double connectedMs = elapsed.Elapsed.TotalMilliseconds;
        var done = new AdminRequest(Guid.NewGuid(), "Done");
        await Messages.WriteAsync(connection.Stream, done, timeout.Token);
        var answer = await Messages.ReadReplyAsync(connection.Stream, timeout.Token);
        if (answer?.Id != done.Id || answer.State != "Done") throw new InvalidDataException("Helper shutdown failed.");
        File.WriteAllText(@"C:\results\handshake.json", JsonSerializer.Serialize(new { passed = true, connectedMs }));
        return 0;
    }
    using var helper = new AdminHelper(BrokerFactory.Create(), new ProbeLogger());
    if (args[0] == "--tools")
    {
        bool toolsPassed = await ToolsCheck.RunAsync(helper, () => BrokerFactory.LaunchCount);
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = toolsPassed }));
        return toolsPassed ? 0 : 1;
    }
    if (args[0] == "--app-updates")
    {
        var source = new SandboxUpdatesSource();
        var stateGate = new object();
        var engine = new AppUpdatesEngine(source, helper, new JsonAppUpdatesStore(@"C:\results\updates-data"),
            dispatch: action => { lock (stateGate) action(); });
        var discovery = Stopwatch.StartNew();
        await engine.CheckAsync();
        double checkMs = discovery.Elapsed.TotalMilliseconds;
        if (engine.Updates.Count != 3 || source.Fixture?.Scope != UpdateScope.Machine || source.UserFixture?.Scope != UpdateScope.User || BrokerFactory.LaunchCount != 0)
            throw new InvalidOperationException("Read-only app discovery or machine scope failed: " + engine.Error);
        var confirmed = engine.CaptureBatch();
        var elapsed = Stopwatch.StartNew();
        await engine.RunAsync(confirmed);
        bool appPassed = confirmed.Packages.Count == 3 && BrokerFactory.LaunchCount == 1 &&
            engine.Rows.Single(r => r.Package.Id == "7zip.7zip").State == UpdateState.Updated &&
            engine.Rows.Single(r => r.Package.Id == "SumatraPDF.SumatraPDF").State == UpdateState.Updated &&
            engine.Rows.Single(r => r.Package.Id == "Chippi.AdminHelperMissingFixture").State == UpdateState.Failed &&
            engine.Preferences.History.Count == 3;
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = appPassed, checkMs, milliseconds = elapsed.Elapsed.TotalMilliseconds,
            scope = source.Fixture.Scope.ToString(), userScope = source.UserFixture.Scope.ToString(), userVersion = source.UserFixture.AvailableVersion, helperLaunches = BrokerFactory.LaunchCount,
            rows = engine.Rows, historyCount = engine.Preferences.History.Count }));
        return appPassed ? 0 : 1;
    }
    if (args[0] == "--guard")
    {
        var check = await helper.UpdatePackagesAsync([new("Chippi.AdminHelperMissingFixture", "1.0.0")]);
        bool accepted = check.Single() is { State: "Failed", ExitCode: not null };
        File.WriteAllText(@"C:\results\guard.json", JsonSerializer.Serialize(new { passed = accepted, results = check }));
        return accepted ? 0 : 1;
    }
    var watch = Stopwatch.StartNew();
    var results = await helper.UpdatePackagesAsync([new("7zip.7zip", Environment.GetEnvironmentVariable("CHIPPI_PROBE_7ZIP_VERSION") ?? "24.09"), new("Chippi.AdminHelperMissingFixture", "1.0.0")]);
    bool passed = results.Count == 2 && results[0].State == "Done" && results[1].State == "Failed";
    File.WriteAllText(report, JsonSerializer.Serialize(new { passed, milliseconds = watch.Elapsed.TotalMilliseconds, results }));
    return passed ? 0 : 1;
}
catch (Exception ex) { File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() })); return 1; }

sealed class ProbeLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) =>
        File.AppendAllText(@"C:\results\client.log", format(state, error) + Environment.NewLine + error + Environment.NewLine);
}

static class BrokerFactory
{
    public static int LaunchCount;
    public static WindowsAdminSession Create() => new(true, async (executable, name) =>
    {
        if (Environment.UserName != "WDAGUtilityAccount" || executable != @"C:\chippi\Chippi.Elevated.exe" || !Messages.PipeNameAllowed(name))
            throw new InvalidOperationException("Sandbox broker only.");
        Interlocked.Increment(ref LaunchCount);
        string response = @"C:\results\" + name + ".pid";
        string request = @"C:\results\broker-" + name;
        File.WriteAllText(request + ".tmp", name);
        File.Move(request + ".tmp", request + ".request");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(response)) await Task.Delay(10, timeout.Token);
        int pid = int.Parse(File.ReadAllText(response));
        return Process.GetProcessById(pid);
    });
}

sealed class SandboxUpdatesSource : IPackageSource
{
    private readonly WindowsPackageSource _source = new();
    public PackageUpdate? Fixture { get; private set; }
    public PackageUpdate? UserFixture { get; private set; }
    public async Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default)
    {
        var found = await _source.CheckAsync(includeUnknown, ct);
        Fixture = found.SingleOrDefault(p => p.Id.Equals("7zip.7zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Installed 7-Zip fixture upgrade missing from COM discovery.");
        UserFixture = found.SingleOrDefault(p => p.Id.Equals("SumatraPDF.SumatraPDF", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Installed user-scope SumatraPDF fixture upgrade missing from COM discovery.");
        return [Fixture, UserFixture, new("Chippi.AdminHelperMissingFixture", "Missing fixture", "1", "2", "winget", UpdateScope.Machine)];
    }
    public Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default) =>
        package.Id == "SumatraPDF.SumatraPDF" && package.Scope == UpdateScope.User
            ? _source.UpdateAsync(package, progress, ct)
            : throw new InvalidOperationException("Only the disposable SumatraPDF user fixture may use COM installation.");
}
