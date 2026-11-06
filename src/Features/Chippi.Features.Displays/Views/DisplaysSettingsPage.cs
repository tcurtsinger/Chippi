// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Chippi.UI.Controls;

namespace Chippi.Features.Displays.Views;

/// <summary>
/// Settings › Displays (designs/settings-capture v3, displays.md Copy): Displays, each display with its level and what it
/// answers to, or "Brightness control failed" with the likely causes and Try again; Brightness keys, Act on and Step;
/// Shortcuts last, read-only with Edit, since they're set in Settings › Shortcuts.
/// </summary>
public sealed class DisplaysSettingsPage : UserControl
{
    internal const string Causes = "Turn on DDC/CI in the monitor's menu, or connect it without a dock";
    internal const string FailedText = "Brightness control failed";

    internal static readonly IReadOnlyList<(BrightnessTarget Target, string Name)> Targets =
    [
        (BrightnessTarget.UnderPointer, "Display under the pointer"),
        (BrightnessTarget.AllDisplays, "All displays"),
    ];

    internal static readonly IReadOnlyList<int> Steps = [5, 10, 20];

    private readonly IDisplaysPageModel _model;
    private readonly Card _displays = SettingsLayout.Card();
    private readonly Dictionary<string, DisplayRow> _rows = [];
    private readonly Border _none = SettingsLayout.Empty("No displays found");
    private readonly ComboBox _actOn = SettingsLayout.Combo(Targets.Select(t => (object)t.Name));
    private readonly ComboBox _step = SettingsLayout.Combo(Steps.Select(s => (object)$"{s}%"));
    private readonly Dictionary<string, ShortcutSetting> _shortcuts = [];
    private readonly TextBlock _keysError = Error();
    private bool _updating, _listening;

    public DisplaysSettingsPage(IDisplaysPageModel model)
    {
        _model = model;
        _actOn.Width = 260;
        _step.Width = 120;
        _actOn.SelectionChanged += (_, _) =>
        {
            if (!_updating && _actOn.SelectedIndex >= 0)
                Change(() => _model.SetActOn(Targets[_actOn.SelectedIndex].Target));
        };
        _step.SelectionChanged += (_, _) =>
        {
            if (!_updating && _step.SelectedIndex >= 0)
                Change(() => _model.SetStep(Steps[_step.SelectedIndex]));
        };
        var shortcuts = SettingsLayout.Card();
        foreach (string name in DisplaysShortcuts.All)
        {
            var setting = new ShortcutSetting(name, () => _model.EditShortcut(name));
            _shortcuts[name] = setting;
            shortcuts.Children.Add(SettingsLayout.Row(name, setting));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Displays", first: true), _displays,
                SettingsLayout.Section("Brightness keys"),
                SettingsLayout.Card(SettingsLayout.Row("Act on", _actOn), SettingsLayout.Row("Step", _step)), _keysError,
                SettingsLayout.Section("Shortcuts"), shortcuts,
            },
        };
        // Listen from the start, not only once loaded: the page is built before Settings shows it.
        Listen(true);
        Loaded += (_, _) => { Listen(true); Refresh(); };
        Unloaded += (_, _) => Listen(false);
        Refresh();
    }

    /// <summary>The display rows in order, for tests and UI Automation checks.</summary>
    internal IEnumerable<SettingRow> DisplayRows => _displays.Children.OfType<SettingRow>();

    internal ComboBox ActOn => _actOn;

    internal ComboBox Step => _step;

    internal ShortcutSetting ShortcutOf(string name) => _shortcuts[name];

    internal Button TryAgainOf(string id) => _rows[id].TryAgain;

    internal TextBlock KeysError => _keysError;

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
        var settings = _model.Settings;
        _updating = true;
        try
        {
            _actOn.SelectedIndex = Math.Max(0, Targets.ToList().FindIndex(t => t.Target == settings.ActOn));
            int step = Steps.ToList().IndexOf(settings.Step);
            _step.SelectedIndex = step >= 0 ? step : Steps.ToList().IndexOf(10);
        }
        finally { _updating = false; }
        foreach (string name in DisplaysShortcuts.All)
        {
            var (keys, error) = _model.Shortcut(name);
            _shortcuts[name].Show(keys, error);
        }
        RefreshDisplays(_model.Displays);
    }

    private void RefreshDisplays(IReadOnlyList<DisplayStatus> displays)
    {
        foreach (string id in _rows.Keys.Except(displays.Select(d => d.Id)).ToArray())
        {
            _displays.Children.Remove(_rows[id].Row);
            _rows.Remove(id);
        }
        _displays.Children.Remove(_none);
        for (int i = 0; i < displays.Count; i++)
        {
            var display = displays[i];
            if (!_rows.TryGetValue(display.Id, out var row))
                _rows[display.Id] = row = new DisplayRow(() => _model.TryAgain(display.Id));
            row.Show(display);
            if (_displays.Children.IndexOf(row.Row) == i) continue;
            _displays.Children.Remove(row.Row);
            _displays.Children.Insert(i, row.Row);
        }
        if (displays.Count == 0)
            _displays.Children.Add(_none);
    }

    private void Change(Func<bool> write)
    {
        bool saved = write();
        Refresh();
        _keysError.Text = saved ? "" : _model.SettingsError ?? "Save failed";
        _keysError.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved) UIElementAutomationPeer.FromElement(_keysError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static TextBlock Error()
    {
        var error = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        error.SetResourceReference(StyleProperty, "BodyTextStyle");
        error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        return error;
    }

    /// <summary>One display: its level, or Try again over "Brightness control failed".</summary>
    private sealed class DisplayRow
    {
        private readonly TextBlock _level = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _failed = new() { Text = FailedText, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        private readonly StackPanel _retry;

        public DisplayRow(Action tryAgain)
        {
            _level.SetResourceReference(FrameworkElement.StyleProperty, "BodyTextStyle");
            _level.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _failed.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTextStyle");
            _failed.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            TryAgain = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Right };
            TryAgain.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
            TryAgain.Click += (_, _) => tryAgain();
            _retry = new StackPanel { Children = { TryAgain, _failed } };
            Row = SettingsLayout.Row("");
        }

        public SettingRow Row { get; }

        public Button TryAgain { get; }

        public void Show(DisplayStatus display)
        {
            Row.Header = display.Name;
            string what = display.Failed ? Causes : display.Contrast ? "Brightness and contrast" : "Brightness";
            Row.Description = display.IsMain ? $"Main display · {what}" : what;
            if (display.Failed)
            {
                AutomationProperties.SetName(TryAgain, $"Try again on {display.Name}");
                AutomationProperties.SetHelpText(TryAgain, FailedText);
                TryAgain.IsEnabled = !display.Retrying;
                Row.Content = _retry;
            }
            else
            {
                _level.Text = display.Brightness is { } level ? $"{level}%" : "";
                AutomationProperties.SetName(_level, display.Brightness is { } l ? $"Brightness {l}%" : "Brightness");
                Row.Content = _level;
            }
        }
    }
}
