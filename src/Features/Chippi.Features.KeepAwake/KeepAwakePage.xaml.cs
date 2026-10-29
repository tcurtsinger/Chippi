// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.UI.Controls;

namespace Chippi.Features.KeepAwake;

public partial class KeepAwakePage : UserControl
{
    private readonly KeepAwakeModule _module;
    private readonly SettingRow _status;
    private readonly Button _stop;
    private readonly ToggleButton _screen = new();
    private readonly List<(ToggleButton Button, KeepAwakeSettings? Length)> _choices = [];
    private readonly ComboBox _until = new() { Width = 180 };
    private readonly Popup _picker = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };

    public KeepAwakePage(KeepAwakeModule module)
    {
        _module = module;
        InitializeComponent();
        _stop = Button("Stop", () => Run(_module.StopSessionAsync()));
        _status = SettingsLayout.Row("Keep awake", _stop, "Off");
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, length) in KeepAwakeText.Lengths) choices.Children.Add(Choice(label, length));
        choices.Children.Add(Choice("Until\u2026", null));
        _screen.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(_screen, "Keep screen on");
        KeepAwakeIslandView.OnFlip(_screen, () =>
        {
            if ((_screen.IsChecked == true) != _module.Settings.KeepDisplayOn)
                Run(_module.ApplyAsync(_module.Settings with { KeepDisplayOn = _screen.IsChecked == true }));
        });
        Body.Children.Add(SettingsLayout.Card(_status, SettingsLayout.Row("Length", choices), SettingsLayout.Row("Keep screen on", _screen)));
        _until.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        AutomationProperties.SetName(_until, "End time");
        for (int minute = 0; minute < 1440; minute++) _until.Items.Add(KeepAwakeText.Until(minute));
        var start = Button("Start", () =>
        {
            _picker.IsOpen = false;
            Run(_module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = Math.Max(0, _until.SelectedIndex) }));
        });
        start.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        var pop = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _until, start } } };
        pop.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        _picker.Child = pop;
        Body.Children.Add(_picker);
        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _module.Changed += Refresh; _tick.Start(); Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _tick.Stop(); _picker.IsOpen = false; };
        Refresh();
    }

    private ToggleButton Choice(string name, KeepAwakeSettings? length)
    {
        var button = new ToggleButton { Content = name, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 0, 8, 0), Height = 32 };
        button.SetResourceReference(StyleProperty, "DurationChipStyle");
        AutomationProperties.SetName(button, name);
        KeepAwakeIslandView.OnFlip(button, () =>
        {
            bool active = _module.Active is { } current && (length is null ? current.Mode == AwakeMode.UntilTime : KeepAwakeText.SameLength(current, length));
            if (button.IsChecked == active) return;
            Refresh();
            if (active) Run(_module.StopSessionAsync());
            else if (length is not null) Run(_module.StartSessionAsync(length));
            else { _until.SelectedIndex = _module.Settings.UntilMinute; _picker.PlacementTarget = button; _picker.IsOpen = true; }
        });
        _choices.Add((button, length));
        return button;
    }

    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _status.Description = _module.Error is null ? KeepAwakeText.Status(_module, DateTimeOffset.Now) : "Keep awake failed";
        _stop.Visibility = _module.IsOn ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, length) in _choices)
            button.IsChecked = _module.Active is { } active && (length is null ? active.Mode == AwakeMode.UntilTime : KeepAwakeText.SameLength(active, length));
        _screen.IsChecked = _module.Settings.KeepDisplayOn;
        _screen.Content = _screen.IsChecked == true ? "On" : "Off";
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch (Exception ex) { Log.Error("Keep awake: Settings change failed", ex); _status.Description = "Keep awake failed"; return; }
        Refresh();
    }
}
