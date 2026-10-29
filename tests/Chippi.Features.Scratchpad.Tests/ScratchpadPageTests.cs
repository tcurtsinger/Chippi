// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.Scratchpad.Tests;

public sealed class ScratchpadPageTests
{
    private sealed class Store : IScratchpadStore
    {
        public ScratchpadDocument Document = ScratchpadDocument.Empty();
        public bool FailRead, FailWrite;
        public ScratchpadDocument Load() => FailRead ? throw new System.IO.IOException() : Document;
        public void Save(ScratchpadDocument value) { if (FailWrite) throw new System.IO.IOException(); Document = value; }
        public void Dispose() { }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Find<T>(root).Single(x => AutomationProperties.GetName(x) == name);
    private static void WithPage(Action<Store, ScratchpadSession, ScratchpadPage> test, bool failRead = false) => WpfTestThread.Run(() =>
    {
        var store = new Store { FailRead = failRead };
        var session = new ScratchpadSession(() => store);
        session.LoadAsync().GetAwaiter().GetResult();
        using var page = new ScratchpadPage(session, new IslandOpenHold().Hold);
        try { test(store, session, page); }
        finally { page.Dispose(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    [Fact]
    public void Typing_and_reopening_preserve_tabs_line_breaks_and_the_draft_after_a_save_failure() => WithPage((store, session, page) =>
    {
        var editor = Named<TextBox>(page, "Note");
        store.FailWrite = true;
        editor.Text = "Invented\ttext\r\nnext";
        session.FlushAsync().GetAwaiter().GetResult(); Pump();
        Assert.True(session.State.SaveFailed);
        Assert.Equal("Invented\ttext\r\nnext", editor.Text);
        Assert.Contains(Find<TextBlock>(page.Header), b => b.Text == "Save failed");
        store.FailWrite = false;
        editor.Text += " again";
        session.FlushAsync().GetAwaiter().GetResult(); Pump();
        Assert.False(session.State.SaveFailed);
        Assert.Equal(editor.Text, store.Document.Selected.Text);
        using var reopened = new ScratchpadPage(session, new IslandOpenHold().Hold);
        Assert.Equal(editor.Text, Named<TextBox>(reopened, "Note").Text);
        Assert.False(editor.AcceptsTab);
        Assert.Equal(12, editor.FontSize);
    });

    [Fact]
    public void Load_failure_hides_the_editor_and_retry_loads_without_overwriting_notes() => WithPage((store, session, page) =>
    {
        Assert.Contains(Find<TextBlock>(page.Header), b => b.Text == "Load failed");
        Assert.Equal(Visibility.Collapsed, ((DockPanel)page.Children[0]).Visibility);
        store.FailRead = false;
        store.Document = ScratchpadDocument.Empty().Edit("Preserved note");
        Named<Button>(page.Header, "Retry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        session.FlushAsync().GetAwaiter().GetResult(); Pump();
        Assert.Equal("Preserved note", Named<TextBox>(page, "Note").Text);
        Assert.Equal(Visibility.Visible, ((DockPanel)page.Children[0]).Visibility);
    }, failRead: true);

    [Fact]
    public void Settings_load_failure_shows_retry_beside_the_title_and_retry_loads_the_notes() => WpfTestThread.Run(() =>
    {
        var store = new Store { FailRead = true };
        var session = new ScratchpadSession(() => store);
        session.LoadAsync().GetAwaiter().GetResult();
        var page = new ScratchpadSettingsPage(session);
        try
        {
            var aside = (FrameworkElement)SettingsLayout.GetTitleAside(page)!;
            var retry = Named<Button>(aside, "Retry");
            Assert.Contains(Find<TextBlock>(aside), b => b.Text == "Load failed");
            Assert.Equal(Visibility.Visible, retry.Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)page.Content).Visibility);
            store.FailRead = false;
            store.Document = ScratchpadDocument.Empty().Edit("Preserved note");
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session.FlushAsync().GetAwaiter().GetResult(); Pump();
            Assert.Equal("Preserved note", Named<TextBox>(page, "Note").Text);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)page.Content).Visibility);
            Assert.Equal(Visibility.Collapsed, retry.Visibility);
            Assert.Contains(Find<TextBlock>(aside), b => b.Text == "Saved");
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    [Fact]
    public void New_note_opens_an_empty_editor_and_the_twelfth_disables_new() => WithPage((store, session, page) =>
    {
        Named<Button>(page, "New note").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        session.FlushAsync().GetAwaiter().GetResult(); Pump();
        Assert.Equal("Note 2", session.State.Document!.Selected.Name);
        Assert.Equal("", Named<TextBox>(page, "Note").Text);
        session.ChangeAsync(d => { while (d.Notes.Count < 12) d = d.Add(); return d; }).GetAwaiter().GetResult(); Pump();
        Assert.False(Named<Button>(page, "New note").IsEnabled);
        Assert.Equal(12, int.Parse(Find<TextBlock>(Named<Button>(page, "All notes")).Single().Text[1..]) + Named<SegmentedControl>(page, "Notes").Items.Count);
    });

    [Fact]
    public void Export_holds_the_island_and_flushes_then_releases_even_on_failure() => WpfTestThread.Run(() =>
    {
        var store = new Store(); var session = new ScratchpadSession(() => store);
        session.LoadAsync().GetAwaiter().GetResult();
        var holds = new IslandOpenHold();
        using var page = new ScratchpadPage(session, holds.Hold, (_, note) =>
        {
            Assert.True(holds.IsHeld); Assert.Equal("Latest export", note.Text);
            Assert.Equal(note.Text, store.Document.Selected.Text);
            throw new System.IO.IOException("Invented failure");
        });
        Named<TextBox>(page, "Note").Text = "Latest export";
        var export = page.ExportAsync();
        while (!export.IsCompleted) Pump();
        export.GetAwaiter().GetResult(); Assert.False(holds.IsHeld);
        Assert.Contains(Find<TextBlock>(page.Header), b => b.Text == "Export failed");
        page.Dispose(); session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

    [Fact]
    public void Preview_has_no_navigation_or_download_controls_and_shows_read_only_checkboxes() => WpfTestThread.Run(() =>
    {
        var rendered = ScratchpadMarkdown.Render("# Title\n- [x] Done\n**bold** and `code`\n[Link](https://invalid.example/)\n![Invented image](https://invalid.example/a.png)");
        Assert.Empty(Find<Hyperlink>(rendered));
        Assert.Empty(Find<Image>(rendered));
        var box = Find<CheckBox>(rendered).Single();
        Assert.True(box.IsChecked); Assert.False(box.IsHitTestVisible); Assert.False(box.Focusable);
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(14, box.DesiredSize.Width, 3);
        Assert.Equal(14, box.DesiredSize.Height, 3);
        Assert.Equal(BaselineAlignment.Center, Find<InlineUIContainer>(rendered).Single(i => ReferenceEquals(i.Child, box)).BaselineAlignment);
        Assert.Contains(Find<TextBlock>(rendered), b => b.Text == "Invented image");
        var runs = Find<Run>(rendered).ToList();
        Assert.Contains(runs, r => r.Text == "bold" && r.FontWeight == FontWeights.SemiBold);
        Assert.Contains(runs, r => r.Text == "Link" && r.TextDecorations.Count > 0);
    });

    [Fact]
    public void The_note_fills_the_body_between_the_tabs_row_and_the_bottom_edge_without_showing_a_window() => WithPage((store, session, page) =>
    {
        page.Measure(new Size(496, 304)); page.Arrange(new Rect(0, 0, 496, 304)); page.UpdateLayout();
        var note = Named<TextBox>(page, "Note");
        Assert.Equal(304 - 30 - 8 - 8 - 28, ((FrameworkElement)((FrameworkElement)note.Parent).Parent).ActualHeight); // 230
        Assert.Equal(304, ((FrameworkElement)Named<Button>(page, "More").Parent).TranslatePoint(new Point(0, 28), page).Y, 3);
        Assert.DoesNotContain(Find<ScrollViewer>(page), s => s.Visibility == Visibility.Visible); // only a note scrolls, inside the editor or its preview
        Assert.Same(note, page.InitialFocus);
    });

    // Build menus without opening their popup windows on the user's desktop.
    private static System.Windows.Controls.Primitives.Popup Menu(ScratchpadPage page, string factory, params object[] args) =>
        (System.Windows.Controls.Primitives.Popup)typeof(ScratchpadPage).GetMethod(factory,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, args)!;

    [Fact]
    public void More_and_tab_actions_use_the_island_menu_and_the_exact_export_label() => WithPage((store, session, page) =>
    {
        var style = new Style(typeof(Button));
        var resources = Application.Current.Resources;
        var previous = resources["IslandMenuItemStyle"];
        resources["IslandMenuItemStyle"] = style;
        try
        {
            foreach (FrameworkElement anchor in new FrameworkElement[] { Named<Button>(page, "More"), Named<SegmentedControl>(page, "Notes") })
            {
                var menu = Menu(page, "CreateActionsMenu", anchor);
                Assert.Same(anchor, menu.PlacementTarget);
                Assert.False(menu.IsOpen);
                var surface = Assert.IsType<Border>(menu.Child);
                Assert.Equal(new CornerRadius(8), surface.CornerRadius);
                var rows = Find<Button>(surface).ToArray();
                Assert.Equal(new[] { "Rename", "Export\u2026", "Delete" }, rows.Select(b => b.Content));
                Assert.All(rows, row => Assert.Same(style, row.Style));
            }
        }
        finally { if (previous is null) resources.Remove("IslandMenuItemStyle"); else resources["IslandMenuItemStyle"] = previous; }
    });

    [Fact]
    public void All_notes_uses_the_island_menu_and_selects_the_clicked_note() => WithPage((store, session, page) =>
    {
        session.ChangeAsync(d => d.Add()).GetAwaiter().GetResult(); Pump();
        var menu = Menu(page, "CreateNotesMenu", session.State.Document!);
        Assert.False(menu.IsOpen);
        var rows = Find<Button>(menu.Child).ToArray();
        Assert.Equal(new[] { "Note 1", "Note 2 \u2713" }, rows.Select(b => b.Content));
        rows[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        session.FlushAsync().GetAwaiter().GetResult(); Pump();
        Assert.Equal("Note 1", session.State.Document!.Selected.Name);
    });
}
