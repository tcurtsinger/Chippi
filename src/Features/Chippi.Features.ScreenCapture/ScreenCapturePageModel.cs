// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.ScreenCapture;

/// <summary>What Enter does on the frozen screen (screen-capture.md, Copy and Save). Copy, Save and Ctrl+drag always do only what they say.</summary>
public enum EnterAction
{
    Copy,
    Save,
    CopyAndSave,
}

/// <summary>Screen capture's settings, kept in the module's settings.</summary>
public sealed record ScreenCaptureSettings
{
    public Dictionary<string, KeyShortcut?>? Shortcuts { get; init; }

    public EnterAction Enter { get; init; } = EnterAction.Copy;

    /// <summary>The quick-save folder Travis chose; null is the default, Pictures\Screenshots (<see cref="CaptureFiles.DefaultFolder"/>).</summary>
    public string? SaveFolder { get; init; }

    public CaptureFileType FileType { get; init; } = CaptureFileType.Png;

    /// <summary>Keep the last 50 copied or saved captures.</summary>
    public bool History { get; init; } = true;

    public bool CtrlDragCopies { get; init; } = true;

    /// <summary>Shows the magnifier while selecting; Pick a point shows it anyway (capture-layer.md).</summary>
    public bool Magnifier { get; init; } = true;
}

/// <summary>The shortcut slots Settings › Screen capture lists, in its order. Open Capture is the Capture host's slot, shown read-only on every capture page.</summary>
public static class ScreenCaptureShortcuts
{
    public const string CaptureRegion = "Capture region";
    public const string CaptureWindow = "Capture window";
    public const string CaptureFullScreen = "Capture full screen";
    public const string CaptureAllDisplays = "Capture all displays";
    public const string CaptureFreeform = "Capture freeform";
    public const string CaptureLastRegion = "Capture last region";
    public const string CaptureAfterDelay = "Capture after delay";
    public const string OpenCapture = "Open Capture";

    public static IReadOnlyList<string> All { get; } =
        [CaptureRegion, CaptureWindow, CaptureFullScreen, CaptureAllDisplays, CaptureFreeform, CaptureLastRegion, CaptureAfterDelay, OpenCapture];
}

/// <summary>
/// What Settings › Screen capture shows and changes (screen-capture.md, Settings; Settings › Capture v4). The feature's
/// module implements it; the page never reads the capture store, the disk or settings itself.
/// </summary>
public interface IScreenCapturePageModel
{
    ScreenCaptureSettings Settings { get; }

    /// <summary>The folder quick saves go to now: the chosen one, or the default.</summary>
    string SaveFolder { get; }

    /// <summary>The chosen folder is gone (a removed drive, a deleted folder): "Folder not found". The default never is; Save creates it.</summary>
    bool SaveFolderMissing { get; }

    /// <summary>Why the last settings write failed, or null for "Save failed".</summary>
    string? SettingsError { get; }

    /// <summary>Recent captures, newest first, up to 50.</summary>
    IReadOnlyList<RecentCapture> Recent { get; }

    /// <summary>Raised on any thread when the settings, the recent captures, a saved file or a shortcut change.</summary>
    event Action? Changed;

    /// <summary>A recent capture's thumbnail, frozen so any thread can hand it over; null when it can't be read.</summary>
    ImageSource? Thumbnail(Guid id);

    /// <summary>The capture was saved, and its file isn't where it was saved any more ("File missing").</summary>
    bool FileMissing(RecentCapture capture);

    /// <summary>A slot's saved keys (null: not set) and why they don't work (null: they do). Capture region's error reads "Used by Windows" while Windows' own Print Screen setting is on.</summary>
    (KeyShortcut? Keys, string? Error) Shortcut(string name);

    bool SetEnter(EnterAction action);

    /// <summary>Quick saves go to <paramref name="folder"/> from now on; null goes back to the default. Old files stay where they are.</summary>
    bool SetSaveFolder(string? folder);

    bool SetFileType(CaptureFileType type);

    bool SetHistory(bool on);

    bool SetCtrlDragCopies(bool on);

    bool SetMagnifier(bool on);

    /// <summary>Opens the capture's flattened pixels on the frozen screen to mark up again.</summary>
    void Annotate(Guid id);

    /// <summary>Removes the entry and its own cache; a saved file stays.</summary>
    void Remove(Guid id);

    /// <summary>Sends the capture's saved file to the Recycle Bin, after its confirm; false when it failed ("Delete failed"), and the file and entry stay.</summary>
    bool DeleteFile(Guid id);

    /// <summary>Clears every recent capture, after its confirm. Saved files stay.</summary>
    void ClearRecent();

    /// <summary>Opens Settings › Shortcuts with this slot's editor open in its row (<c>ModuleContext.EditShortcut</c>).</summary>
    void EditShortcut(string name);
}
