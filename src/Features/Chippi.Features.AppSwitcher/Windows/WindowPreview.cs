// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.AppSwitcher.Windows;

/// <summary>
/// A live DWM preview of one window, drawn by Windows into the overlay (architecture §6: it draws in a WPF
/// <c>AllowsTransparency</c> window too). Nothing is captured or kept: the pixels are the window's own, and they go
/// when this is disposed or the window closes. It draws above WPF content in its rectangle and can't be clipped to
/// rounded corners, so the overlay insets it. A minimized window has none: the overlay shows its icon.
/// </summary>
public sealed partial class WindowPreview : IDisposable
{
    private nint _thumbnail;

    private WindowPreview(nint thumbnail) => _thumbnail = thumbnail;

    /// <summary>A preview of <paramref name="source"/> inside <paramref name="overlay"/>, or null when Windows has none for it.</summary>
    public static WindowPreview? Create(nint overlay, nint source)
    {
        if (overlay == 0 || source == 0 || DwmRegisterThumbnail(overlay, source, out nint thumbnail) < 0)
            return null;
        return new WindowPreview(thumbnail);
    }

    /// <summary>The window's size in physical pixels, for fitting the preview; empty when it's gone.</summary>
    public (int Width, int Height) SourceSize =>
        _thumbnail != 0 && DwmQueryThumbnailSourceSize(_thumbnail, out var size) >= 0 ? (size.X, size.Y) : (0, 0);

    /// <summary>Draws the preview into the overlay's client rectangle, in physical pixels.</summary>
    public bool Place(int left, int top, int right, int bottom, bool visible = true)
    {
        if (_thumbnail == 0)
            return false;
        var properties = new ThumbnailProperties
        {
            Flags = RectDestination | Visible | SourceClientAreaOnly | Opacity,
            Destination = new Rect { Left = left, Top = top, Right = right, Bottom = bottom },
            Opacity = 255,
            Visible = visible ? 1 : 0,
            SourceClientAreaOnly = 0,
        };
        return DwmUpdateThumbnailProperties(_thumbnail, ref properties) >= 0;
    }

    public void Dispose()
    {
        if (_thumbnail != 0)
            DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
    }

    private const uint RectDestination = 0x1, Opacity = 0x4, Visible = 0x8, SourceClientAreaOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public uint Flags;
        public Rect Destination;
        public Rect Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }

    [LibraryImport("dwmapi.dll")] private static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUnregisterThumbnail(nint thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [LibraryImport("dwmapi.dll")] private static partial int DwmQueryThumbnailSourceSize(nint thumbnail, out Size size);
}
