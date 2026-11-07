// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;

namespace Chippi.UI.Capture;

/// <summary>
/// What a Recent capture's page (<see cref="CaptureRecentView"/>) reads and asks for (screen-capture.md, Recent: "Reopening
/// offers Copy, Save as, Annotate and Open folder"). Screen capture implements it over its history; the Capture panel's
/// <see cref="ICapturePanelModel.OpenRecent"/> points it at an entry and opens the page. The view reads it on the UI thread.
/// </summary>
public interface ICaptureRecentModel
{
    /// <summary>The capture the page shows; null once it has left Recent (removed, cleared or evicted).</summary>
    CaptureRecentResult? Result { get; }

    /// <summary>Copies the retained image, composed, with the bounded clipboard retry. False reports Copy failed.</summary>
    Task<bool> CopyAsync();

    /// <summary>
    /// Asks for a path and type, then saves the retained image there. Save to stays as it was. The dialog's Cancel is
    /// <see cref="CaptureSaveOutcome.Cancelled"/> and leaves the page as it was.
    /// </summary>
    Task<CaptureSave> SaveAsAsync();

    /// <summary>
    /// Opens a new draft in the editor from the retained pixels. The host closes the island first. False reports Annotate
    /// failed: the pixels couldn't be read, and the page stays.
    /// </summary>
    bool Annotate();

    /// <summary>Opens the saved file's folder, otherwise Save to. False reports Open folder failed; nothing guessed opens.</summary>
    bool OpenFolder();

    /// <summary><see cref="Result"/> changed: the full image is ready, the file went missing, or the entry left. Any thread.</summary>
    event Action? Changed;
}

/// <summary>A finished capture from Recent.</summary>
/// <param name="Id">The history entry.</param>
/// <param name="Width">The capture's width in pixels.</param>
/// <param name="Height">The capture's height in pixels.</param>
/// <param name="When">Its age, as the panel says it: "2 min ago", "Yesterday".</param>
/// <param name="Thumbnail">The prepared thumbnail, frozen, shown until <see cref="Image"/> is ready.</param>
public sealed record CaptureRecentResult(string Id, int Width, int Height, string When, ImageSource? Thumbnail)
{
    /// <summary>The retained image, decoded off the UI thread and frozen; null until then.</summary>
    public ImageSource? Image { get; init; }

    /// <summary>The saved file's full path, or null when it was only copied.</summary>
    public string? SavedFile { get; init; }

    /// <summary>The saved file is no longer there. The retained copy still serves Copy, Save as and Annotate.</summary>
    public bool FileMissing { get; init; }
}

/// <summary>How Save as went.</summary>
public enum CaptureSaveOutcome
{
    Saved,
    Cancelled,
    Failed,
}

/// <summary>Save as's outcome, and the file it wrote.</summary>
public sealed record CaptureSave(CaptureSaveOutcome Outcome, string? File = null);
