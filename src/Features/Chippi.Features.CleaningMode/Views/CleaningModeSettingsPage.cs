// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.CleaningMode.Views;

/// <summary>Settings › Cleaning mode (docs/phase-5.5/cleaning-mode.md, Start): one row, the droplet in the accent, "Cleaning mode" and Start.</summary>
public sealed class CleaningModeSettingsPage : UserControl
{
    public CleaningModeSettingsPage(CleaningModeModule module)
    {
        var glyph = new LucideIcon { Data = LucideIcons.Droplet, Size = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "AccentBrush");
        var start = new Button { Content = "Start", VerticalAlignment = VerticalAlignment.Center };
        start.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        AutomationProperties.SetName(start, "Start Cleaning mode");
        var name = new TextBlock { Text = "Cleaning mode" };
        name.SetResourceReference(StyleProperty, "BodyTextStyle");
        var failed = new TextBlock { Text = "Cleaning mode failed", Visibility = Visibility.Collapsed };
        failed.SetResourceReference(StyleProperty, "CaptionTextStyle");
        failed.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(failed, AutomationLiveSetting.Polite);
        start.Click += (_, _) =>
        {
            try
            {
                module.Start();
                failed.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                failed.Visibility = Visibility.Visible;
            }
        };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(start, Dock.Right);
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, failed } };
        var row = new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = new DockPanel { Children = { glyph, start, label } } };
        Content = SettingsLayout.Card(row);
    }
}

/// <summary>Cleaning mode's row in Settings › Shortcuts: the keys, or "Not set" in secondary text, with Set or Change and Remove.</summary>
public sealed class ShortcutRow : UserControl
{
    private readonly CleaningModeModule _module;
    private readonly string _title;
    private readonly StackPanel _body = new();
    private bool _capturing;

    public ShortcutRow(CleaningModeModule module, string title)
    {
        _module = module;
        _title = title;
        Content = _body;
        Focusable = true;
        PreviewKeyDown += Capture;
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            Cancel();
        };
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        if (_capturing)
            return;
        var shortcut = _module.Settings.Shortcut;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_module.ShortcutError is { } error
            ? Text(error, "WarningTextBrush")
            : Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
        row.Children.Add(Button(shortcut is null ? "Set" : "Change", Begin));
        if (shortcut is not null)
            row.Children.Add(Button("Remove", _module.RemoveShortcut));
        Show(row);
    }

    private void Show(UIElement controls)
    {
        _body.Children.Clear();
        var card = SettingsLayout.Card(SettingsLayout.Row(_title, controls));
        card.SetResourceReference(StyleProperty, "CardRowsStyle");
        _body.Children.Add(card);
    }

    private void Begin()
    {
        _capturing = true;
        _module.CaptureShortcut(true);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Text("New shortcut", "TextSecondaryBrush"), Button("Cancel", Cancel) } };
        Show(row);
        Focus();
    }

    private void Cancel()
    {
        if (!_capturing)
            return;
        _capturing = false;
        _module.CaptureShortcut(false);
        Refresh();
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Cancel();
            return;
        }
        if (MicShortcut.From(e) is not { } shortcut)
            return;
        _capturing = false;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(shortcut);
        Refresh();
    }

    private static TextBlock Text(string text, string brush)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, $"{label} {_title} shortcut");
        button.Click += (_, _) => action();
        return button;
    }
}
