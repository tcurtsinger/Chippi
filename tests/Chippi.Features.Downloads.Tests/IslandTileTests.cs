// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.SystemIntegration;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Tests;

/// <summary>Karl's S6 review: the File organizer tile had no status on the island grid.</summary>
public sealed class IslandTileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-tile-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\Chippi.Explorer.TileTest-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, false);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void The_status_is_what_changes_held_files_files_to_review_or_a_mode_other_than_auto()
    {
        var folder = new WatchedFolder { Path = @"C:\Users\me\Downloads", Mode = FolderMode.Manual };
        PendingFile Pending(string name, PendingStatus status) => new(@"C:\Users\me\Downloads\", @"C:\Users\me\Downloads\" + name, name, status, null);
        PendingFile Ready(string name) => Pending(name, PendingStatus.Ready);
        string Status(params PendingFile[] pending) => DownloadsModule.TileStatus([folder], pending, _ => true);

        Assert.Equal("", DownloadsModule.TileStatus([], []));
        Assert.Equal("Manual", Status());
        Assert.Equal("2 to review", Status(Ready("a.pdf"), Ready("b.zip")));
        Assert.Equal("1 held", Status(Ready("a.pdf"), Pending("setup.pdf.exe", PendingStatus.Held)));
        folder.Mode = FolderMode.Silent;
        Assert.Equal("", Status()); // Auto is the setting, not news
        Assert.Equal("1 to review", Status(Ready("a.pdf"))); // already there when the folder was added
        folder.Mode = FolderMode.Paused;
        Assert.Equal("Paused", Status(Ready("a.pdf")));
    }

    /// <summary>Critique 2026-10-31: the home read only the first folder, so a second folder's held files never showed.</summary>
    [Fact]
    public void The_status_counts_every_folder_and_says_when_one_is_gone()
    {
        var downloads = new WatchedFolder { Path = @"C:\Users\me\Downloads", Mode = FolderMode.Silent };
        var desktop = new WatchedFolder { Path = @"C:\Users\me\Desktop", Mode = FolderMode.Manual };
        var gone = new WatchedFolder { Path = @"D:\Old inbox", Mode = FolderMode.Manual };
        PendingFile Held(string folder, string name) => new(folder, Path.Combine(folder, name), name, PendingStatus.Held, "Check before opening");

        Assert.Equal("1 held", DownloadsModule.TileStatus([downloads, desktop], [Held(desktop.Path, "a.torrent")], _ => true));
        Assert.Equal("", DownloadsModule.TileStatus([downloads, desktop], [], _ => true)); // one on Auto
        Assert.Equal("Not found", DownloadsModule.TileStatus([downloads, gone], [], p => p != gone.Path));
        Assert.Equal("1 held", DownloadsModule.TileStatus([downloads, gone], [Held(downloads.Path, "a.torrent")], p => p != gone.Path)); // files first
    }

    [Fact]
    public async Task The_running_module_sets_its_tile_and_follows_a_mode_change()
    {
        var watched = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(watched);
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new DownloadsSettings
        {
            Initialized = true,
            OrganizeInExplorer = false,
            Folders = [new WatchedFolder { Path = watched, Mode = FolderMode.Silent, BaselineUtc = DateTimeOffset.UtcNow }],
        });
        var registration = new ExplorerRegistration("test", _key, @"C:\Tools\Chippi.exe");
        var module = new DownloadsModule(registration);
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance,
            new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            var tile = Assert.Single(registry.Find("downloads-organizer")!.IslandTiles);
            Assert.Equal("", tile.Status); // Auto with nothing waiting

            var paused = module.Settings.Clone();
            paused.Folders[0].Mode = FolderMode.Paused;
            module.SaveSettings(paused);
            Assert.Equal("Paused", tile.Status);
        }
        finally { await registry.StopAllAsync(); registration.SetEnabled(false); }
    }
}
