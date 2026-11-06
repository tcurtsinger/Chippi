// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenText.Views;

/// <summary>
/// Settings › Text and QR (Settings › Capture v4, text-and-qr.md Settings): Text (Language, from the OCR languages
/// installed in Windows, and Keep line breaks), then Shortcuts, read-only with Edit, since they're set in Settings ›
/// Shortcuts. Open Capture is the Capture host's slot, shown here too. With no OCR language, the row says so and opens
/// Windows Settings.
/// </summary>
public sealed class ScreenTextSettingsPage : UserControl
{
    internal const string NoLanguage = "No OCR language installed";

    private readonly IScreenTextSettingsModel _model;
    private readonly ComboBox _language = SettingsLayout.Combo([]);
    private readonly Button _openSettings = new() { Content = "Add language", VerticalAlignment = VerticalAlignment.Center };
    private readonly SettingRow _languageRow;
    private readonly ToggleButton _keepLines = new();
    private readonly Dictionary<string, ShortcutSetting> _shortcuts = [];
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<OcrLanguage> _shown = [];
    private bool _updating, _listening;

    public ScreenTextSettingsPage(IScreenTextSettingsModel model)
    {
        _model = model;
        _language.Width = 260;
        AutomationProperties.SetName(_language, "Language");
        _language.SelectionChanged += (_, _) =>
        {
            if (!_updating && _language.SelectedIndex >= 0 && _language.SelectedIndex < _shown.Count)
                Change(() => _model.SetLanguage(_shown[_language.SelectedIndex].Tag));
        };
        _openSettings.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        _openSettings.Click += (_, _) => _model.OpenLanguageSettings();
        _keepLines.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(_keepLines, "Keep line breaks");
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click.
        _keepLines.Checked += (_, _) => { if (!_updating) Change(() => _model.SetKeepLineBreaks(true)); };
        _keepLines.Unchecked += (_, _) => { if (!_updating) Change(() => _model.SetKeepLineBreaks(false)); };
        _error.SetResourceReference(StyleProperty, "BodyTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);

        _languageRow = SettingsLayout.Row("Language", _language);
        var shortcuts = SettingsLayout.Card();
        foreach (string name in ScreenTextShortcuts.All)
        {
            var setting = new ShortcutSetting(name, () => _model.EditShortcut(name));
            _shortcuts[name] = setting;
            shortcuts.Children.Add(SettingsLayout.Row(name, setting));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Text", first: true),
                SettingsLayout.Card(_languageRow, SettingsLayout.Row("Keep line breaks", _keepLines)), _error,
                SettingsLayout.Section("Shortcuts"), shortcuts,
            },
        };
        // Listen from the start, not only once loaded: the page is built before Settings shows it.
        Listen(true);
        Loaded += (_, _) => { Listen(true); Refresh(); };
        Unloaded += (_, _) => Listen(false);
        Refresh();
    }

    internal ComboBox LanguagePicker => _language;

    internal SettingRow LanguageRow => _languageRow;

    internal Button OpenSettings => _openSettings;

    internal ToggleButton KeepLines => _keepLines;

    internal TextBlock Error => _error;

    internal ShortcutSetting ShortcutOf(string name) => _shortcuts[name];

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) _model.Changed += OnChanged;
        else _model.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(() => { if (_listening) Refresh(); });
    }

    private void Refresh()
    {
        var languages = _model.Languages;
        _updating = true;
        try
        {
            if (!_shown.SequenceEqual(languages))
            {
                _shown = [.. languages];
                _language.ItemsSource = _shown.Select(l => l.Name).ToArray();
            }
            _language.SelectedIndex = _shown.ToList().FindIndex(l => l.Tag == _model.Language);
            _keepLines.IsChecked = _model.KeepLineBreaks;
            _keepLines.Content = _model.KeepLineBreaks ? "On" : "Off";
        }
        finally { _updating = false; }

        bool none = languages.Count == 0;
        _languageRow.Content = none ? _openSettings : _language;
        _languageRow.Description = none ? NoLanguage : "";

        foreach (string name in ScreenTextShortcuts.All)
        {
            var (keys, error) = _model.Shortcut(name);
            _shortcuts[name].Show(keys, error);
        }
    }

    private void Change(Func<bool> write)
    {
        bool saved = write();
        Refresh();
        _error.Text = saved ? "" : _model.SettingsError ?? "Save failed";
        _error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved) UIElementAutomationPeer.FromElement(_error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
