// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>Which area to watch: a display (its HMONITOR and bounds) and the area on it. All physical virtual-screen pixels.</summary>
public sealed record AreaFramesRequest(nint Monitor, PixelRect Display, PixelRect Area)
{
    /// <summary>The area in the display's own pixels (its top left is 0, 0).</summary>
    public PixelRect AreaOnDisplay => new(Area.X - Display.X, Area.Y - Display.Y, Area.Width, Area.Height);

    /// <summary>Whether the area is a non-empty rectangle inside the display.</summary>
    public bool IsValid => !Area.IsEmpty && Display.Contains(Area);
}

/// <summary>One live frame of the area: tight BGRA rows (width × 4 bytes each). The array belongs to the receiver.</summary>
public sealed record AreaFrame(int Width, int Height, byte[] Bgra, TimeSpan Time);

/// <summary>
/// Live frames of an area of a display (screen scrolling capture, docs/phase-7/scrolling-capture.md). Frames arrive only
/// when the screen changes, newest only (a slow receiver skips frames, never queues them), on a worker thread.
/// </summary>
public interface IAreaFrames : IDisposable
{
    /// <summary>A new frame of the area. Raised on a worker thread, never reentrantly.</summary>
    event Action<AreaFrame>? Frame;

    /// <summary>The source is lost or unusable (display resolution changed, display removed, capture refused), raised once, after which no more frames come.</summary>
    event Action<string>? Failed;

    /// <summary>Starts capturing. Completes when capture runs (the first frame follows), or throws when Windows refuses.</summary>
    Task StartAsync(AreaFramesRequest request, CancellationToken cancellationToken = default);
}
