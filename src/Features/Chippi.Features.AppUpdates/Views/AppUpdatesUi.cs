// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.AppUpdates.Views;

internal static class AppUpdatesUi
{
    internal static TextBlock Text(string text = "", double size = 12, string brush = "IslandTextBrush")
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    internal static LucideIcon Glyph(Geometry data, double size = 16, string brush = "IslandTextSecondaryBrush")
    {
        var icon = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    internal static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text };
        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    internal static Button Icon(Geometry data, string name, Action action)
    {
        var button = new Button { Content = Glyph(data, 14), ToolTip = name };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    internal static FrameworkElement AppMark(string name, string? path, bool loadIcon = true, double size = 26)
    {
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string letters = words.Length switch
        {
            0 => "?",
            1 => new string([.. words[0].Where(char.IsLetterOrDigit).Take(2)]),
            _ => string.Concat(words.Take(2).Select(w => char.ToUpper(w[0], CultureInfo.CurrentCulture))),
        };
        var text = Text(string.IsNullOrEmpty(letters) ? "?" : letters, size <= 20 ? 9 : 10, "IslandAccentBrush");
        text.FontWeight = FontWeights.SemiBold; text.HorizontalAlignment = HorizontalAlignment.Center;
        var monogram = new Border { Width = size, Height = size, CornerRadius = new(size <= 20 ? 4 : 6), Child = text };
        monogram.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        return loadIcon ? AppIcon.View(path, size, monogram) : monogram;
    }

    internal static string Versions(string? from, string to, string source)
        => $"{(string.IsNullOrWhiteSpace(from) ? "Unknown" : from)} → {to}" + (string.IsNullOrEmpty(source) ? "" : $" · {source}");

    internal static string CheckedAt(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is null) return "Not checked";
        TimeSpan age = now - time.Value;
        return age < TimeSpan.FromMinutes(1) ? "Checked just now"
            : age < TimeSpan.FromHours(1) ? $"Checked {(int)age.TotalMinutes} min ago"
            : time.Value.LocalDateTime.Date == now.LocalDateTime.Date ? $"Checked {time.Value.LocalDateTime:t}"
            : $"Checked {time.Value.LocalDateTime:g}";
    }

    internal static void Add(Grid grid, UIElement element, int row = 0, int column = 0)
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, column); grid.Children.Add(element);
    }
}
