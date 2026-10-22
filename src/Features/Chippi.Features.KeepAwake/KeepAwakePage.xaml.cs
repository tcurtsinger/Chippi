// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;

namespace Chippi.Features.KeepAwake;

public partial class KeepAwakePage : UserControl
{
    private readonly KeepAwakeModule _module;
    private bool _ready;

    public KeepAwakePage(KeepAwakeModule module)
    {
        _module = module;
        InitializeComponent();
        var settings = module.Settings;
        foreach (int minutes in new[] { 15, 30, 60, 90, 120, 240, settings.DurationMinutes }.Distinct().Order())
            Duration.Items.Add(new ComboBoxItem { Content = $"{minutes} min", Tag = minutes });
        // Minute precision without an unthemed text field or an extra dependency.
        for (int minute = 0; minute < 1440; minute++)
            Until.Items.Add(new ComboBoxItem { Content = DateTime.Today.AddMinutes(minute).ToString("t"), Tag = minute });
        Duration.SelectedItem = Duration.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == settings.DurationMinutes);
        Until.SelectedIndex = settings.UntilMinute;
        Display.IsChecked = settings.KeepDisplayOn;
        Mode.SelectedIndex = (int)settings.Mode;
        _ready = true;
        OnModeChanged(this, null!);
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
    }

    private KeepAwakeSettings ReadSettings() => new()
    {
        Mode = (AwakeMode)Mode.SelectedIndex,
        DurationMinutes = (int)((ComboBoxItem)Duration.SelectedItem).Tag,
        UntilMinute = Until.SelectedIndex,
        KeepDisplayOn = Display.IsChecked == true,
    };

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        DurationPanel.Visibility = Mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        UntilPanel.Visibility = Mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        State.Text = _module.Tile.StateLine;
        Toggle.Content = _module.Tile.IsOn ? "Stop keep awake" : "Start keep awake";
        Error.Text = _module.Error ?? "";
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        Toggle.IsEnabled = false;
        try
        {
            if (!_module.Tile.IsOn) await _module.ApplyAsync(ReadSettings());
            await _module.ToggleAsync();
            Refresh();
        }
        catch (Exception ex) { Error.Text = ex.Message; }
        finally { Toggle.IsEnabled = true; }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try { await _module.ApplyAsync(ReadSettings()); Error.Text = "Settings saved"; }
        catch (Exception ex) { Error.Text = ex.Message; }
    }
}
