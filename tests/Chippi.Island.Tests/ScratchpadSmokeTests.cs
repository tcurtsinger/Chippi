// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.App.Flyout;
using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Scratchpad;
using Xunit.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Real Scratchpad page and shell measurement, fake store and monitor; no HWND or input injection.</summary>
public sealed class ScratchpadSmokeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Left)]
    [InlineData(IslandEdge.Right)]
    public void Full_notes_page_and_shell_fit_the_island_on_a_960_by_540_screen(IslandEdge edge) => WpfTestThread.Run(() =>
    {
        var store = new Store(); var ui = new Ui();
        var module = new ScratchpadModule(_ => store);
        var panel = new IslandPanelWindow();
        try
        {
            module.StartAsync(new ModuleContext("scratchpad", new Settings(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, new Island(), ui), default).GetAwaiter().GetResult();
            var tile = ui.Tile!;
            Assert.Equal("12 notes", tile.Status);
            panel.SetTiles(IslandTileContribution.Group([tile]));
            Set(panel, "_page", "scratchpad");
            Invoke(panel, "Attach", "scratchpad");
            var page = Assert.IsType<ScratchpadPage>(tile.Spec.CreateSection());
            Assert.Same(page.Header, tile.Spec.CreateHeader!());
            var editor = Assert.IsType<TextBox>(tile.Spec.InitialFocus!());
            Assert.Equal(store.Document.Selected.Text, editor.Text);
            Assert.Equal(editor.Text.Length, editor.CaretIndex);
            Assert.False(editor.AcceptsTab);
            IslandBounds.Place(panel, edge, 960, 540);
            var footprint = IslandBounds.Check(panel, edge, 960, 540);
            output.WriteLine($"edge={edge}: footprint={footprint}, work=960x492");
            Assert.False(panel.IsVisible); Assert.Equal(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(panel).Handle);
        }
        finally { panel.Close(); module.StopAsync(default).GetAwaiter().GetResult(); }
    });

    [Fact]
    public void Editor_keeps_pasted_tabs_and_leaves_escape_to_the_shell() => WpfTestThread.Run(() =>
    {
        var store = new Store(); var session = new ScratchpadSession(() => store);
        session.LoadAsync().GetAwaiter().GetResult();
        using var page = new ScratchpadPage(session, new IslandOpenHold().Hold);
        try
        {
            var editor = Assert.IsType<TextBox>(page.InitialFocus);
            Assert.False(editor.AcceptsTab);
            editor.Text = "Invented\ttab\r\nsecond line";
            session.FlushAsync().GetAwaiter().GetResult();
            Assert.Equal(editor.Text, store.Document.Selected.Text);
            // Exercise the routed preview path, without sending a key to the desktop.
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, new UnshownSource(), 0, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent };
            editor.RaiseEvent(escape);
            Assert.False(escape.Handled);
            Assert.Equal(editor.Text, store.Document.Selected.Text);
        }
        finally { page.Dispose(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    private sealed class UnshownSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Invoke(object value, string name, params object?[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private sealed class Store : IScratchpadStore
    {
        public ScratchpadDocument Document = Make();
        private static ScratchpadDocument Make() { var doc = ScratchpadDocument.Empty(); while (doc.Notes.Count < 12) doc = doc.Add(); return doc.Rename("Invented long note title").Edit(string.Join("\n", Enumerable.Repeat("Invented text\twith a tab", 100))); }
        public ScratchpadDocument Load() => Document;
        public void Save(ScratchpadDocument document) => Document = document;
        public void Dispose() { }
    }
    private sealed class Settings : IModuleSettings { public T Get<T>() where T : class, new() => new(); public void Save<T>(T value) where T : class { } }
    private sealed class Ui : IModuleUi { public IslandTileContribution? Tile; public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new("scratchpad", spec); public void SetSettingsPage(Func<string?, object> createView) => throw new InvalidOperationException("Scratchpad has no Settings page."); }
    private sealed class Island : IIslandPublisher { public void Publish(IslandActivity activity) => throw new InvalidOperationException(); public void Clear(string key) { } public void ReportSuccess(string? message = null) => throw new InvalidOperationException(); }
}

