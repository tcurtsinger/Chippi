// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using Chippi.UI.Shortcuts;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.RadialMenu.Views;

/// <summary>The shortcut's row on Settings › Shortcuts: "Not set" with Set, or the shortcut with Change and Remove.</summary>
public sealed class ShortcutRow : UserControl
{
    private readonly RadialMenuModule _module;
    private readonly string _title;
    private readonly StackPanel _body = new();
    private bool _capturing;

    public ShortcutRow(RadialMenuModule module, string title)
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
            ? Ui.Text(error, "WarningTextBrush")
            : Ui.Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
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
        Show(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text("New shortcut", "TextSecondaryBrush"), Button("Cancel", Cancel) } });
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
        if (ShortcutKeys.From(e) is not { } shortcut)
            return;
        _capturing = false;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(shortcut);
        Refresh();
    }

    private Button Button(string label, Action action)
    {
        var button = Ui.Button(label, action);
        button.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetName(button, $"{label} {_title} shortcut");
        return button;
    }
}
