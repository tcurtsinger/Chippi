// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Controls;

namespace Chippi.Features.Scratchpad;

public sealed class ScratchpadPage : Grid, IDisposable
{
    private readonly ScratchpadWorkspace _workspace;

    public ScratchpadPage(ScratchpadSession session, Func<IDisposable> hold, Action<Window?, ScratchpadNote>? export = null)
    {
        _workspace = new ScratchpadWorkspace(session, ScratchpadSurface.Widget, hold, export);
        Children.Add(_workspace);
        Header = new DockPanel { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_workspace.StatusArea, Dock.Right);
        Header.Children.Add(_workspace.StatusArea);
        Header.Children.Add(new TextBlock { Text = "Scratchpad", FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        _workspace.Attach(this);
    }

    public DockPanel Header { get; }
    public UIElement InitialFocus => _workspace.InitialFocus;
    public Task ExportAsync() => _workspace.ExportAsync();
    internal bool HandleEscape() => _workspace.HandleEscape();
    public void Dispose() => _workspace.Dispose();
}
