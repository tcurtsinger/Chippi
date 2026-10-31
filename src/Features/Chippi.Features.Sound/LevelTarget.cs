// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Chippi.Features.Sound;

/// <summary>
/// The device a level slider shows and sets. When another device takes its place (the headset is
/// unplugged and the speakers take over), a drag in progress ends and the slider jumps to the new
/// device's level even while it has the keyboard, so the rest of a drag, or the next arrow key, can't
/// set the new device from the old one's level: a headset's 100% never lands on the speakers.
/// Call on the UI thread, with the slider's own writes held off.
/// </summary>
internal sealed class LevelTarget
{
    /// <summary>The device the slider shows; its writes go here, never to whatever is in use by then.</summary>
    public string? Id { get; private set; }

    public void Show(Slider slider, string? id, float level)
    {
        bool moved = !string.Equals(Id, id, StringComparison.OrdinalIgnoreCase);
        Id = id;
        if (moved && slider.Template?.FindName("PART_Track", slider) is Track { Thumb: { IsDragging: true } thumb })
            thumb.CancelDrag();
        if (moved || (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin))
            slider.Value = level * 100;
    }
}
