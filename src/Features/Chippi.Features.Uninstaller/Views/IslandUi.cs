// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chippi.UI.Controls;

namespace Chippi.Features.Uninstaller.Views;

/// <summary>Small builders the Uninstaller's island page shares. Colors are the island's theme resources; there are no new ones.</summary>
internal static class IslandUi
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

    /// <summary>A quiet uninstaller has no measure: a short segment slides along the track while it runs.</summary>
    internal static FrameworkElement Indeterminate()
    {
        const double width = 180, segment = 61;
        var slide = new TranslateTransform(width * .28, 0);
        var fill = new Border { Width = segment, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = slide };
        fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
        var track = new Border { Width = width, Height = 4, CornerRadius = new CornerRadius(2), Child = fill, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
        fill.Loaded += (_, _) => slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-segment, width, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
        fill.Unloaded += (_, _) => slide.BeginAnimation(TranslateTransform.XProperty, null);
        return track;
    }

    /// <summary>An island button: a bare verb. <paramref name="shield"/> puts the UAC shield left of it; critical fills it; accent is a result's primary.</summary>
    internal static Button IslandButton(string text, Action action, bool critical = false, bool shield = false, bool accent = false)
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

    internal static Button IslandIcon(Geometry data, string name, Action action)
    {
        var button = new Button { Content = Glyph(data, 14), ToolTip = name };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>An app's letters on the accent tint, replaced by its own icon once read.</summary>
    internal static FrameworkElement Mark(string name, string? iconPath, double size)
    {
        var label = Text(UninstallerUi.Letters(name), size <= 20 ? 9 : size <= 28 ? 10 : 13, "IslandAccentBrush");
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size <= 20 ? 4 : 6), Child = label, VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        return UninstallerUi.LoadIcons && !string.IsNullOrEmpty(iconPath) ? AppIcon.View(iconPath, size, tile) : tile;
    }

    /// <summary>An 18-high badge: neutral (Store) or accent (New).</summary>
    internal static FrameworkElement Badge(string text, bool accent)
    {
        var label = Text(text, 11, accent ? "IslandAccentBrush" : "IslandTextSecondaryBrush");
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(6, 0, 6, 0);
        var box = new Border { Height = 18, CornerRadius = new CornerRadius(4), Child = label, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.BackgroundProperty, accent ? "IslandAccentTintBrush" : "IslandButtonBrush");
        return box;
    }

    internal static void Add(Grid grid, UIElement element, int row = 0, int column = 0)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
