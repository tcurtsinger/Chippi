// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>The entire tile opens its destination. Its live reading is available on hover and to assistive technology.</summary>
internal sealed class IslandGalleryTile : Button
{
    private readonly LucideIcon _glyph = new() { Size = 25 };
    private readonly Ellipse _waiting = new() { Width = 8, Height = 8, StrokeThickness = 1.5,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, -4, 0) };
    private readonly TextBlock _name = new() { FontSize = 12, FontWeight = FontWeights.Medium,
        TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(3, 6, 3, 0) };

    internal IslandGalleryTile(IslandTileView tile, Action<string> open)
    {
        Tag = tile.Key;
        Margin = new Thickness(0, 0, 5, 5);
        SetResourceReference(StyleProperty, "IslandTileStyle");
        _waiting.SetResourceReference(Shape.FillProperty, "PetSparkBrush");
        _waiting.SetResourceReference(Shape.StrokeProperty, "IslandBrush");
        var icon = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Children = { _glyph, _waiting } };
        Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { icon, _name } };
        Click += (_, _) => open((string)Tag);
        Show(tile);
    }

    internal void Show(IslandTileView tile)
    {
        _glyph.Data = tile.Icon;
        _glyph.SetResourceReference(LucideIcon.BrushProperty,
            tile.Caution && !tile.Waiting ? "WarningBrush" : tile.On ? "IslandAccentBrush" : "IslandTextBrush");
        _waiting.Visibility = tile.Waiting ? Visibility.Visible : Visibility.Collapsed;
        _name.Text = tile.Name;
        string destination = tile.Page is { } page && page != tile.Name ? $"{tile.Name} · {page}" : tile.Name;
        string description = string.IsNullOrWhiteSpace(tile.Status) ? destination : $"{destination} · {tile.Status}";
        if (tile.Waiting && !description.Contains("Waiting on you", StringComparison.OrdinalIgnoreCase))
            description += " · Waiting on you";
        ToolTip = description;
        AutomationProperties.SetName(this, $"Open {tile.Name}");
        AutomationProperties.SetHelpText(this, description);
    }
}

/// <summary>ToggleButton routes both a native press and UI Automation's Toggle pattern through OnToggle.
/// The module owns IsChecked. A refresh only reflects its state and never sends another command.</summary>
internal sealed class IslandGallerySwitch : ToggleButton
{
    private readonly Action<string> _toggle;

    internal IslandGallerySwitch(IslandTileView tile, Action<string> toggle)
    {
        _toggle = toggle;
        Tag = tile.Key;
        SetResourceReference(StyleProperty, "IslandGallerySwitchStyle");
        Show(tile);
    }

    internal void Show(IslandTileView tile)
    {
        string action = tile.Key switch
        {
            "sound" or "sound.microphone" => "Mute mic",
            "keep-awake" => "Keep awake",
            _ => tile.Name,
        };
        IsChecked = tile.On;
        Content = tile.Status == "Failed" ? $"{action} · Failed" : action;
        SetResourceReference(ForegroundProperty, tile.Status == "Failed" ? "WarningTextBrush" : "IslandTextSecondaryBrush");
        AutomationProperties.SetName(this, action);
        AutomationProperties.SetHelpText(this, tile.Status);
        ToolTip = string.IsNullOrWhiteSpace(tile.Status) ? action : $"{action} · {tile.Status}";
    }

    protected override void OnToggle() => _toggle((string)Tag);
}
