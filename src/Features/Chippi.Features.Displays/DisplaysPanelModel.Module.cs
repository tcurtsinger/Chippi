// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.UI.Displays;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.Features.Displays;

// The island's Displays panel reads and changes the monitors through the module, never itself.
public sealed partial class DisplaysModule : IDisplaysPanelModel
{
    public IReadOnlyList<DisplayStatus> PanelDisplays => [.. Displays.Take(DisplayBrightness.PanelMax)];

    public unsafe Rect? Bounds(string displayId)
    {
        var info = _displays().FirstOrDefault(d => d.Id == displayId);
        if (info is null) return null;
        var monitor = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(new HMONITOR(info.Handle), ref monitor)) return null;
        var r = monitor.rcMonitor;
        return new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }

    public string? UnderPointer() => _engine?.UnderPointerId(PanelDisplays);

    public void Read() => _engine?.Refresh();

    public void Set(string displayId, DisplayControl control, int percent) => _engine?.Set(displayId, Code(control), percent);

    public void BeginAll(DisplayControl control) => _engine?.BeginAll(Code(control));

    public void MoveAll(DisplayControl control, int value) => _engine?.MoveAll(value, Code(control));

    public void EndAll() => _engine?.EndAll();

    private static Vcp Code(DisplayControl control) => control == DisplayControl.Contrast ? Vcp.Contrast : Vcp.Brightness;
}
