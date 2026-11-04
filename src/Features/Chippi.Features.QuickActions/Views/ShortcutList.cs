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
/// Quick actions' rows in Settings › Shortcuts: one per available action. Unchanged rows retain their controls and
/// focus across a PC refresh. Capture ends when focus leaves, the page closes, or its action becomes unavailable.
/// </summary>
public sealed class ShortcutList : UserControl
{
    private readonly QuickActionsModule _module;
    private readonly Card _card = SettingsLayout.Card();
    private readonly Dictionary<QuickActionId, (SettingRow View, string Key)> _rows = [];
    private QuickActionId? _capturing;
    private string? _captureError;
    private bool _loaded, _refreshing;

    public ShortcutList(QuickActionsModule module)
    {
        _module = module;
        _card.SetResourceReference(StyleProperty, "CardRowsStyle");
        Content = _card;
        Focusable = true;
        PreviewKeyDown += Capture;
        LostKeyboardFocus += (_, e) =>
        {
            if (!_refreshing && _capturing is not null && (e.NewFocus is not DependencyObject next || (next != this && !IsAncestorOf(next))))
                Cancel(restoreFocus: false);
        };
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            _module.Changed += OnChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _module.Changed -= OnChanged;
            Cancel(restoreFocus: false);
        };
        Refresh();
    }

    private void OnChanged()
    {
        if (!_loaded) return;
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(() => { if (_loaded) Refresh(); });
    }

    private void Refresh()
    {
        // Cleaning mode owns its shortcut outside this list.
        var offered = _module.Offered.Where(a => a.Id != QuickActionId.Cleaning).ToArray();
        if (_capturing is { } capturing && !offered.Any(a => a.Id == capturing))
        {
            _capturing = null;
            _captureError = null;
            _module.CaptureShortcut(false);
        }
        _refreshing = true;
        try
        {
            foreach (var id in _rows.Keys.Except(offered.Select(a => a.Id)).ToArray())
            {
                _card.Children.Remove(_rows[id].View);
                _rows.Remove(id);
            }
            for (int index = 0; index < offered.Length; index++)
            {
                var action = offered[index];
                string key = $"{_module.Settings.ShortcutOf(action.Id)}|{_module.ShortcutErrors.GetValueOrDefault(action.Id)}|{(_capturing == action.Id ? "capture:" + _captureError : "")}";
                if (!_rows.TryGetValue(action.Id, out var row))
                {
                    row = (SettingsLayout.Row(action.Name, Controls(action)), key);
                    _rows.Add(action.Id, row);
                }
                else if (row.Key != key)
                {
                    bool focused = row.View.IsKeyboardFocusWithin;
                    row.View.Content = Controls(action);
                    row.Key = key;
                    _rows[action.Id] = row;
                    if (focused && _capturing != action.Id) FocusPrimary(action.Id);
                }
                if (_card.Children.IndexOf(row.View) == index) continue;
                _card.Children.Remove(row.View);
                _card.Children.Insert(index, row.View);
            }
            if (offered.Length == 0 && _card.Children.Count == 0)
                _card.Children.Add(SettingsLayout.Empty("No actions available"));
            else if (offered.Length > 0)
                foreach (var empty in _card.Children.OfType<Border>().ToArray()) _card.Children.Remove(empty);
        }
        finally { _refreshing = false; }
    }

    private StackPanel Controls(QuickAction action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // SettingRow need not auto-name (and retain) each replaced controls panel.
        AutomationProperties.SetName(row, action.Name);
        if (_capturing == action.Id)
        {
            row.Children.Add(Text(_captureError ?? "Press shortcut", _captureError is null ? "TextSecondaryBrush" : "WarningTextBrush"));
            row.Children.Add(Button("Cancel", $"Cancel {action.Name} shortcut", () => Cancel()));
            return row;
        }
        var shortcut = _module.Settings.ShortcutOf(action.Id);
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
        if (_module.ShortcutErrors.TryGetValue(action.Id, out var error))
            status.Children.Add(Text(error, "WarningTextBrush"));
        row.Children.Add(status);
        row.Children.Add(Button(shortcut is null ? "Set" : "Change", $"{(shortcut is null ? "Set" : "Change")} {action.Name} shortcut", () => Begin(action.Id)));
        if (shortcut is not null)
            row.Children.Add(Button("Remove", $"Remove {action.Name} shortcut", () =>
            {
                _module.RemoveShortcut(action.Id);
                FocusPrimary(action.Id);
            }));
        return row;
    }

    private void Begin(QuickActionId id)
    {
        // Move focus before replacing the Set/Change button, so its removal cannot cancel capture.
        Focus();
        _capturing = id;
        _captureError = null;
        _module.CaptureShortcut(true);
        Refresh();
    }

    private void Cancel(bool restoreFocus = true)
    {
        if (_capturing is not { } id) return;
        _capturing = null;
        _captureError = null;
        _module.CaptureShortcut(false);
        Refresh();
        if (restoreFocus) FocusPrimary(id);
    }

    private void FocusPrimary(QuickActionId id)
    {
        if (_loaded && _rows.TryGetValue(id, out var row) && row.View.Content is StackPanel controls)
            controls.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (_capturing is not { } id) return;
        // Tab moves past the former Set/Change control; it never becomes an invalid shortcut.
        if (e.Key == Key.Tab)
        {
            Cancel();
            if (_rows.TryGetValue(id, out var row) && row.View.Content is StackPanel controls)
                controls.Children.OfType<Button>().FirstOrDefault()?.MoveFocus(new TraversalRequest(
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            e.Handled = true;
            return;
        }
        if (e.OriginalSource is Button && e.Key is Key.Enter or Key.Space) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Cancel();
            return;
        }
        if (MicShortcut.From(e) is not { } shortcut) return;
        if (!shortcut.IsValid)
        {
            _captureError = "Shortcut unavailable";
            Refresh();
            return;
        }
        _capturing = null;
        _captureError = null;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(id, shortcut);
        Refresh();
        FocusPrimary(id);
    }

    private static TextBlock Text(string text, string brush)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (brush == "WarningTextBrush") AutomationProperties.SetLiveSetting(block, AutomationLiveSetting.Polite);
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
