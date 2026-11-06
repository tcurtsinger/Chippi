// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenText.Views;

/// <summary>The pieces the Text and QR code pages share, in the island's styles.</summary>
internal static class Bits
{
    public const double FootHeight = 28;

    /// <summary>The capture's column on the left of both pages (Capture results v3).</summary>
    public const double CaptureWidth = 168;

    public static TextBlock Text(string text, double size = 12, string brush = "IslandTextBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static LucideIcon Glyph(Geometry icon, double size = 16, string brush = "IslandTextSecondaryBrush")
    {
        var glyph = new LucideIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    /// <summary>A 28 island button, the verb only; the accent one is the page's main verb.</summary>
    public static Button Button(string text, bool accent = false, Geometry? glyph = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var icon = Glyph(glyph, 14, accent ? "IslandOnAccentBrush" : "IslandTextSecondaryBrush");
            icon.Margin = new Thickness(0, 0, 6, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
        }
        var button = new Button { Content = content, Height = 28, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        AutomationProperties.SetName(button, text);
        return button;
    }

    public static Button Icon(Geometry icon, string name)
    {
        var button = new Button { Content = Glyph(icon, 16), Width = 28, Height = 24 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        button.ToolTip = name;
        return button;
    }

    /// <summary>An island card: the gallery face, the card edge and its 6 corners.</summary>
    public static Border Card(UIElement? child = null, Thickness? padding = null)
    {
        var card = new Border { Child = child, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = padding ?? new Thickness(0) };
        card.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");
        return card;
    }

    /// <summary>A state in place of a result: its glyph, its line, and a second line under it.</summary>
    public static StackPanel Message(Geometry icon, string line, string? detail = null, bool caution = false)
    {
        var glyph = Glyph(icon, 18, caution ? "IslandCautionBrush" : "IslandTextTertiaryBrush");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Text(line, 12, caution ? "IslandCautionBrush" : "IslandTextSecondaryBrush");
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 6, 0, 0);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { glyph, text } };
        if (detail is not null)
        {
            var second = Text(detail, 11, "IslandTextTertiaryBrush");
            second.HorizontalAlignment = HorizontalAlignment.Center;
            second.TextWrapping = TextWrapping.Wrap;
            second.TextAlignment = TextAlignment.Center;
            second.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(second);
        }
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return panel;
    }

    /// <summary>The page's header: its title, with anything else on the right.</summary>
    public static DockPanel Header(string title, UIElement? right = null)
    {
        var header = new DockPanel { LastChildFill = true };
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            header.Children.Add(right);
        }
        header.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return header;
    }

    /// <summary>"31 words", "1 word": whitespace-separated tokens (text-and-qr.md, Word count).</summary>
    public static string Words(string text)
    {
        int count = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return count == 1 ? "1 word" : $"{count.ToString(System.Globalization.CultureInfo.CurrentCulture)} words";
    }
}
