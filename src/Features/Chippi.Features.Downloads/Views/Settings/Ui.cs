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

/// <summary>Small builders for the organizer's Settings views, on Chippi.UI's cards and rows (DESIGN.md §5, §6).</summary>
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

    public static TextBlock Number(string text, string brush = "TextSecondaryBrush") => Text(text, "NumberTextStyle", brush);

    /// <summary>
    /// A section header ("Watched folders"), 24px above and 8px below, with an optional element on
    /// the right (Add folder). The first section under the SelectorBar sits closer.
    /// </summary>
    public static FrameworkElement Section(string text, FrameworkElement? right = null, bool first = false)
    {
        return SettingsLayout.Section(text, right, first);
    }

    /// <summary>A card of rows with dividers between them (DESIGN.md §6 "Card").</summary>
    public static Card Card(params UIElement[] rows)
    {
        var card = SettingsLayout.Card();
        foreach (var row in rows)
            card.Children.Add(row);
        return card;
    }

    /// <summary>A Settings row: label over caption on the left, the control on the right.</summary>
    public static SettingRow Setting(string header, string description, object control, Geometry? icon = null)
    {
        var row = SettingsLayout.Row(header, (UIElement)control, description);
        row.Icon = icon;
        return row;
    }

    /// <summary>A free-form row inside a card, with the shared row padding (16, 8) and at least 48px.</summary>
    public static Border Pad(UIElement child, double minHeight = 48) =>
        new() { Child = child, Padding = SettingsLayout.RowPadding, MinHeight = minHeight };

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
            var glyph = new LucideIcon { Data = icon, Size = 16, VerticalAlignment = VerticalAlignment.Center };
            // The button's own label color, so the glyph is on-accent on an accent button.
            glyph.SetBinding(LucideIcon.BrushProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = button });
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

    public static TextBox Field(string text = "", bool code = false, string? name = null, double width = double.NaN)
    {
        var box = new TextBox { Text = text, Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, "TextFieldStyle");
        if (code)
            box.SetResourceReference(Control.FontFamilyProperty, "CodeFont");
        if (name is not null)
            AutomationProperties.SetName(box, name);
        return box;
    }

    /// <summary>
    /// A text field with no chrome of its own, for a card that is itself the field (the Rules test
    /// card): the placeholder in muted text while empty, then the text and caret.
    /// </summary>
    public static TextBox BareField(string placeholder, string name)
    {
        var hint = new FrameworkElementFactory(typeof(TextBlock), "Placeholder");
        hint.SetValue(TextBlock.TextProperty, placeholder);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        hint.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        hint.SetValue(UIElement.IsHitTestVisibleProperty, false);
        hint.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        host.SetValue(UIElement.FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent); // the whole field takes the click
        root.AppendChild(hint);
        root.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = root };
        template.Triggers.Add(new Trigger { Property = TextBox.TextProperty, Value = "", Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Placeholder") } });

        var box = new TextBox { Template = template, Height = 32, FocusVisualStyle = null, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "TextPrimaryBrush");
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "AccentBrush");
        AutomationProperties.SetName(box, name);
        return box;
    }

    public static ComboBox Choice(IEnumerable<string> items, int selected, string name, double width = SettingsLayout.ComboWidth, bool code = false)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selected, Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        combo.SetResourceReference(FrameworkElement.StyleProperty, "HopperComboBoxStyle");
        if (code)
            combo.SetResourceReference(Control.FontFamilyProperty, "CodeFont");
        AutomationProperties.SetName(combo, name);
        return combo;
    }

    /// <summary>A segmented control at the Settings size: items sized to their labels.</summary>
    public static SegmentedControl Segment(string name, params string[] labels)
    {
        var segment = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        foreach (string label in labels)
            segment.Items.Add(new SegmentItem { Label = label });
        AutomationProperties.SetName(segment, name);
        return segment;
    }

    /// <summary>The SelectorBar under a page title: text tabs with the accent underline.</summary>
    public static SegmentedControl SelectorBar(string name, params string[] labels)
    {
        var bar = Segment(name, labels);
        bar.SetResourceReference(FrameworkElement.StyleProperty, "SelectorBarStyle");
        return bar;
    }

    public static Badge Badge(string text, BadgeKind kind) => new() { Text = text, Kind = kind, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A click came from inside a <typeparamref name="T"/> (a button or grip in a header that toggles on click).</summary>
    public static bool IsInside<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is T)
                return true;
        }
        return false;
    }

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

    /// <summary>"1.2 MB", for the size column.</summary>
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
