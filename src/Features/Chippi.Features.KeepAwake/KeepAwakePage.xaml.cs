// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.KeepAwake;

public partial class KeepAwakePage : UserControl
{
    private readonly KeepAwakeModule _module;
    private bool _ready;

    public KeepAwakePage(KeepAwakeModule module)
    {
        _module = module;
        InitializeComponent();
        var saved = module.Settings;
        var lengths = KeepAwakeText.Lengths.ToList();
        // A saved length the list doesn't offer (an older setting) stays selectable.
        if (!lengths.Any(l => KeepAwakeText.SameLength(l.Length, saved)))
            lengths.Add((saved.Mode == AwakeMode.UntilTime ? $"Until {KeepAwakeText.Until(saved.UntilMinute)}" : KeepAwakeText.Left(TimeSpan.FromMinutes(saved.DurationMinutes)), saved));
        foreach (var (label, length) in lengths)
            Length.Items.Add(new ComboBoxItem { Content = label, Tag = length });
        Length.SelectedItem = Length.Items.Cast<ComboBoxItem>().First(i => KeepAwakeText.SameLength((KeepAwakeSettings)i.Tag, saved));
        _ready = true;
        KeepAwakeIslandView.OnFlip(Switch, OnSwitch);
        KeepAwakeIslandView.OnFlip(Screen, OnScreen);
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Status.Description = _module.Error ?? KeepAwakeText.Status(_module, DateTimeOffset.Now);
        Switch.IsChecked = _module.IsOn;
        Screen.IsChecked = _module.Settings.KeepDisplayOn;
    }

    private void OnSwitch()
    {
        if (Switch.IsChecked != _module.IsOn)
            Run(_module.ToggleAsync());
    }

    private void OnLength(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || Length.SelectedItem is not ComboBoxItem { Tag: KeepAwakeSettings length })
            return;
        Run(_module.ApplyAsync(_module.Settings with
        {
            Mode = length.Mode,
            DurationMinutes = length.DurationMinutes,
            UntilMinute = length.UntilMinute,
        }));
    }

    private void OnScreen()
    {
        bool on = Screen.IsChecked == true;
        if (on != _module.Settings.KeepDisplayOn)
            Run(_module.ApplyAsync(_module.Settings with { KeepDisplayOn = on }));
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch (Exception ex)
        {
            Log.Error("Keep awake: a change in Settings failed", ex);
            Refresh();
            Status.Description = ex.Message;
            return;
        }
        Refresh();
    }
}
