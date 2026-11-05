// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.UI.Actions;

/// <summary>The picker's fields and buttons, styled as Settings' own.</summary>
internal static class PickerUi
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

    public static TextBox Field(string name, double width = 220, bool code = false)
    {
        var box = new TextBox { Width = width, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, "TextFieldStyle");
        if (code)
            box.SetResourceReference(Control.FontFamilyProperty, "CodeFont");
        AutomationProperties.SetName(box, name);
        return box;
    }

    public static ComboBox Combo(string name, IEnumerable<string> items, double width = 220)
    {
        var combo = SettingsLayout.Combo(items.Cast<object>());
        combo.Width = width;
        AutomationProperties.SetName(combo, name);
        return combo;
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

    /// <summary>A glyph or an app's icon on the island's fill, 32 square: the Glyph row's preview and the picker's buttons.</summary>
    public static Border Swatch(Geometry glyph, ImageSource? image = null)
    {
        FrameworkElement shown = image is not null ? new Image { Source = image, Width = 24, Height = 24 } : new LucideIcon { Data = glyph, Size = 20 };
        if (shown is Image appImage) AppIcon.Configure(appImage);
        if (shown is LucideIcon icon)
            icon.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
        shown.HorizontalAlignment = HorizontalAlignment.Center;
        shown.VerticalAlignment = VerticalAlignment.Center;
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = shown };
        swatch.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        swatch.SetResourceReference(Border.BorderBrushProperty, "ButtonBorderBrush");
        return swatch;
    }
}
