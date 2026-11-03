// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Controls;
using Chippi.UI.Controls;

namespace Chippi.Features.Scratchpad;

public sealed class ScratchpadSettingsPage : UserControl
{
    private readonly ScratchpadWorkspace _workspace;
    public ScratchpadSettingsPage(ScratchpadSession session, Action<Window?, ScratchpadNote>? export = null)
    {
        _workspace = new ScratchpadWorkspace(session, ScratchpadSurface.Settings, null, export);
        Content = _workspace;
        SettingsLayout.SetTitleAside(this, _workspace.StatusArea);
        _workspace.Attach(this);
    }

    public Task ExportAsync() => _workspace.ExportAsync();
}
