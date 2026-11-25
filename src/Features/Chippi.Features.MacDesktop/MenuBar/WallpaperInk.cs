// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>The menu bar's ink: white with a soft dark shadow over a dark wallpaper, near-black with a light halo over a light one.</summary>
public enum BarInk { Light, Dark }

/// <summary>How Windows fits the wallpaper to a display (IDesktopWallpaper's DESKTOP_WALLPAPER_POSITION).</summary>
public enum WallpaperFit { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>
/// Reads each display's wallpaper and picks the menu bar's ink from the strip under the bar (docs/mac-style/brief.md, Menu
/// bar): the average luminance, white below 0.5. The arithmetic is pure and tested; reading the wallpaper is Windows'.
/// </summary>
public static class WallpaperInk
{
    /// <summary>White ink below this average luminance (0 black, 1 white).</summary>
    public const double Threshold = 0.5;

    public static BarInk For(double luminance) => luminance < Threshold ? BarInk.Light : BarInk.Dark;

    /// <summary>The perceived luminance of an sRGB colour, 0 to 1 (Rec. 709 weights on the stored values).</summary>
    public static double Luminance(byte r, byte g, byte b) => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;

    /// <summary>The average luminance of 32-bit BGRA pixels; 0.5 for none.</summary>
    public static double Average(ReadOnlySpan<byte> bgra)
    {
        int count = bgra.Length / 4;
        if (count == 0)
            return Threshold;
        double sum = 0;
        for (int i = 0; i < count * 4; i += 4)
            sum += Luminance(bgra[i + 2], bgra[i + 1], bgra[i]);
        return sum / count;
    }

    /// <summary>
    /// The part of the picture (in its own pixels) under a bar <paramref name="barHeight"/> deep across the top of a
    /// <paramref name="displayWidth"/> × <paramref name="displayHeight"/> display. Fill crops the picture to cover the display,
    /// centered; the others start at the picture's top.
    /// </summary>
    public static Int32Rect UnderBar(int imageWidth, int imageHeight, double displayWidth, double displayHeight, double barHeight, WallpaperFit fit)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || displayWidth <= 0 || displayHeight <= 0)
            return Int32Rect.Empty;
        double fraction = Math.Clamp(barHeight / displayHeight, 0.001, 1);
        if (fit is WallpaperFit.Fill or WallpaperFit.Span)
        {
            double cover = Math.Max(displayWidth / imageWidth, displayHeight / imageHeight);
            double shownWidth = displayWidth / cover, shownHeight = displayHeight / cover;
            int left = (int)Math.Round((imageWidth - shownWidth) / 2), top = (int)Math.Round((imageHeight - shownHeight) / 2);
            return new Int32Rect(Math.Max(0, left), Math.Max(0, top), Math.Max(1, (int)Math.Round(shownWidth)), Math.Max(1, (int)Math.Round(shownHeight * fraction)));
        }
        return new Int32Rect(0, 0, imageWidth, Math.Max(1, (int)Math.Round(imageHeight * fraction)));
    }

    /// <summary>
    /// The ink for the bar on the display at <paramref name="monitor"/> (physical pixels), <paramref name="barHeight"/>
    /// physical pixels deep. Reads the wallpaper file; call it off the UI thread. Dark ink when nothing can be read.
    /// </summary>
    public static BarInk Read(Rect monitor, double barHeight)
    {
        try
        {
            var (path, color, fit) = Wallpaper(monitor);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return For(Luminance(color.R, color.G, color.B));
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 480;
            image.EndInit();
            image.Freeze();
            var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var region = UnderBar(pixels.PixelWidth, pixels.PixelHeight, monitor.Width, monitor.Height, barHeight, fit);
            region = new Int32Rect(region.X, region.Y, Math.Min(region.Width, pixels.PixelWidth - region.X), Math.Min(region.Height, pixels.PixelHeight - region.Y));
            if (region.Width <= 0 || region.Height <= 0)
                return For(Luminance(color.R, color.G, color.B));
            var buffer = new byte[region.Width * region.Height * 4];
            pixels.CopyPixels(region, buffer, region.Width * 4, 0);
            return For(Average(buffer));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            Log.Warn("Mac-style desktop: reading the wallpaper failed", ex);
            return BarInk.Dark;
        }
    }

    /// <summary>
    /// A cheap signature of what's on the display's wallpaper now (its path, the file's time and the colour), so a slideshow
    /// change is seen without decoding.
    /// </summary>
    public static string Signature(Rect monitor)
    {
        try
        {
            var (path, color, fit) = Wallpaper(monitor);
            string time = !string.IsNullOrEmpty(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            return $"{path}|{time}|{color}|{fit}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            return "";
        }
    }

    private static (string? Path, Color Color, WallpaperFit Fit) Wallpaper(Rect monitor)
    {
        var wallpaper = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!)!;
        try
        {
            string? path = null;
            wallpaper.GetMonitorDevicePathCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                wallpaper.GetMonitorDevicePathAt(i, out string id);
                if (wallpaper.GetMonitorRECT(id, out var rect) != 0)
                    continue;
                if (rect.Left == (int)monitor.Left && rect.Top == (int)monitor.Top && rect.Right == (int)monitor.Right && rect.Bottom == (int)monitor.Bottom)
                {
                    wallpaper.GetWallpaper(id, out path);
                    break;
                }
            }
            if (path is null && count > 0)
                wallpaper.GetWallpaper(null, out path); // the same picture on every display
            wallpaper.GetBackgroundColor(out uint colorRef);
            wallpaper.GetPosition(out int position);
            var color = Color.FromRgb((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));
            return (path, color, Enum.IsDefined((WallpaperFit)position) ? (WallpaperFit)position : WallpaperFit.Fill);
        }
        finally { Marshal.ReleaseComObject(wallpaper); }
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Shell.Native.Rect rect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(int position);
        [PreserveSig] int GetPosition(out int position);
    }
}
