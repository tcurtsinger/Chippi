// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
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

    /// <summary>The theme-aware Settings menu, as on the Sound pages. Created closed for windowless checks.</summary>
    public static Popup Menu(FrameworkElement anchor, params (string Text, bool Enabled, Action Click)[] items)
    {
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        border.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        border.SetValue(TextElement.FontSizeProperty, 14d);
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var buttons = new List<Button>();
        foreach (var (text, enabled, click) in items)
        {
            var button = new Button { Content = text, IsEnabled = enabled, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 160 };
            button.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutRowButtonStyle");
            button.Click += (_, _) => { popup.IsOpen = false; click(); };
            stack.Children.Add(button);
            if (enabled) buttons.Add(button);
        }
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; anchor.Focus(); e.Handled = true; }
            else if (buttons.Count > 0 && e.Key is Key.Up or Key.Down)
            {
                var index = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
                buttons[(index + (e.Key == Key.Up ? buttons.Count - 1 : 1)) % buttons.Count].Focus();
                e.Handled = true;
            }
        };
        anchor.Unloaded += Close;
        popup.Closed += (_, _) => anchor.Unloaded -= Close;
        void Close(object sender, RoutedEventArgs args) => popup.IsOpen = false;
        popup.Opened += (_, _) => buttons.FirstOrDefault()?.Focus();
        return popup;
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
