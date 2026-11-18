// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chippi.UI.Controls;

namespace Chippi.Features.Cleaner.Views;

/// <summary>Small builders the Cleaner screens share. Colors are the app's theme resources; there are no new ones.</summary>
internal static class CleanerUi
{
    internal static TextBlock Text(string text = "", double size = 12, string brush = "IslandTextBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    internal static LucideIcon Glyph(Geometry data, double size = 16, string brush = "IslandTextSecondaryBrush")
    {
        var icon = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    /// <summary>The loader glyph, turning while it is on screen.</summary>
    internal static LucideIcon Spinner(double size)
    {
        var icon = Glyph(LucideIcons.LoaderCircle, size, "IslandAccentBrush");
        var turn = new RotateTransform();
        icon.RenderTransformOrigin = new Point(.5, .5);
        icon.RenderTransform = turn;
        icon.Loaded += (_, _) => turn.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        icon.Unloaded += (_, _) => turn.BeginAnimation(RotateTransform.AngleProperty, null);
        return icon;
    }

    /// <summary>An island button: a bare verb. <paramref name="shield"/> puts the UAC shield left of it; critical fills it; accent is the header's Clean.</summary>
    internal static Button IslandButton(string text, Action action, bool critical = false, bool shield = false, bool small = false, bool accent = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        object content = label;
        if (shield)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new UacShield(13) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(label);
            content = row;
        }
        var button = new Button { Content = content };
        button.SetResourceReference(FrameworkElement.StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        if (small) { button.Height = 24; button.MinWidth = 44; button.FontSize = 11; button.Padding = new Thickness(8, 0, 8, 0); }
        if (critical)
        {
            button.SetResourceReference(Control.BackgroundProperty, "DangerBrush");
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.SemiBold;
        }
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>The list's checkbox: 16 DIPs, the accent fill when ticked.</summary>
    internal static Border Check(bool on, bool disabled)
    {
        var box = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            box.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
            box.BorderBrush = Brushes.Transparent;
            box.Child = Glyph(LucideIcons.Check, 12, "IslandOnAccentBrush");
            ((FrameworkElement)box.Child).HorizontalAlignment = HorizontalAlignment.Center;
        }
        else box.SetResourceReference(Border.BorderBrushProperty, "IslandTextTertiaryBrush");
        if (disabled) box.Opacity = .4;
        return box;
    }

    internal static void Add(Grid grid, UIElement element, int row = 0, int column = 0)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
