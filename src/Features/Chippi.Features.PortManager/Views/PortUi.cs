// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.PortManager.Views;

/// <summary>Small builders both Port manager screens share. Colors are the app's theme resources; there are no new ones.</summary>
internal static class PortUi
{
    /// <summary>False in the offscreen shots and tests: a holder's own icon is read from its program, and a fixture has none.</summary>
    internal static bool LoadIcons { get; set; } = true;

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

    /// <summary>An island button: bare verb. <paramref name="shield"/> puts the UAC shield inside it, left of the verb; critical fills it.</summary>
    internal static Button IslandButton(string text, Action action, bool critical = false, bool shield = false, bool small = false)
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
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandButtonStyle");
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

    internal static Button IslandIcon(Geometry data, string name, Action action)
    {
        var button = new Button { Content = Glyph(data, 14), ToolTip = name };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// The holder's mark: its own program icon with its letters until it's read, Chippi's gradient, or a plain tile for Windows' own
    /// processes (the program is Windows').
    /// </summary>
    internal static FrameworkElement Mark(PortHolder holder, double size)
    {
        if (holder.Kind == HolderKind.Chippi)
        {
            var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
            return dot;
        }
        if (holder.Kind is HolderKind.Windows or HolderKind.Gone)
        {
            var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size <= 20 ? 4 : 6), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = Glyph(LucideIcons.Monitor, Math.Round(size * .6)) };
            tile.Child.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tile.SetResourceReference(Border.BackgroundProperty, "IslandButtonBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush");
            return tile;
        }
        return Monogram(holder.Name, holder.Path, size);
    }

    /// <summary>A program's letters on the accent tint, replaced by its icon once read.</summary>
    internal static FrameworkElement Monogram(string name, string? path, double size)
    {
        string stem = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        string letters = new([.. stem.Where(char.IsLetterOrDigit).Take(2)]);
        letters = letters.Length == 0 ? "?" : char.ToUpper(letters[0], CultureInfo.CurrentCulture) + letters[1..];
        var label = Text(letters, size <= 20 ? 9 : size <= 28 ? 10 : 14, "IslandAccentBrush");
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var mono = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size <= 20 ? 4 : 6), Child = label, VerticalAlignment = VerticalAlignment.Center };
        mono.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        return LoadIcons && !string.IsNullOrEmpty(path) ? AppIcon.View(path, size, mono) : mono;
    }

    internal static void Add(Grid grid, UIElement element, int row = 0, int column = 0)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>"Port 3000" or "5353 +2": the lowest port and how many more a process holds.</summary>
    internal static string PortsText(int first, int count) => count > 1 ? $"{first} +{count - 1}" : first.ToString(CultureInfo.InvariantCulture);
}
