// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Features.ColorPicker;

/// <summary>The slots Settings › Color picker lists, in its order. Open Capture is the Capture host's slot, shown read-only on every capture page.</summary>
public static class ColorPickerShortcuts
{
    public const string PickColor = "Pick color";
    public const string PickAndReview = "Pick and review color";
    public const string OpenCapture = "Open Capture";

    public static IReadOnlyList<string> All { get; } = [PickColor, PickAndReview, OpenCapture];
}

/// <summary>
/// What Settings › Color picker shows and changes (color-picker.md, Settings; Settings › Capture v4). The feature's module
/// implements it; the page never writes settings itself.
/// </summary>
public interface IColorPickerSettingsModel
{
    /// <summary>The recent palette as HEX ("#7C5CFF"), newest first, up to 10.</summary>
    IReadOnlyList<string> Recent { get; }

    /// <summary>Every format Pick color can copy, in Copy as's order (HEX, RGB, HSL, HSV, CMYK), each written for the newest recent color, or #7C5CFF with none.</summary>
    IReadOnlyList<ColorFormat> Formats { get; }

    /// <summary>The format Pick color copies: a <see cref="ColorFormat.Name"/>, "HEX" at first.</summary>
    string CopyAs { get; }

    /// <summary>Why the last settings write failed, or null for "Save failed".</summary>
    string? SettingsError { get; }

    /// <summary>Raised on any thread when the palette, the format or a shortcut change.</summary>
    event Action? Changed;

    /// <summary>A slot's saved keys (null: not set) and why they don't work (null: they do).</summary>
    (KeyShortcut? Keys, string? Error) Shortcut(string name);

    bool SetCopyAs(string format);

    /// <summary>Clears the recent palette, after its confirm.</summary>
    void ClearRecent();

    /// <summary>Opens Settings › Shortcuts with this slot's editor open in its row (<c>ModuleContext.EditShortcut</c>).</summary>
    void EditShortcut(string name);
}
