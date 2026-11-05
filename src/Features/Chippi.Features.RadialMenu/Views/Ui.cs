// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Chippi.Features.RadialMenu.Views;

/// <summary>Settings › Radial menu's small parts, in Settings' brushes and styles.</summary>
internal static class Ui
{
    public static TextBlock Text(string text, string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(FrameworkElement.StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A switch with its On or Off beside it, Settings' own.</summary>
    public static ToggleButton Switch(string name)
    {
        var toggle = new ToggleButton();
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    /// <summary>Keys as caps, "Ctrl" "Shift" "T", 24 tall with a heavier bottom edge.</summary>
    public static StackPanel Caps(IEnumerable<string> keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string key in keys)
        {
            var label = new TextBlock { Text = key, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var cap = new Border
            {
                Height = 24,
                MinWidth = 24,
                Padding = new Thickness(7, 0, 7, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Margin = new Thickness(row.Children.Count == 0 ? 0 : 4, 0, 0, 0),
                Child = label,
            };
            cap.SetResourceReference(Border.BackgroundProperty, "ButtonBackgroundBrush");
            cap.SetResourceReference(Border.BorderBrushProperty, "ButtonBorderBrush");
            row.Children.Add(cap);
        }
        return row;
    }
}
