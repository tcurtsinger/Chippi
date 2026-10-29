// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Settings.Pages;
using Chippi.Clipboard.Storage;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard;
using Chippi.Features.Clipboard.History;
using Chippi.Features.Clipboard.Views;
using Chippi.Features.PlainPaste;
using Chippi.Features.Scratchpad;
using Chippi.Features.Shelf;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Island.Tests.ClipboardSmokeTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders Settings › Clipboard history, Clean URL, Paste as plain text, Scratchpad and Shelf off screen with
/// invented content and fake clipboard and keys, as settings-shell c2, c3 and c8 show them. Runs only when
/// CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class ClipboardSettingsShots
{
    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        var directory = Path.Combine(Path.GetTempPath(), "chippi-f-shots-" + Guid.NewGuid().ToString("N"));
        WpfTestThread.Run(() =>
        {
            var styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") };
            Application.Current.Resources.MergedDictionaries.Add(styles);
            var hub = new Hub();
            var features = new ClipboardFeatures();
            var history = new ClipboardHistoryModule(features, directory);
            var clean = new CleanUrlModule(features);
            var plain = new PlainPasteModule(() => new NoHotkey()); // never a real global shortcut
            ModuleContext Context(string id, IModuleUi ui) => new(id, new Settings(), NullLogger.Instance, new ClipboardSmokeTests.Island(), ui) { Clipboard = hub, DataDirectory = directory };
            try
            {
                var historyUi = new Pages();
                history.StartAsync(Context(ClipboardHistoryModule.Id, historyUi), default).GetAwaiter().GetResult();
                history.History!.Dispose();
                var store = new ClipboardHistory(hub, () => HistoryStore.Open(Path.Combine(directory, "fake.db"), new Keys()));
                typeof(ClipboardHistoryModule).GetProperty(nameof(ClipboardHistoryModule.History))!.SetValue(history, store);
                store.Apply(true, false, []);
                foreach (var text in new[] { "Gate 14, boarding 6:10", "https://example.org/blog/release-notes", "SELECT name, total FROM orders WHERE total > 100;",
                    "Meeting moved to 3:30 PM, Room 2B.", "https://example.org/docs/setup?ref=nav#install", "git log --oneline --graph -20", "Thanks, I'll review the draft by Friday and send notes." })
                    store.RecordAsync(text).GetAwaiter().GetResult();
                foreach (var entry in store.Entries.Where(e => e.Text is "git log --oneline --graph -20" or "Thanks, I'll review the draft by Friday and send notes."))
                    store.PinAsync(entry.Id, true).GetAwaiter().GetResult();
                Shoot("Clipboard history", (FrameworkElement)historyUi.Page!(null), Path.Combine(folder, "clipboard-history.png"));

                var cleanUi = new Pages();
                clean.StartAsync(Context(CleanUrlModule.Id, cleanUi), default).GetAwaiter().GetResult();
                var cleanPage = (FrameworkElement)cleanUi.Page!(null);
                foreach (var field in Find<TextBox>(cleanPage).Where(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Link"))
                    field.Text = "https://example.org/p?utm_source=news&utm_medium=email&item=7#part";
                Shoot("Clean URL", cleanPage, Path.Combine(folder, "clean-url.png"));

                var plainUi = new Pages();
                plain.StartAsync(new ModuleContext("paste-plain-text", new PlainSettings(), NullLogger.Instance, new ClipboardSmokeTests.Island(), plainUi) { Clipboard = hub }, default).GetAwaiter().GetResult();
                plain.RemoveShortcut();
                Shoot("Paste as plain text", (FrameworkElement)plainUi.Page!(null), Path.Combine(folder, "paste-plain-text.png"));

                //+ new pages
                var notes = ScratchpadDocument.Empty().Rename("Groceries").Edit("Milk, eggs").Add().Rename("Standup")
                    .Edit("## Standup, Tue\n- Shipped: island fit fix\n- Today: **Shelf** drag-out\n- Blocked: none\n\n- [ ] Book a room for Thursday\nBranch: `release/0.6`")
                    .Add().Rename("Ideas").Add().Rename("Draft reply").Select(2);
                var scratchUi = new Pages();
                var scratchpad = new ScratchpadModule(_ => new ScratchpadSmokeTests.Store { Document = notes });
                scratchpad.StartAsync(new ModuleContext("scratchpad", new ScratchpadSmokeTests.Settings(), NullLogger.Instance, new ScratchpadSmokeTests.Island(), scratchUi), default).GetAwaiter().GetResult();
                Shoot("Scratchpad", (FrameworkElement)scratchUi.Page!(null), Path.Combine(folder, "scratchpad.png"));
                scratchpad.StopAsync(default).GetAwaiter().GetResult();

                var shelfUi = new Pages();
                var shelf = new ShelfModule(path => ShelfStore.Open(path, new Keys()));
                shelf.StartAsync(Context(ShelfModule.Id, shelfUi), default).GetAwaiter().GetResult();
                foreach (var text in new[] { "https://example.org/launch", "Call Dana about the venue\nand the caterer" })
                    shelf.Shelf!.AddTextAsync(text).GetAwaiter().GetResult();
                Shoot("Shelf", (FrameworkElement)shelfUi.Page!(null), Path.Combine(folder, "shelf.png"));
                Shoot("Shelf", new ShelfSettingsPage(new ShelfService(hub, () => ShelfStore.Open(Path.Combine(directory, "empty.db"), new Keys()))), Path.Combine(folder, "shelf-empty.png"));
                shelf.StopAsync(default).GetAwaiter().GetResult();
                //- new pages
            }
            finally
            {
                history.StopAsync(default).GetAwaiter().GetResult();
                clean.StopAsync(default).GetAwaiter().GetResult();
                plain.StopAsync(default).GetAwaiter().GetResult();
                Application.Current.Resources.MergedDictionaries.Remove(styles);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        });
    }

    private static void Shoot(string title, FrameworkElement view, string path)
    {
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        for (int i = 0; i < 10; i++)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(20);
        }
        var page = new Border { Width = 824, Padding = new Thickness(32, 24, 32, 32), Child = new ModulePageHost(title, view) };
        page.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        page.Measure(new Size(824, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1236, (int)Math.Ceiling(page.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        page.Child = null;
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item)
            yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child))
                yield return nested;
    }

    private sealed class Pages : IModuleUi
    {
        public Func<string?, object>? Page;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("shots", spec);
        public void SetSettingsPage(Func<string?, object> factory) => Page = factory;
    }

    private sealed class NoHotkey : Chippi.Features.Sound.Mute.IMicHotkey
    {
        public bool Register(Chippi.Features.Sound.Mute.MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }

    private sealed class PlainSettings : IModuleSettings
    {
        private object? _value;
        public T Get<T>() where T : class, new() => _value as T ?? new();
        public void Save<T>(T value) where T : class => _value = value;
    }
}
