// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

public sealed class GalleryTests
{
    [Fact]
    public void Pencil_opens_a_draft_and_UIA_selection_never_runs_an_action() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var page = f.Page;
        var edit = Find<Button>(page.Header).Single(b => AutomationProperties.GetName(b) == "Edit actions");
        Assert.IsType<Chippi.UI.Controls.LucideIcon>(edit.Content);
        edit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal("Edit actions", page.Title);
        Assert.True(page.IsEditing);
        var lockPick = Pick(page, QuickActionId.Explorer);
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(lockPick)).Toggle();
        Assert.DoesNotContain(QuickActionId.Explorer, page.EditorSelection);
        Assert.Contains("explorer", f.Module.Settings.Shown!);
        Assert.Empty(f.Pc.Calls);
        Assert.True(page.Back());
        Assert.Equal("Actions", page.Title);
        Assert.Contains("explorer", f.Module.Settings.Shown!);
    });

    [Fact]
    public void Dragging_swaps_occupied_slots_then_moves_into_an_empty_slot_and_Done_saves_the_gap() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var page = f.Page;
        page.BeginEdit();
        Arrange(page);
        string? first = page.EditorLayout[0], second = page.EditorLayout[1];
        Drag(page, 0, 1);
        Assert.Equal(second, page.EditorLayout[0]);
        Assert.Equal(first, page.EditorLayout[1]);
        Arrange(page);
        int empty = Enumerable.Range(0, 20).First(i => page.EditorLayout[i] is null);
        Drag(page, 1, empty);
        Assert.Null(page.EditorLayout[1]);
        Assert.Equal(first, page.EditorLayout[empty]);
        Assert.Empty(f.Pc.Calls);
        page.FinishEdit();
        Assert.Null(f.Module.Layout[1]);
        Assert.Equal(first, f.Module.Layout[empty]);
        page.BeginEdit();
        Assert.Null(page.EditorLayout[1]);
        Assert.Equal(first, page.EditorLayout[empty]);
    });

    [Fact]
    public void Subthreshold_click_only_changes_visibility_and_Back_discards_it() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var page = f.Page;
        page.BeginEdit();
        Arrange(page);
        var original = page.EditorLayout.ToArray();
        var start = Center(page.EditorSlotBounds(0));
        Assert.True(page.EditorPointerDown(0, start));
        page.EditorPointerMove(start + new Vector(4, 0));
        Assert.False(page.IsDragging);
        page.EditorPointerUp(start + new Vector(4, 0));
        Assert.Equal(original, page.EditorLayout);
        Assert.DoesNotContain(QuickActionId.Dark, page.EditorSelection);
        Assert.True(page.Back());
        Assert.True(f.Module.Settings.Shows(QuickActionId.Dark));
        Assert.Equal(original, f.Module.Layout);
    });

    [Fact]
    public void Leaving_the_grid_or_pressing_Escape_cancels_a_drag_without_a_click() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var page = f.Page;
        page.BeginEdit();
        Arrange(page);
        var original = page.EditorLayout.ToArray();
        var selected = page.EditorSelection.ToArray();
        var start = Center(page.EditorSlotBounds(0));
        page.EditorPointerDown(0, start);
        page.EditorPointerMove(start + new Vector(12, 0));
        Assert.True(page.IsDragging);
        page.EditorPointerMove(new Point(-1, start.Y));
        page.EditorPointerUp(Center(page.EditorSlotBounds(1)));
        Assert.False(page.IsDragging);
        Assert.Equal(original, page.EditorLayout);
        Assert.Equal(selected, page.EditorSelection);
        page.EditorPointerDown(0, start);
        page.EditorPointerMove(start + new Vector(12, 0));
        Assert.True(page.Escape());
        Assert.True(page.IsEditing);
        Assert.False(page.IsDragging);
        Assert.True(page.Escape());
        Assert.False(page.IsEditing);
        Assert.Equal(original, f.Module.Layout);
    });

    [Fact]
    public void Keyboard_move_stays_in_the_grid_and_saves_the_same_positions_as_drag() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var page = f.Page;
        page.BeginEdit();
        var before = page.EditorLayout.ToArray();
        page.MoveEditorByKey(0, Key.Left);
        Assert.Equal(before, page.EditorLayout);
        page.MoveEditorByKey(0, Key.Up);
        Assert.Equal(before, page.EditorLayout);
        page.MoveEditorByKey(0, Key.Down);
        Assert.Equal(before[0], page.EditorLayout[5]);
        Assert.Equal(before[5], page.EditorLayout[0]);
        Assert.Contains("Row 2, column 1", AutomationProperties.GetItemStatus(Pick(page, QuickActionId.Dark)), StringComparison.Ordinal);
        page.FinishEdit();
        Assert.Equal(before[0], f.Module.Layout[5]);
    });

    [Fact]
    public void First_run_has_a_named_picker_and_cannot_save_an_empty_selection() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(firstRun: true);
        var page = f.Page;
        Assert.Equal("Choose actions", page.Title);
        Assert.NotNull(page.InitialFocus);
        Assert.IsType<ToggleButton>(page.InitialFocus);
        var done = Find<Button>(page.Header).Single(b => Equals(b.Content, "Done"));
        Assert.False(done.IsEnabled);
        page.FinishEdit();
        Assert.Null(f.Module.Settings.Shown);
        var pick = Pick(page, QuickActionId.Explorer);
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(pick)).Toggle();
        Assert.True(done.IsEnabled);
        page.FinishEdit();
        Assert.Equal(["explorer"], f.Module.Settings.Shown);
        Assert.Empty(f.Pc.Calls);
    });

    [Fact]
    public void Failed_save_preserves_the_exact_draft_and_Done_can_retry() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Page.BeginEdit();
        var original = f.Module.Layout.ToArray();
        f.Page.MoveEditor(0, 19);
        var draft = f.Page.EditorLayout.ToArray();
        f.Settings.FailSave = true;
        f.Page.FinishEdit();
        Assert.True(f.Page.IsEditing);
        Assert.Equal(original, f.Module.Layout);
        Assert.Equal(draft, f.Page.EditorLayout);
        Assert.Contains(Find<TextBlock>(f.Page), t => t.Text == "Save failed" && t.Visibility == Visibility.Visible);
        f.Settings.FailSave = false;
        f.Page.FinishEdit();
        Assert.False(f.Page.IsEditing);
        Assert.Equal(draft, f.Module.Layout);
        Assert.Empty(f.Pc.Calls);
    });

    [Fact]
    public void An_extra_pointer_cannot_steal_an_in_progress_drag_and_unload_cancels_it() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var page = f.Page;
        page.BeginEdit();
        Arrange(page);
        string? first = page.EditorLayout[0];
        Assert.True(page.EditorPointerDown(0, Center(page.EditorSlotBounds(0))));
        page.EditorPointerMove(Center(page.EditorSlotBounds(1)));
        Assert.False(page.EditorPointerDown(2, Center(page.EditorSlotBounds(2))));
        page.EditorPointerUp(Center(page.EditorSlotBounds(1)));
        Assert.Equal(first, page.EditorLayout[1]);
        Arrange(page);
        var savedDraft = page.EditorLayout.ToArray();
        Assert.True(page.EditorPointerDown(1, Center(page.EditorSlotBounds(1))));
        page.EditorPointerMove(Center(page.EditorSlotBounds(2)));
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        page.EditorPointerUp(Center(page.EditorSlotBounds(2)));
        Assert.False(page.IsDragging);
        Assert.Equal(savedDraft, page.EditorLayout);
        Assert.True(page.EditorPointerDown(1, Center(page.EditorSlotBounds(1))));
        page.EditorPointerMove(Center(page.EditorSlotBounds(2)));
        page.EditorPointerUp(Center(page.EditorSlotBounds(2)));
        Assert.Equal(first, page.EditorLayout[2]);
    });

    [Fact]
    public void Command_tiles_expose_Invoke_and_state_tiles_expose_Toggle_with_full_names() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var commands = Find<Button>(f.Page).Where(b => b.Tag is string).ToList();
        var lockButton = commands.Single(b => Equals(b.Tag, "explorer"));
        var lockPeer = UIElementAutomationPeer.CreatePeerForElement(lockButton);
        Assert.NotNull(lockPeer.GetPattern(PatternInterface.Invoke));
        Assert.Null(lockPeer.GetPattern(PatternInterface.Toggle));
        var dark = Find<ToggleButton>(f.Page).Single(b => Equals(b.Tag, "dark"));
        Assert.Equal("Dark mode", AutomationProperties.GetName(dark));
        Assert.Equal("Off", AutomationProperties.GetItemStatus(dark));
        Assert.NotNull(UIElementAutomationPeer.CreatePeerForElement(dark).GetPattern(PatternInterface.Toggle));
        Assert.Contains(Find<TextBlock>((DependencyObject)dark.Content), t => t.Text == "Dark");
        Assert.Contains(Find<TextBlock>((DependencyObject)dark.Content), t => t.Text == "Off");
    });

    [Fact]
    public void A_failed_on_toggle_keeps_its_state_and_uses_a_neutral_surface_until_retry() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Pc.State = QuickState.Empty with { On = new Dictionary<QuickActionId, bool> { [QuickActionId.Dark] = true } };
        f.Module.RefreshAsync().GetAwaiter().GetResult();
        f.Page.Render();
        f.Pc.Fail = true;
        f.Page.Press(QuickActionId.Dark);
        var button = Find<ToggleButton>(f.Page).Single(b => Equals(b.Tag, "dark"));
        Assert.True(button.IsChecked);
        Assert.Equal("Dark failed", AutomationProperties.GetItemStatus(button));
        Assert.Same(f.Page.FindResource("IslandGalleryFaceBrush"), button.Background);
        Assert.Same(f.Page.FindResource("IslandTextBrush"), button.Foreground);
        f.Pc.Fail = false;
        f.Page.Press(QuickActionId.Dark);
        Assert.False(button.IsChecked);
        Assert.Equal(DependencyProperty.UnsetValue, button.ReadLocalValue(Control.BackgroundProperty));
        Assert.Equal(DependencyProperty.UnsetValue, button.ReadLocalValue(Control.ForegroundProperty));
    });
    private static ToggleButton Pick(ActionsPage page, QuickActionId id) => Find<ToggleButton>(page).Single(b => Equals(b.Tag, id));
    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static void Arrange(FrameworkElement page)
    {
        page.Measure(new Size(IslandPage.BodyWidth, IslandPage.BodyHeight));
        page.Arrange(new Rect(0, 0, IslandPage.BodyWidth, IslandPage.BodyHeight));
        page.UpdateLayout();
    }
    private static void Drag(ActionsPage page, int from, int to)
    {
        Assert.True(page.EditorPointerDown(from, Center(page.EditorSlotBounds(from))));
        var end = Center(page.EditorSlotBounds(to));
        page.EditorPointerMove(end);
        Assert.True(page.IsDragging);
        page.EditorPointerUp(end);
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class Fixture : IDisposable
    {
        internal FakePc Pc { get; } = new();
        internal Store Settings { get; } = new();
        internal QuickActionsModule Module { get; }
        internal ActionsPage Page { get; }
        internal Fixture(bool firstRun = false)
        {
            Module = new QuickActionsModule(() => Pc, () => new Hotkey());
            Module.StartAsync(new ModuleContext(QuickActionsModule.Id, Settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new Pages()), default).GetAwaiter().GetResult();
            if (!firstRun) Module.Pick([QuickActionId.Dark, QuickActionId.Explorer]);
            Page = new ActionsPage(Module);
        }
        public void Dispose() => Module.StopAsync(default).GetAwaiter().GetResult();
    }
    private sealed class Store : IModuleSettings
    {
        private QuickActionsSettings _value = new();
        internal bool FailSave { get; set; }
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class
        {
            if (FailSave) throw new System.IO.IOException("Fixture disk failure");
            _value = (QuickActionsSettings)(object)value;
        }
    }
    private sealed class Pages : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }
    private sealed class Hotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
