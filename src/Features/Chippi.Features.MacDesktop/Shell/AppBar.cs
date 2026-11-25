// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>An edge of a display.</summary>
public enum ScreenEdge { Top, Bottom, Left, Right }

/// <summary>The strips an AppBar reserves. Pure arithmetic in physical pixels, so it's tested without a window.</summary>
public static class AppBarStrips
{
    /// <summary>The strip <paramref name="thickness"/> deep along <paramref name="edge"/> of <paramref name="screen"/>.</summary>
    public static Rect Strip(ScreenEdge edge, Rect screen, double thickness) => edge switch
    {
        ScreenEdge.Top => new Rect(screen.Left, screen.Top, screen.Width, thickness),
        ScreenEdge.Bottom => new Rect(screen.Left, screen.Bottom - thickness, screen.Width, thickness),
        ScreenEdge.Left => new Rect(screen.Left, screen.Top, thickness, screen.Height),
        _ => new Rect(screen.Right - thickness, screen.Top, thickness, screen.Height),
    };

    /// <summary>
    /// <paramref name="rect"/> given back its depth from the edge: another bar on the same edge (the taskbar before it hides)
    /// pushes it inward.
    /// </summary>
    public static Rect KeepThickness(ScreenEdge edge, Rect rect, double thickness) => edge switch
    {
        ScreenEdge.Top => new Rect(rect.Left, rect.Top, rect.Width, thickness),
        ScreenEdge.Bottom => new Rect(rect.Left, rect.Bottom - thickness, rect.Width, thickness),
        ScreenEdge.Left => new Rect(rect.Left, rect.Top, thickness, rect.Height),
        _ => new Rect(rect.Right - thickness, rect.Top, thickness, rect.Height),
    };

    /// <summary>
    /// Windows granted the strip: the right edge and depth. Along the edge it may be shorter (a side dock ends at the menu
    /// bar), which is fine; demanding the full length would retry, and notify, forever.
    /// </summary>
    public static bool Granted(ScreenEdge edge, Rect got, Rect wanted) => edge is ScreenEdge.Top or ScreenEdge.Bottom
        ? got.Top == wanted.Top && got.Bottom == wanted.Bottom
        : got.Left == wanted.Left && got.Right == wanted.Right;
}

/// <summary>
/// A window registered as an AppBar: it reserves a strip of a display, so maximized windows stop beside it, and hears when
/// an app goes fullscreen (<see cref="Native.AbnFullscreenApp"/> through its callback message). Remove it with Dispose.
/// </summary>
internal sealed class AppBar : IDisposable
{
    private readonly nint _window;
    private Rect? _reserved;
    private bool _registered;

    private AppBar(nint window) => _window = window;

    /// <summary>The message Windows sends the window with the notice in wParam.</summary>
    public static uint CallbackMessage { get; } = Native.RegisterWindowMessage("Chippi.MacDesktop.AppBar");

    public static AppBar? Register(nint window)
    {
        var data = Data(window);
        data.CallbackMessage = CallbackMessage;
        if (Native.SHAppBarMessage(Native.AbmNew, ref data) == 0)
            return null;
        return new AppBar(window) { _registered = true };
    }

    /// <summary>
    /// Reserves the strip, in physical pixels. Calls Windows only when the strip changes: each reservation sends a settings
    /// change, and repeating it would loop. Returns the strip Windows granted.
    /// </summary>
    public Rect Reserve(ScreenEdge edge, Rect screen, double thickness)
    {
        var wanted = AppBarStrips.Strip(edge, screen, thickness);
        if (_reserved == wanted || !_registered)
            return wanted;
        var data = Data(_window);
        data.Edge = edge switch { ScreenEdge.Top => Native.AbeTop, ScreenEdge.Bottom => Native.AbeBottom, ScreenEdge.Left => Native.AbeLeft, _ => Native.AbeRight };
        data.Bounds = ToNative(wanted);
        Native.SHAppBarMessage(Native.AbmQueryPos, ref data);
        data.Bounds = ToNative(AppBarStrips.KeepThickness(edge, data.Bounds.ToWpf(), thickness));
        Native.SHAppBarMessage(Native.AbmSetPos, ref data);
        var got = data.Bounds.ToWpf();
        _reserved = AppBarStrips.Granted(edge, got, wanted) ? wanted : null;
        return got;
    }

    /// <summary>Forgets the reservation, so the next <see cref="Reserve"/> asks again (Windows moved the bars about).</summary>
    public void Invalidate() => _reserved = null;

    public void Dispose()
    {
        if (!_registered)
            return;
        _registered = false;
        var data = Data(_window);
        Native.SHAppBarMessage(Native.AbmRemove, ref data);
    }

    private static Native.AppBarData Data(nint window) => new() { Size = Marshal.SizeOf<Native.AppBarData>(), Window = window };

    private static Native.Rect ToNative(Rect rect) => new((int)Math.Round(rect.Left), (int)Math.Round(rect.Top), (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom));
}
