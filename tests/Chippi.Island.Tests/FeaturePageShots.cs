// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.App.Island;
using Chippi.Clipboard.Storage;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard;
using Chippi.Features.Clipboard.History;
using Chippi.Features.Scratchpad;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders the real Clipboard, Scratchpad and Shelf pages off screen on the top edge, with invented
/// content, in the states the refresh mockups show. Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class FeaturePageShots
{
    private static readonly MonitorArea Area = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            Shelf(folder);
            Scratchpad(folder);
            Clipboard(folder);
        });
    }

    private static void Shelf(string folder)
    {
        string[] names = ["Call Dana about the venue", "https://example.org/launch", "Gate 14, boarding 6:10", "invoice-0412 draft", "https://shop.example.org/cart", "Budget notes for 2027"];
        using var shelf = new ShelfSmokeTests.Fixture();
        foreach (var count in new[] { 0, 9, 28, 40 })
        {
            while (shelf.Shelf.Items.Count < count)
                shelf.Shelf.AddTextAsync(names[shelf.Shelf.Items.Count % names.Length]).GetAwaiter().GetResult();
            Shoot(shelf.Tiles, "shelf", Path.Combine(folder, $"shelf-{count}.png"));
        }
    }

    private static void Scratchpad(string folder)
    {
        var document = ScratchpadDocument.Empty().Rename("Groceries").Edit("Milk, eggs");
        foreach (var name in new[] { "Standup", "Ideas", "Draft reply", "Travel", "Gifts", "Reading list" })
            document = document.Add().Rename(name);
        document = document.Select(2).Edit("## Standup, Tue\n- Shipped: island fit fix\n- Today: **Shelf** drag-out\n- Blocked: none\n\n- [ ] Book a room for Thursday\nBranch: `release/0.6`");
        var store = new ScratchpadSmokeTests.Store { Document = document };
        var ui = new ScratchpadSmokeTests.Ui();
        var module = new ScratchpadModule(_ => store);
        try
        {
            module.StartAsync(new ModuleContext("scratchpad", new ScratchpadSmokeTests.Settings(), NullLogger.Instance, new ScratchpadSmokeTests.Island(), ui), default).GetAwaiter().GetResult();
            Shoot(IslandTileContribution.Group([ui.Tile!]), "scratchpad", Path.Combine(folder, "scratchpad.png"));
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    }

    private static void Clipboard(string folder)
    {
        var directory = Path.Combine(Path.GetTempPath(), "chippi-shots-" + Guid.NewGuid().ToString("N"));
        var hub = new ClipboardSmokeTests.Hub();
        var features = new ClipboardFeatures();
        var module = new ClipboardHistoryModule(features, directory);
        var ui = new ClipboardSmokeTests.Ui(ClipboardHistoryModule.Id);
        try
        {
            module.StartAsync(new(ClipboardHistoryModule.Id, new ClipboardSmokeTests.Settings(), NullLogger.Instance, new ClipboardSmokeTests.Island(), ui) { Clipboard = hub, DataDirectory = directory }, default).GetAwaiter().GetResult();
            // The persistence boundary only, as the smoke test does: no production DPAPI call.
            module.History!.Dispose();
            var history = new ClipboardHistory(hub, () => HistoryStore.Open(Path.Combine(directory, "fake.db"), new ClipboardSmokeTests.Keys()));
            typeof(ClipboardHistoryModule).GetProperty(nameof(ClipboardHistoryModule.History))!.SetValue(module, history);
            history.Apply(true, false, []);
            foreach (var text in new[] { "Thanks, I'll review the draft by Friday and send notes.", "git log --oneline --graph -20" })
                history.RecordAsync(text).GetAwaiter().GetResult();
            foreach (var entry in history.Entries)
                history.PinAsync(entry.Id, true).GetAwaiter().GetResult();
            for (int i = 0; i < 9; i++)
                history.RecordAsync(i % 3 == 0 ? $"https://example.org/docs/setup?ref=nav&item={i}#install" : $"Meeting moved to 3:30 PM, Room {i}B.\nAgenda: Q3 numbers, launch checklist\nBring: laptops, the Q3 deck and the printed handouts\nThen lunch").GetAwaiter().GetResult();
            var tiles = IslandTileContribution.Group([ui.Tile!]);
            Shoot(tiles, "clipboard", Path.Combine(folder, "clipboard.png"));
            Shoot(tiles, "clipboard", Path.Combine(folder, "clipboard-open.png"), panel =>
            {
                // Down three times from Search opens the third newest copy in place.
                var search = Find<SearchBox>(panel.PageBody!).Single();
                for (int i = 0; i < 3; i++)
                    search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel)!, 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            });
            module.SetRecording(false);
            history.EraseAllAsync().GetAwaiter().GetResult();
            Shoot(tiles, "clipboard", Path.Combine(folder, "clipboard-off.png"));
        }
        finally
        {
            module.StopAsync(default).GetAwaiter().GetResult();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Shoot(IReadOnlyList<IslandTileGroup> tiles, string key, string path, Action<IslandPanelWindow>? arrange = null)
    {
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(tiles);
            panel.SetLastPage(null);
            panel.Open(IslandEdge.Top, new Rect(-20000 + 960 - 68, 0, 136, 36), Area);
            Pump(TimeSpan.FromMilliseconds(400));
            panel.ShowPage(key);
            Pump(TimeSpan.FromMilliseconds(400));
            if (arrange is not null)
            {
                arrange(panel);
                Pump(TimeSpan.FromMilliseconds(300));
            }
            IslandPanelShots.Save(panel, path);
        }
        finally
        {
            panel.ShowPage(null, remember: false); // lets go of the page, which its module keeps for the next shot
            panel.Close();
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }
}
#endif
