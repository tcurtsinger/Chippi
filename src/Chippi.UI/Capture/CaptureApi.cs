// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.UI.Capture;

/// <summary>Where a starter came from; it decides how the overlay earns the keyboard (capture-layer.md, Foreground).</summary>
public enum CaptureDoor
{
    /// <summary>A <c>RegisterHotKey</c> shortcut, Print Screen included: the overlay may take the foreground directly.</summary>
    Shortcut,

    /// <summary>A press the input hub swallowed (a Caps Lock combo, a mouse button, a Radial slice): the 0x07 tap first.</summary>
    Swallowed,

    /// <summary>A Capture panel card or Search: the island closes once the overlay holds the keyboard.</summary>
    Island,
}

/// <summary>What a capture feature asks the layer for.</summary>
public sealed record CaptureRequest(CaptureMode Mode)
{
    public CaptureDoor Door { get; init; } = CaptureDoor.Shortcut;

    /// <summary>Seconds to count before the freeze: 0, or one of <see cref="Countdown.Delays"/>.</summary>
    public int Delay { get; init; }

    /// <summary>The frozen screen dims outside the selection; Color picker's pick shows it as it was.</summary>
    public bool Dim { get; init; } = true;

    /// <summary>The magnifier follows the pointer while choosing (Settings › Screen capture); always on for a pick.</summary>
    public bool Magnifier { get; init; } = true;

    /// <summary>Record mode: the selection stays on the display where the drag started (one display per recording).</summary>
    public bool OneDisplay { get; init; }
}

/// <summary>Why a capture's overlay closed.</summary>
public enum CaptureEnd
{
    /// <summary>The feature finished with the selection (Copy, Save, a pick).</summary>
    Finished,

    /// <summary>Esc, a right click while picking, the countdown cancelled, or the feature discarded it.</summary>
    Cancelled,

    /// <summary>The screen couldn't be read ("Capture failed").</summary>
    Failed,

    /// <summary>A lock, Ctrl+Alt+Del, a UAC prompt or a display change: nothing is kept.</summary>
    Interrupted,
}

/// <summary>A display's part of the overlay, for a feature's own drawing over the selection.</summary>
/// <param name="Layer">
/// Above the frozen image and the dimming, below the selection's frame and the bars, laid out in the display's physical
/// pixels from its top left (image pixels: no DPI to undo). Takes the mouse only while <see cref="ICaptureSession.Tooling"/>.
/// </param>
public sealed record CaptureSurface(string DisplayId, PixelRect Bounds, int Dpi, Canvas Layer);

/// <summary>An editor crop, in physical desktop bounds and original image-pixel dimensions.</summary>
public sealed record CapturePreview(PixelRect Bounds, PixelSize Size);

/// <summary>
/// One capture, from the starter until its overlay closes. Its events are raised on the UI thread. The pixels are released
/// when it closes; a feature that keeps an image copies it out of <see cref="Selection"/> first.
/// </summary>
public interface ICaptureSession
{
    CaptureRequest Request { get; }

    /// <summary>The frozen screen; null during a delay and before the read lands.</summary>
    Freeze? Freeze { get; }

    /// <summary>The selection once released, and after each change (handles, arrows, Ctrl+A); null while choosing.</summary>
    Selection? Selection { get; }

    /// <summary>The overlay's parts, one per display, once frozen.</summary>
    IReadOnlyList<CaptureSurface> Surfaces { get; }

    /// <summary>A markup tool is on: a press inside the selection goes to <see cref="CaptureSurface.Layer"/>, not a move.</summary>
    bool Tooling { get; set; }

    /// <summary>The selection was released, or changed after release.</summary>
    event Action<Selection>? Selected;

    /// <summary>A pixel was picked (Pick a point): its point and colour (0xAARRGGBB). The overlay stays until <see cref="Close"/>.</summary>
    event Action<PixelPoint, uint>? Picked;

    /// <summary>A key the overlay doesn't act on, after release: the feature's (Esc, Enter, Ctrl+C, tool letters).</summary>
    event Action<KeyEventArgs>? KeyDown;

    /// <summary>The frozen screen first showed: a feature times key to frozen screen from here.</summary>
    event Action? Frozen;

    /// <summary>The overlay closed; raised once.</summary>
    event Action<CaptureEnd>? Closed;

    /// <summary>
    /// The feature's bars, placed by the layer beside the selection on its release display (capture-layer.md, The bars'
    /// placement): the tool column and the output bar under it. Null leaves one out. Sized in DIPs as WPF measures them.
    /// </summary>
    void SetBars(FrameworkElement? tools, FrameworkElement? output);

    /// <summary>Shows an editor crop while retaining its source pixels and undo history. Null restores the selection.</summary>
    void SetPreview(CapturePreview? preview) { }

    /// <summary>
    /// Closes the overlay. With <paramref name="handBack"/>, the window that was in front at the starter gets the keyboard
    /// back first, so Ctrl+V lands where Travis was.
    /// </summary>
    void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true);
}

/// <summary>
/// The shared capture layer (docs/phase-7/capture-layer.md): one freeze, one selector, the same keys for every capture
/// feature. Call on the UI thread.
/// </summary>
public interface ICaptureLayer
{
    /// <summary>
    /// A capture feature started: the overlay windows are made with the first use and kept hidden; disposing the last use
    /// closes them.
    /// </summary>
    IDisposable Use();

    /// <summary>The capture in progress, from its starter (a delay included) until its overlay closes.</summary>
    ICaptureSession? Current { get; }

    /// <summary>
    /// Starts a capture. Null when one is already running: a starter during the delay cancels it, and any other is
    /// ignored (after release, the feature asks itself, as Screen capture's "Discard capture?").
    /// </summary>
    ICaptureSession? Start(CaptureRequest request);

    /// <summary>
    /// Reopens a finished capture for Annotate (capture-layer.md, Reopening a capture): the screen freezes as for a
    /// capture, and the image shows centred on the display under the pointer, already selected and fixed in place, at its
    /// own size or shrunk to fit. <see cref="ICaptureSession.Selected"/> fires at once; the bars, keys, Tooling and Close
    /// work as for a capture. <see cref="Selection.Image"/> returns <paramref name="image"/> unchanged,
    /// <see cref="Selection.Scale"/> maps it to <see cref="Selection.Rect"/>. Null when a capture is already running.
    /// </summary>
    /// <param name="image">The flattened pixels, BGRA with straight alpha, as <see cref="Selection.Image"/> returns them.</param>
    ICaptureSession? Reopen(CapturedImage image, CaptureDoor door);

    /// <summary>The previous released rectangle, for Last region; the feature saves it in its settings and sets it at start.</summary>
    PixelRect? LastRegion { get; set; }
}

/// <summary>What the layer needs from the shell. The App implements it; tests pass a fake.</summary>
public interface ICaptureShell
{
    /// <summary>The last window in front that wasn't the island's (the return window for a starter from the island).</summary>
    nint IslandReturnWindow();

    /// <summary>Closes the island if it's open; completes once its close has finished.</summary>
    Task CloseIslandAsync();

    /// <summary>Shows the layer's line on the bar ("Capturing in 3", "Capture failed"), or clears it for null.</summary>
    void Bar(CaptureBarLine? line);
}

/// <summary>
/// The layer's line on the bar, as Capture results v3 draws it (DESIGN.md §5): the title over the mode ("Capturing in 3"
/// over "Region").
/// </summary>
/// <param name="Source">The mode, as <see cref="ModeName"/> names it.</param>
public sealed record CaptureBarLine(string Title, string Source)
{
    /// <summary>The countdown's ring: 1 at the start of the delay, down towards 0.</summary>
    public double? Progress { get; init; }

    /// <summary>"Capture failed": Caution, with the triangle; it goes after 6 seconds.</summary>
    public bool Failed { get; init; }

    /// <summary>What a click on the line does (the countdown's cancel).</summary>
    public Action? Clicked { get; init; }

    public static string ModeName(CaptureMode mode) => mode switch
    {
        CaptureMode.Region => "Region",
        CaptureMode.Window => "Window",
        CaptureMode.Screen => "Screen",
        CaptureMode.AllDisplays => "All displays",
        CaptureMode.Freeform => "Freeform",
        CaptureMode.LastRegion => "Last region",
        CaptureMode.PickPoint => "Pick a point",
        _ => "",
    };
}
