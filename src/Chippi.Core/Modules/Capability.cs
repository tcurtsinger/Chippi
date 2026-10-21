// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>
/// Something a module needs beyond a standard user process: elevation, a third-party driver,
/// global input hooks. Shown on the Permissions page. Equality is by <see cref="Id"/>.
/// </summary>
public sealed record Capability(string Id, string Name, string Description)
{
    /// <summary>Phase 1 only models this; the elevated helper arrives with the features that need it.</summary>
    public static Capability Admin { get; } = new(
        "admin", "Administrator helper", "An optional elevated helper for tasks Windows reserves for administrators");

    public static Capability VbCable { get; } = new(
        "vb-cable", "VB-CABLE driver", "A virtual audio cable, installed separately, used to route processed audio");

    public static Capability InputHooks { get; } = new(
        "input-hooks", "Keyboard and mouse hooks", "Watches global keyboard or mouse input to run shortcuts");

    public static Capability ScreenCapture { get; } = new(
        "screen-capture", "Screen capture", "Reads what is on screen for screenshots, recording and OCR");

    public bool Equals(Capability? other) => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}
