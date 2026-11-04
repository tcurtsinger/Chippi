// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Soundboard.Views;

internal static class SoundboardUi
{
    internal static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }

    internal static TextBlock Text(string text, double size = 12, string brush = "IslandTextBrush")
    {
        var result = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        result.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return result;
    }

    internal static LucideIcon Glyph(Geometry data, double size = 16)
    {
        var icon = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
        return icon;
    }

    internal static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, MinWidth = 64, Height = 28, Padding = new Thickness(10, 2, 10, 2), FontSize = 12 };
        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        if (primary) button.SetResourceReference(Control.ForegroundProperty, "SoundboardGradientInkBrush");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    internal static Button Icon(Geometry icon, string name, Action action)
    {
        var button = new Button { Content = Glyph(icon), Width = 28, Height = 28, Padding = new Thickness(5), ToolTip = name };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SoundboardQuietButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    internal static TextBox Input(string name, string text = "")
    {
        var input = new TextBox { Text = text, Height = 28, Padding = new Thickness(7, 3, 7, 3), FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 120 };
        input.SetResourceReference(FrameworkElement.StyleProperty, "TextFieldStyle");
        AutomationProperties.SetName(input, name);
        return input;
    }

    internal static Grid Columns(params GridLength[] sizes)
    {
        var grid = new Grid();
        foreach (var size in sizes) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = size });
        return grid;
    }

    internal static void At(Grid grid, UIElement child, int column, int row = 0)
    {
        Grid.SetColumn(child, column); Grid.SetRow(child, row); grid.Children.Add(child);
    }

    internal static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0);
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    internal static bool TryTime(string text, out double seconds)
    {
        seconds = 0;
        var parts = text.Split(':');
        if (parts.Length is < 1 or > 3) return false;
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            const NumberStyles style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
            if ((!double.TryParse(parts[i], style, CultureInfo.CurrentCulture, out var segment)
                && !double.TryParse(parts[i], style, CultureInfo.InvariantCulture, out segment))
                || !double.IsFinite(segment) || segment < 0 || (i > 0 && segment >= 60)
                || (i < parts.Length - 1 && segment != Math.Truncate(segment))) return false;
            total = total * 60 + segment;
        }
        if (!double.IsFinite(total)) return false;
        seconds = total;
        return true;
    }

    internal static ImageSource? Cover(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var file = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 128;
            image.StreamSource = file; image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException) { return null; }
    }
}
