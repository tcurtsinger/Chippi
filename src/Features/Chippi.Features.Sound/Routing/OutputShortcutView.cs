// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Controls;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>
/// The output shortcut on Settings › Output, read-only: the keys or "Not set", with Edit, which records it in
/// Settings › Shortcuts. Pressed, it switches to the next connected output.
/// </summary>
public sealed class OutputShortcutView : UserControl
{
    private readonly AudioRoutingModule _module;

    public OutputShortcutView(AudioRoutingModule module)
    {
        _module = module;
        Shortcut = new ShortcutSetting("Switch output", module.EditShortcut);
        Content = SettingsLayout.Card(SettingsLayout.Row("Shortcut", Shortcut));
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    public ShortcutSetting Shortcut { get; }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Shortcut.Show(_module.Settings.SwitchShortcut, _module.ShortcutError);
    }
}
