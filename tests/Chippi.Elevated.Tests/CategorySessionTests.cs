// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO.Pipes;
using System.Text;
using Chippi.Core.Admin;
using Chippi.Elevated.Protocol;
using Chippi.UI.Admin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Elevated.Tests;

public sealed class CategorySessionTests
{
    private static readonly AdminCategory[] Two = [AdminCategory.WindowsTemp, AdminCategory.SystemCrashDumps];

    private static AdminReply Answer(AdminRequest r) => r.Verb == Messages.MeasureCategory
        ? new(r.Id, Messages.CategoryMeasured, Category: r.Category, Bytes: 1000, Files: 3, InUse: 0)
        : new(r.Id, Messages.CategoryCleaned, Category: r.Category, Bytes: 900, Files: 2, InUse: 1);

    [Fact]
    public async Task Measure_then_clean_share_one_session_and_one_prompt_and_the_clean_ends_it()
    {
        await using var factory = new Factory(Answer);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var measured = await helper.MeasureCategoriesAsync(Two);
        Assert.Equal([new AdminCategoryResult(AdminCategory.WindowsTemp, AdminCategoryState.Measured, 1000, 3), new AdminCategoryResult(AdminCategory.SystemCrashDumps, AdminCategoryState.Measured, 1000, 3)], measured);
        Assert.Equal(1, factory.Opens);
        Assert.False(factory.Finished.IsCompleted);   // waiting for the owner's answer
        var seen = new System.Collections.Concurrent.ConcurrentBag<AdminCategoryProgress>();
        var cleaned = await helper.CleanCategoriesAsync(Two, new Progress<AdminCategoryProgress>(seen.Add));
        Assert.Equal(1, factory.Opens);               // no second prompt
        Assert.All(cleaned, r => Assert.Equal((AdminCategoryState.Cleaned, 900L, 2, 1), (r.State, r.Bytes, r.Files, r.InUse)));
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));   // the clean's end sent Done
        Assert.Equal(new[] { "MeasureCategory", "MeasureCategory", "CleanCategory", "CleanCategory" }, factory.Seen.Select(r => r.Verb));
        Assert.Equal(new[] { "WindowsTemp", "SystemCrashDumps", "WindowsTemp", "SystemCrashDumps" }, factory.Seen.Select(r => r.Category));
        for (int i = 0; i < 100 && seen.Count < 4; i++) await Task.Delay(50);   // Progress posts to the thread pool
        Assert.Equal(4, seen.Count);                  // a start and an end per category
    }

    [Fact]
    public async Task Cancel_after_the_measure_ends_the_session_with_Done_and_deletes_nothing()
    {
        await using var factory = new Factory(Answer);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await helper.MeasureCategoriesAsync([AdminCategory.WindowsTemp]);
        await helper.EndSessionAsync();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["MeasureCategory"], factory.Seen.Select(r => r.Verb));
        await helper.EndSessionAsync();               // nothing open: harmless
    }

    [Fact]
    public async Task A_measure_left_alone_ends_after_the_idle_time()
    {
        var clock = new Clock();
        await using var factory = new Factory(Answer);
        using var helper = new AdminHelper(factory, NullLogger.Instance, sessionIdle: TimeSpan.FromSeconds(50), time: clock);
        await helper.MeasureCategoriesAsync([AdminCategory.WindowsTemp]);
        clock.Advance(TimeSpan.FromSeconds(50));
        clock.FireTimers();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_declined_prompt_is_needs_admin_for_every_category_and_nothing_runs()
    {
        var declined = new RefusingFactory(new Win32Exception(1223));
        using var helper = new AdminHelper(declined, NullLogger.Instance);
        var results = await helper.MeasureCategoriesAsync(Two);
        Assert.All(results, r => Assert.Equal(AdminCategoryState.NeedsAdmin, r.State));
        Assert.Equal(["Needs admin", "Needs admin"], results.Select(r => r.Text));
        Assert.Equal(Two, results.Select(r => r.Category));
        Assert.Equal(1, declined.Opens);
        var clean = await helper.CleanCategoriesAsync(Two);
        Assert.All(clean, r => Assert.Equal(AdminCategoryState.NeedsAdmin, r.State));
        var absent = new RefusingFactory(new FileNotFoundException());
        using var missing = new AdminHelper(absent, NullLogger.Instance);
        Assert.All(await missing.MeasureCategoriesAsync(Two), r => Assert.Equal(AdminCategoryState.Failed, r.State));
    }

    [Fact]
    public async Task Unavailable_and_failed_answers_come_through_per_category()
    {
        await using var factory = new Factory(r => r.Category == "WindowsTemp"
            ? new AdminReply(r.Id, Messages.CategoryUnavailable, Category: r.Category) : new AdminReply(r.Id, Messages.CategoryFailed, Category: r.Category, Reason: "x"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var results = await helper.MeasureCategoriesAsync(Two);
        Assert.Equal([AdminCategoryState.Unavailable, AdminCategoryState.Failed], results.Select(r => r.State));
        Assert.Equal(["Unavailable", "Failed"], results.Select(r => r.Text));
        await helper.EndSessionAsync();
    }

    [Fact]
    public async Task Cancelling_a_clean_sends_Done_and_reports_what_was_freed_then_cancels_the_rest()
    {
        using var started = new ManualResetEventSlim();
        await using var factory = new Factory(Answer, blockClean: started);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await helper.MeasureCategoriesAsync(Two);
        using var cancel = new CancellationTokenSource();
        var run = helper.CleanCategoriesAsync(Two, ct: cancel.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));   // the helper is mid-clean of the first category
        cancel.Cancel();
        var results = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((AdminCategoryState.Cancelled, 5L, 1), (results[0].State, results[0].Bytes, results[0].Files));   // what it freed so far
        Assert.Equal(AdminCategoryState.Cancelled, results[1].State);
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(factory.Seen, r => r.Verb == Messages.CleanCategory);
        Assert.Equal(1, factory.Opens);
    }

    [Fact]
    public async Task A_cancelled_call_never_opens_a_session_and_bad_arguments_are_refused_first()
    {
        await using var factory = new Factory(Answer);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => helper.CleanCategoriesAsync(Two, ct: cancel.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => helper.MeasureCategoriesAsync([(AdminCategory)99]));
        Assert.Empty(await helper.MeasureCategoriesAsync([]));
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task A_wrong_id_category_or_state_in_the_reply_fails_the_category()
    {
        foreach (Func<AdminRequest, AdminReply> bad in new Func<AdminRequest, AdminReply>[]
        {
            r => new(Guid.NewGuid(), Messages.CategoryMeasured, Category: r.Category),
            r => new(r.Id, Messages.CategoryMeasured, Category: "DeliveryOptimization"),
            r => new(r.Id, Messages.CategoryCleaned, Category: r.Category),      // a clean answer to a measure
            r => new(r.Id, "Done", Category: r.Category),
            r => new(r.Id, Messages.CategoryMeasured, Category: r.Category, Bytes: -1),
        })
        {
            await using var factory = new Factory(bad);
            using var helper = new AdminHelper(factory, NullLogger.Instance);
            Assert.Equal(AdminCategoryState.Failed, (await helper.MeasureCategoriesAsync([AdminCategory.WindowsTemp]))[0].State);
        }
    }

    [Fact]
    public async Task The_helper_stops_a_running_category_when_Done_arrives_or_the_pipe_closes_and_still_answers()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates(), categories: new BlockingActions()).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var request = new AdminRequest(Guid.NewGuid(), Messages.CleanCategory, Category: "WindowsTemp");
            await Messages.WriteAsync(client, request);
            await Task.Delay(100);
            await Messages.WriteAsync(client, new AdminRequest(Guid.NewGuid(), "Done"));
            var reply = (await Messages.ReadReplyAsync(client))!;
            Assert.Equal((request.Id, Messages.CategoryCleaned, Messages.CancelledReason), (reply.Id, reply.State, reply.Reason));
            Assert.Equal("Done", (await Messages.ReadReplyAsync(client))!.State);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task An_unknown_category_name_ends_the_helper_session()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates(), categories: new BlockingActions()).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var bytes = Encoding.UTF8.GetBytes("{\"Id\":\"00000000-0000-0000-0000-000000000009\",\"Verb\":\"CleanCategory\",\"Category\":\"C:\\\\Windows\"}");
            await client.WriteAsync(BitConverter.GetBytes(bytes.Length)); await client.WriteAsync(bytes); await client.FlushAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task A_session_without_category_actions_fails_the_request_and_still_ends_cleanly()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates()).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var request = new AdminRequest(Guid.NewGuid(), Messages.MeasureCategory, Category: "WindowsTemp");
            await Messages.WriteAsync(client, request);
            var reply = (await Messages.ReadReplyAsync(client))!;
            Assert.Equal((request.Id, Messages.CategoryFailed), (reply.Id, reply.State));
            await Messages.WriteAsync(client, new AdminRequest(Guid.NewGuid(), "Done"));
            Assert.Equal("Done", (await Messages.ReadReplyAsync(client))!.State);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task A_clean_ends_its_session_and_the_next_job_opens_a_new_one()
    {
        await using var factory = new Factory(Answer);
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await helper.MeasureCategoriesAsync([AdminCategory.WindowsTemp]);
        await helper.CleanCategoriesAsync([AdminCategory.WindowsTemp]);
        await helper.MeasureCategoriesAsync([AdminCategory.WindowsTemp]);   // the clean ended its session: a new one
        Assert.Equal(2, factory.Opens);
        await helper.EndSessionAsync();
    }

    private static async Task<(NamedPipeClientStream Client, NamedPipeServerStream Helper)> Pair()
    {
        string name = "chippi-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(); await client.ConnectAsync(5000); await accept;
        return (client, server);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ms;
        private readonly List<Timer> _timers = [];
        public override long GetTimestamp() => Interlocked.Read(ref _ms);
        public override long TimestampFrequency => 1000;
        public void Advance(TimeSpan span) => Interlocked.Add(ref _ms, (long)span.TotalMilliseconds);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state);
            lock (_timers) _timers.Add(timer);
            return timer;
        }
        public void FireTimers() { Timer[] live; lock (_timers) live = [.. _timers]; foreach (var timer in live) timer.Fire(); }
        private sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            private volatile bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class NoUpdates : IPackageUpdater
    { public Task<AdminReply> UpdateAsync(AdminRequest request, CancellationToken ceiling) => throw new InvalidOperationException(); }

    /// <summary>Runs until cancelled, then answers with what it "freed" and the cancelled reason.</summary>
    private sealed class BlockingActions(ManualResetEventSlim? started = null) : ICategoryActions
    {
        public async Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct)
        {
            started?.Set();
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return new(request.Id, Messages.CategoryCleaned, Reason: Messages.CancelledReason, Category: request.Category, Bytes: 5, Files: 1, InUse: 0);
        }
    }

    private sealed class FakeCategories(Func<AdminRequest, AdminReply> reply, Action<AdminRequest> seen, ManualResetEventSlim? blockClean) : ICategoryActions
    {
        private readonly BlockingActions _blocking = new(blockClean);
        public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct)
        {
            seen(request);
            // The first clean waits to be cancelled when a test asks, like a long delete.
            return blockClean is not null && request.Verb == Messages.CleanCategory ? _blocking.RunAsync(request, ct) : Task.FromResult(reply(request));
        }
    }

    private sealed class RefusingFactory(Exception error) : IAdminSessionFactory
    {
        public int Opens;
        public Task<IAdminConnection> OpenAsync(CancellationToken ct) { Opens++; return Task.FromException<IAdminConnection>(error); }
    }

    private sealed class Factory(Func<AdminRequest, AdminReply> reply, ManualResetEventSlim? blockClean = null) : IAdminSessionFactory, IAsyncDisposable
    {
        public int Opens;
        public List<AdminRequest> Seen { get; } = [];
        public Task Finished { get; private set; } = Task.CompletedTask;
        private readonly List<IDisposable> _ends = [];
        public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
        {
            Opens++;
            var (client, server) = await Pair();
            _ends.Add(client); _ends.Add(server);
            Finished = new HelperSession(new NoUpdates(), categories: new FakeCategories(reply, r => { lock (Seen) Seen.Add(r); }, blockClean)).RunAsync(client);
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
