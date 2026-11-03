// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.Scratchpad.Tests;

public sealed class ScratchpadTabsTests
{
    private static readonly ScratchpadDocument Notes = new(1, Enumerable.Range(1, 12)
        .Select(i => new ScratchpadNote(i, $"Release preparation and follow-up {i:00}", "")).ToArray());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actual_width_keeps_active_note_and_commands_on_screen_and_reveals_more_tabs_when_wider(bool widget) => WpfTestThread.Run(() =>
    {
        var strip = new ScratchpadTabs(widget);
        strip.Display(Notes, 12);
        Arrange(strip, 320);
        var list = strip.Children.OfType<SegmentedControl>().Single();
        var selected = Assert.IsType<ListBoxItem>(list.SelectedItem);
        Assert.Equal(12L, selected.Tag);
        Assert.Equal(Notes.Notes[^1].Name, AutomationProperties.GetName(selected));
        Assert.EndsWith("12", Assert.IsType<string>(selected.Content));
        int narrowCount = list.Items.Count;
        var picker = ButtonNamed(strip, "All notes");
        var add = ButtonNamed(strip, "New note");
        Assert.Equal(Visibility.Visible, picker.Visibility);
        Assert.False(add.IsEnabled);
        Assert.InRange(add.TranslatePoint(new Point(), strip).X, 0, 320 - 14);
        Assert.InRange(list.Items.Cast<ListBoxItem>().Sum(t => t.Width), 0, 320 - add.Width - picker.Width);
        Arrange(strip, 820);
        Assert.True(list.Items.Count > narrowCount);
        Assert.Same(selected, list.SelectedItem);
        Assert.Equal(list.Items.Count, list.Items.Cast<ListBoxItem>().Select(t => t.Content).Distinct().Count());
        Arrange(strip, 320);
        Assert.Equal(narrowCount, list.Items.Count);
        Assert.Same(selected, list.SelectedItem);
    });

    [Fact]
    public void Body_edits_leave_tab_instances_in_place_and_a_short_list_needs_no_picker() => WpfTestThread.Run(() =>
    {
        var strip = new ScratchpadTabs(false);
        var document = ScratchpadDocument.Empty().Add();
        strip.Display(document, 1);
        Arrange(strip, 500);
        var list = strip.Children.OfType<SegmentedControl>().Single();
        var before = list.Items.Cast<ListBoxItem>().ToArray();
        strip.Display(document.Edit("Synthetic body"), 1);
        Assert.Equal(before, list.Items.Cast<ListBoxItem>().ToArray());
        Assert.Same(before[0], list.SelectedItem);
        Assert.Equal(Visibility.Collapsed, ButtonNamed(strip, "All notes").Visibility);
        Assert.True(ButtonNamed(strip, "New note").IsEnabled);
    });

    [Fact]
    public void Picker_contains_every_full_name_and_its_row_and_tab_arrows_select_by_identity() => WpfTestThread.Run(() =>
    {
        var strip = new ScratchpadTabs(true);
        strip.Display(Notes, 12);
        Arrange(strip, 320);
        var popup = strip.BuildNotesMenu();
        var rows = ((StackPanel)((ScrollViewer)((Border)popup.Child).Child).Content).Children.OfType<Button>().ToArray();
        Assert.Equal(Notes.Notes.Select(n => n.Name), rows.Select(AutomationProperties.GetName));
        Assert.Equal("Selected", AutomationProperties.GetItemStatus(rows[^1]));
        long selection = 0;
        strip.Selected += id => { selection = id; strip.Display(Notes, id); };
        rows[7].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(8, selection);
        var list = strip.Children.OfType<SegmentedControl>().Single();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, new UnshownSource(), 0, Key.Right) { RoutedEvent = UIElement.PreviewKeyDownEvent };
        list.RaiseEvent(key);
        Assert.True(key.Handled);
        Assert.Equal(9L, Assert.IsType<ListBoxItem>(list.SelectedItem).Tag);
    });

    [Fact]
    public void Rename_stays_inside_the_active_tab_and_cancel_preserves_its_title() => WpfTestThread.Run(() =>
    {
        var holds = new IslandOpenHold();
        var strip = new ScratchpadTabs(true, holds.Hold);
        strip.Display(Notes, 12);
        Arrange(strip, 496);
        var list = strip.Children.OfType<SegmentedControl>().Single();
        var selected = Assert.IsType<ListBoxItem>(list.SelectedItem);
        (long Id, string Name)? committed = null;
        strip.RenameRequested += (id, name) => committed = (id, name);
        strip.BeginRename();
        var rename = Assert.IsType<TextBox>(selected.Content);
        rename.Text = "Cancelled";
        Assert.True(holds.IsHeld);
        Assert.True(strip.HandleEscape());
        Assert.Null(committed);
        Assert.False(holds.IsHeld);
        Assert.False(strip.HandleEscape());
        strip.BeginRename();
        Assert.IsType<TextBox>(selected.Content).Text = " Edited title ";
        strip.EndRename(true);
        Assert.Equal((12L, "Edited title"), committed);
        Assert.False(holds.IsHeld);
    });

    private static Button ButtonNamed(ScratchpadTabs strip, string name) =>
        strip.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    private static void Arrange(ScratchpadTabs strip, double width)
    {
        strip.Measure(new Size(width, strip.Height));
        strip.Arrange(new Rect(0, 0, width, strip.Height));
        strip.UpdateLayout();
    }

    private sealed class UnshownSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
}
