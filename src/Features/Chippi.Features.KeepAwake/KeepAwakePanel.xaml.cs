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

        OnFlip(Switch, OnSwitch);
        OnFlip(Screen, OnScreen);
        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _module.Changed += Refresh; _tick.Start(); Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _tick.Stop(); };
    }

    private void AddChip(string label, KeepAwakeSettings? length)
    {
        var chip = new ToggleButton { Content = label, Margin = new Thickness(0, 0, 8, 8) };
        chip.SetResourceReference(StyleProperty, "DurationChipStyle");
        OnFlip(chip, () => OnChip(chip, length));
        _chips.Add((chip, length));
        Chips.Children.Add(chip);
    }

    /// <summary>
    /// Runs <paramref name="changed"/> when the button's state flips by mouse, keyboard or UI
    /// Automation (whose Toggle raises no Click). Handlers compare with the module's state, so
    /// Refresh writing that state back does nothing.
    /// </summary>
    internal static void OnFlip(ToggleButton button, Action changed)
    {
        button.Checked += (_, _) => changed();
        button.Unchecked += (_, _) => changed();
    }

    private bool IsSelected(KeepAwakeSettings? length) => _module.Active is { } active && (length is null
        ? active.Mode == AwakeMode.UntilTime
        : KeepAwakeText.SameLength(active, length));

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Status.Text = KeepAwakeText.Status(_module, DateTimeOffset.Now);
        Detail.Text = _module.Error ?? KeepAwakeText.Detail(_module);
        Detail.Visibility = Detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Switch.IsChecked = _module.IsOn;
        Screen.IsChecked = _module.Settings.KeepDisplayOn;
        foreach (var (chip, length) in _chips)
        {
            bool selected = IsSelected(length);
            chip.IsChecked = selected;
            if (length is null)
                chip.Content = selected ? $"Until {KeepAwakeText.Until(_module.Active!.UntilMinute)}" : UntilLabel;
        }
    }

    private void OnSwitch()
    {
        if (Switch.IsChecked != _module.IsOn)
            Run(_module.ToggleAsync());
    }

    private void OnChip(ToggleButton chip, KeepAwakeSettings? length)
    {
        bool selected = IsSelected(length);
        if (chip.IsChecked == selected)
            return;
        Refresh(); // the module's state shows until the change lands
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

    private void OnScreen()
    {
        bool on = Screen.IsChecked == true;
        if (on != _module.Settings.KeepDisplayOn)
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
            Detail.Visibility = Visibility.Visible;
            return;
        }
        Refresh();
    }
}
