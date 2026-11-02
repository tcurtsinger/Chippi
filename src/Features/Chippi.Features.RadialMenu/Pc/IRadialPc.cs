// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.RadialMenu.Slices;

namespace Chippi.Features.RadialMenu.Pc;

/// <summary>A point on the desktop in physical pixels, as the mouse hook and Windows report it.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A monitor: its whole rectangle in physical pixels and its scale (1.5 at 144 DPI).</summary>
public readonly record struct Screen(int Left, int Top, int Width, int Height, double Scale);

/// <summary>Why a slice didn't run, as the bar says it: "Snap left failed" over <see cref="Exception.Message"/> ("No window").</summary>
public sealed class SliceFailure(string reason) : Exception(reason);

/// <summary>
/// The PC's side of the Radial menu: what's under the pointer, and the windows, keys, media and programs a slice acts on.
/// Nothing here runs in tests or dev runs (docs/phase-5.5-handoff.md §3): tests pass a fake. Each action throws
/// <see cref="SliceFailure"/> when Windows refuses.
/// </summary>
public interface IRadialPc : IDisposable
{
    /// <summary>The Start menu's apps as last read; empty until the first read is done.</summary>
    IReadOnlyList<StartApp> Apps { get; }

    /// <summary>Reads the Start menu again off the UI thread, and raises <see cref="AppsChanged"/> on any thread when done.</summary>
    void RefreshApps();

    event Action? AppsChanged;

    /// <summary>An app's icon, or a file's, a folder's or a program's (a full path), <paramref name="pixels"/> square; null when there's none.</summary>
    Task<ImageSource?> IconAsync(string appOrPath, int pixels);

    /// <summary>Whether the file or folder is there.</summary>
    bool Exists(string path);

    /// <summary>Where the pointer is, and whether it shows (a game hides it).</summary>
    (PixelPoint At, bool Shown) Pointer();

    /// <summary>The monitor that holds the point.</summary>
    Screen ScreenAt(PixelPoint at);

    /// <summary>The top-level window under the point, or 0 for the desktop, the taskbar or one of Chippi's.</summary>
    nint WindowAt(PixelPoint at);

    /// <summary>Opens a file, folder, app, Settings page or web address as Explorer would.</summary>
    void Open(string target);

    void Window(nint window, WindowAction action);

    void Media(MediaAction action);

    /// <summary>Presses the keys in the app in front, once the keys still held are let go.</summary>
    Task PressAsync(KeyCombo keys);

    /// <summary>Types the text at the cursor of the app in front, as keystrokes, without the clipboard.</summary>
    Task TypeAsync(string text);

    /// <summary>Runs the line hidden, without waiting for it.</summary>
    void Run(CommandShell shell, string line);

    /// <summary>
    /// The text selected in a terminal in front, read through UI Automation, since Ctrl+C there would stop what runs when
    /// nothing is selected. Null for any other window (the clipboard hub copies it), or when nothing is selected.
    /// </summary>
    Task<(bool Terminal, string? Text)> TerminalSelectionAsync();
}
