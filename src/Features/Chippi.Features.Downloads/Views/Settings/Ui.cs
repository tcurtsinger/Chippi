// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>Small builders for the organizer's Settings views, all on DESIGN.md tokens.</summary>
internal static class Ui
{
    public static TextBlock Text(string text, string style = "BodyTextStyle", string? brush = null)
    {
        var block = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (brush is not null)
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static TextBlock Mono(string text, string brush = "TextSecondaryBrush") => Text(text, "MonoTextStyle", brush);

    /// <summary>A section eyebrow: "WATCHED FOLDERS".</summary>
    public static TextBlock Eyebrow(string text)
    {
        var block = Text(text.ToUpperInvariant(), "CaptionTextStyle", "TextTertiaryBrush");
        block.FontSize = 10.5;
        block.FontWeight = FontWeights.Bold;
        return block;
    }

    /// <summary>An amber line under a field or a name.</summary>
    public static TextBlock Warning(string text)
    {
        var block = Text(text, "CaptionTextStyle", "WarningTextBrush");
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    public static Button Button(string label, string style = "SecondaryButtonStyle", Geometry? icon = null, string? name = null)
    {
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (icon is null)
        {
            button.Content = label;
        }
        else
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = new LucideIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "ButtonTextBrush");
            content.Children.Add(glyph);
            content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
        }
        AutomationProperties.SetName(button, name ?? label);
        return button;
    }

    public static IconButton Icon(Geometry icon, string name)
    {
        var button = new IconButton { Icon = icon, ToolTip = name, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, name);
        return button;
    }

    public static LucideIcon Glyph(Geometry icon, string brush, double size = 15) =>
        WithBrush(new LucideIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center }, brush);

    private static LucideIcon WithBrush(LucideIcon icon, string brush)
    {
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    /// <summary>A Surface row container, 12px radius.</summary>
    public static Border Surface(UIElement child, Thickness? padding = null) => Box("SurfaceBrush", child, 12, padding ?? new Thickness(12, 9, 10, 9));

    /// <summary>A Panel container, 14px radius (history runs, the test box).</summary>
    public static Border Panel(UIElement child, Thickness? padding = null) => Box("PanelBrush", child, 14, padding ?? new Thickness(8));

    private static Border Box(string background, UIElement child, double radius, Thickness padding)
    {
        var border = new Border { CornerRadius = new CornerRadius(radius), Padding = padding, Child = child, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
        border.SetResourceReference(Border.BackgroundProperty, background);
        return border;
    }

    public static TextBox Field(string text = "", bool mono = false, string? name = null, double width = double.NaN)
    {
        var box = new TextBox { Text = text, Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, "TextFieldStyle");
        if (mono)
            box.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        if (name is not null)
            AutomationProperties.SetName(box, name);
        return box;
    }

    public static ComboBox Choice(IEnumerable<string> items, int selected, string name, double width = double.NaN, bool mono = false)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selected, Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        combo.SetResourceReference(FrameworkElement.StyleProperty, "HopperComboBoxStyle");
        if (mono)
            combo.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        AutomationProperties.SetName(combo, name);
        return combo;
    }

    /// <summary>A segmented control. Compact (folder modes, the rule action) uses 28px segments with 12px labels, like the flyout's.</summary>
    public static SegmentedControl Segment(string name, bool compact, params string[] labels)
    {
        var segment = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (compact && Application.Current?.TryFindResource("SegmentItemStyle") is Style item)
        {
            var style = new Style(typeof(ListBoxItem), item);
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 28.0));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            segment.ItemContainerStyle = style;
        }
        foreach (string label in labels)
            segment.Items.Add(new SegmentItem { Label = label });
        AutomationProperties.SetName(segment, name);
        return segment;
    }

    public static Badge Badge(string text, BadgeKind kind) => new() { Text = text, Kind = kind, VerticalAlignment = VerticalAlignment.Center };

    public static CheckBox Check(string name, bool? isChecked = false)
    {
        var box = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, name);
        return box;
    }

    public static ToggleButton Switch(string name, bool isOn)
    {
        var toggle = new ToggleButton { IsChecked = isOn, VerticalAlignment = VerticalAlignment.Center };
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "SwitchStyle");
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    /// <summary>A grid with the given column widths ("*", "Auto", or a number).</summary>
    public static Grid Columns(params string[] widths)
    {
        var grid = new Grid();
        foreach (string width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width switch
                {
                    "*" => new GridLength(1, GridUnitType.Star),
                    "Auto" => GridLength.Auto,
                    _ when width.EndsWith('*') => new GridLength(double.Parse(width[..^1], CultureInfo.InvariantCulture), GridUnitType.Star),
                    _ => new GridLength(double.Parse(width, CultureInfo.InvariantCulture)),
                },
            });
        }
        return grid;
    }

    public static T At<T>(this T element, int column, int row = 0) where T : UIElement
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        return element;
    }

    public static T Margin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    /// <summary>"1.2 MB", for the mono size column.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

    public static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    public static string FolderName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>A path shown relative to the watched folder when it is inside it ("Documents\2026\10").</summary>
    public static string Relative(string folder, string path)
    {
        string root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..] : path;
    }
}
