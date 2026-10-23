// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Notifications;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Downloads.Tests;

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class RecordingNotifier : INotifier
{
    private readonly List<Notice> _shown = [];

    public IReadOnlyList<Notice> Shown
    {
        get { lock (_shown) return [.. _shown]; }
    }

    public IReadOnlyList<NoticeKind> Kinds { get; private set; } = [];

    public void Declare(IReadOnlyList<NoticeKind> kinds) => Kinds = kinds;

    public NoticePlaces PlacesFor(string kindId) => NoticePlaces.Island;

    public void SetPlaces(string kindId, NoticePlaces places)
    {
    }

    public void Show(Notice notice)
    {
        lock (_shown)
            _shown.Add(notice);
    }
}

/// <summary>A temp watched folder, a history database and an engine driven by hand with a fake clock.</summary>
internal sealed class Fixture : IDisposable
{
    public Fixture(FolderMode mode = FolderMode.Silent, bool existingFilesAreOld = false, EngineOptions? options = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "chippi-tests-" + Guid.NewGuid().ToString("N"));
        Folder = Path.Combine(Root, "Downloads");
        Directory.CreateDirectory(Folder);
        Database = Path.Combine(Root, "data", "history.db");
        Options = options ?? new EngineOptions();
        Settings = new DownloadsSettings
        {
            Folders =
            [
                new WatchedFolder
                {
                    Path = Folder,
                    Mode = mode,
                    GraceSeconds = 0,
                    // Files made by the test count as new unless the folder was "added" after them.
                    BaselineUtc = existingFilesAreOld ? DateTimeOffset.UtcNow.AddHours(1) : DateTimeOffset.UtcNow.AddMinutes(-5),
                },
            ],
            Initialized = true,
        };
        Open();
    }

    public string Root { get; }

    public string Folder { get; }

    public string Database { get; }

    public EngineOptions Options { get; }

    public DownloadsSettings Settings { get; }

    public ManualTime Time { get; } = new(DateTimeOffset.UtcNow);

    public RecordingNotifier Notifier { get; } = new();

    public SqliteHistoryStore History { get; private set; } = null!;

    public DownloadsEngine Engine { get; private set; } = null!;

    public string In(params string[] parts) => Path.Combine([Folder, .. parts]);

    public string Write(string name, string content = "hello")
    {
        string path = In(name);
        File.WriteAllText(path, content);
        // The watcher reports the file on its own thread, which can outlast Settle's passes on a slow
        // CI runner. Wait until the engine has seen it (a Paused folder never tracks it).
        if (Settings.Folders[0].Mode != FolderMode.Paused)
            SpinWait.SpinUntil(() => Engine.IsTracking(path), TimeSpan.FromSeconds(5));
        return path;
    }

    public void AddRule(RuleSetting rule)
    {
        rule.Id = rule.Id.Length > 0 ? rule.Id : "r" + Settings.Rules.Count;
        rule.Name = rule.Name.Length > 0 ? rule.Name : rule.Id;
        rule.Order = Settings.Rules.Count;
        Settings.Rules.Add(rule);
        Engine.Apply(Settings);
    }

    /// <summary>Lets the clock run past every wait (debounce, two samples, grace) and the loop see it.</summary>
    public void Settle(int passes = 8)
    {
        for (int i = 0; i < passes; i++)
        {
            Thread.Sleep(40); // watcher events arrive on their own threads
            Time.Advance(TimeSpan.FromSeconds(3));
            Engine.ProcessDueNow();
        }
    }

    /// <summary>Simulates an app restart on the same database.</summary>
    public void Restart()
    {
        Engine.Dispose();
        History.Dispose();
        Open();
    }

    public void Dispose()
    {
        Engine.Dispose();
        History.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher handle can linger a moment; the temp folder is swept later.
        }
    }

    private void Open()
    {
        History = new SqliteHistoryStore(Database, Time);
        Engine = new DownloadsEngine(History, Notifier, NullLogger.Instance, Options, Time);
        Engine.Start(Settings, runLoop: false);
    }
}
