// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TextElement = System.Windows.Documents.TextElement;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.UI.Controls;

namespace Chippi.Features.MacDesktop;

/// <summary>
/// Settings › Mac-style desktop: Desktop style, then the Menu bar, Dock and Notch switches and each surface's options. Terse
/// labels and values only (AGENTS.md, copy). The Menu bar section is the bar's items, top to bottom as they sit left to
/// right: a grip to reorder (or Up and Down on it), ⋯ to move or remove, Add item for the rest, and the clock, always last.
/// </summary>
public sealed class MacDesktopRows : UserControl
{
    private readonly MacDesktopModule _module;
    private readonly ComboBox _style = SettingsLayout.Combo(["Windows", "Mac-style"]);
    private readonly ToggleButton _menuBar = Switch("Menu bar"), _dock = Switch("Dock"), _notch = Switch("Notch");
    private readonly ComboBox _hide = SettingsLayout.Combo(["Always shown", "Auto-hide", "When a window touches it"]);
    private readonly ComboBox _position = SettingsLayout.Combo(["Bottom", "Left", "Right"]);
    private readonly ToggleButton _magnify = Switch("Magnification");
    private readonly Slider _size = new() { Minimum = MacDesktopSettings.MinMagnified, Maximum = MacDesktopSettings.MaxMagnified, Width = SettingsLayout.ComboWidth, IsSnapToTickEnabled = true, TickFrequency = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _opens = SettingsLayout.Combo(["Hover or click", "Click"]);
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Card _items = new();
    private readonly Button _add = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly FrameworkElement _surfaces, _barRows, _dockRows, _notchRows;
    private string _itemsKey = "";
    private bool _updating, _shownAsOn;

    public MacDesktopRows(MacDesktopModule module)
    {
        _module = module;
        AutomationProperties.SetName(_style, "Desktop style");
        AutomationProperties.SetName(_hide, "Hide");
        AutomationProperties.SetName(_position, "Position");
        AutomationProperties.SetName(_size, "Magnified size");
        AutomationProperties.SetName(_opens, "Opens on");
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");

        var center = new Button { Content = "Center", VerticalAlignment = VerticalAlignment.Center };
        center.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        center.Click += (_, _) => Save(s => s with { NotchDisplay = null, NotchPlace = 0.5 });

        _surfaces = SettingsLayout.Card(
            SettingsLayout.Row("Menu bar", _menuBar),
            SettingsLayout.Row("Dock", _dock),
            SettingsLayout.Row("Notch", _notch));
        _surfaces.Margin = new Thickness(0, 8, 0, 0);
        var plus = new LucideIcon { Data = LucideIcons.Plus, Size = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        plus.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        _add.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { plus, new TextBlock { Text = "Add item", VerticalAlignment = VerticalAlignment.Center } } };
        _add.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(_add, "Add item");
        _add.Click += (_, _) => AddMenu();
        _barRows = new StackPanel { Children = { SettingsLayout.Section("Menu bar", _add), _items } };
        _dockRows = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Dock"),
                SettingsLayout.Card(
                    SettingsLayout.Row("Hide", _hide),
                    SettingsLayout.Row("Position", _position),
                    SettingsLayout.Row("Magnification", _magnify),
                    SettingsLayout.Row("Magnified size", _size)),
            },
        };
        _notchRows = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Notch"),
                SettingsLayout.Card(
                    SettingsLayout.Row("Opens on", _opens),
                    SettingsLayout.Row("Place", center)),
            },
        };
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Card(SettingsLayout.Row("Desktop style", _style)),
                _error,
                _surfaces,
                _barRows,
                _dockRows,
                _notchRows,
            },
        };

        _style.SelectionChanged += (_, _) => Save(s => s with { Style = _style.SelectedIndex == 1 ? DesktopStyle.MacStyle : DesktopStyle.Windows });
        Hook(_menuBar, on => s => s with { MenuBar = on });
        Hook(_dock, on => s => s with { Dock = on });
        Hook(_notch, on => s => s with { Notch = on });
        Hook(_magnify, on => s => s with { Magnify = on });
        _hide.SelectionChanged += (_, _) => Save(s => s with { DockHide = (DockHiding)Math.Max(0, _hide.SelectedIndex) });
        _position.SelectionChanged += (_, _) => Save(s => s with { DockPosition = (DockEdge)Math.Max(0, _position.SelectedIndex) });
        _size.ValueChanged += (_, _) => Save(s => s with { MagnifiedSize = (int)Math.Round(_size.Value) });
        _opens.SelectionChanged += (_, _) => Save(s => s with { NotchOpensOnHover = _opens.SelectedIndex != 1 });

        Loaded += (_, _) => { module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => module.Changed -= Refresh;
        Refresh();
    }

    private static ToggleButton Switch(string name)
    {
        var toggle = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    private void Hook(ToggleButton toggle, Func<bool, Func<MacDesktopSettings, MacDesktopSettings>> change)
    {
        toggle.Checked += (_, _) => Save(change(true));
        toggle.Unchecked += (_, _) => Save(change(false));
    }

    private void Save(Func<MacDesktopSettings, MacDesktopSettings> change)
    {
        if (_updating)
            return;
        var next = change(_module.Settings);
        if (next == _module.Settings)
            return;
        try
        {
            _ = _module.ApplyAsync(next);
            _error.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Chippi.Core.Diagnostics.Log.Warn("Mac-style desktop: saving settings failed", ex);
            _error.Text = "Save failed";
            _error.Visibility = Visibility.Visible;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _updating = true;
        try
        {
            var s = _module.Settings;
            _style.SelectedIndex = s.IsMac ? 1 : 0;
            Set(_menuBar, s.MenuBar);
            Set(_dock, s.Dock);
            Set(_notch, s.Notch);
            Set(_magnify, s.Magnify);
            _hide.SelectedIndex = (int)s.DockHide;
            _position.SelectedIndex = (int)s.DockPosition;
            _size.Value = s.MagnifiedSize;
            _size.IsEnabled = s.Magnify;
            _opens.SelectedIndex = s.NotchOpensOnHover ? 0 : 1;
            RefreshItems();
            Dim(_surfaces, s.IsMac);
            Dim(_barRows, s.IsMac && s.MenuBar);
            Dim(_dockRows, s.IsMac && s.Dock);
            Dim(_notchRows, s.IsMac && s.Notch);
            if (_module.Error is { } error)
            {
                _error.Text = error;
                _error.Visibility = Visibility.Visible;
            }
        }
        finally { _updating = false; }
    }

    // ----- Menu bar items -----

    /// <summary>A row per item on the bar, then the clock. Built again only when the items or what can be added change.</summary>
    private void RefreshItems()
    {
        var shown = _module.BarItemsShown;
        var toAdd = _module.BarItemsToAdd;
        _add.IsEnabled = toAdd.Count > 0;
        string key = string.Join('|', shown);
        if (key == _itemsKey && _items.Children.Count > 0)
            return;
        _itemsKey = key;
        _items.Children.Clear();
        for (int i = 0; i < shown.Count; i++)
            _items.Children.Add(ItemRow(shown, i));
        _items.Children.Add(Row(null, MenuBarController.Clock, "Clock", Lock()));
    }

    private FrameworkElement ItemRow(IReadOnlyList<string> shown, int index)
    {
        string id = shown[index];
        string name = BarItems.Find(id)?.Name ?? id;
        var handle = new DragHandle { Width = 16, Height = 24, ToolTip = "Reorder" };
        AutomationProperties.SetName(handle, $"Reorder {name}");
        handle.MoveRequested += (_, delta) => Move(shown, id, index + delta);
        double distance = 0;
        int target = index;
        handle.DragStarted += (_, _) => { distance = 0; target = index; };
        handle.DragDelta += (_, e) =>
        {
            distance += e.VerticalChange;
            target = Math.Clamp(index + (int)Math.Round(distance / SettingsLayout.RowHeight), 0, shown.Count - 1);
            handle.ToolTip = $"Position {target + 1}";
        };
        handle.DragCompleted += (_, e) =>
        {
            if (!e.Canceled)
                Move(shown, id, target);
        };
        var more = new IconButton { Icon = LucideIcons.Ellipsis, ToolTip = $"Options for {name}", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(more, $"Options for {name}");
        more.Click += (_, _) => Menu(more,
        [
            new("Move up", () => Move(shown, id, index - 1), index > 0),
            new("Move down", () => Move(shown, id, index + 1), index + 1 < shown.Count),
            new("Remove", () => SaveItems(shown, BarItems.Removed(shown, id)), Divider: true),
        ]);
        return Row(handle, id, name, more);
    }

    /// <summary>A grip (or its room), the item's glyph, its name, and what ends the row.</summary>
    private static Border Row(DragHandle? handle, string id, string name, FrameworkElement end)
    {
        FrameworkElement grip = handle is not null ? handle : new Border { Width = 16 };
        grip.Margin = new Thickness(0, 0, 16, 0);
        var glyph = new LucideIcon { Data = MenuBar.Glyphs.Of(id), Size = 16, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel.SetDock(grip, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(glyph, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(end, System.Windows.Controls.Dock.Right);
        return new Border { MinHeight = SettingsLayout.RowHeight, Padding = new Thickness(16, 0, 8, 0), Child = new DockPanel { Children = { grip, glyph, end, label } } };
    }

    /// <summary>The clock's row ends in a lock: it's always last.</summary>
    private static FrameworkElement Lock()
    {
        var glyph = new LucideIcon { Data = LucideIcons.Lock, Size = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextTertiaryBrush");
        return new Border { Width = 32, Child = glyph, ToolTip = "Always last" };
    }

    private void Move(IReadOnlyList<string> shown, string id, int to)
    {
        var moved = BarItems.Moved(shown, id, to);
        if (ReferenceEquals(moved, shown))
            return;
        SaveItems(shown, moved);
        // The grip keeps the keyboard, so Up and Down can be pressed again.
        int at = moved.ToList().IndexOf(id);
        if (_items.Children.OfType<Border>().ElementAtOrDefault(at)?.Child is DockPanel row)
            row.Children.OfType<DragHandle>().FirstOrDefault()?.Focus();
    }

    /// <summary>Saves an edit of the rows. Items this PC can't show now have no row, and stay saved (<see cref="BarItems.Kept"/>).</summary>
    private void SaveItems(IReadOnlyList<string> before, IReadOnlyList<string> after) =>
        Save(s => s with { BarItems = BarItems.Kept(s.BarItems, before, after) });

    /// <summary>Add item: what isn't on the bar, Chippi's first, then Windows' own. A pick goes at the right end.</summary>
    private void AddMenu()
    {
        var shown = _module.BarItemsShown;
        var choices = new List<Choice>();
        bool windows = false;
        foreach (string id in _module.BarItemsToAdd)
        {
            if (BarItems.Find(id) is not { } item)
                continue;
            choices.Add(new Choice(item.Name, () => SaveItems(shown, BarItems.Added(shown, id)), Glyph: MenuBar.Glyphs.Of(id), Divider: item.Windows && !windows && choices.Count > 0));
            windows |= item.Windows;
        }
        if (choices.Count > 0)
            Menu(_add, choices);
    }

    private sealed record Choice(string Label, Action Run, bool Enabled = true, Geometry? Glyph = null, bool Divider = false);

    /// <summary>A small menu under <paramref name="anchor"/>, in the flyout's look: Up, Down and Esc work, and a pick closes it.</summary>
    private static void Menu(FrameworkElement anchor, IReadOnlyList<Choice> choices)
    {
        var stack = new StackPanel();
        var border = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        border.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        border.SetValue(TextElement.FontSizeProperty, 14d);
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var buttons = new List<Button>();
        foreach (var choice in choices)
        {
            if (choice.Divider)
            {
                var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
                line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                stack.Children.Add(line);
            }
            var content = new DockPanel();
            if (choice.Glyph is { } data)
            {
                var glyph = new LucideIcon { Data = data, Size = 16, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
                DockPanel.SetDock(glyph, System.Windows.Controls.Dock.Left);
                content.Children.Add(glyph);
            }
            content.Children.Add(new TextBlock { Text = choice.Label, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, IsEnabled = choice.Enabled, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 160 };
            AutomationProperties.SetName(button, choice.Label);
            button.SetResourceReference(StyleProperty, "FlyoutRowButtonStyle");
            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                choice.Run();
            };
            stack.Children.Add(button);
            if (choice.Enabled)
                buttons.Add(button);
        }
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                anchor.Focus();
                e.Handled = true;
            }
            else if (buttons.Count > 0 && e.Key is Key.Up or Key.Down)
            {
                int index = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
                buttons[(index + (e.Key == Key.Up ? buttons.Count - 1 : 1) + buttons.Count) % buttons.Count].Focus();
                e.Handled = true;
            }
        };
        popup.Opened += (_, _) => buttons.FirstOrDefault()?.Focus();
        popup.IsOpen = true;
    }

    private static void Set(ToggleButton toggle, bool on)
    {
        toggle.IsChecked = on;
        toggle.Content = on ? "On" : "Off";
    }

    /// <summary>
    /// The sections as they are with the mode on, for <c>--settings-snapshot</c>: a snapshot run never turns the mode on
    /// (the bars would show on the desktop), and with it off they're dimmed.
    /// </summary>
    public void ShowAsOnForShot()
    {
        _shownAsOn = true;
        Refresh();
    }

    private void Dim(FrameworkElement rows, bool enabled)
    {
        enabled |= _shownAsOn;
        rows.IsEnabled = enabled;
        rows.Opacity = enabled ? 1 : 0.45;
    }
}
