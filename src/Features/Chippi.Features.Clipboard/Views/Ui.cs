// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Views;

/// <summary>The island page's small parts, in the island's brushes and styles.</summary>
internal static class Ui
{
    public static TextBlock Text(string text = "", double size = 13, string brush = "IslandTextBrush")
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

    /// <summary>A Caution line with its dot: a limit, "Clipboard busy", "Not a web link". Read out when it appears.</summary>
    public static StackPanel Caution()
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "WarningTextBrush");
        var text = Text("", 12, "WarningTextBrush");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Children = { dot, text } };
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        return line;
    }

    public static void Show(StackPanel caution, string? text)
    {
        var block = (TextBlock)caution.Children[1];
        block.Text = text ?? "";
        caution.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(caution, text ?? "");
    }

    /// <summary>A text button: "Paste" (accent), "Copy", "Clear" (Critical).</summary>
    public static Button Button(string text, string style = "SecondaryButtonStyle")
    {
        var button = new Button { Content = text };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        return button;
    }

    /// <summary>A 32px icon button with its name for Narrator.</summary>
    public static IconButton Icon(Geometry icon, string name)
    {
        var button = new IconButton { Icon = icon, ToolTip = name };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>The island's menu: one row per item, under <paramref name="anchor"/>.</summary>
    public static Popup Menu(UIElement anchor, params (string Text, bool Enabled, Action Click)[] items)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var (text, enabled, click) in items)
        {
            var item = new Button { Content = text, IsEnabled = enabled };
            item.SetResourceReference(FrameworkElement.StyleProperty, "IslandMenuItemStyle");
            item.Click += (_, _) =>
            {
                popup.IsOpen = false;
                click();
            };
            list.Children.Add(item);
        }
        popup.Child = Panel(list, minWidth: 160);
        return popup;
    }

    /// <summary>A question under <paramref name="anchor"/>: the question, then Cancel and the verb (Critical) (notes #11).</summary>
    public static Popup Confirm(UIElement anchor, string question, string verb, Action confirmed)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
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
                Text(question),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, act } },
            },
        };
        popup.Child = Panel(body, minWidth: 260);
        popup.Opened += (_, _) => cancel.Focus();
        return popup;
    }

    private static Border Panel(UIElement child, double minWidth)
    {
        var panel = new Border { MinWidth = minWidth, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = child };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        return panel;
    }
}
