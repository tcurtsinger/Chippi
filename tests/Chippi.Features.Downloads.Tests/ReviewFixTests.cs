// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO.Compression;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Import;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;

namespace Chippi.Features.Downloads.Tests;

/// <summary>Karl's review of 8338091, one test per finding.</summary>
public sealed class ReviewFixTests
{
    private static RuleSetting MovePdfs() => new() { Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Docs" };

    [Fact]
    public async Task A_copy_never_touches_a_file_that_already_had_the_partial_name()
    {
        using var f = new Fixture(FolderMode.Manual, options: new EngineOptions { ForceCopy = true });
        f.AddRule(MovePdfs());
        f.Write("a.pdf", "source");
        Directory.CreateDirectory(f.In("Docs"));
        string theirs = f.In("Docs", "a.pdf.chippi-partial");
        File.WriteAllText(theirs, "pre-existing user data");

        var preview = await f.Engine.CreatePreviewAsync(f.Folder);
        var result = await f.Engine.ApproveAsync(preview.Id, [1]);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal("source", File.ReadAllText(f.In("Docs", "a.pdf")));
        Assert.Equal("pre-existing user data", File.ReadAllText(theirs));
    }

    [Fact]
    public async Task Stopping_waits_for_an_approval_that_is_moving_a_file()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        using var gate = new GatedHistory(f.History);
        using var engine = new DownloadsEngine(gate, f.Notifier, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, f.Options, f.Time);
        engine.Start(f.Settings, runLoop: false);
        var plan = await engine.CreatePreviewAsync(f.Folder);

        var approval = engine.ApproveAsync(plan.Id, [1]);
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)), "the approval never reached MarkStarted");
        var stop = Task.Run(engine.Dispose);

        Assert.NotSame(stop, await Task.WhenAny(stop, Task.Delay(300))); // Dispose must not return while a file is being moved
        gate.Release.Set();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        // By the time Dispose returned, the move and its history row were finished.
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.Equal(OperationState.Completed, Assert.Single(f.History.Query(new HistoryQuery())).State);
        Assert.Equal(1, (await approval).Succeeded);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.CreatePreviewAsync(f.Folder));
    }

    [Fact]
    public void An_unreadable_ignore_file_holds_the_folder_and_recovers()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        string ignore = f.In(EngineOptions.IgnoreFileName);
        File.WriteAllText(ignore, "keep*\n");

        using (new FileStream(ignore, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            f.Write("keep.pdf");
            f.Write("other.pdf");
            f.Settle();
            Assert.True(File.Exists(f.In("keep.pdf")));
            Assert.True(File.Exists(f.In("other.pdf"))); // held too: the rules are unknown
        }
        f.Write("keep-again.pdf");
        f.Settle(passes: 20);

        Assert.True(File.Exists(f.In("keep.pdf")));
        Assert.True(File.Exists(f.In("keep-again.pdf")));
        Assert.True(File.Exists(f.In("Docs", "other.pdf")));
    }

    [Fact]
    public async Task A_same_size_edit_with_the_old_timestamp_fails_approval()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        string path = f.Write("a.pdf", "aaaaa");
        var preview = await f.Engine.CreatePreviewAsync(f.Folder);
        var modified = File.GetLastWriteTimeUtc(path);

        // NTFS stamps change time from the system clock (about 16 ms ticks); a person's edit comes later than that.
        await Task.Delay(50);
        File.WriteAllText(path, "bbbbb");
        File.SetLastWriteTimeUtc(path, modified);
        var result = await f.Engine.ApproveAsync(preview.Id, [1]);

        Assert.False(Assert.Single(result.Items).Succeeded);
        Assert.Equal("bbbbb", File.ReadAllText(path));
        Assert.False(File.Exists(f.In("Docs", "a.pdf")));
    }

    [Fact]
    public async Task An_imported_file_can_be_undone_into_the_folder_now_and_after_a_restart()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        string zip = Path.Combine(f.Root, "in.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "a.pdf", "b.pdf", "notes.txt" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(name);
            }
        }
        var extracted = ArchiveImporter.Extract(zip, f.Folder, Path.Combine(f.Root, "data", "imports"));
        var preview = await f.Engine.PreviewImportAsync(f.Folder, extracted);
        var applied = await f.Engine.ApproveAsync(preview.Id, [.. preview.Items.Select(i => i.Id)]);
        var ops = f.History.RunOperations(applied.RunId);
        long a = ops.Single(o => o.DestinationPath.EndsWith("a.pdf", StringComparison.OrdinalIgnoreCase)).Id;
        long b = ops.Single(o => o.DestinationPath.EndsWith("b.pdf", StringComparison.OrdinalIgnoreCase)).Id;
        var notes = ops.Single(o => o.DestinationPath.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase));

        Assert.True((await f.Engine.UndoAsync(a)).Succeeded);
        Assert.Equal("a.pdf", File.ReadAllText(f.In("a.pdf")));

        f.Restart();
        Assert.True((await f.Engine.UndoAsync(b)).Succeeded);
        Assert.True(File.Exists(f.In("b.pdf")));

        Assert.Equal(HistoryOperationKind.Import, notes.Kind);
        var none = await f.Engine.UndoAsync(notes.Id);
        Assert.False(none.Succeeded);
        Assert.True(File.Exists(f.In("notes.txt")));
    }

    /// <summary>Pauses the first MarkStarted until released, standing in for a long copy.</summary>
    private sealed class GatedHistory(IHistoryStore inner) : IHistoryStore
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public long BeginRun(RunTrigger trigger, string folder) => inner.BeginRun(trigger, folder);

        public void EndRun(long runId) => inner.EndRun(runId);

        public long Plan(long runId, HistoryOperationKind kind, string source, string destination, string? ruleId, string? ruleName, long bytes, long? undoOf = null) =>
            inner.Plan(runId, kind, source, destination, ruleId, ruleName, bytes, undoOf);

        public void MarkStarted(long operationId)
        {
            inner.MarkStarted(operationId);
            Entered.Set();
            Release.Wait();
        }

        public void MarkCompleted(long operationId, string destination, string sha256, DateTimeOffset destinationModifiedUtc) =>
            inner.MarkCompleted(operationId, destination, sha256, destinationModifiedUtc);

        public void MarkFailed(long operationId, string error) => inner.MarkFailed(operationId, error);

        public void MarkUndone(long operationId) => inner.MarkUndone(operationId);

        public OperationRecord? Get(long operationId) => inner.Get(operationId);

        public IReadOnlyList<OperationRecord> Query(HistoryQuery query) => inner.Query(query);

        public IReadOnlyList<OperationRecord> RunOperations(long runId) => inner.RunOperations(runId);

        public IReadOnlyList<OperationRecord> Incomplete() => inner.Incomplete();

        public OperationRecord? LatestOutputAt(string destination) => inner.LatestOutputAt(destination);

        public int ClearBefore(DateTimeOffset cutoff) => inner.ClearBefore(cutoff);

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
