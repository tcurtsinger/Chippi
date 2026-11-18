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

public sealed class LeftoverSessionTests
{
    private static readonly Guid Record = Guid.NewGuid();
    private static LeftoverItem WireFile(string path, string cls = Messages.ClassSafe, bool ticked = true) => new(Messages.KindFile, path, null, cls, ticked);
    private static AdminLeftoverItem File(string path, bool ticked = true) => new(AdminLeftoverKind.File, path, AdminLeftoverClass.Safe, ticked);
    private static AdminLeftoverItem Key(string path, AdminRegistryView view = AdminRegistryView.Registry64) => new(AdminLeftoverKind.RegistryKey, path, AdminLeftoverClass.CheckFirst, true, view);
    private static AdminRequest Request(params LeftoverItem[] items) => new(Guid.NewGuid(), Messages.DeleteLeftovers, RecordId: Record, Items: items);

    // ---- the protocol ----

    [Fact]
    public void A_leftover_request_is_a_record_id_and_between_one_and_two_hundred_valid_items_and_nothing_else()
    {
        var good = Request(WireFile(@"C:\Tools\App\a.dat"));
        Assert.True(Messages.RequestAllowed(good));
        Assert.True(Messages.RequestAllowed(good with { Items = [.. Enumerable.Range(0, 200).Select(i => WireFile($@"C:\Tools\App\{i}.dat"))] }));
        Assert.False(Messages.RequestAllowed(good with { Items = [.. Enumerable.Range(0, 201).Select(i => WireFile($@"C:\Tools\App\{i}.dat"))] }));
        Assert.False(Messages.RequestAllowed(good with { Items = [] }));
        Assert.False(Messages.RequestAllowed(good with { Items = null }));
        Assert.False(Messages.RequestAllowed(good with { Items = [null!] }));
        Assert.False(Messages.RequestAllowed(good with { RecordId = null }));
        Assert.False(Messages.RequestAllowed(good with { RecordId = Guid.Empty }));
        Assert.False(Messages.RequestAllowed(good with { Id = Guid.Empty }));
        Assert.False(Messages.RequestAllowed(good with { View = 64 }));
        Assert.False(Messages.RequestAllowed(good with { KeyName = "x" }));
        Assert.False(Messages.RequestAllowed(good with { PackageId = "a.b" }));
        Assert.False(Messages.RequestAllowed(good with { Category = "WindowsTemp" }));
        Assert.False(Messages.RequestAllowed(good with { ServiceName = "s" }));
        Assert.False(Messages.RequestAllowed(good with { Pid = 4 }));
        Assert.False(Messages.RequestAllowed(good with { IncludeUnknown = true }));
    }

    [Fact]
    public void An_item_has_a_kind_a_class_a_bounded_path_and_a_view_only_for_a_key()
    {
        Assert.True(Messages.LeftoverItemAllowed(WireFile(@"C:\a")));
        Assert.True(Messages.LeftoverItemAllowed(new(Messages.KindFolder, @"C:\a", null, Messages.ClassCheckFirst, false)));
        Assert.True(Messages.LeftoverItemAllowed(new(Messages.KindRegistryKey, @"Software\A", 32, Messages.ClassSafe, true)));
        Assert.True(Messages.LeftoverItemAllowed(WireFile(@"C:\" + new string('a', 397))));
        Assert.False(Messages.LeftoverItemAllowed(WireFile(@"C:\" + new string('a', 398))));
        Assert.False(Messages.LeftoverItemAllowed(WireFile("")));
        Assert.False(Messages.LeftoverItemAllowed(WireFile("C:\\a\u0001")));
        Assert.False(Messages.LeftoverItemAllowed(WireFile("C:\\a\nb")));
        Assert.False(Messages.LeftoverItemAllowed(new("Directory", @"C:\a", null, Messages.ClassSafe, true)));
        Assert.False(Messages.LeftoverItemAllowed(new(Messages.KindFile, @"C:\a", null, "Maybe", true)));
        Assert.False(Messages.LeftoverItemAllowed(new(Messages.KindFile, @"C:\a", 64, Messages.ClassSafe, true)));          // a file has no view
        Assert.False(Messages.LeftoverItemAllowed(new(Messages.KindRegistryKey, @"Software\A", null, Messages.ClassSafe, true)));
        Assert.False(Messages.LeftoverItemAllowed(new(Messages.KindRegistryKey, @"Software\A", 128, Messages.ClassSafe, true)));
        Assert.False(Messages.LeftoverItemAllowed(new(Messages.KindRegistryKey, @"\Software\A", 64, Messages.ClassSafe, true)));
        Assert.False(Messages.LeftoverItemAllowed(new(Messages.KindRegistryKey, @"Software\A\", 64, Messages.ClassSafe, true)));
    }

    [Fact]
    public async Task The_biggest_request_and_reply_fit_a_frame_even_with_every_backslash_escaped()
    {
        var request = Request([.. Enumerable.Range(0, 200).Select(_ => WireFile(@"C:\" + new string('\\', 397)))]);
        Assert.True(Messages.RequestAllowed(request));
        using var stream = new MemoryStream();
        await Messages.WriteAsync(stream, request);
        Assert.True(stream.Length < Messages.MaxBytes);
        var reply = new AdminReply(request.Id, Messages.LeftoversDone, Results: [.. Enumerable.Range(0, 200).Select(_ => new LeftoverResult(Messages.ItemKept, new string('x', 80)))]);
        stream.SetLength(0);
        await Messages.WriteAsync(stream, reply);
        Assert.True(stream.Length < Messages.MaxBytes);
    }

    [Fact]
    public void Drop_carries_a_record_id_and_nothing_else_and_no_other_verb_takes_one()
    {
        var drop = new AdminRequest(Guid.NewGuid(), Messages.DropRecord, RecordId: Record);
        Assert.True(Messages.RequestAllowed(drop));
        Assert.False(Messages.RequestAllowed(drop with { RecordId = null }));
        Assert.False(Messages.RequestAllowed(drop with { RecordId = Guid.Empty }));
        Assert.False(Messages.RequestAllowed(drop with { Items = [WireFile(@"C:\a")] }));
        Assert.False(Messages.RequestAllowed(drop with { KeyName = "x" }));
        var id = Guid.NewGuid();
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, "UpdatePackage", "a.b", "1.0", RecordId: Record)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, "Done", RecordId: Record)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.Uninstall, View: 64, KeyName: "x", RecordId: Record)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.Uninstall, View: 64, KeyName: "x", Items: [WireFile(@"C:\a")])));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.MeasureCategory, Category: "WindowsTemp", RecordId: Record)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.CleanCategory, Category: "WindowsTemp", Items: [WireFile(@"C:\a")])));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.CloseProcess, Protocol: "Tcp", LocalAddress: "127.0.0.1", LocalPort: 80, Pid: 9, StartTime: 1, RecordId: Record)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(id, Messages.StopService, Protocol: "Tcp", LocalAddress: "127.0.0.1", LocalPort: 80, Pid: 9, StartTime: 1, ServiceName: "s", Items: [WireFile(@"C:\a")])));
    }

    [Fact]
    public async Task Leftovers_round_trip_and_an_extra_duplicate_or_misspelled_field_is_refused()
    {
        using var stream = new MemoryStream();
        var request = Request(WireFile(@"C:\Tools\a.dat"), new(Messages.KindRegistryKey, @"Software\A", 32, Messages.ClassCheckFirst, true));
        await Messages.WriteAsync(stream, request); stream.Position = 0;
        var read = (await Messages.ReadRequestAsync(stream))!;
        Assert.Equal((request.Id, request.Verb, request.RecordId), (read.Id, read.Verb, read.RecordId));
        Assert.Equal(request.Items, read.Items);
        var reply = new AdminReply(request.Id, Messages.LeftoversDone, Results: [new(Messages.ItemRemoved), new(Messages.ItemKept, "Kept: no uninstall record")]);
        stream.SetLength(0); await Messages.WriteAsync(stream, reply); stream.Position = 0;
        var back = (await Messages.ReadReplyAsync(stream))!;
        Assert.Equal(reply.Results, back.Results);
        string head = "{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"DeleteLeftovers\",\"RecordId\":\"00000000-0000-0000-0000-000000000002\",";
        foreach (string json in new[]
        {
            head + "\"Items\":[{\"Kind\":\"File\",\"Path\":\"C:\\\\a\",\"Class\":\"Safe\",\"Ticked\":true,\"Command\":\"x\"}]}",        // an unknown field
            head + "\"Items\":[{\"Kind\":\"File\",\"Path\":\"C:\\\\a\",\"Path\":\"C:\\\\b\",\"Class\":\"Safe\",\"Ticked\":true}]}",       // a duplicate field (in an item)
            head + "\"Items\":[{\"Kind\":\"File\",\"Path\":\"C:\\\\a\",\"View\":null,\"Class\":\"Safe\",\"Ticked\":\"yes\"}]}",
            head + "\"Items\":[{\"Kind\":\"Registry\",\"Path\":\"Software\\\\a\",\"View\":64,\"Class\":\"Safe\",\"Ticked\":true}]}",
            head + "\"Items\":[null]}",
            head + "\"Items\":\"C:\\\\a\"}",
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
    public async Task A_malformed_leftover_request_ends_the_helper_session()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates(), leftovers: new Answering(r => Done(r))).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var bytes = Encoding.UTF8.GetBytes("{\"Id\":\"00000000-0000-0000-0000-000000000009\",\"Verb\":\"DeleteLeftovers\",\"RecordId\":\"00000000-0000-0000-0000-000000000002\",\"Items\":[]}");
            await client.WriteAsync(BitConverter.GetBytes(bytes.Length)); await client.WriteAsync(bytes); await client.FlushAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task A_session_without_leftover_actions_fails_the_request_and_still_ends_cleanly()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates()).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var request = Request(WireFile(@"C:\a"));
            await Messages.WriteAsync(client, request);
            var reply = (await Messages.ReadReplyAsync(client))!;
            Assert.Equal((request.Id, Messages.LeftoversFailed), (reply.Id, reply.State));
            var drop = new AdminRequest(Guid.NewGuid(), Messages.DropRecord, RecordId: Record);
            await Messages.WriteAsync(client, drop);
            Assert.Equal((drop.Id, Messages.LeftoversFailed), ((await Messages.ReadReplyAsync(client))!.Id, "Failed"));
            await Messages.WriteAsync(client, new AdminRequest(Guid.NewGuid(), "Done"));
            Assert.Equal("Done", (await Messages.ReadReplyAsync(client))!.State);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task The_real_actions_run_end_to_end_over_the_pipe_and_a_record_is_read_in_a_new_session()
    {
        var rig = new LeftoverRig();
        string a = rig.Exists(@"C:\Program Files\Mozilla Firefox\a.dll"), b = rig.Exists(@"C:\Program Files\Mozilla Firefox\b.dll");
        rig.Keys.Has(64, @"Software\Mozilla Firefox");
        await using var factory = new Factory(rig.Actions);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var first = await helper.DeleteLeftoversAsync(rig.Record.Id, [File(a), Key(@"Software\Mozilla Firefox")]);
        Assert.Equal([AdminLeftoverState.Removed, AdminLeftoverState.Removed], first.Select(r => r.State));
        await helper.EndSessionAsync();                                    // the first session ends; the record is the helper's, not the session's
        var second = await helper.DeleteLeftoversAsync(rig.Record.Id, [File(b)]);
        Assert.Equal(AdminLeftoverState.Removed, second[0].State);
        Assert.Equal(2, factory.Opens);
        Assert.True(await helper.DropRecordAsync(rig.Record.Id));
        var after = await helper.DeleteLeftoversAsync(rig.Record.Id, [File(b)]);
        Assert.Equal((AdminLeftoverState.Kept, "Kept: no uninstall record"), (after[0].State, after[0].Text));
        await helper.EndSessionAsync();
    }

    // ---- the client ----

    [Fact]
    public async Task Items_go_in_order_in_one_session_one_prompt_and_each_result_carries_its_item()
    {
        await using var factory = new Factory(new Answering(r => new(r.Id, Messages.LeftoversDone, Results:
            [.. r.Items!.Select(i => i.Path.EndsWith("keep.dat") ? new LeftoverResult(Messages.ItemKept, "Kept: shared with Mozilla Thunderbird") : new(Messages.ItemRemoved))])));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var items = new[] { File(@"C:\Tools\a.dat"), File(@"C:\Tools\keep.dat"), Key(@"Software\A", AdminRegistryView.Registry32) };
        var results = await helper.DeleteLeftoversAsync(Record, items);
        Assert.Equal(1, factory.Opens);
        var seen = Assert.Single(factory.Seen);
        Assert.Equal((Messages.DeleteLeftovers, Record), (seen.Verb, seen.RecordId));
        Assert.Equal([Messages.KindFile, Messages.KindFile, Messages.KindRegistryKey], seen.Items!.Select(i => i.Kind));
        Assert.Equal([null, null, 32], seen.Items!.Select(i => i.View));
        Assert.Equal([Messages.ClassSafe, Messages.ClassSafe, Messages.ClassCheckFirst], seen.Items!.Select(i => i.Class));
        Assert.Equal(items, results.Select(r => r.Item));
        Assert.Equal(["Removed", "Kept: shared with Mozilla Thunderbird", "Removed"], results.Select(r => r.Text));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task More_than_two_hundred_items_go_in_batches_in_the_same_session()
    {
        await using var factory = new Factory(new Answering(r => new(r.Id, Messages.LeftoversDone, Results: [.. r.Items!.Select(_ => new LeftoverResult(Messages.ItemRemoved))])));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var items = Enumerable.Range(0, 450).Select(i => File($@"C:\Tools\{i}.dat")).ToArray();
        var results = await helper.DeleteLeftoversAsync(Record, items);
        Assert.Equal(1, factory.Opens);
        Assert.Equal([200, 200, 50], factory.Seen.Select(r => r.Items!.Length));
        Assert.Equal(items, results.Select(r => r.Item));
        Assert.All(results, r => Assert.Equal(AdminLeftoverState.Removed, r.State));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task Each_state_maps_to_its_word_and_yours_is_left_to_chippi()
    {
        var states = new[] { Messages.ItemRemoved, Messages.ItemKept, Messages.ItemInUse, Messages.ItemFailed, Messages.ItemYours };
        await using var factory = new Factory(new Answering(r => new(r.Id, Messages.LeftoversDone, Results: [.. states.Select(s => new LeftoverResult(s, s == Messages.ItemKept ? "Kept: protected folder" : null))])));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var results = await helper.DeleteLeftoversAsync(Record, [.. states.Select((_, i) => File($@"C:\Tools\{i}.dat"))]);
        Assert.Equal([AdminLeftoverState.Removed, AdminLeftoverState.Kept, AdminLeftoverState.InUse, AdminLeftoverState.Failed, AdminLeftoverState.Yours], results.Select(r => r.State));
        Assert.Equal(["Removed", "Kept: protected folder", "In use", "Remove failed", ""], results.Select(r => r.Text));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task An_item_the_client_cant_describe_is_failed_without_being_sent_and_a_batch_of_only_those_opens_nothing()
    {
        await using var factory = new Factory(new Answering(r => new(r.Id, Messages.LeftoversDone, Results: [.. r.Items!.Select(_ => new LeftoverResult(Messages.ItemRemoved))])));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var tooLong = File(@"C:\" + new string('a', 500));
        var onlyBad = await helper.DeleteLeftoversAsync(Record, [tooLong]);
        Assert.Equal((AdminLeftoverState.Failed, "Remove failed"), (onlyBad[0].State, onlyBad[0].Text));
        Assert.Equal(0, factory.Opens);
        var mixed = await helper.DeleteLeftoversAsync(Record, [File(@"C:\Tools\a"), tooLong, new(AdminLeftoverKind.RegistryKey, @"Software\A", AdminLeftoverClass.Safe, true)]);
        Assert.Equal([AdminLeftoverState.Removed, AdminLeftoverState.Failed, AdminLeftoverState.Failed], mixed.Select(r => r.State));   // a key with no view can't be sent
        Assert.Single(factory.Seen.Single().Items!);
        Assert.Empty(await helper.DeleteLeftoversAsync(Record, []));
        await Assert.ThrowsAsync<ArgumentException>(() => helper.DeleteLeftoversAsync(Guid.Empty, [File(@"C:\a")]));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task A_declined_prompt_is_needs_admin_for_every_item_and_nothing_runs()
    {
        var declined = new RefusingFactory(new Win32Exception(1223));
        using var helper = new AdminHelper(declined, NullLogger.Instance);
        var items = new[] { File(@"C:\Tools\a"), Key(@"Software\A") };
        var results = await helper.DeleteLeftoversAsync(Record, items);
        Assert.All(results, r => Assert.Equal((AdminLeftoverState.NeedsAdmin, "Needs admin"), (r.State, r.Text)));
        Assert.Equal(1, declined.Opens);
        Assert.False(await helper.DropRecordAsync(Record));
        var absent = new RefusingFactory(new FileNotFoundException());
        using var missing = new AdminHelper(absent, NullLogger.Instance);
        Assert.All(await missing.DeleteLeftoversAsync(Record, items), r => Assert.Equal((AdminLeftoverState.Failed, "Admin helper failed"), (r.State, r.Reason)));
        Assert.False(await missing.DropRecordAsync(Record));
    }

    [Fact]
    public async Task Cancel_before_a_batch_is_sent_skips_it_and_never_cuts_one_in_flight()
    {
        using var cancel = new CancellationTokenSource();
        await using var factory = new Factory(new Answering(r => { cancel.Cancel(); return new(r.Id, Messages.LeftoversDone, Results: [.. r.Items!.Select(_ => new LeftoverResult(Messages.ItemRemoved))]); }));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var results = await helper.DeleteLeftoversAsync(Record, [.. Enumerable.Range(0, 250).Select(i => File($@"C:\Tools\{i}.dat"))], cancel.Token);
        Assert.Equal(200, results.Count(r => r.State == AdminLeftoverState.Removed));
        Assert.Equal(50, results.Count(r => r.State == AdminLeftoverState.Cancelled));
        Assert.Single(factory.Seen);
        using var already = new CancellationTokenSource(); already.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => helper.DeleteLeftoversAsync(Record, [File(@"C:\a")], already.Token));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task A_reply_that_doesnt_match_what_was_asked_fails_every_item_of_the_batch()
    {
        foreach (Func<AdminRequest, AdminReply> bad in new Func<AdminRequest, AdminReply>[]
        {
            r => new(Guid.NewGuid(), Messages.LeftoversDone, Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),   // wrong id
            r => new(r.Id, Messages.LeftoversDone, Results: [new(Messages.ItemRemoved)]),                                       // one result short
            r => new(r.Id, Messages.LeftoversDone, Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),
            r => new(r.Id, Messages.LeftoversDone, Results: [new(Messages.ItemRemoved), new("Deleted")]),                        // a state that isn't an item's
            r => new(r.Id, Messages.LeftoversDone, Results: [new(Messages.ItemRemoved), null!]),
            r => new(r.Id, Messages.LeftoversDone),                                                                               // no results
            r => new(r.Id, "Measured", Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),
            r => new(r.Id, Messages.LeftoversDone, ExitCode: 0, Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),
            r => new(r.Id, Messages.LeftoversDone, RecordId: Guid.NewGuid(), Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),
            r => new(r.Id, Messages.LeftoversDone, PackageId: "a.b", Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),
            r => new(r.Id, Messages.LeftoversFailed, Results: [new(Messages.ItemRemoved), new(Messages.ItemRemoved)]),            // a failure carries no results
        })
        {
            await using var factory = new Factory(new Answering(bad));
            using var helper = new AdminHelper(factory, NullLogger.Instance);
            var results = await helper.DeleteLeftoversAsync(Record, [File(@"C:\Tools\a"), File(@"C:\Tools\b")]);
            Assert.All(results, r => Assert.Equal(AdminLeftoverState.Failed, r.State));
        }
    }

    [Fact]
    public async Task A_failed_batch_answer_and_a_long_reason_are_handled()
    {
        await using var failed = new Factory(new Answering(r => new(r.Id, Messages.LeftoversFailed, Reason: "Admin helper failed")));
        using var helper = new AdminHelper(failed, NullLogger.Instance);
        var results = await helper.DeleteLeftoversAsync(Record, [File(@"C:\Tools\a")]);
        Assert.Equal(AdminLeftoverState.Failed, results[0].State);
        await helper.EndSessionAsync();
        await using var wordy = new Factory(new Answering(r => new(r.Id, Messages.LeftoversDone, Results: [new(Messages.ItemKept, new string('x', 200)), new(Messages.ItemKept, "Kept:\u0001 x")])));
        using var other = new AdminHelper(wordy, NullLogger.Instance);
        var shown = await other.DeleteLeftoversAsync(Record, [File(@"C:\Tools\a"), File(@"C:\Tools\b")]);
        Assert.All(shown, r => Assert.Equal(("Kept", null), (r.Text, r.Reason)));   // the helper's words are shown only when short and plain
        await other.EndSessionAsync();
    }

    [Fact]
    public async Task Drop_is_one_session_and_true_when_the_helper_says_dropped()
    {
        await using var factory = new Factory(new Answering(r => new(r.Id, r.Verb == Messages.DropRecord ? Messages.RecordDropped : Messages.LeftoversFailed)));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        Assert.True(await helper.DropRecordAsync(Record));
        Assert.Equal((Messages.DropRecord, Record), (factory.Seen[0].Verb, factory.Seen[0].RecordId));
        Assert.Null(factory.Seen[0].Items);
        await Assert.ThrowsAsync<ArgumentException>(() => helper.DropRecordAsync(Guid.Empty));
        await helper.EndSessionAsync();
        await using var failing = new Factory(new Answering(r => new(r.Id, Messages.LeftoversFailed, Reason: "Records unavailable")));
        using var other = new AdminHelper(failing, NullLogger.Instance);
        Assert.False(await other.DropRecordAsync(Record));
        await other.EndSessionAsync();
        await using var odd = new Factory(new Answering(r => new(r.Id, Messages.RecordDropped, Results: [new(Messages.ItemRemoved)])));
        using var third = new AdminHelper(odd, NullLogger.Instance);
        Assert.False(await third.DropRecordAsync(Record));   // a drop answer with results is invalid
    }

    [Fact]
    public async Task The_log_names_no_path_no_key_and_no_record()
    {
        var log = new CaptureLogger();
        await using var factory = new Factory(new Answering(r => r.Verb == Messages.DropRecord ? new(r.Id, Messages.RecordDropped) : Done(r)));
        using var helper = new AdminHelper(factory, log);
        await helper.DeleteLeftoversAsync(Record, [File(@"C:\SecretFolder\a.dat"), Key(@"Software\SecretVendor")]);
        await helper.DropRecordAsync(Record);
        await helper.EndSessionAsync();
        Assert.NotEmpty(log.Lines);
        Assert.DoesNotContain(log.Lines, l => l.Contains("Secret") || l.Contains(@"C:\") || l.Contains(Record.ToString()));
    }

    [Fact]
    public async Task The_default_helper_answers_needs_admin_and_false_without_running_anything()
    {
        IAdminHelper stub = new DefaultHelper();
        var results = await stub.DeleteLeftoversAsync(Record, [File(@"C:\a"), File(@"C:\b")]);
        Assert.All(results, r => Assert.Equal(AdminLeftoverState.NeedsAdmin, r.State));
        Assert.False(await stub.DropRecordAsync(Record));
    }

    // ---- plumbing ----

    private static AdminReply Done(AdminRequest r) => new(r.Id, Messages.LeftoversDone, Results: [.. r.Items!.Select(_ => new LeftoverResult(Messages.ItemRemoved))]);

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

    private sealed class NoUpdates : IPackageUpdater
    { public Task<AdminReply> UpdateAsync(AdminRequest request, CancellationToken ceiling) => throw new InvalidOperationException(); }

    private sealed class Answering(Func<AdminRequest, AdminReply> reply) : ILeftoverActions
    {
        public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) => Task.FromResult(reply(request));
    }

    private sealed class Spy(ILeftoverActions inner, Action<AdminRequest> seen) : ILeftoverActions
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
        private readonly ILeftoverActions _actions;
        public Factory(ILeftoverActions actions) => _actions = new Spy(actions, r => { lock (Seen) Seen.Add(r); });
        public int Opens;
        public List<AdminRequest> Seen { get; } = [];
        public Task Finished { get; private set; } = Task.CompletedTask;
        private readonly List<IDisposable> _ends = [];
        public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
        {
            Opens++;
            var (client, server) = await Pair();
            _ends.Add(client); _ends.Add(server);
            Finished = new HelperSession(new NoUpdates(), leftovers: _actions).RunAsync(client);
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
