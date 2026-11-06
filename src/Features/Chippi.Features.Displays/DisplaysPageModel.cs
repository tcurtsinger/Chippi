// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Features.Displays;

/// <summary>Which displays Brightness up and down move (displays.md, Brightness up and down).</summary>
public enum BrightnessTarget
{
    UnderPointer,
    AllDisplays,
}

/// <summary>The brightness keys' settings, kept in the module's settings.</summary>
public sealed record DisplaysSettings
{
    public BrightnessTarget ActOn { get; init; } = BrightnessTarget.UnderPointer;

    /// <summary>Percent a press moves: 5, 10 or 20.</summary>
    public int Step { get; init; } = 10;
}

/// <summary>A display as Settings › Displays lists it.</summary>
/// <param name="Id">DisplayCatalog's id, passed back to Try again.</param>
/// <param name="Name">DisplayCatalog's name, " 1" and " 2" added for two of the same model.</param>
/// <param name="Brightness">The last level read, 0 to 100; null before the first read lands.</param>
/// <param name="Contrast">The display answered VCP 0x12.</param>
/// <param name="Failed">The display doesn't answer: "Brightness control failed", the likely causes and Try again.</param>
/// <param name="Retrying">Try again is reading it.</param>
public sealed record DisplayStatus(string Id, string Name, bool IsMain, int? Brightness, bool Contrast, bool Failed = false, bool Retrying = false)
{
    /// <summary>The last contrast level read, 0 to 100; null when the display has no contrast or it hasn't been read.</summary>
    public int? ContrastLevel { get; init; }

    /// <summary>The last write didn't go through; <see cref="Brightness"/> is the last level that did.</summary>
    public bool WriteFailed { get; init; }
}

/// <summary>The feature's three global slots, in the page's order.</summary>
public static class DisplaysShortcuts
{
    public const string BrightnessUp = "Brightness up";
    public const string BrightnessDown = "Brightness down";
    public const string OpenDisplays = "Open Displays";

    public static IReadOnlyList<string> All { get; } = [BrightnessUp, BrightnessDown, OpenDisplays];
}

/// <summary>
/// What Settings › Displays shows and changes. The feature's module implements it; the page never reads monitors or
/// writes settings itself.
/// </summary>
public interface IDisplaysPageModel
{
    DisplaysSettings Settings { get; }

    /// <summary>Every connected display in display order, those that don't answer included.</summary>
    IReadOnlyList<DisplayStatus> Displays { get; }

    /// <summary>Why the last settings write failed, or null for "Save failed".</summary>
    string? SettingsError { get; }

    /// <summary>Raised on any thread when the settings, the displays or a shortcut change.</summary>
    event Action? Changed;

    /// <summary>A slot's saved keys (null: not set) and why they don't work (null: they do).</summary>
    (KeyShortcut? Keys, string? Error) Shortcut(string name);

    bool SetActOn(BrightnessTarget target);

    bool SetStep(int percent);

    /// <summary>Reopens the display's handle and reads it again (displays.md, Try again).</summary>
    void TryAgain(string displayId);

    /// <summary>Opens Settings › Shortcuts with this slot's editor open in its row (<c>ModuleContext.EditShortcut</c>).</summary>
    void EditShortcut(string name);
}
