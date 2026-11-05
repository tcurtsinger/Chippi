// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.UI.Controls;
using Chippi.UI.Shortcuts;

namespace Chippi.Features.AppSwitcher.Views;

/// <summary>
/// Settings › App switcher (docs/phase-6/design/wave-3.html, board switcher-settings): Opening, its shortcut ("Not set"
/// and Record until one is set); Windows, which displays it lists and the Previews switch; then Apps without previews,
/// whose windows show their icon instead.
/// </summary>
internal sealed class AppSwitcherPage : UserControl
{
    private readonly AppSwitcherModule _module;
    private readonly SettingRow _shortcut = SettingsLayout.Row("Shortcut");
    private readonly ComboBox _from = SettingsLayout.Combo(["All displays", "This display"]);
    private readonly ToggleButton _previews = new();
    private readonly ExcludedAppsCard _noPreview;
    private bool _recording, _updating, _loaded;
    private string? _recordError;

    public AppSwitcherPage(AppSwitcherModule module)
    {
        _module = module;
        _shortcut.Icon = LucideIcons.Keyboard;
        _from.Width = 200;
        AutomationProperties.SetName(_from, "Windows from");
        _from.SelectionChanged += (_, _) =>
        {
            if (!_updating && _from.SelectedIndex >= 0)
                _module.Update(s => s with { CurrentDisplayOnly = _from.SelectedIndex == 1 });
        };
        _previews.SetResourceReference(StyleProperty, "SwitchStyle");
        AutomationProperties.SetName(_previews, "Previews");
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click (AGENTS.md).
        _previews.Checked += (_, _) => { if (!_updating) _module.Update(s => s with { Previews = true }); };
        _previews.Unchecked += (_, _) => { if (!_updating) _module.Update(s => s with { Previews = false }); };
        _noPreview = new ExcludedAppsCard(() => _module.Settings.NoPreview, apps => _module.Update(s => s with { NoPreview = [.. apps] }), "Apps without previews")
        {
            Margin = new Thickness(0, 8, 0, 0),
        };
        var from = SettingsLayout.Row("Windows from", _from);
        from.Icon = LucideIcons.Monitor;
        var previews = SettingsLayout.Row("Previews", _previews);
        previews.Icon = LucideIcons.Eye;
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Opening", first: true), SettingsLayout.Card(_shortcut),
                SettingsLayout.Section("Windows"), SettingsLayout.Card(from, previews), _noPreview,
            },
        };
        PreviewKeyDown += Record;
        LostKeyboardFocus += (_, e) =>
        {
            if (_recording && (e.NewFocus is not DependencyObject next || !IsAncestorOf(next)))
                StopRecording(focus: false);
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
            StopRecording(focus: false);
        };
        Refresh();
    }

    /// <summary>For tests: the shortcut row's text, and whether it's listening.</summary>
    internal string ShortcutText => (_shortcut.Content as StackPanel)?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text
        ?? (_shortcut.Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";

    internal bool Recording => _recording;

    internal void StartRecording()
    {
        Focus();
        _recording = true;
        _module.CaptureShortcut(true);
        _recordError = null;
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
            _from.SelectedIndex = settings.CurrentDisplayOnly ? 1 : 0;
            _previews.IsChecked = settings.Previews;
        }
        finally { _updating = false; }
        _noPreview.Render();
        RefreshShortcut();
    }

    private void RefreshShortcut()
    {
        var shortcut = _module.Settings.Shortcut;
        bool focused = _shortcut.IsKeyboardFocusWithin;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(row, "Shortcut");
        if (_recording)
        {
            row.Children.Add(Text(_recordError ?? "Press shortcut", _recordError is null ? "TextSecondaryBrush" : "WarningTextBrush"));
            row.Children.Add(Button("Cancel", "Cancel recording", () => StopRecording()));
        }
        else
        {
            var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
            if (_module.ShortcutError is { } error) status.Children.Add(Text(error, "WarningTextBrush"));
            row.Children.Add(status);
            row.Children.Add(Button(shortcut is null ? "Record" : "Change", shortcut is null ? "Record shortcut" : "Change shortcut", StartRecording));
            if (shortcut is not null)
                row.Children.Add(Button("Remove", "Remove shortcut", () => { _module.RemoveShortcut(); Refresh(); FocusPrimary(); }));
        }
        _shortcut.Content = row;
        if (focused && !_recording) FocusPrimary();
    }

    private void StopRecording(bool focus = true)
    {
        if (!_recording) return;
        _recording = false;
        _module.CaptureShortcut(false);
        _recordError = null;
        RefreshShortcut();
        if (focus) FocusPrimary();
    }

    private void Record(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        if (e.Key == Key.Tab)
        {
            StopRecording(focus: false);
            return;
        }
        if (e.OriginalSource is Button && e.Key is Key.Enter or Key.Space) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (ShortcutKeys.From(e) is not { } shortcut) return; // a modifier alone: keep listening
        if (!shortcut.IsValid)
        {
            _recordError = "Shortcut unavailable";
            RefreshShortcut();
            return;
        }
        _recording = false;
        _module.CaptureShortcut(false);
        _recordError = null;
        _module.ChangeShortcut(shortcut);
        Refresh();
        FocusPrimary();
    }

    private void FocusPrimary()
    {
        if (_loaded) (_shortcut.Content as StackPanel)?.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }

    private static TextBlock Text(string text, string brush)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (brush == "WarningTextBrush") AutomationProperties.SetLiveSetting(block, AutomationLiveSetting.Polite);
        return block;
    }

    private static Button Button(string label, string name, Action click)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }
}
