// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Config;

public enum ThemePreference
{
    Dark,
    Light,

    /// <summary>"Follow Windows": tracks the system app theme.</summary>
    System,
}

/// <summary>Gumdrop's body color (DESIGN.md §7). Synth is the gradient and the default.</summary>
public enum PetColor
{
    Synth,
    Iris,
    Azure,
    Signal,
    Ember,
    Frost,
}

/// <summary>Shell settings, the "app" section of settings.json.</summary>
public sealed class AppSettings
{
    public const string SectionName = "app";

    public ThemePreference Theme { get; set; } = ThemePreference.Dark;

    public bool IslandEnabled { get; set; } = true;

    public PetColor PetColor { get; set; } = PetColor.Synth;

    /// <summary>Null follows the Windows "Animation effects" setting; true or false overrides it.</summary>
    public bool? ReduceMotion { get; set; }
}
