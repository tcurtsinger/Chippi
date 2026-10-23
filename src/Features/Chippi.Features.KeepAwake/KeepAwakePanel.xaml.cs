// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.KeepAwake;

public partial class KeepAwakePanel : UserControl
{
    private readonly KeepAwakeModule _module;

    public KeepAwakePanel(KeepAwakeModule module)
    {
        _module = module;
        InitializeComponent();
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Status.Description = _module.Error ?? _module.StateLine;
        Switch.IsChecked = _module.IsOn;
    }

    private async void OnSwitch(object sender, RoutedEventArgs e)
    {
        // The module owns the state: undo the switch's own flip and let IsOn decide.
        Switch.IsChecked = _module.IsOn;
        try { await _module.ToggleAsync(); }
        catch (Exception ex) { Log.Error("Keep awake: toggling from the flyout failed", ex); }
        Refresh();
    }
}
