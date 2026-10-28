// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Views;

/// <summary>Settings pages' parts (DESIGN.md §5, §6), as on the other feature pages.</summary>
internal static class SettingsUi
{
    public static TextBlock Text(string text, string style = "BodyTextStyle")
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        return block;
    }

    public static Button Button(string text, string style = "SecondaryButtonStyle")
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        return button;
    }

    /// <summary>An On/Off switch that calls <paramref name="changed"/> only when the user flips it.</summary>
    public static ToggleButton Switch(string name, Func<bool> get, Action<bool> changed, out Action sync)
    {
        var toggle = new ToggleButton();
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        bool updating = false;
        sync = () =>
        {
            updating = true;
            toggle.IsChecked = get();
            toggle.Content = get() ? "On" : "Off";
            updating = false;
        };
        // Checked/Unchecked, not Click: UI Automation's Toggle raises no Click.
        toggle.Checked += (_, _) => { if (!updating && !get()) changed(true); };
        toggle.Unchecked += (_, _) => { if (!updating && get()) changed(false); };
        sync();
        return toggle;
    }

    /// <summary>A section's heading with its command on the right ("Apps to skip" · Add app).</summary>
    public static DockPanel Heading(string text, Button command)
    {
        var heading = Text(text, "HeadlineTextStyle");
        DockPanel.SetDock(command, Dock.Right);
        return new DockPanel { Margin = new Thickness(0, 24, 0, 8), Children = { command, heading } };
    }

    /// <summary>A list row: an optional lead, the name over an optional caption, and Remove.</summary>
    public static DockPanel ListRow(UIElement? lead, string name, string? caption, Action remove)
    {
        var button = Button("Remove");
        AutomationProperties.SetName(button, $"Remove {name}");
        button.Click += (_, _) => remove();
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { MinHeight = caption is null ? 48 : 60, Margin = new Thickness(16, 0, 16, 0), Children = { button } };
        if (lead is not null)
        {
            DockPanel.SetDock(lead, Dock.Left);
            row.Children.Add(lead);
        }
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Children = { Text(name) } };
        if (caption is not null)
            label.Children.Add(Text(caption, "CaptionTextStyle"));
        row.Children.Add(label);
        return row;
    }
}
