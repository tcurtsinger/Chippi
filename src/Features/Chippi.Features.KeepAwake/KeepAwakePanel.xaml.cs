// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.KeepAwake;

/// <summary>
/// The flyout panel. The module owns the state: every control shows it and asks the module to change
/// it, so a click never leaves a switch or chip out of step with what Windows is doing.
/// </summary>
public partial class KeepAwakePanel : UserControl
{
    private const string UntilLabel = "Until…";

    private readonly KeepAwakeModule _module;
    private readonly List<(ToggleButton Chip, KeepAwakeSettings? Length)> _chips = [];
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };

    public KeepAwakePanel(KeepAwakeModule module)
    {
        _module = module;
        InitializeComponent();
        foreach (var (label, length) in KeepAwakeText.Lengths)
            AddChip(label, length);
        AddChip(UntilLabel, null);
        // Minute precision, as on the page.
        for (int minute = 0; minute < 1440; minute++)
            UntilTime.Items.Add(new ComboBoxItem { Content = KeepAwakeText.Until(minute), Tag = minute });

        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _module.Changed += Refresh; _tick.Start(); Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _tick.Stop(); };
    }

    private void AddChip(string label, KeepAwakeSettings? length)
    {
        var chip = new ToggleButton { Content = label, Margin = new Thickness(0, 0, 8, 8) };
        chip.SetResourceReference(StyleProperty, "DurationChipStyle");
        chip.Click += (_, _) => OnChip(chip, length);
        _chips.Add((chip, length));
        Chips.Children.Add(chip);
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Status.Text = KeepAwakeText.Status(_module, DateTimeOffset.Now);
        Detail.Text = _module.Error ?? KeepAwakeText.Detail(_module);
        Switch.IsChecked = _module.IsOn;
        Screen.IsChecked = _module.Settings.KeepDisplayOn;

        var active = _module.Active;
        foreach (var (chip, length) in _chips)
        {
            bool selected = active is not null && (length is null
                ? active.Mode == AwakeMode.UntilTime
                : KeepAwakeText.SameLength(active, length));
            chip.IsChecked = selected;
            if (length is null)
                chip.Content = selected ? $"Until {KeepAwakeText.Until(active!.UntilMinute)}" : UntilLabel;
        }
    }

    private void OnSwitch(object sender, RoutedEventArgs e) => Run(_module.ToggleAsync());

    private void OnChip(ToggleButton chip, KeepAwakeSettings? length)
    {
        bool selected = chip.IsChecked != true; // the click already flipped it
        Refresh();
        if (selected)
            Run(_module.StopSessionAsync());
        else if (length is not null)
            Run(_module.StartSessionAsync(length));
        else
        {
            UntilTime.SelectedIndex = _module.Settings.UntilMinute;
            UntilPopup.PlacementTarget = chip;
            UntilPopup.IsOpen = true;
            UntilTime.Focus();
        }
    }

    private void OnStartUntil(object sender, RoutedEventArgs e)
    {
        UntilPopup.IsOpen = false;
        Run(_module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = Math.Max(0, UntilTime.SelectedIndex) }));
    }

    private void OnScreen(object sender, RoutedEventArgs e)
    {
        bool on = Screen.IsChecked == true;
        Refresh();
        Run(_module.ApplyAsync(_module.Settings with { KeepDisplayOn = on }));
    }

    private void OnMoreOptions(object sender, RoutedEventArgs e) => _module.OpenSettings();

    private async void Run(Task change)
    {
        try { await change; }
        catch (Exception ex)
        {
            Log.Error("Keep awake: a change from the flyout failed", ex);
            Refresh();
            Detail.Text = ex.Message;
            return;
        }
        Refresh();
    }
}
