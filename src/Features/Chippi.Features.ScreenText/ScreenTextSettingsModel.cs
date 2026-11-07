// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Features.ScreenText;

/// <summary>An OCR language installed in Windows: its tag ("en-US") and its name ("English (United States)").</summary>
public sealed record OcrLanguage(string Tag, string Name);

/// <summary>The slots Settings › Text and QR lists, in its order. Open Capture is the Capture host's slot, shown read-only on every capture page.</summary>
public static class ScreenTextShortcuts
{
    public const string CopyText = "Copy text from screen";
    public const string ExtractText = "Extract text";
    public const string ScanQrCode = "Scan QR";
    public const string OpenCapture = "Open Capture";

    public static IReadOnlyList<string> All { get; } = [CopyText, ExtractText, ScanQrCode, OpenCapture];
}

/// <summary>
/// What Settings › Text and QR shows and changes (text-and-qr.md, Settings; Settings › Capture v4). The feature's module
/// implements it; the page never asks Windows for languages or writes settings itself.
/// </summary>
public interface IScreenTextSettingsModel
{
    /// <summary>The languages Windows has OCR for, by name; empty when none is installed.</summary>
    IReadOnlyList<OcrLanguage> Languages { get; }

    /// <summary>The tag reading uses now: the saved one, or the profile default when that's gone or unset. Null when none is installed.</summary>
    string? Language { get; }

    /// <summary>Keep lines as read; off joins lines into paragraphs.</summary>
    bool KeepLineBreaks { get; }

    /// <summary>Why the last settings write failed, or null for "Save failed".</summary>
    string? SettingsError { get; }

    /// <summary>Raised on any thread when the languages, the settings or a shortcut change.</summary>
    event Action? Changed;

    /// <summary>A slot's saved keys (null: not set) and why they don't work (null: they do).</summary>
    (KeyShortcut? Keys, string? Error) Shortcut(string name);

    bool SetLanguage(string tag);

    bool SetKeepLineBreaks(bool on);

    /// <summary>Opens Windows Settings › Time &amp; language › Language &amp; region, where an OCR language is added.</summary>
    void OpenLanguageSettings();

    /// <summary>Opens Settings › Shortcuts with this slot's editor open in its row (<c>ModuleContext.EditShortcut</c>).</summary>
    void EditShortcut(string name);
}
