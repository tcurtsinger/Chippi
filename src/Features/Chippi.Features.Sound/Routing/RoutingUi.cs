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
        glyph.Size = 16;
        glyph.Margin = new Thickness(0, 0, 10, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        var card = new Border { Child = new DockPanel { Children = { glyph, right, middle } } };
        card.SetResourceReference(FrameworkElement.StyleProperty, "IslandControlCardStyle");
        return card;
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

    public static void Menu(FrameworkElement anchor, IEnumerable<(string Label, Action Action, bool Enabled)> items)
    {
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        border.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        border.SetValue(TextElement.FontSizeProperty, 14d);
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var buttons = new List<Button>();
        foreach (var item in items)
        {
            var button = new Button { Content = item.Label, IsEnabled = item.Enabled, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 160 };
            button.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutRowButtonStyle");
            button.Click += (_, _) => { popup.IsOpen = false; item.Action(); };
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
        buttons.FirstOrDefault()?.Focus();
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