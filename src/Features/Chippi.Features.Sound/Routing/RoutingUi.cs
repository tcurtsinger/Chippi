// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

internal static class RoutingUi
{
    public static TextBlock Text(string text, string style = "BodyTextStyle", string? brush = null)
    {
        var block = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (brush is not null) block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static Button Button(string text, Action? clicked = null)
    {
        var button = new Button { Content = text };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        if (clicked is not null) button.Click += (_, _) => clicked();
        return button;
    }

    /// <summary>A text button on the Sound page: 28 tall and at least 64 wide, so the cards' buttons line up.</summary>
    public static Button IslandButton(string text, Action clicked)
    {
        var button = Button(text, clicked);
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandButtonStyle");
        button.Margin = new Thickness(8, 0, 0, 0);
        return button;
    }

    /// <summary>A 44 tall card on the Sound page: a 16 glyph, then <paramref name="middle"/>, then <paramref name="right"/>.</summary>
    public static Border IslandCard(LucideIcon glyph, UIElement middle, FrameworkElement right)
    {
        var card = new Border { Child = Line(glyph, middle, right) };
        card.SetResourceReference(FrameworkElement.StyleProperty, "IslandControlCardStyle");
        return card;
    }

    /// <summary>The hairline between a section's top row and its list.</summary>
    public static Border Hairline()
    {
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "IslandHairBrush");
        return line;
    }

    private static DockPanel Line(LucideIcon glyph, UIElement middle, FrameworkElement right)
    {
        glyph.Size = 16;
        glyph.Margin = new Thickness(0, 0, 10, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        return new DockPanel { Children = { glyph, right, middle } };
    }

    public static ToggleButton Switch(string name, bool on, Action<bool> changed)
    {
        var toggle = new ToggleButton { IsChecked = on, Content = on ? "On" : "Off" };
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        toggle.Checked += (_, _) => { toggle.Content = "On"; changed(true); };
        toggle.Unchecked += (_, _) => { toggle.Content = "Off"; changed(false); };
        return toggle;
    }

    /// <summary>A popup menu under <paramref name="anchor"/>. With <paramref name="current"/>, that item gets a check and the focus.</summary>
    public static void Menu(FrameworkElement anchor, IEnumerable<(string Label, Action Action, bool Enabled)> items, int current = -1, string? title = null)
    {
        var stack = new StackPanel();
        var border = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        if (title is not null)
        {
            var name = Text(title, "NavGroupTextStyle", "TextSecondaryBrush");
            name.Margin = new Thickness(10, 5, 10, 5);
            name.ToolTip = title;
            DockPanel.SetDock(name, Dock.Top);
            var choices = new ScrollViewer { Content = stack, MaxHeight = 224, Focusable = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            border.MaxWidth = 368;
            border.Child = new DockPanel { Children = { name, choices } };
            AutomationProperties.SetName(border, title);
        }
        else border.Child = stack;
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        border.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        border.SetValue(TextElement.FontSizeProperty, 14d);
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var buttons = new List<Button>();
        Button? focus = null;
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            // A long device name ends in "…" at 360 rather than stretching the menu across the screen.
            var label = new TextBlock { Text = item.Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { IsEnabled = item.Enabled, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 160, MaxWidth = 360 };
            AutomationProperties.SetName(button, item.Label);
            button.ToolTip = item.Label;
            if (current >= 0)
            {
                // A 20 column for the check, so the labels line up whichever item has it.
                var row = Columns(new GridLength(20), new(1, GridUnitType.Star));
                if (index == current)
                {
                    var check = new LucideIcon { Data = LucideIcons.Check, Size = 12, VerticalAlignment = VerticalAlignment.Center };
                    check.SetResourceReference(LucideIcon.BrushProperty, "AccentBrush");
                    Add(row, check, 0);
                    AutomationProperties.SetItemStatus(button, "Selected");
                }
                Add(row, label, 1);
                button.Content = row;
                if (index == current && item.Enabled) focus = button;
            }
            else button.Content = label;
            button.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutRowButtonStyle");
            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                if (title is not null) anchor.Focus();
                item.Action();
            };
            stack.Children.Add(button);
            if (button.IsEnabled) buttons.Add(button);
        }
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; anchor.Focus(); e.Handled = true; }
            else if (buttons.Count > 0 && e.Key is Key.Up or Key.Down)
            {
                var index = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
                buttons[(index + (e.Key == Key.Up ? buttons.Count - 1 : 1)) % buttons.Count].Focus();
                e.Handled = true;
            }
        };
        anchor.Unloaded += Close;
        popup.Closed += (_, _) => anchor.Unloaded -= Close;
        void Close(object sender, RoutedEventArgs args) => popup.IsOpen = false;
        popup.IsOpen = true;
        (focus ?? buttons.FirstOrDefault())?.Focus();
    }

    public static Grid Columns(params GridLength[] columns)
    {
        var grid = new Grid();
        foreach (var width in columns) grid.ColumnDefinitions.Add(new() { Width = width });
        return grid;
    }

    public static void Add(Grid grid, UIElement child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    public static FrameworkElement Section(string label, FrameworkElement? action = null)
    {
        var grid = Columns(new(1, GridUnitType.Star), GridLength.Auto);
        grid.Margin = new Thickness(0, 24, 0, 8);
        Add(grid, Text(label, "HeadlineTextStyle"), 0);
        if (action is not null) Add(grid, action, 1);
        return grid;
    }

    public static Card Card(params UIElement[] rows)
    {
        var card = new Card();
        foreach (var row in rows) card.Children.Add(row);
        return card;
    }

    public static Border Pad(UIElement child, double height = 60) =>
        new() { Child = child, MinHeight = height, Padding = new Thickness(16, 10, 16, 10) };
}
