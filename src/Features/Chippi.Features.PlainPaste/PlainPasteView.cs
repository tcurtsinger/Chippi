// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.UI.Controls;
using System.Windows.Controls;

namespace Chippi.Features.PlainPaste;

/// <summary>The shortcut's row in Clipboard settings, read-only, with Edit, which records it in Settings › Shortcuts.</summary>
public sealed class PlainPasteView : UserControl
{
    private readonly PlainPasteModule _module;

    public PlainPasteView(PlainPasteModule module)
    {
        _module = module;
        Shortcut = new ShortcutSetting(module.Descriptor.Name, module.EditShortcut);
        Content = SettingsLayout.Card(SettingsLayout.Row("Shortcut", Shortcut));
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    public ShortcutSetting Shortcut { get; }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Shortcut.Show(_module.Settings.Shortcut, _module.ShortcutError);
    }
}
