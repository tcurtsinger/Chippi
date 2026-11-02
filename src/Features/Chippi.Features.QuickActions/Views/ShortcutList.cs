// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

/// <summary>
/// Quick actions' rows in Settings › Shortcuts: one per action this PC can do, each its keys, or "Not set" in secondary
/// text, with Set or Change and Remove; a clash says "Shortcut in use" in Caution, as the other shortcuts do.
/// </summary>
public sealed class ShortcutList : UserControl
{
    private readonly QuickActionsModule _module;
    private readonly Card _card = SettingsLayout.Card();
    private QuickActionId? _capturing;

    public ShortcutList(QuickActionsModule module)
    {
        _module = module;
        _card.SetResourceReference(StyleProperty, "CardRowsStyle");
        Content = _card;
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
        _card.Children.Clear();
        // Cleaning mode's own shortcut is its row, not one of these.
        foreach (var action in _module.Offered.Where(a => a.Id != QuickActionId.Cleaning))
            _card.Children.Add(SettingsLayout.Row(action.Name, Controls(action)));
    }

    private StackPanel Controls(QuickAction action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_capturing == action.Id)
        {
            row.Children.Add(Text("New shortcut", "TextSecondaryBrush"));
            row.Children.Add(Button("Cancel", $"Cancel {action.Name} shortcut", Cancel));
            return row;
        }
        var shortcut = _module.Settings.ShortcutOf(action.Id);
        if (_module.ShortcutErrors.TryGetValue(action.Id, out var error))
            row.Children.Add(Text(error, "WarningTextBrush"));
        else
            row.Children.Add(Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
        row.Children.Add(Button(shortcut is null ? "Set" : "Change", $"{(shortcut is null ? "Set" : "Change")} {action.Name} shortcut", () => Begin(action.Id)));
        if (shortcut is not null)
            row.Children.Add(Button("Remove", $"Remove {action.Name} shortcut", () => _module.RemoveShortcut(action.Id)));
        return row;
    }

    private void Begin(QuickActionId id)
    {
        _capturing = id;
        _module.CaptureShortcut(true);
        Refresh();
        Focus();
    }

    private void Cancel()
    {
        if (_capturing is null)
            return;
        _capturing = null;
        _module.CaptureShortcut(false);
        Refresh();
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (_capturing is not { } id)
            return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Cancel();
            return;
        }
        if (MicShortcut.From(e) is not { } shortcut)
            return;
        _capturing = null;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(id, shortcut);
        Refresh();
    }

    private static TextBlock Text(string text, string brush)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Button Button(string label, string name, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
}
