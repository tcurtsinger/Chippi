// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Core.Actions;
using Chippi.Features.CommandBar.Search;

namespace Chippi.Features.RadialMenu.Pc;

/// <summary>A point on the desktop in physical pixels, as the mouse hook and Windows report it.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A monitor: its whole rectangle in physical pixels and its scale (1.5 at 144 DPI).</summary>
public readonly record struct Screen(int Left, int Top, int Width, int Height, double Scale);

/// <summary>
/// The PC's side of the Radial menu: what a slice's action acts on (<see cref="IActionPc"/>), and the Start menu's apps,
/// their icons, the pointer and the screens the wheel needs. Nothing here runs in tests or dev runs
/// (docs/phase-5.5-handoff.md §3): tests pass a fake.
/// </summary>
public interface IRadialPc : IActionPc, IDisposable
{
    /// <summary>The Start menu's apps as last read; empty until the first read is done.</summary>
    IReadOnlyList<StartApp> Apps { get; }

    /// <summary>Reads the Start menu again off the UI thread, and raises <see cref="AppsChanged"/> on any thread when done.</summary>
    void RefreshApps();

    event Action? AppsChanged;

    /// <summary>An app's icon, or a file's, a folder's or a program's (a full path), <paramref name="pixels"/> square; null when there's none.</summary>
    Task<ImageSource?> IconAsync(string appOrPath, int pixels);

    /// <summary>Where the pointer is, and whether it shows (a game hides it).</summary>
    (PixelPoint At, bool Shown) Pointer();

    /// <summary>The monitor that holds the point.</summary>
    Screen ScreenAt(PixelPoint at);

    /// <summary>The top-level window under the point, or 0 for the desktop, the taskbar or one of Chippi's.</summary>
    nint WindowAt(PixelPoint at);

    bool? IActionPc.HasApp(string parsingName) =>
        Apps.Count == 0 ? null : Apps.Any(a => string.Equals(a.ParsingName, parsingName, StringComparison.OrdinalIgnoreCase));

    nint IActionPc.WindowUnderPointer() => WindowAt(Pointer().At);
}
