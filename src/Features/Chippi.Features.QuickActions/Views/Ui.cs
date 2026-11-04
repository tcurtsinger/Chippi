// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

/// <summary>The Actions page's small parts, in the island's brushes and styles.</summary>
internal static class Ui
{
    public static TextBlock Text(string text = "", double size = 12, string brush = "IslandTextBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static TextBlock Muted(string text = "", double size = 12) => Text(text, size, "IslandTextTertiaryBrush");

    public static LucideIcon Glyph(Geometry data, double size = 16, string brush = "IslandTextBrush")
    {
        var glyph = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    /// <summary>A text button: Done (IslandAccentButtonStyle), "Quit all". 28 tall, at least 64 wide.</summary>
    public static Button Button(string text, string style = "IslandButtonStyle")
    {
        var button = new Button { Content = text };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        AutomationProperties.SetName(button, text);
        return button;
    }

    /// <summary>A row's icon button, 24 x 24 with a 14 glyph, with its name for Narrator.</summary>
    public static Button Icon(Geometry icon, string name)
    {
        var button = new Button { Content = Glyph(icon, 14, "IslandTextSecondaryBrush"), ToolTip = name, Width = 24 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>"Front" or "Kept" beside an app: 18 tall, the control fill with its edge, 11 semibold secondary.</summary>
    public static Border Badge(string text)
    {
        var label = Text(text, 11, "IslandTextSecondaryBrush");
        label.FontWeight = FontWeights.SemiBold;
        var badge = new Border { Height = 18, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = label, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty, "IslandButtonBrush");
        badge.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush");
        return badge;
    }

    /// <summary>
    /// An app's letters, as App volume's name tiles have them (DESIGN.md): each word's first ("ZW" for Zoom Workplace), a
    /// one-word name's first two ("Sp"), from letters and digits only.
    /// </summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsLetterOrDigit)])).Where(w => w.Length > 0).ToList();
        string letters = words.Count switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => string.Concat(words.Take(2).Select(w => char.ToUpper(w[0], System.Globalization.CultureInfo.CurrentCulture))),
        };
        return letters;
    }

    /// <summary>An app's mark when its icon can't be read: its letters on the accent tint.</summary>
    public static Border Monogram(string name)
    {
        var label = Text(Initials(name), 8, "IslandAccentBrush");
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var mark = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Child = label, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        return mark;
    }

    private static TextBlock WithForeground(this TextBlock block, string brush)
    {
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    /// <summary>A question under <paramref name="anchor"/>: the question, then Cancel and the verb (Critical).</summary>
    public static Popup Confirm(UIElement anchor, string question, string verb, Action confirmed, string? detail = null)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, VerticalOffset = 2, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var cancel = Button("Cancel");
        cancel.Click += (_, _) => popup.IsOpen = false;
        var act = Button(verb, "CriticalButtonStyle");
        act.Margin = new Thickness(8, 0, 0, 0);
        act.Click += (_, _) =>
        {
            popup.IsOpen = false;
            confirmed();
        };
        var body = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                // A long app name wraps rather than losing the app spared.
                new TextBlock { Text = question, FontSize = 12, TextWrapping = TextWrapping.Wrap }.WithForeground("IslandTextBrush"),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, act } },
            },
        };
        if (!string.IsNullOrEmpty(detail))
        {
            var kept = new TextBlock { Text = detail, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 96, ToolTip = detail, Margin = new Thickness(0, 6, 0, 0) }.WithForeground("IslandTextSecondaryBrush");
            // Exception names stay with the reviewed choice, including the full names in the tooltip.
            body.Children.Insert(1, kept);
        }
        var panel = new Border { Width = 280, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(panel, KeyboardNavigationMode.Cycle);
        panel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            popup.IsOpen = false;
            e.Handled = true;
        };
        popup.Child = panel;
        popup.Opened += (_, _) => cancel.Focus();
        popup.Closed += (_, _) => { if (anchor.IsVisible && anchor.IsEnabled) anchor.Focus(); };
        return popup;
    }
}
