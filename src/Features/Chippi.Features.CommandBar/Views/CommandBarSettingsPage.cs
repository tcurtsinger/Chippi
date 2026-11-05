// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using Chippi.UI.Shortcuts;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.CommandBar.Views;

/// <summary>
/// Settings › Command bar (docs/phase-5.5/command-bar.md): Search engine (Google, Bing or DuckDuckGo); Results, a switch per
/// kind, all on; Hidden, each hidden result with what it is under it and Show, or "None".
/// </summary>
public sealed class CommandBarSettingsPage : UserControl
{
    /// <summary>Results' rows, in the order of <see cref="ResultSwitch"/>.</summary>
    internal static readonly IReadOnlyList<(ResultSwitch Kind, string Name)> Kinds =
    [
        (ResultSwitch.Apps, "Apps"),
        (ResultSwitch.Windows, "Open windows"),
        (ResultSwitch.Files, "Files and folders"),
        (ResultSwitch.Settings, "Windows Settings"),
        (ResultSwitch.Chippi, "Chippi"),
        (ResultSwitch.Clipboard, "Clipboard history"),
        (ResultSwitch.Math, "Math and conversions"),
        (ResultSwitch.Emoji, "Emoji"),
        (ResultSwitch.Web, "Web search"),
    ];

    private static readonly string[] Engines = ["Google", "Bing", "DuckDuckGo"];

    private readonly CommandBarModule _module;
    private readonly ComboBox _web = SettingsLayout.Combo(Engines);
    private readonly Dictionary<ResultSwitch, ToggleButton> _switches = [];
    private readonly Card _hidden = SettingsLayout.Card();
    private readonly Dictionary<string, (SettingRow Row, Button Show, StackPanel Actions)> _hiddenRows = [];
    private readonly TextBlock _webError = Error();
    private readonly TextBlock _resultsError = Error();
    private readonly TextBlock _hiddenError = Error();
    private readonly StackPanel _restoreStatus = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private HiddenResult? _restored;
    private bool _updating, _loaded;

    public CommandBarSettingsPage(CommandBarModule module)
    {
        _module = module;
        AutomationProperties.SetName(_web, "Search engine");
        _web.SelectionChanged += (_, _) =>
        {
            if (!_updating && _web.SelectedIndex >= 0)
                Change(() => _module.SetWebSearch((WebEngine)_web.SelectedIndex), _webError);
        };
        var results = SettingsLayout.Card();
        foreach (var (kind, name) in Kinds)
        {
            var toggle = new ToggleButton();
            toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
            AutomationProperties.SetName(toggle, name);
            // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click (AGENTS.md).
            toggle.Checked += (_, _) => { if (!_updating) Change(() => _module.SetOn(kind, true), _resultsError); };
            toggle.Unchecked += (_, _) => { if (!_updating) Change(() => _module.SetOn(kind, false), _resultsError); };
            _switches[kind] = toggle;
            results.Children.Add(SettingsLayout.Row(name, toggle));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Card(SettingsLayout.Row("Search engine", _web)), _webError,
                SettingsLayout.Section("Results"), results, _resultsError,
                SettingsLayout.Section("Hidden"), _hidden, _hiddenError, _restoreStatus,
            },
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
        var settings = _module.Settings;
        _updating = true;
        try
        {
            _web.SelectedIndex = (int)settings.WebSearch;
            foreach (var (kind, toggle) in _switches)
                toggle.IsChecked = !settings.Off.Contains(kind);
        }
        finally { _updating = false; }
        foreach (string key in _hiddenRows.Keys.Except(settings.Hidden.Select(h => h.Key)).ToArray())
        {
            _hidden.Children.Remove(_hiddenRows[key].Row);
            _hiddenRows.Remove(key);
        }
        foreach (var empty in _hidden.Children.OfType<Border>().ToArray()) _hidden.Children.Remove(empty);
        for (int i = 0; i < settings.Hidden.Count; i++)
        {
            var result = settings.Hidden[i];
            if (!_hiddenRows.TryGetValue(result.Key, out var item))
            {
                var show = Button("Show", $"Show {result.Name}", () => Restore(result));
                var enable = Button("Enable", $"Enable {SourceName(result.Switch)} results", () => Enable(result.Switch));
                enable.Margin = new Thickness(0, 0, 8, 0);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { enable, show } };
                AutomationProperties.SetName(actions, result.Name);
                item = (SettingsLayout.Row(result.Name, actions, result.Kind), show, actions);
                _hiddenRows.Add(result.Key, item);
            }
            bool off = settings.Off.Contains(result.Switch);
            item.Row.Description = off ? $"{result.Kind} · {SourceName(result.Switch)} off" : result.Kind;
            item.Actions.Children[0].Visibility = off ? Visibility.Visible : Visibility.Collapsed;
            if (_hidden.Children.IndexOf(item.Row) == i) continue;
            _hidden.Children.Remove(item.Row);
            _hidden.Children.Insert(i, item.Row);
        }
        if (_hiddenRows.Count == 0)
            _hidden.Children.Add(SettingsLayout.Empty("None"));
        RefreshRestoreStatus();
    }

    private void Restore(HiddenResult result)
    {
        int index = _module.Settings.Hidden.FindIndex(h => h.Key == result.Key);
        if (!Change(() => _module.Unhide(result.Key), _hiddenError)) return;
        _restored = result;
        RefreshRestoreStatus();
        if (!_loaded) return;
        if (_module.Settings.Hidden.Count > 0)
        {
            var next = _module.Settings.Hidden[Math.Clamp(index, 0, _module.Settings.Hidden.Count - 1)];
            _hiddenRows[next.Key].Show.Focus();
        }
        else if (_restoreStatus.Visibility == Visibility.Visible)
            _restoreStatus.Children.OfType<Button>().FirstOrDefault()?.Focus();
        else
            _switches[result.Switch].Focus();
    }

    private void Enable(ResultSwitch kind)
    {
        if (Change(() => _module.SetOn(kind, true), _hiddenError) && _loaded)
            _switches[kind].Focus();
    }

    private void RefreshRestoreStatus()
    {
        bool off = _restored is { } result && _module.Settings.Off.Contains(result.Switch);
        _restoreStatus.Visibility = off ? Visibility.Visible : Visibility.Collapsed;
        if (!off) return;
        // Keep the recovery available after Show removed the original row. Enabling remains an explicit choice.
        if (Equals(_restoreStatus.Tag, _restored)) return;
        _restoreStatus.Tag = _restored;
        _restoreStatus.Children.Clear();
        var source = _restored!.Switch;
        var label = new TextBlock { Text = $"Shown · {SourceName(source)} off", VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(StyleProperty, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        var enable = Button("Enable", $"Enable {SourceName(source)} results", () => Enable(source));
        enable.Margin = new Thickness(8, 0, 0, 0);
        _restoreStatus.Children.Add(label);
        _restoreStatus.Children.Add(enable);
    }

    private bool Change(Func<bool> write, TextBlock error)
    {
        bool saved = write();
        Refresh(); // The control also recovers when changed before its Loaded subscription.
        error.Text = saved ? "" : _module.SettingsError ?? "Save failed";
        error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved) UIElementAutomationPeer.FromElement(error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        return saved;
    }

    private static string SourceName(ResultSwitch kind) => Kinds.First(k => k.Kind == kind).Name;

    private static Button Button(string label, string name, Action click)
    {
        var button = new Button { Content = label };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Error()
    {
        var error = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        error.SetResourceReference(StyleProperty, "BodyTextStyle");
        error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        return error;
    }
}

/// <summary>The shortcut's row on Settings › Shortcuts: Ctrl+Alt+Space by default, with Change and Remove, or Set once removed.</summary>
public sealed class ShortcutRow : UserControl
{
    private readonly CommandBarModule _module;
    private readonly string _title;
    private readonly SettingRow _row;
    private string? _shown;
    private string? _captureError;
    private bool _capturing, _loaded, _refreshing;

    public ShortcutRow(CommandBarModule module, string title)
    {
        _module = module;
        _title = title;
        _row = SettingsLayout.Row(title);
        var card = SettingsLayout.Card(_row);
        card.SetResourceReference(StyleProperty, "CardRowsStyle");
        Content = card;
        Focusable = true;
        PreviewKeyDown += Capture;
        LostKeyboardFocus += (_, e) =>
        {
            if (!_refreshing && _capturing && (e.NewFocus is not DependencyObject next || (next != this && !IsAncestorOf(next))))
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
        var shortcut = _module.Settings.Shortcut;
        string shown = $"{shortcut}|{_module.ShortcutError}|{_capturing}|{_captureError}";
        if (shown == _shown) return;
        _shown = shown;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(row, _title);
        if (_capturing)
        {
            row.Children.Add(Text(_captureError ?? "Press shortcut", _captureError is null ? "TextSecondaryBrush" : "WarningTextBrush"));
            row.Children.Add(Button("Cancel", () => Cancel()));
        }
        else
        {
            var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
            if (_module.ShortcutError is { } error) status.Children.Add(Text(error, "WarningTextBrush"));
            row.Children.Add(status);
            row.Children.Add(Button(shortcut is null ? "Set" : "Change", Begin));
            if (shortcut is not null)
                row.Children.Add(Button("Remove", () => { _module.RemoveShortcut(); Refresh(); FocusPrimary(); }));
        }
        _refreshing = true;
        try
        {
            bool focused = _row.IsKeyboardFocusWithin;
            _row.Content = row;
            if (focused && !_capturing) FocusPrimary();
        }
        finally { _refreshing = false; }
    }

    private void Begin()
    {
        Focus(); // Preserve capture while replacing the Change/Set control that held focus.
        _capturing = true;
        _captureError = null;
        _module.CaptureShortcut(true);
        Refresh();
    }

    private void Cancel(bool restoreFocus = true)
    {
        if (!_capturing)
            return;
        _capturing = false;
        _captureError = null;
        _module.CaptureShortcut(false);
        Refresh();
        if (restoreFocus) FocusPrimary();
    }

    private Button? Primary => (_row.Content as StackPanel)?.Children.OfType<Button>().FirstOrDefault();

    private void FocusPrimary()
    {
        if (_loaded) Primary?.Focus();
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;
        if (e.Key == Key.Tab)
        {
            Cancel();
            Primary?.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
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
        if (ShortcutKeys.From(e) is not { } shortcut)
            return;
        if (!shortcut.IsValid)
        {
            _captureError = "Shortcut unavailable";
            Refresh();
            return;
        }
        _capturing = false;
        _captureError = null;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(shortcut);
        Refresh();
        FocusPrimary();
    }

    private static TextBlock Text(string text, string brush)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (brush == "WarningTextBrush") AutomationProperties.SetLiveSetting(block, AutomationLiveSetting.Polite);
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
