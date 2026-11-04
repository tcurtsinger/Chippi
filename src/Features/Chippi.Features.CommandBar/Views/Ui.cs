// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.CommandBar.Views;

/// <summary>The Command bar page's small parts, in the island's brushes and styles.</summary>
internal static class Ui
{
    public static TextBlock Text(string text = "", double size = 12, string brush = "IslandTextBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static LucideIcon Glyph(Geometry data, double size = 16, string brush = "IslandTextBrush")
    {
        var glyph = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    /// <summary>A row's icon button, 24 × 24 with a 14 glyph, with its name for Narrator.</summary>
    public static Button Icon(Geometry icon, string name, string tip)
    {
        var button = new Button { Content = Glyph(icon, 14, "IslandTextSecondaryBrush"), ToolTip = tip, Width = 24, Height = 24, Focusable = false };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>
    /// An app's letters on the accent tint, 20 square, until its icon is read (DESIGN.md: each word's first, "ZW" for
    /// Zoom Workplace; a one-word name's first two, "Sp").
    /// </summary>
    public static Border Monogram(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsLetterOrDigit)])).Where(w => w.Length > 0).ToList();
        string letters = words.Count switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => string.Concat(words.Take(2).Select(w => char.ToUpper(w[0], System.Globalization.CultureInfo.CurrentCulture))),
        };
        var label = Text(letters, 9);
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var mark = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Child = label };
        mark.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        return mark;
    }
}
