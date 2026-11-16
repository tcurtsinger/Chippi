// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Chippi.UI.Admin;
using Chippi.Elevated.Protocol;
using Microsoft.Extensions.Logging;
using System.Security.Principal;

if (Environment.UserName != "WDAGUtilityAccount" || args.Length != 1 || args[0] is not ("--sandbox" or "--preflight" or "--handshake" or "--guard")) return 2;
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
    if (args[0] == "--guard")
    {
        var check = await helper.UpdatePackagesAsync(["Chippi.AdminHelperMissingFixture"]);
        bool accepted = check.Single() is { State: "Failed", ExitCode: not null };
        File.WriteAllText(@"C:\results\guard.json", JsonSerializer.Serialize(new { passed = accepted, results = check }));
        return accepted ? 0 : 1;
    }
    var watch = Stopwatch.StartNew();
    var results = await helper.UpdatePackagesAsync(["7zip.7zip", "Chippi.AdminHelperMissingFixture"]);
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
    public static WindowsAdminSession Create() => new(true, async (executable, name) =>
    {
        if (Environment.UserName != "WDAGUtilityAccount" || executable != @"C:\chippi\Chippi.Elevated.exe" || !Messages.PipeNameAllowed(name))
            throw new InvalidOperationException("Sandbox broker only.");
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
