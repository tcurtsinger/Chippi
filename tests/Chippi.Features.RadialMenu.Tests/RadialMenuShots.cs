// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.RadialMenu.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.RadialMenu.Tests;

/// <summary>
/// The real wheel and Settings › Radial menu in the mockup's states (docs/phase-5.5/radial-menu.md), with its invented
/// wheel: pointed, a submenu, picked, Not found, empty, Settings set up and just installed. Runs only when
/// CHIPPI_ISLAND_SHOTS names an output folder; CHIPPI_SHOTS_THEME=light renders them light. The wheel's window sits far
/// off every screen. CHIPPI_RADIAL_TIMINGS names a report of opening and pointing times.
/// </summary>
public sealed class RadialMenuShots
{
    private static string Theme => WpfTestThread.ShotsTheme.ToLowerInvariant();

    /// <summary>The mockup's wheel: 8 slices, a Window submenu of 7 and an Apps submenu of 5, Steam gone.</summary>
    internal static List<Slice> MockupWheel() =>
    [
        new() { Kind = SliceKind.Media, Value = "PlayPause", Name = "Play/pause" },
        new() { Kind = SliceKind.Media, Value = "Next", Name = "Next" },
        new()
        {
            Kind = SliceKind.Submenu, Name = "Window", Glyph = nameof(LucideIcons.AppWindow),
            Slices = [.. new[] { "SnapLeft", "Maximize", "KeepOnTop", "SnapRight", "OtherScreen", "Minimize", "Close" }
                .Select(v => new Slice { Kind = SliceKind.Window, Value = v, Name = SliceCatalog.NameFor(new Slice { Kind = SliceKind.Window, Value = v }) })],
        },
        new() { Kind = SliceKind.Chippi, Value = "command-bar/open", Name = "Search" },
        new() { Kind = SliceKind.Keys, Value = "Win+Shift+S", Name = "Screenshot", Glyph = nameof(LucideIcons.Scissors) },
        new()
        {
            Kind = SliceKind.Submenu, Name = "Apps",
            Slices =
            [
                new() { Kind = SliceKind.App, Value = "Chrome", Name = "Chrome" },
                new() { Kind = SliceKind.App, Value = "Spotify", Name = "Spotify" },
                new() { Kind = SliceKind.App, Value = "Microsoft.WindowsTerminal", Name = "Terminal" },
                new() { Kind = SliceKind.App, Value = "Microsoft.VisualStudioCode", Name = "VS Code" },
                new() { Kind = SliceKind.App, Value = "Steam", Name = "Steam" },
            ],
        },
        new() { Kind = SliceKind.Selection, Value = "Search", Name = "Look up" },
        new() { Kind = SliceKind.Chippi, Value = "mic-mute/toggle", Name = "Mute mic" },
    ];

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            Wheel(folder, "pointed", f => Flick(f, 45, 100));
            Wheel(folder, "submenu", f =>
            {
                Flick(f, 90, 170);
                var sub = f.Module.Open!.Top.Middle;
                Point(f, sub, 180 + 45, 96);
            });
            Wheel(folder, "picked", f =>
            {
                Flick(f, 0, 96);
                ((IWheelInputSink)f.Module).Released(At(f, 0, 96));
            }, settle: false);
            Wheel(folder, "notfound", f =>
            {
                Flick(f, 225, 170);
                var sub = f.Module.Open!.Top.Middle;
                Point(f, sub, 45 + 5 * (360.0 / 6), 96);
            });
            Wheel(folder, "empty", f => { }, slices: []);
            Settings(folder, "settings-setup", MockupWheel(), back: true);
            Settings(folder, "settings-fresh", [], back: false);
        });
    }

    [Fact]
    public void Timings()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_RADIAL_TIMINGS") is not { Length: > 0 } report)
            return;
        WpfTestThread.Run(() =>
        {
            var f = Start(MockupWheel());
            f.Module.SetHold(MouseButton.Middle, true);
            Pump(TimeSpan.FromMilliseconds(300)); // the window is made while idle
            IWheelInputSink sink = f.Module;
            var opens = new List<double>();
            var frames = new List<double>();
            var moves = new List<double>();
            for (int i = 0; i < 31; i++)
            {
                var clock = Stopwatch.StartNew();
                sink.Opened(f.Pc.Middle, MouseButton.Middle);
                opens.Add(clock.Elapsed.TotalMilliseconds);
                NextFrame();
                frames.Add(clock.Elapsed.TotalMilliseconds);
                for (int step = 0; step < 36; step++)
                {
                    var move = Stopwatch.StartNew();
                    sink.Moved(At(f, step * 10, 100));
                    moves.Add(move.Elapsed.TotalMilliseconds);
                }
                f.Module.Dismissed();
                Pump(TimeSpan.FromMilliseconds(150));
            }
            f.Module.StopAsync(default).GetAwaiter().GetResult();
            string Line(string name, List<double> times, int skip)
            {
                var kept = times.Skip(skip).OrderBy(t => t).ToList();
                return string.Create(CultureInfo.InvariantCulture,
                    $"{name}: first {times[0]:0.0} ms; then median {kept[kept.Count / 2]:0.00} ms, max {kept[^1]:0.00} ms over {kept.Count}");
            }
            File.WriteAllLines(report,
            [
                "Radial menu timings (the mockup's wheel, 8 slices; a fake PC)",
                Line("Opening, the call", opens, 1),
                Line("Opening, to the first frame", frames, 1),
                Line("Pointing, per move", moves, 0),
            ]);
        });
    }

    private static Fixture Start(List<Slice> slices)
    {
        var f = new Fixture { Slices = slices };
        f.Pc.AppList =
        [
            new("Chrome", "Chrome"), new("Spotify", "Spotify"), new("Windows Terminal", "Microsoft.WindowsTerminal"),
            new("Visual Studio Code", "Microsoft.VisualStudioCode"), new("File Explorer", "Microsoft.Windows.Explorer"),
        ];
        f.Pc.Icons = true;
        f.StartAsync().GetAwaiter().GetResult();
        return f;
    }

    private static PixelPoint At(Fixture f, double degrees, double distance) => At(f, f.Module.Open?.Top.Middle ?? new Point(960, 540), degrees, distance);

    private static PixelPoint At(Fixture f, Point middle, double degrees, double distance)
    {
        var to = RadialMenu.Wheel.WheelGeometry.At(middle, distance, degrees);
        return new PixelPoint(FakePc.Far.Left + (int)Math.Round(to.X), FakePc.Far.Top + (int)Math.Round(to.Y));
    }

    // The pointer moves out in small steps, as a hand does.
    private static void Flick(Fixture f, double degrees, double distance)
    {
        var middle = f.Module.Open!.Top.Middle;
        for (double d = 10; d <= distance; d += 10)
            ((IWheelInputSink)f.Module).Moved(At(f, middle, degrees, d));
    }

    private static void Point(Fixture f, Point middle, double degrees, double distance) => ((IWheelInputSink)f.Module).Moved(At(f, middle, degrees, distance));

    private static void Wheel(string folder, string name, Action<Fixture> act, bool settle = true, List<Slice>? slices = null)
    {
        var f = Start(slices ?? MockupWheel());
        try
        {
            ((IWheelInputSink)f.Module).Opened(f.Pc.Middle, MouseButton.Middle);
            Pump(TimeSpan.FromMilliseconds(250));
            var stage = (FrameworkElement)f.Module.View!.Parent;
            act(f);
            // Picked, the wheel fades over 150ms: shot 20 in, as the grin and the accent fill show.
            Pump(TimeSpan.FromMilliseconds(settle ? 250 : 20));
            stage.UpdateLayout();
            Save(Backdrop(stage), Path.Combine(folder, $"radial-{name}-{Theme}.png"));
        }
        finally { f.Module.StopAsync(default).GetAwaiter().GetResult(); }
    }

    private static void Settings(string folder, string name, List<Slice> slices, bool back)
    {
        var f = Start(slices);
        try
        {
            if (back)
                f.Module.SetHold(MouseButton.Back, true);
            var page = new RadialMenuSettingsPage(f.Module);
            var frame = new Border { Width = 848, Padding = new Thickness(40, 32, 40, 32), Child = page };
            frame.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            frame.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
            var host = new Window { Content = frame, SizeToContent = SizeToContent.WidthAndHeight, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            host.Show();
            Pump(TimeSpan.FromMilliseconds(300));
            Save(Snap(frame), Path.Combine(folder, $"radial-{name}-{Theme}.png"));
            host.Close();
        }
        finally { f.Module.StopAsync(default).GetAwaiter().GetResult(); }
    }

    /// <summary>The wheel's window as it draws, over a wallpaper-like ground so its rim and shadow show.</summary>
    private static BitmapSource Backdrop(FrameworkElement stage)
    {
        var wheel = Snap(stage);
        bool light = Theme == "light";
        var ground = new LinearGradientBrush(
            light ? Color.FromRgb(0xE3, 0xEA, 0xF7) : Color.FromRgb(0x3E, 0x4C, 0x6E),
            light ? Color.FromRgb(0xA5, 0xB3, 0xCB) : Color.FromRgb(0x12, 0x16, 0x20), 45);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var size = new Rect(0, 0, wheel.PixelWidth / 2.0, wheel.PixelHeight / 2.0);
            dc.DrawRectangle(ground, null, size);
            dc.DrawImage(wheel, size);
        }
        var bitmap = new RenderTargetBitmap(wheel.PixelWidth, wheel.PixelHeight, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static BitmapSource Snap(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 2), (int)Math.Ceiling(element.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Pump(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void NextFrame()
    {
        var frame = new DispatcherFrame();
        EventHandler? rendered = null;
        rendered = (_, _) =>
        {
            CompositionTarget.Rendering -= rendered;
            frame.Continue = false;
        };
        CompositionTarget.Rendering += rendered;
        Dispatcher.PushFrame(frame);
    }
}
