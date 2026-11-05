// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using Chippi.Core.Actions;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Windows;
using Chippi.UI.Actions;

namespace Chippi.Features.RadialMenu.Pc;

/// <summary>
/// The real PC. Apps and their icons come from the Command bar's Start menu reader; a slice's action runs on the shell's
/// <paramref name="actions"/>.
/// </summary>
internal sealed class WindowsRadialPc(IActionPc actions) : IRadialPc
{
    private readonly WindowsPc _pc = new();

    public IReadOnlyList<StartApp> Apps => _pc.Apps;

    public event Action? AppsChanged
    {
        add => _pc.AppsChanged += value;
        remove => _pc.AppsChanged -= value;
    }

    public void RefreshApps() => _pc.RefreshApps();

    public Task<ImageSource?> IconAsync(string appOrPath, int pixels) => Path.IsPathFullyQualified(appOrPath)
        ? _pc.PathIconAsync(appOrPath, pixels)
        : _pc.AppIconAsync(new StartApp("", appOrPath), pixels);

    public (PixelPoint At, bool Shown) Pointer()
    {
        var info = new Native.CursorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.CursorInfo>() };
        bool read = Native.GetCursorInfo(ref info);
        if (!read)
            Native.GetCursorPos(out info.At);
        // CURSOR_SHOWING, and not CURSOR_SUPPRESSED (touch and pen hide it that way).
        return (new PixelPoint(info.At.X, info.At.Y), !read || (info.Flags & 0x1) != 0);
    }

    public Screen ScreenAt(PixelPoint at)
    {
        var (bounds, _, scale) = Native.Monitor(Native.MonitorFromPoint(new Native.Point { X = at.X, Y = at.Y }, 2)); // MONITOR_DEFAULTTONEAREST
        return new Screen(bounds.Left, bounds.Top, bounds.Width, bounds.Height, scale);
    }

    public nint WindowAt(PixelPoint at) => WindowsActionPc.WindowAt(at.X, at.Y);

    public bool Exists(string path) => actions.Exists(path);

    public nint WindowInFront() => actions.WindowInFront();

    public void Open(string target) => actions.Open(target);

    public void Window(nint window, WindowAction action) => actions.Window(window, action);

    public void Media(MediaAction action) => actions.Media(action);

    public Task PressAsync(KeyCombo keys) => actions.PressAsync(keys);

    public Task TypeAsync(string text) => actions.TypeAsync(text);

    public void Run(CommandShell shell, string line) => actions.Run(shell, line);

    public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => actions.TerminalSelectionAsync();

    public void Dispose() => _pc.Dispose();
}
