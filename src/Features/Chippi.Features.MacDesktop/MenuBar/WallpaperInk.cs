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
/// How light the wallpaper is under a display's bar, column by column from left to right (0 black, 1 white). The bar's two
/// groups each take their ink from the columns they sit over, so a picture dark on one side and light on the other gets
/// white ink on one group and dark on the other.
/// </summary>
public sealed class InkStrip(IReadOnlyList<double> columns)
{
    public IReadOnlyList<double> Columns { get; } = columns.Count > 0 ? columns : [WallpaperInk.Threshold];

    /// <summary>The same lightness all the way across: a solid colour, or nothing read yet.</summary>
    public static InkStrip Flat(double luminance) => new([luminance]);

    /// <summary>The average over the part of the bar from <paramref name="from"/> to <paramref name="to"/>, each 0 (left end) to 1.</summary>
    public double Average(double from, double to)
    {
        int count = Columns.Count;
        int first = Math.Clamp((int)Math.Floor(Math.Min(from, to) * count), 0, count - 1);
        int last = Math.Clamp((int)Math.Ceiling(Math.Max(from, to) * count) - 1, first, count - 1);
        double sum = 0;
        for (int i = first; i <= last; i++)
            sum += Columns[i];
        return sum / (last - first + 1);
    }

    /// <summary>
    /// The ink for that part of the bar. It keeps <paramref name="current"/> while the average is within
    /// <see cref="WallpaperInk.Hold"/> of the threshold, so a group that grows or shrinks by a title doesn't flip its ink.
    /// </summary>
    public BarInk Ink(double from, double to, BarInk? current = null)
    {
        double average = Average(from, to);
        return current is { } now && Math.Abs(average - WallpaperInk.Threshold) < WallpaperInk.Hold ? now : WallpaperInk.For(average);
    }
}

/// <summary>
/// Reads each display's wallpaper and how light it is under the bar (docs/mac-style/brief.md, Menu bar): white ink below
/// 0.5. The arithmetic is pure and tested; reading the wallpaper is Windows'.
/// </summary>
public static class WallpaperInk
{
    /// <summary>White ink below this average luminance (0 black, 1 white).</summary>
    public const double Threshold = 0.5;

    /// <summary>How close to the threshold an average may come without changing the ink a group already has.</summary>
    public const double Hold = 0.04;

    /// <summary>How many columns a strip is read in.</summary>
    public const int ColumnCount = 96;

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
    /// Where Windows draws the picture, as a rectangle from the display's top-left corner in the display's pixels: larger
    /// than the display where it's cropped (Fill), smaller where the background colour shows around it (Fit, Center). Span
    /// fills the whole <paramref name="desktop"/> (every display together) with one picture, so each display shows its own
    /// part. For Tile it's the tile that starts at the desktop's origin; the others repeat from it.
    /// </summary>
    public static Rect Drawn(double imageWidth, double imageHeight, Rect display, Rect desktop, WallpaperFit fit)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || display.Width <= 0 || display.Height <= 0)
            return Rect.Empty;
        switch (fit)
        {
            case WallpaperFit.Stretch:
                return new Rect(0, 0, display.Width, display.Height);
            case WallpaperFit.Center:
                return new Rect((display.Width - imageWidth) / 2, (display.Height - imageHeight) / 2, imageWidth, imageHeight);
            case WallpaperFit.Tile:
                return new Rect(-display.Left, -display.Top, imageWidth, imageHeight);
            case WallpaperFit.Fit:
            {
                double scale = Math.Min(display.Width / imageWidth, display.Height / imageHeight);
                return new Rect((display.Width - imageWidth * scale) / 2, (display.Height - imageHeight * scale) / 2, imageWidth * scale, imageHeight * scale);
            }
            case WallpaperFit.Span when desktop.Width > 0 && desktop.Height > 0:
            {
                double scale = Math.Max(desktop.Width / imageWidth, desktop.Height / imageHeight);
                return new Rect(desktop.Left + (desktop.Width - imageWidth * scale) / 2 - display.Left,
                    desktop.Top + (desktop.Height - imageHeight * scale) / 2 - display.Top, imageWidth * scale, imageHeight * scale);
            }
            default: // Fill
            {
                double scale = Math.Max(display.Width / imageWidth, display.Height / imageHeight);
                return new Rect((display.Width - imageWidth * scale) / 2, (display.Height - imageHeight * scale) / 2, imageWidth * scale, imageHeight * scale);
            }
        }
    }

    /// <summary>
    /// The strip under a bar <paramref name="barHeight"/> deep across a display <paramref name="displayWidth"/> wide, in
    /// <paramref name="count"/> columns. <paramref name="picture"/> gives the picture's luminance at a place in it (0 to 1
    /// each way); where the picture isn't drawn, the column is the <paramref name="background"/>.
    /// </summary>
    public static double[] Strip(int count, double displayWidth, double barHeight, Rect drawn, bool tile, double background, Func<double, double, double> picture)
    {
        const int Rows = 4;
        var columns = new double[Math.Max(1, count)];
        for (int c = 0; c < columns.Length; c++)
        {
            double x = (c + 0.5) / columns.Length * displayWidth, sum = 0;
            for (int r = 0; r < Rows; r++)
            {
                double y = (r + 0.5) / Rows * barHeight;
                if (drawn.IsEmpty || drawn.Width <= 0 || drawn.Height <= 0)
                {
                    sum += background;
                    continue;
                }
                double u = (x - drawn.X) / drawn.Width, v = (y - drawn.Y) / drawn.Height;
                if (tile)
                {
                    u -= Math.Floor(u);
                    v -= Math.Floor(v);
                }
                sum += u is >= 0 and < 1 && v is >= 0 and < 1 ? picture(u, v) : background;
            }
            columns[c] = sum / Rows;
        }
        return columns;
    }

    /// <summary>
    /// How light the wallpaper is under the bar on the display at <paramref name="monitor"/> (physical pixels),
    /// <paramref name="barHeight"/> physical pixels deep. Reads the wallpaper file; call it off the UI thread. A light strip
    /// (dark ink) when nothing can be read.
    /// </summary>
    public static InkStrip Read(Rect monitor, double barHeight)
    {
        try
        {
            var (path, color, fit, desktop) = Wallpaper(monitor);
            double background = Luminance(color.R, color.G, color.B);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return InkStrip.Flat(background);
            int fullWidth, fullHeight;
            var image = new BitmapImage();
            // Windows replaces the file when the wallpaper changes: it's opened so that it still can, and closed at once.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // The picture's own size places it (Center and Tile draw it pixel for pixel); a small copy is enough to read.
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                (fullWidth, fullHeight) = (frame.PixelWidth, frame.PixelHeight);
                stream.Position = 0;
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = 480;
                image.EndInit();
            }
            image.Freeze();
            var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int width = pixels.PixelWidth, height = pixels.PixelHeight;
            if (width <= 0 || height <= 0 || fullWidth <= 0 || fullHeight <= 0)
                return InkStrip.Flat(background);
            var buffer = new byte[width * height * 4];
            pixels.CopyPixels(buffer, width * 4, 0);
            double At(double u, double v)
            {
                int i = (Math.Min(height - 1, (int)(v * height)) * width + Math.Min(width - 1, (int)(u * width))) * 4;
                return Luminance(buffer[i + 2], buffer[i + 1], buffer[i]);
            }
            var drawn = Drawn(fullWidth, fullHeight, monitor, desktop, fit);
            return new InkStrip(Strip(ColumnCount, monitor.Width, barHeight, drawn, fit == WallpaperFit.Tile, background, At));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            Log.Warn("Mac-style desktop: reading the wallpaper failed", ex);
            return InkStrip.Flat(1);
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
            var (path, color, fit, desktop) = Wallpaper(monitor);
            string time = !string.IsNullOrEmpty(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            return $"{path}|{time}|{color}|{fit}|{desktop}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidCastException or ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    private static (string? Path, Color Color, WallpaperFit Fit, Rect Desktop) Wallpaper(Rect monitor)
    {
        var wallpaper = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!)!;
        try
        {
            string? path = null;
            var desktop = Rect.Empty;
            wallpaper.GetMonitorDevicePathCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                wallpaper.GetMonitorDevicePathAt(i, out string id);
                if (wallpaper.GetMonitorRECT(id, out var rect) != 0)
                    continue;
                desktop.Union(new Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)));
                if (path is null && rect.Left == (int)monitor.Left && rect.Top == (int)monitor.Top && rect.Right == (int)monitor.Right && rect.Bottom == (int)monitor.Bottom)
                    wallpaper.GetWallpaper(id, out path);
            }
            if (path is null && count > 0)
                wallpaper.GetWallpaper(null, out path); // the same picture on every display
            wallpaper.GetBackgroundColor(out uint colorRef);
            wallpaper.GetPosition(out int position);
            var color = Color.FromRgb((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));
            return (path, color, Enum.IsDefined((WallpaperFit)position) ? (WallpaperFit)position : WallpaperFit.Fill, desktop.IsEmpty ? monitor : desktop);
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
