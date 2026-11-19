// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO.Pipes;
using System.Text;
using Chippi.Core.Admin;
using Chippi.Elevated.Protocol;
using Chippi.UI.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Elevated.Tests;

public sealed class UninstallSessionTests
{
    private const string Msi = "{3F2504E0-4F89-11D3-9A0C-0305E82C3301}";
    private static AdminUninstallItem Item(string key, string? name = null, AdminRegistryView view = AdminRegistryView.Registry64) => new(view, key, name ?? key);

    private static AdminReply Done(AdminRequest r) => new(r.Id, Messages.UninstallDone, ExitCode: 0, RecordId: Guid.NewGuid(), KeyGone: true);

    // ---- the protocol ----

    [Fact]
    public void An_uninstall_request_is_a_view_and_a_key_name_and_nothing_else()
    {
        var good = new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 64, KeyName: Msi);
        Assert.True(Messages.RequestAllowed(good));
        Assert.True(Messages.RequestAllowed(good with { View = 32, KeyName = "Notepad++" }));
        Assert.False(Messages.RequestAllowed(good with { View = 0 }));
        Assert.False(Messages.RequestAllowed(good with { View = 128 }));
        Assert.False(Messages.RequestAllowed(good with { View = null }));
        Assert.False(Messages.RequestAllowed(good with { KeyName = null }));
        Assert.False(Messages.RequestAllowed(good with { KeyName = "" }));
        Assert.False(Messages.RequestAllowed(good with { KeyName = @"A\B" }));              // can't leave the Uninstall key
        Assert.False(Messages.RequestAllowed(good with { KeyName = "A\u0001B" }));          // control characters
        Assert.False(Messages.RequestAllowed(good with { KeyName = "A\nB" }));
        Assert.True(Messages.RequestAllowed(good with { KeyName = new string('a', 255) }));
        Assert.False(Messages.RequestAllowed(good with { KeyName = new string('a', 256) }));
        Assert.False(Messages.RequestAllowed(good with { PackageId = "a.b" }));
        Assert.False(Messages.RequestAllowed(good with { Category = "WindowsTemp" }));
        Assert.False(Messages.RequestAllowed(good with { ServiceName = "x" }));
        Assert.False(Messages.RequestAllowed(good with { Pid = 4 }));
        Assert.False(Messages.RequestAllowed(good with { IncludeUnknown = true }));
    }

    [Fact]
    public void No_other_verb_accepts_a_view_or_a_key_name()
    {
        var id = Guid.NewGuid();
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, "UpdatePackage", "a.b", "1.0", View: 64)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, "UpdatePackage", "a.b", "1.0", KeyName: "x")));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, "Done", View: 64)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.MeasureCategory, Category: "WindowsTemp", KeyName: "x")));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.CleanCategory, Category: "WindowsTemp", View: 64)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.CloseProcess, Protocol: "Tcp", LocalAddress: "127.0.0.1", LocalPort: 80, Pid: 9, StartTime: 1, KeyName: "x")));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.StopService, Protocol: "Tcp", LocalAddress: "127.0.0.1", LocalPort: 80, Pid: 9, StartTime: 1, ServiceName: "s", View: 32)));
    }

    [Fact]
    public async Task Uninstall_round_trips_and_an_extra_or_duplicate_field_is_refused()
    {
        using var stream = new MemoryStream();
        var request = new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 32, KeyName: Msi);
        await Messages.WriteAsync(stream, request); stream.Position = 0;
        Assert.Equal(request, await Messages.ReadRequestAsync(stream));
        var reply = new AdminReply(request.Id, Messages.UninstallRestart, ExitCode: 3010, RecordId: Guid.NewGuid(), KeyGone: false);
        stream.SetLength(0); await Messages.WriteAsync(stream, reply); stream.Position = 0;
        Assert.Equal(reply, await Messages.ReadReplyAsync(stream));
        foreach (string json in new[]
        {
            "{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Uninstall\",\"View\":64,\"KeyName\":\"a\",\"CommandLine\":\"x.exe\"}",   // the helper takes no command from Chippi
            "{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Uninstall\",\"View\":64,\"View\":32,\"KeyName\":\"a\"}",
            "{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Uninstall\",\"View\":64,\"KeyName\":\"a\\\\b\"}",
            "{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Uninstall\",\"View\":\"64\",\"KeyName\":\"a\"}",
        })
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using var raw = new MemoryStream();
            raw.Write(BitConverter.GetBytes(bytes.Length)); raw.Write(bytes); raw.Position = 0;
            await Assert.ThrowsAnyAsync<Exception>(() => Messages.ReadRequestAsync(raw));
        }
    }

    // ---- the helper's session ----

    [Fact]
    public async Task A_malformed_uninstall_request_ends_the_helper_session()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates(), uninstalls: new Answering(Done)).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var bytes = Encoding.UTF8.GetBytes("{\"Id\":\"00000000-0000-0000-0000-000000000009\",\"Verb\":\"Uninstall\",\"View\":64,\"KeyName\":\"a\\\\..\\\\b\"}");
            await client.WriteAsync(BitConverter.GetBytes(bytes.Length)); await client.WriteAsync(bytes); await client.FlushAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task A_session_without_uninstall_actions_fails_the_request_and_still_ends_cleanly()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates()).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var request = new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 64, KeyName: "a");
            await Messages.WriteAsync(client, request);
            var reply = (await Messages.ReadReplyAsync(client))!;
            Assert.Equal((request.Id, Messages.UninstallFailed), (reply.Id, reply.State));
            await Messages.WriteAsync(client, new AdminRequest(Guid.NewGuid(), "Done"));
            Assert.Equal("Done", (await Messages.ReadReplyAsync(client))!.State);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task The_real_actions_run_end_to_end_over_the_pipe_on_fakes()
    {
        const string exe = @"C:\Program Files\Fixture App\unins000.exe";
        var registry = new FakeRegistry(); var probe = new FakeProbe().Trusting(exe); var host = new FakeHost(); host.Files.Add(exe);
        registry.Keys[(64, "FixtureApp")] = new(false, null, null, null, "Fixture App", "Co", null, null, @"""" + exe + @""" /S");
        var launcher = new FakeLauncher(probe, registry) { RemoveKeyWhenDone = (64, "FixtureApp") };
        var records = new FakeRecords();
        var actions = new UninstallActions(registry, records, probe, launcher, host);
        await using var factory = new Factory(actions);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var result = await helper.UninstallAsync(AdminRegistryView.Registry64, "FixtureApp", "Fixture App");
        Assert.Equal((AdminUninstallState.Uninstalled, 0, true), (result.State, result.ExitCode, result.KeyGone));
        Assert.Equal(records.All.Single().Id, result.RecordId);
        Assert.Equal("Uninstalled", result.Text);
        await helper.EndSessionAsync();
    }

    // ---- the client ----

    [Fact]
    public async Task A_queue_is_one_session_one_prompt_run_in_order_with_a_record_id_each()
    {
        var ids = new Dictionary<string, Guid>();
        await using var factory = new Factory(new Answering(r => { var id = Guid.NewGuid(); ids[r.KeyName!] = id; return new(r.Id, Messages.UninstallDone, ExitCode: 0, RecordId: id, KeyGone: true); }));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var seen = new System.Collections.Concurrent.ConcurrentBag<AdminUninstallProgress>();
        var results = await helper.UninstallAsync([Item("A"), Item("B", view: AdminRegistryView.Registry32), Item(Msi)], new Progress<AdminUninstallProgress>(seen.Add));
        Assert.Equal(1, factory.Opens);
        Assert.Equal(["A", "B", Msi], factory.Seen.Select(r => r.KeyName));
        Assert.Equal([64, 32, 64], factory.Seen.Select(r => r.View));
        Assert.All(factory.Seen, r => Assert.Equal(Messages.Uninstall, r.Verb));
        Assert.All(results, r => Assert.Equal((AdminUninstallState.Uninstalled, true), (r.State, r.KeyGone)));
        Assert.Equal([ids["A"], ids["B"], ids[Msi]], results.Select(r => r.RecordId!.Value));
        for (int i = 0; i < 100 && seen.Count < 6; i++) await Task.Delay(50);
        Assert.Equal(6, seen.Count);
        await helper.EndSessionAsync();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_duplicate_in_the_queue_runs_once_and_arguments_are_checked_before_any_prompt()
    {
        await using var factory = new Factory(new Answering(Done));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var results = await helper.UninstallAsync([Item("A"), Item("a")]);   // key names are case-insensitive
        Assert.Single(results);
        await Assert.ThrowsAsync<ArgumentException>(() => helper.UninstallAsync([Item(@"A\B")]));
        await Assert.ThrowsAsync<ArgumentException>(() => helper.UninstallAsync([new AdminUninstallItem((AdminRegistryView)7, "A")]));
        Assert.Empty(await helper.UninstallAsync([]));
        Assert.Equal(1, factory.Opens);
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task Each_state_maps_to_its_word()
    {
        var replies = new Dictionary<string, Func<AdminRequest, AdminReply>>
        {
            ["restart"] = r => new(r.Id, Messages.UninstallRestart, ExitCode: 3010, RecordId: Guid.NewGuid(), KeyGone: false),
            ["failed"] = r => new(r.Id, Messages.UninstallFailed, ExitCode: 1603, RecordId: Guid.NewGuid(), KeyGone: false),
            ["refused"] = r => new(r.Id, Messages.UninstallRefused, Reason: "Registry link"),
            ["done"] = Done,
        };
        await using var factory = new Factory(new Answering(r => replies[r.KeyName!](r)));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var results = await helper.UninstallAsync([Item("restart"), Item("failed"), Item("refused"), Item("done")]);
        Assert.Equal(["Restart to finish", "Uninstall failed", "Refused", "Uninstalled"], results.Select(r => r.Text));
        Assert.Equal((3010, false), (results[0].ExitCode, results[0].KeyGone));
        Assert.Equal((1603, false), (results[1].ExitCode, results[1].KeyGone));
        Assert.Equal(("Registry link", null), (results[2].Reason, results[2].RecordId));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task A_declined_prompt_is_needs_admin_for_every_item_and_nothing_runs()
    {
        var declined = new RefusingFactory(new Win32Exception(1223));
        using var helper = new AdminHelper(declined, NullLogger.Instance);
        var results = await helper.UninstallAsync([Item("A"), Item("B")]);
        Assert.All(results, r => Assert.Equal((AdminUninstallState.NeedsAdmin, "Needs admin"), (r.State, r.Text)));
        Assert.Equal(["A", "B"], results.Select(r => r.Item.KeyName));
        Assert.Equal(1, declined.Opens);
        var single = await ((IAdminHelper)helper).UninstallAsync(AdminRegistryView.Registry64, "A");
        Assert.Equal(AdminUninstallState.NeedsAdmin, single.State);
        var absent = new RefusingFactory(new FileNotFoundException());
        using var missing = new AdminHelper(absent, NullLogger.Instance);
        Assert.All(await missing.UninstallAsync([Item("A")]), r => Assert.Equal((AdminUninstallState.Failed, "Admin helper failed"), (r.State, r.Reason)));
    }

    [Fact]
    public async Task Cancel_skips_the_apps_not_yet_started_and_never_cuts_the_one_running()
    {
        using var cancel = new CancellationTokenSource();
        await using var factory = new Factory(new Answering(r => { if (r.KeyName == "A") cancel.Cancel(); return Done(r); }));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var results = await helper.UninstallAsync([Item("A"), Item("B"), Item("C")], ct: cancel.Token);
        Assert.Equal([AdminUninstallState.Uninstalled, AdminUninstallState.Cancelled, AdminUninstallState.Cancelled], results.Select(r => r.State));
        Assert.Equal(["A"], factory.Seen.Select(r => r.KeyName));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task A_cancelled_call_never_opens_a_session()
    {
        await using var factory = new Factory(new Answering(Done));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => helper.UninstallAsync([Item("A")], ct: cancel.Token));
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task A_reply_that_doesnt_match_what_was_asked_fails_the_item()
    {
        foreach (Func<AdminRequest, AdminReply> bad in new Func<AdminRequest, AdminReply>[]
        {
            r => new(Guid.NewGuid(), Messages.UninstallDone, ExitCode: 0),                                     // wrong id
            r => new(r.Id, "Measured", ExitCode: 0),                                                           // a state that isn't Uninstall's
            r => new(r.Id, Messages.UninstallDone, ExitCode: 5),                                               // Uninstalled with a failing exit
            r => new(r.Id, Messages.UninstallRestart, ExitCode: 0),                                            // Restart without 3010
            r => new(r.Id, Messages.UninstallDone, ExitCode: 0, PackageId: "a.b"),
            r => new(r.Id, Messages.UninstallDone, ExitCode: 0, Category: "WindowsTemp"),
            r => new(r.Id, Messages.UninstallDone, ExitCode: 0, Bytes: 5),
            r => new(r.Id, Messages.UninstallRefused, Reason: "x", RecordId: Guid.NewGuid()),                   // a refusal wrote no record
        })
        {
            await using var factory = new Factory(new Answering(bad));
            using var helper = new AdminHelper(factory, NullLogger.Instance);
            Assert.Equal(AdminUninstallState.Failed, (await helper.UninstallAsync([Item("A")]))[0].State);
        }
    }

    [Fact]
    public async Task An_idle_session_the_helper_left_is_replaced_by_one_fresh_one()
    {
        var clock = new Clock();
        await using var factory = new Factory(new Answering(Done));
        using var helper = new AdminHelper(factory, NullLogger.Instance, sessionIdle: TimeSpan.FromSeconds(50), time: clock);
        await helper.UninstallAsync([Item("A")]);
        clock.Advance(TimeSpan.FromSeconds(60));
        await helper.UninstallAsync([Item("B")]);
        Assert.Equal(2, factory.Opens);
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task The_log_names_the_app_and_never_the_key_or_a_path()
    {
        var log = new CaptureLogger();
        await using var factory = new Factory(new Answering(Done));
        using var helper = new AdminHelper(factory, log);
        await helper.UninstallAsync([new AdminUninstallItem(AdminRegistryView.Registry64, "SecretKey_{1234}", "Notepad++")]);
        await helper.EndSessionAsync();
        Assert.Contains(log.Lines, l => l.Contains("Notepad++"));
        Assert.DoesNotContain(log.Lines, l => l.Contains("SecretKey") || l.Contains(@"C:\"));
    }

    [Fact]
    public async Task The_default_helper_answers_needs_admin_without_running_anything()
    {
        IAdminHelper stub = new DefaultHelper();
        var results = await stub.UninstallAsync([Item("A"), Item("B")]);
        Assert.All(results, r => Assert.Equal(AdminUninstallState.NeedsAdmin, r.State));
        Assert.Equal(AdminUninstallState.NeedsAdmin, (await stub.UninstallAsync(AdminRegistryView.Registry32, "A")).State);
    }

    // ---- plumbing ----

    private static async Task<(NamedPipeClientStream Client, NamedPipeServerStream Helper)> Pair()
    {
        string name = "chippi-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(); await client.ConnectAsync(5000); await accept;
        return (client, server);
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { lock (Lines) Lines.Add(formatter(state, exception)); }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ms;
        public override long GetTimestamp() => Interlocked.Read(ref _ms);
        public override long TimestampFrequency => 1000;
        public void Advance(TimeSpan span) => Interlocked.Add(ref _ms, (long)span.TotalMilliseconds);
    }

    private sealed class NoUpdates : IPackageUpdater
    { public Task<AdminReply> UpdateAsync(AdminRequest request, CancellationToken ceiling) => throw new InvalidOperationException(); }

    private sealed class Answering(Func<AdminRequest, AdminReply> reply, Action<AdminRequest>? seen = null) : IUninstallActions
    {
        public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) { seen?.Invoke(request); return Task.FromResult(reply(request)); }
    }

    private sealed class Spy(IUninstallActions inner, Action<AdminRequest> seen) : IUninstallActions
    {
        public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) { seen(request); return inner.RunAsync(request, ct); }
    }

    private sealed class RefusingFactory(Exception error) : IAdminSessionFactory
    {
        public int Opens;
        public Task<IAdminConnection> OpenAsync(CancellationToken ct) { Opens++; return Task.FromException<IAdminConnection>(error); }
    }

    private sealed class DefaultHelper : IAdminHelper
    {
        public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Factory : IAdminSessionFactory, IAsyncDisposable
    {
        private readonly IUninstallActions _actions;
        public Factory(IUninstallActions actions)
        {
            // Every request that reaches the helper's actions is noted, whatever answers it.
            _actions = new Spy(actions, r => { lock (Seen) Seen.Add(r); });
        }
        public int Opens;
        public List<AdminRequest> Seen { get; } = [];
        public Task Finished { get; private set; } = Task.CompletedTask;
        private readonly List<IDisposable> _ends = [];
        public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
        {
            Opens++;
            var (client, server) = await Pair();
            _ends.Add(client); _ends.Add(server);
            Finished = new HelperSession(new NoUpdates(), uninstalls: _actions).RunAsync(client);
            return new Connection(server);
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var end in _ends) end.Dispose();
            try { await Finished; } catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException) { }
        }
        private sealed class Connection(Stream stream) : IAdminConnection
        { public Stream Stream => stream; public ValueTask DisposeAsync() { stream.Dispose(); return ValueTask.CompletedTask; } }
    }
}
