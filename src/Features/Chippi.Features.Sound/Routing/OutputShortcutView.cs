// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>
/// The output shortcut (Settings › Output, and the Shortcuts list): the keys or "Not set", Set or Change,
/// and Remove, recorded as the Mute shortcut is. Pressed, it switches to the next connected output.
/// </summary>
public sealed class OutputShortcutView : UserControl
{
    private readonly AudioRoutingModule _module;
    private readonly bool _list;
    private readonly StackPanel _body = new();
    private bool _recording;

    /// <param name="list">In the app's Shortcuts list, named for what it does.</param>
    public OutputShortcutView(AudioRoutingModule module, bool list)
    {
        _module = module;
        _list = list;
        Content = _body;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            if (_recording) { _recording = false; _module.CaptureShortcut(false); }
        };
        PreviewKeyDown += Capture;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_recording) return;
        _body.Children.Clear();
        _body.Children.Add(Card(SettingsLayout.Row(_list ? "Switch output" : "Shortcut", Controls())));
        if (_module.ShortcutError is { } error)
        {
            var text = RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush");
            text.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            _body.Children.Add(text);
        }
    }

    private Card Card(UIElement row)
    {
        var card = SettingsLayout.Card(row);
        if (_list) card.SetResourceReference(StyleProperty, "CardRowsStyle");
        return card;
    }

    /// <summary>The keys (or "Not set"), Set or Change, and Remove, as the Mute shortcut's.</summary>
    private StackPanel Controls()
    {
        var shortcut = _module.Settings.SwitchShortcut;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var keys = shortcut is null ? RoutingUi.Text("Not set", brush: "TextSecondaryBrush") : RoutingUi.Text(shortcut.ToString());
        keys.Margin = new Thickness(0, 0, 12, 0);
        panel.Children.Add(keys);
        var change = RoutingUi.Button(shortcut is null ? "Set" : "Change", BeginCapture);
        AutomationProperties.SetName(change, shortcut is null ? "Set output shortcut" : "Change output shortcut");
        panel.Children.Add(change);
        if (shortcut is not null)
        {
            var remove = RoutingUi.Button("Remove", _module.RemoveShortcut);
            remove.Margin = new Thickness(8, 0, 0, 0);
            AutomationProperties.SetName(remove, "Remove output shortcut");
            panel.Children.Add(remove);
        }
        return panel;
    }

    private void BeginCapture()
    {
        _recording = true;
        _module.CaptureShortcut(true);
        Focusable = true;
        _body.Children.Clear();
        _body.Children.Add(Card(SettingsLayout.Row("New shortcut", RoutingUi.Button("Cancel", CancelCapture))));
        Focus();
    }

    private void CancelCapture() { _module.CaptureShortcut(false); _recording = false; Refresh(); }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        if (e.Key == Key.Escape) { CancelCapture(); e.Handled = true; return; }
        if (MicShortcut.From(e) is not { } shortcut) return;
        _module.ChangeShortcut(shortcut);
        _module.CaptureShortcut(false);
        _recording = false;
        e.Handled = true;
        Refresh();
    }
}
