// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>Where a feature's outside requirement stands; Add features shows its row by it (docs/phase-3-handoff.md §2).</summary>
public enum RequirementState
{
    /// <summary>Not there: the row offers to install it.</summary>
    Missing,

    /// <summary>Its setup is running: the row is busy.</summary>
    Installing,

    /// <summary>Setup finished but Windows needs a restart before it shows up.</summary>
    RestartNeeded,

    /// <summary>There, but turned off in Windows: the row offers Windows' own settings, never setup again.</summary>
    Disabled,

    /// <summary>There and usable: the row has the feature's normal switch.</summary>
    Ready,

    /// <summary>Can't work on this PC (such as no driver for its architecture).</summary>
    Unsupported,
}

/// <summary>
/// A feature's requirement outside Chippi, such as a driver. <see cref="State"/> is current as soon
/// as it is created. Created on the UI thread; <see cref="StateChanged"/> is raised there.
/// </summary>
public interface IFeatureRequirement : IDisposable
{
    RequirementState State { get; }

    event EventHandler? StateChanged;

    /// <summary>
    /// Starts the requirement's setup from <see cref="RequirementState.Missing"/>. Completes when the
    /// setup ends; <see cref="State"/> then says what happened. A cancelled admin prompt returns to Missing.
    /// </summary>
    Task InstallAsync(CancellationToken ct);

    /// <summary>Opens where the user turns it back on in Windows, from <see cref="RequirementState.Disabled"/>.</summary>
    void OpenSettings();
}
