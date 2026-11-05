// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;

namespace Chippi.Features.AppSwitcher.Views;

/// <summary>
/// Settings › App switcher (docs/phase-6/design/wave-3.html, board switcher-settings): Opening, its shortcut read-only
/// with Edit, which records it in Settings › Shortcuts; Windows, which displays it lists and the Previews switch; then
/// Apps without previews, whose windows show their icon instead.
/// </summary>
internal sealed class AppSwitcherPage : UserControl
{
    private readonly AppSwitcherModule _module;
    private readonly SettingRow _shortcut = SettingsLayout.Row("Shortcut");
    private readonly ComboBox _from = SettingsLayout.Combo(["All displays", "This display"]);
    private readonly ToggleButton _previews = new();
    private readonly ExcludedAppsCard _noPreview;
    private readonly ShortcutSetting _shortcutView;
    private bool _updating, _loaded;

    public AppSwitcherPage(AppSwitcherModule module)
    {
        _module = module;
        _shortcut.Icon = LucideIcons.Keyboard;
        _shortcutView = new ShortcutSetting(module.Descriptor.Name, module.EditShortcut);
        _shortcut.Content = _shortcutView;
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

    /// <summary>For tests: the shortcut row's keys, or "Not set".</summary>
    internal string ShortcutText => _shortcutView.KeysText;

    internal ShortcutSetting ShortcutView => _shortcutView;

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

    private void RefreshShortcut() => _shortcutView.Show(_module.Settings.Shortcut, _module.ShortcutError);
}
