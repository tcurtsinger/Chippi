// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Features.WindowLayouts;

public sealed record SnapDisplay(string Id, string Name, nint Handle, bool IsMain, SnapRect Bounds, SnapRect WorkArea);

public interface IZoneOverlay : IDisposable
{
    void Show(SnapDisplay display, IReadOnlyList<SnapZone> zones);
    void Point(int index);
    void Hide();
}

/// <summary>The UI-thread drag state machine; native events and tests call the same methods.</summary>
public sealed class SnapDragController(WindowLayoutsModule module, Func<IReadOnlyList<SnapDisplay>> displays, IZoneOverlay overlay)
{
    private ActionWindow? _target;
    private SnapDisplay? _display;
    private IReadOnlyList<SnapZone> _zones = [];
    private int _point = -1;
    public bool Active => _target is not null;

    public void Begin(ActionWindow target)
    {
        Cancel();
        var settings = module.Layouts?.Settings;
        if (settings?.ShiftDragEnabled != true || target.Elevated
            || settings.ExcludedApps.Any(a => string.Equals(a, target.Executable, StringComparison.OrdinalIgnoreCase)))
            return;
        module.BeginDrag(target);
        _target = target;
    }

    public void Move(int x, int y, bool shift, bool escape, bool paused)
    {
        if (!Active) return;
        if (!shift || escape || paused) { Cancel(); return; }
        var display = displays().FirstOrDefault(d => x >= d.Bounds.X && y >= d.Bounds.Y
            && x < d.Bounds.X + d.Bounds.Width && y < d.Bounds.Y + d.Bounds.Height);
        if (display is null || module.Layouts is not { } store) { Cancel(); return; }
        if (_display?.Id != display.Id || _display.WorkArea != display.WorkArea)
        {
            _display = display;
            _zones = store.ForDisplay(display.Id).Root.Flatten().Zones;
            overlay.Show(display, _zones);
        }
        _point = -1;
        var work = display.WorkArea;
        double fx = (double)(x - work.X) / work.Width, fy = (double)(y - work.Y) / work.Height;
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            if (fx >= z.X && fy >= z.Y && fx < z.X + z.Width && fy < z.Y + z.Height) { _point = i; break; }
        }
        overlay.Point(_point);
    }

    public async Task EndAsync(bool shift, bool escape, bool paused)
    {
        var target = _target;
        var display = _display;
        int point = _point;
        var zones = _zones;
        Clear();
        try
        {
            if (target is null || display is null || point < 0 || !shift || escape || paused || module.Layouts is not { } store)
                return;
            var layout = store.ForDisplay(display.Id);
            await module.RunAsync(target, new SnapZoneKey(layout.Id, zones[point].Id), display);
        }
        finally { if (target is not null) module.EndDrag(target); }
    }

    public void Cancel()
    {
        if (_target is { } target) module.EndDrag(target);
        Clear();
    }

    private void Clear()
    {
        _target = null;
        _display = null;
        _zones = [];
        _point = -1;
        overlay.Hide();
    }
}

