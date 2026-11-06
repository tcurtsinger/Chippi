// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.UI.Displays;

namespace Chippi.Features.Displays;

/// <summary>The only two VCP codes Chippi ever reads or writes (docs/phase-7/displays.md, Writing).</summary>
public enum Vcp : byte
{
    Brightness = 0x10,
    Contrast = 0x12,
}

/// <summary>One monitor's open handle. Every call blocks (a DDC/CI read takes about 60 ms) and runs on the monitor's own worker.</summary>
public interface IMonitor : IDisposable
{
    /// <summary>Reads the raw value and the maximum the monitor reports; false when it doesn't answer.</summary>
    bool TryRead(Vcp code, out int current, out int max);

    /// <summary>Writes a raw value; false when the monitor refuses or doesn't answer.</summary>
    bool TryWrite(Vcp code, int raw);
}

/// <summary>Opens monitors: DDC/CI for an external one, WMI for a laptop's panel. Tests pass a fake.</summary>
public interface IMonitorBus
{
    /// <summary>The display's monitor, or null when it can't be opened.</summary>
    IMonitor? Open(DisplayInfo display);
}
