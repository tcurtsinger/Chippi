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
            foreach (int dpi in new[] { 96, 144 })
            {
                var display = new PixelRect(0, 0, 1920, 1080);
                var freeze = new Freeze([Screen(display, dpi)], [Terminal, Editor], DateTimeOffset.UnixEpoch);
                string at = dpi == 96 ? "" : "-150";
                var drag = new PixelSize(4, 4);
                var request = new CaptureRequest(CaptureMode.Region);

                var hover = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(1200, 600), drag, 6);
                Shoot(hover, request, $"capture-hover{at}.png");

                var dragging = new Selector(freeze, CaptureMode.Region, null, new PixelPoint(400, 300), drag, 6);
                dragging.Down(new PixelPoint(400, 300), ctrl: false);
                dragging.Move(new PixelPoint(1100, 700));
                Shoot(dragging, request, $"capture-drag{at}.png");

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
            }

            void Shoot(Selector selector, CaptureRequest request, string name, bool bars = false)
            {
                var screen = selector.Freeze.Screens[0];
                var view = new OverlayView();
                view.Place(screen.Bounds, screen.Dpi);
                view.SetImage(screen);
                if (bars)
                    view.SetBars(Bar(40, 280, vertical: true), Bar(220, 40, vertical: false));
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

    /// <summary>A stand-in for a feature's bar: a dark rounded strip with a few square buttons.</summary>
    private static Border Bar(double width, double height, bool vertical)
    {
        var buttons = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Margin = new Thickness(4) };
        int count = (int)((vertical ? height : width) / 36);
        for (int i = 0; i < count; i++)
            buttons.Children.Add(new Border { Width = 28, Height = 28, Margin = new Thickness(2), CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) });
        return new Border { Width = width, Height = height, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2E)), Child = buttons };
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
