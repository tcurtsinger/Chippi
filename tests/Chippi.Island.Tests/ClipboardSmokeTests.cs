// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.App.Flyout;
using Chippi.App.Island;
using Chippi.Clipboard.Storage;
using Chippi.Core.Clipboard;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard;
using Chippi.Features.Clipboard.History;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Production Clipboard page and shell with a fake clipboard, key protector and screen. Never opens a window.</summary>
public sealed class ClipboardSmokeTests
{
    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Left)]
    [InlineData(IslandEdge.Right)]
    public void Clipboard_page_header_search_and_clean_url_fit_the_island_on_a_960_by_540_screen(IslandEdge edge) => WpfTestThread.Run(() =>
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chippi-c6-" + Guid.NewGuid().ToString("N"));
        var hub = new Hub(); var features = new ClipboardFeatures();
        var historyModule = new ClipboardHistoryModule(features, directory); var cleanModule = new CleanUrlModule(features);
        var historyUi = new Ui(ClipboardHistoryModule.Id); var cleanUi = new Ui(CleanUrlModule.Id);
        var panel = new IslandPanelWindow(); FrameworkElement? page = null;
        try
        {
            ModuleContext Context(string id, Ui ui) => new(id, new Settings(), NullLogger.Instance, new Island(), ui) { Clipboard = hub, DataDirectory = directory };
            historyModule.StartAsync(Context(ClipboardHistoryModule.Id, historyUi), default).GetAwaiter().GetResult();
            // Substitute only the persistence boundary before any copy is recorded. No production DPAPI call.
            historyModule.History!.Dispose();
            var history = new ClipboardHistory(hub, () => HistoryStore.Open(System.IO.Path.Combine(directory, "fake.db"), new Keys()));
            typeof(ClipboardHistoryModule).GetProperty(nameof(ClipboardHistoryModule.History))!.SetValue(historyModule, history);
            history.Apply(true, false, []);
            history.RecordAsync("Invented first copy").GetAwaiter().GetResult();
            history.RecordAsync("Invented second copy").GetAwaiter().GetResult();
            cleanModule.StartAsync(Context(CleanUrlModule.Id, cleanUi), default).GetAwaiter().GetResult();
            panel.SetTiles(IslandTileContribution.Group([historyUi.Tile!, cleanUi.Tile!]));
            Set(panel, "_page", "clipboard"); Invoke(panel, "Attach", "clipboard");
            page = Assert.IsAssignableFrom<FrameworkElement>(historyUi.Tile!.Spec.CreateSection());
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var header = Assert.IsAssignableFrom<FrameworkElement>(historyUi.Tile.Spec.CreateHeader!());
            Assert.Single(Find<IconButton>(header), b => AutomationProperties.GetName(b) == "Clean URL");
            Assert.Single(Find<IconButton>(header), b => AutomationProperties.GetName(b) == "Pause");
            var search = Assert.IsType<SearchBox>(historyUi.Tile.Spec.InitialFocus!());
            Assert.Contains(Find<TextBlock>(page), text => text.Text == "Invented first copy");
            IslandBounds.Place(panel, edge, 960, 540);
            IslandBounds.Check(panel, edge, 960, 540);

            search.Text = "no matching invented text";
            Assert.Contains(Find<TextBlock>(page), text => text.Text == "No matches");
            Assert.True(historyUi.Tile.Spec.HandleEscape!());
            Assert.Equal("", search.Text);
            Assert.False(historyUi.Tile.Spec.HandleEscape!());
            var clean = Find<IconButton>(header).Single(b => AutomationProperties.GetName(b) == "Clean URL");
            clean.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var link = Assert.IsType<TextBox>(historyUi.Tile.Spec.InitialFocus!());
            Assert.Equal("Link", AutomationProperties.GetName(link));
            Assert.Equal("https://example.org/?utm_source=invented", link.Text);
            IslandBounds.Check(panel, edge, 960, 540);
            Assert.True(historyUi.Tile.Spec.HandleEscape!());
            Assert.Same(search, historyUi.Tile.Spec.InitialFocus!());
            Assert.False(panel.IsVisible);
            Assert.Equal(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(panel).Handle);
        }
        finally
        {
            page?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            panel.Close(); historyModule.StopAsync(default).GetAwaiter().GetResult(); cleanModule.StopAsync(default).GetAwaiter().GetResult();
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
        }
    });

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var nested in Find<T>(child)) yield return nested;
    }
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static void Invoke(object value, string name, params object?[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private sealed class Keys : IKeyProtector { public byte[] Protect(byte[] key) => key.ToArray(); public byte[] Unprotect(byte[] key) => key.ToArray(); }
    private sealed class Settings : IModuleSettings { public T Get<T>() where T : class, new() => typeof(T) == typeof(ClipboardHistorySettings) ? (T)(object)new ClipboardHistorySettings { Recording = true } : new(); public void Save<T>(T value) where T : class { } }
    private sealed class Ui(string id) : IModuleUi { public IslandTileContribution? Tile; public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new(id, spec); public void SetSettingsPage(Func<string?, object> factory) { } }
    private sealed class Island : IIslandPublisher { public void Publish(IslandActivity activity) => throw new InvalidOperationException(); public void Clear(string key) { } public void ReportSuccess(string? message = null) => throw new InvalidOperationException(); }
    private sealed class Hub : IClipboardHub
    {
        public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null) => new Lease();
        public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => Task.FromResult(new ClipRead(ClipStatus.Ok, ClipContent.FromText("https://example.org/?utm_source=invented")));
        public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default) => throw new InvalidOperationException();
        public PasteTarget? CaptureTarget() => null;
        public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new InvalidOperationException();
        public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) => throw new InvalidOperationException();
        public void Beep() => throw new InvalidOperationException();
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}

