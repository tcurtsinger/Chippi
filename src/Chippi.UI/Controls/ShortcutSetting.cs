// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Core.Actions;
using Chippi.Core.Shortcuts;

namespace Chippi.UI.Controls;

/// <summary>
/// A feature's shortcut on its own Settings page, read-only: its keys as caps, or "Not set", and Edit, which opens it in
/// Settings › Shortcuts. Shortcuts are recorded only there (the owner, 2026-11-05).
/// </summary>
public sealed class ShortcutSetting : StackPanel
{
    private readonly ContentControl _keys = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };

    /// <param name="name">The shortcut's name in Settings › Shortcuts, for Edit's accessible name ("App switcher").</param>
    /// <param name="edit">Opens Settings › Shortcuts with this shortcut's editor open in its row.</param>
    public ShortcutSetting(string name, Action edit)
    {
        VerticalAlignment = VerticalAlignment.Center;
        var label = new TextBlock { Text = "Edit", VerticalAlignment = VerticalAlignment.Center };
        var open = new LucideIcon { Data = LucideIcons.ExternalLink, Size = 14, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        open.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        EditButton = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, open } }, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        EditButton.SetResourceReference(StyleProperty, "SubtleButtonStyle");
        AutomationProperties.SetName(EditButton, $"Edit {name} shortcut");
        EditButton.Click += (_, _) => edit();
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _keys, EditButton } };
        AutomationProperties.SetName(line, "Shortcut");
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        _error.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        Children.Add(line);
        Children.Add(_error);
        Show(null, null);
    }

    public Button EditButton { get; }

    /// <summary>What the keys read as, "Not set" while unset (for tests and UI Automation).</summary>
    public string KeysText { get; private set; } = "";

    /// <summary>Shows <paramref name="keys"/> (null: "Not set") and, under them, why they don't work.</summary>
    public void Show(KeyShortcut? keys, string? error)
    {
        KeysText = keys?.ToString() ?? "Not set";
        if (keys is null)
        {
            var unset = new TextBlock { Text = "Not set", VerticalAlignment = VerticalAlignment.Center };
            unset.SetResourceReference(StyleProperty, "BodyTextStyle");
            unset.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _keys.Content = unset;
        }
        else
            _keys.Content = Keycaps.Of(Keycaps.CapsOf(keys), dim: error is not null);
        AutomationProperties.SetName(_keys, KeysText);
        _error.Text = error ?? "";
        _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>Keys drawn as caps, one bordered cap a key, as Settings › Shortcuts lists them.</summary>
public static class Keycaps
{
    /// <summary>The caps of <paramref name="keys"/>, modifiers first ("Ctrl", "Alt", "F").</summary>
    public static IReadOnlyList<string> CapsOf(KeyShortcut keys)
    {
        var mods = KeyModifiers.None;
        if (keys.Modifiers.HasFlag(ShortcutModifiers.Control)) mods |= KeyModifiers.Ctrl;
        if (keys.Modifiers.HasFlag(ShortcutModifiers.Alt)) mods |= KeyModifiers.Alt;
        if (keys.Modifiers.HasFlag(ShortcutModifiers.Shift)) mods |= KeyModifiers.Shift;
        if (keys.Modifiers.HasFlag(ShortcutModifiers.Windows)) mods |= KeyModifiers.Win;
        return new KeyCombo(mods, keys.Key).Caps;
    }

    /// <summary>Caps from the left edge; with <paramref name="mouse"/>, each with the mouse glyph; dimmed when they aren't working.</summary>
    public static FrameworkElement Of(IReadOnlyList<string> caps, bool mouse = false, bool dim = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Opacity = dim ? 0.45 : 1, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (string text in caps)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (mouse)
            {
                var m = new LucideIcon { Data = LucideIcons.Mouse, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                m.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
                content.Children.Add(m);
            }
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTextStyle");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            content.Children.Add(label);
            var cap = new Border { Child = content, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
            cap.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            panel.Children.Add(cap);
        }
        return panel;
    }
}
