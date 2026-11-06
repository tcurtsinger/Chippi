// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Island.Tests;

/// <summary>
/// The capture overlay's states over a synthetic frozen screen (docs/phase-7/capture-layer.md, Tests: offscreen shots),
/// rendered off screen: no window shows and nothing reads the real screen. Runs only when CHIPPI_ISLAND_SHOTS names a
/// folder (capture-*.png). The look is the layer's neutral one until Lumen's board 3 restyles it.
/// </summary>
public sealed class CaptureOverlayShots
{
    private static readonly FrozenWindow Editor = new(1, "Notes", @"C:\Apps\notes.exe", new PixelRect(260, 160, 900, 600));
    private static readonly FrozenWindow Terminal = new(2, "Terminal", @"C:\Apps\terminal.exe", new PixelRect(980, 420, 760, 520));

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            // The overlays take the island's styles for the bars, as the shell's capture layer does.
            var styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") };
            string theme = WpfTestThread.ShotsTheme == "Light" ? "-light" : "";
            Gallery(styles, Path.Combine(folder, $"capture-markup{theme}.png"));
            foreach (int dpi in new[] { 96, 144 })
            {
                var display = new PixelRect(0, 0, 1920, 1080);
                var freeze = new Freeze([Screen(display, dpi)], [Terminal, Editor], DateTimeOffset.UnixEpoch);
                string at = (dpi == 96 ? "" : "-150") + theme;
                var drag = new PixelSize(4, 4);
                var request = new CaptureRequest(CaptureMode.Region);

                var hover = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(1200, 600), drag, 6);
                Shoot(hover, request, $"capture-hover{at}.png");

                var dragging = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(400, 300), drag, 6);
                dragging.Down(new PixelPoint(400, 300), ctrl: false);
                dragging.Move(new PixelPoint(1100, 700));
                Shoot(dragging, request, $"capture-drag{at}.png");

                var copying = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(400, 300), drag, 6);
                copying.Down(new PixelPoint(400, 300), ctrl: true);
                copying.Move(new PixelPoint(1100, 700));
                Shoot(copying, request, $"capture-ctrl-drag{at}.png");

                var released = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(400, 300), drag, 6);
                released.Down(new PixelPoint(400, 300), false);
                released.Up(new PixelPoint(1100, 700));
                Shoot(released, request, $"capture-released{at}.png", bars: true);

                var corner = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(1500, 760), drag, 6);
                corner.Down(new PixelPoint(1500, 760), false);
                corner.Up(new PixelPoint(1915, 1075));
                Shoot(corner, request, $"capture-flipped{at}.png", bars: true);

                var full = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(500, 500), drag, 6);
                full.Key(SelectorKey.A, false, true);
                Shoot(full, request, $"capture-full-display{at}.png", bars: true);

                var window = new Selector(freeze, CaptureMode.Window, null, new PixelPoint(1200, 600), drag, 6);
                window.Down(new PixelPoint(1200, 600), false);
                window.Up(new PixelPoint(1200, 600));
                Shoot(window, request, $"capture-window{at}.png", bars: true);

                var free = new Selector(freeze, CaptureMode.Freeform, null, new PixelPoint(1020, 500), drag, 6);
                free.Down(new PixelPoint(1020, 500), false);
                for (int i = 0; i <= 60; i++)
                {
                    double t = i / 60.0 * Math.PI * 2;
                    free.Move(new PixelPoint(700 + (int)(260 * Math.Cos(t) + 60 * Math.Cos(3 * t)), 500 + (int)(200 * Math.Sin(t))));
                }
                free.Up(new PixelPoint(960, 500));
                Shoot(free, request with { Mode = CaptureMode.Freeform }, $"capture-freeform{at}.png", bars: true);

                var pick = new Selector(freeze, CaptureMode.PickPoint, null, new PixelPoint(300, 175), drag, 6);
                Shoot(pick, new CaptureRequest(CaptureMode.PickPoint) { Dim = false }, $"capture-pick{at}.png");

                // A Recent capture reopened for Annotate: 900 × 600 at its own size, then 2600 × 1400 shrunk to fit.
                foreach (var (w, h) in new[] { (900, 600), (2600, 1400) })
                {
                    var crop = freeze.Cut(new PixelRect(200, 120, Math.Min(w, 1700), Math.Min(h, 960)));
                    var image = new CapturedImage(w, h, new byte[w * h * 4]);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x += crop.Width)
                            Buffer.BlockCopy(crop.Pixels, y % crop.Height * crop.Stride, image.Pixels, (y * w + x) * 4, Math.Min(crop.Width, w - x) * 4);
                    var shown = Reopening.Place(freeze, image, new PixelPoint(900, 500), 32 * dpi / 96);
                    var reopened = new Selector(shown.Freeze, shown.Rect, shown.Display, image, shown.Scale, new PixelPoint(900, 500));
                    Shoot(reopened, request with { Magnifier = false }, $"capture-reopen-{w}{at}.png", bars: true);
                }
            }

            void Shoot(Selector selector, CaptureRequest request, string name, bool bars = false)
            {
                var screen = selector.Freeze.Screens[0];
                var view = new OverlayView();
                view.Resources.MergedDictionaries.Add(styles);
                view.Place(screen.Bounds, screen.Dpi);
                view.SetImage(screen);
                if (bars)
                    view.SetBars(new MarkupTools { Tool = CaptureTool.Pen }, new FinishBar { ShowCopyText = true, ShowScanQrCode = true });
                view.Measure(new Size(view.Width, view.Height));
                view.Arrange(new Rect(0, 0, view.Width, view.Height));
                view.Render(selector, request, screen);
                view.UpdateLayout();
                var bitmap = new RenderTargetBitmap(screen.Bounds.Width, screen.Bounds.Height, screen.Dpi, screen.Dpi, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, name));
                png.Save(file);
            }
        });
    }

    /// <summary>
    /// The bars on their own over a wallpaper, at 2×: the column with Rectangle lit and Undo on, the colour choice (blue,
    /// Thick, Fill for the Rectangle), a Text mark's bar with its size menu open and Bold on, Crop's bar, the finish bar
    /// without Copy text and Scan QR code (Text and QR off) and with them, and the finish bar after a failed Copy and after
    /// a Save that failed once Copy had worked.
    /// </summary>
    private static void Gallery(ResourceDictionary styles, string path)
    {
        var canvas = new Canvas { Width = 760, Height = 680 };
        canvas.Resources.MergedDictionaries.Add(styles);
        canvas.Background = new LinearGradientBrush(Color.FromRgb(0x2C, 0x3E, 0x57), Color.FromRgb(0x1B, 0x28, 0x38), 45);
        var tools = new MarkupTools { Tool = CaptureTool.Rectangle, Ink = CaptureBars.Inks[3].Color, CanUndo = true };
        var ink = new InkChoice { Ink = CaptureBars.Inks[3].Color, Stroke = InkWidth.Thick, ShowFill = true, Filled = true };
        var text = new TextMarkBar { TextSize = 24, Bold = true };
        var crop = new CropBar();
        var finish = new FinishBar { ShowCopyText = false, ShowScanQrCode = false };
        var full = new FinishBar { ShowCopyText = true, ShowScanQrCode = true };
        var copyFailed = new FinishBar { ShowCopyText = true, ShowScanQrCode = true, Failure = "Copy failed" };
        var saveFailed = new FinishBar { ShowCopyText = false, ShowScanQrCode = false, Failure = "Copied. Save failed" };
        foreach (var (bar, x, y) in new (FrameworkElement, double, double)[] { (tools, 24, 24), (ink, 96, 24), (text, 96, 330), (crop, 300, 330), (finish, 96, 420), (full, 96, 480), (copyFailed, 96, 550), (saveFailed, 96, 610) })
        {
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            canvas.Children.Add(bar);
        }
        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(760 * 2, 680 * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    /// <summary>A desktop with a gradient wallpaper and two app windows, drawn into BGRA like a screen read.</summary>
    private static CapturedScreen Screen(PixelRect display, int dpi)
    {
        var pixels = new byte[display.Area * 4];
        void Fill(PixelRect r, Func<int, int, (byte R, byte G, byte B)> color)
        {
            var clip = r.Intersect(display);
            for (int y = clip.Y; y < clip.Bottom; y++)
                for (int x = clip.X; x < clip.Right; x++)
                {
                    var (cr, cg, cb) = color(x, y);
                    int i = ((y - display.Y) * display.Width + x - display.X) * 4;
                    pixels[i] = cb;
                    pixels[i + 1] = cg;
                    pixels[i + 2] = cr;
                    pixels[i + 3] = 255;
                }
        }
        Fill(display, (x, y) => ((byte)(30 + x * 60 / display.Width), (byte)(70 + y * 80 / display.Height), (byte)(140 + x * 60 / display.Width)));
        foreach (var (w, dark) in new[] { (Editor, false), (Terminal, true) })
        {
            var f = w.Frame;
            Fill(f, (_, _) => dark ? ((byte)24, (byte)24, (byte)28) : ((byte)250, (byte)250, (byte)250));
            Fill(f with { Height = 32 }, (_, _) => dark ? ((byte)40, (byte)40, (byte)46) : ((byte)232, (byte)232, (byte)236));
            for (int line = 0; line < (f.Height - 60) / 22; line++)
                Fill(new PixelRect(f.X + 24, f.Y + 52 + line * 22, 120 + (line * 97 % (f.Width - 200)), 8), (_, _) => dark ? ((byte)120, (byte)220, (byte)140) : ((byte)90, (byte)90, (byte)96));
        }
        return new CapturedScreen(@"\\?\DISPLAY#TEST", 1, display, dpi, pixels);
    }
}
