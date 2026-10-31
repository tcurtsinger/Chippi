// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Flyout;
using Chippi.App.Island;
using Chippi.UI.Pet;
using Chippi.Core.Config;
using Chippi.Features.Sound;
using Chippi.Features.Sound.Volume;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.SoundSmoke;

/// <summary>
/// Renders the island's real Sound, App volume and Now Playing pages over fake devices: on the top
/// edge and on the left edge. Runs only when CHIPPI_ISLAND_SHOTS names an
/// output folder.
/// </summary>
public sealed class SoundPageShots
{
    [Fact]
    public Task Render() => SmokeDispatcher.Run(async () =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        await using var scene = new SoundScene();
        await scene.Start();
        var monitor = new Rect(-20000, 0, 1920, 1080);
        var work = new Rect(-20000, 0, 1920, 1032);
        var area = new MonitorArea(default, monitor, work, 1);
        foreach (var (name, open) in new (string, Action<IslandPanelWindow>)[]
        {
            ("bottomright", p => p.Open(IslandGeometry.Start(work, 1), area)),
            ("topleft", p => p.Open(IslandGeometry.PuckRect(0, 0, work, 1), area)),
        })
        {
            var panel = new IslandPanelWindow();
            try
            {
                panel.Initialize();
                panel.SetMedia(new FakeMedia());
                panel.SetTiles(Chippi.Core.Modules.IslandTileContribution.Group(scene.Shell.Registry.Features.SelectMany(e => e.IslandTiles)));
                panel.SetActivities(scene.Shell.Island.Activities);
                panel.SetLastPage(null);
                open(panel);
                await Task.Delay(500);
                Save(panel, work, Path.Combine(folder, $"sound-{name}-grid.png"));
                foreach (var key in new[] { SoundTile.Key, AppVolumeModule.TileKey, IslandPanelWindow.NowPlayingKey })
                {
                    panel.ShowPage(key);
                    await Task.Delay(500);
                    Save(panel, work, Path.Combine(folder, $"sound-{name}-{key}.png"));
                }
            }
            finally { panel.Close(); }
        }
    });

    /// <summary>The Sound page's states (fixed-size f1 and ps2, sound-monitor sound.png) on the top edge.</summary>
    [Fact]
    public Task RenderStates() => SmokeDispatcher.Run(async () =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        string[] outputs = ["Speakers (INZONE Buds - Game)", "Headphones (RØDE VideoMic GO II)", "LG ULTRAGEAR (NVIDIA High Definition Audio)", "Speakers (Steam Streaming Speakers)", "Digital Output (S/PDIF)", "Headphones (Xbox Wireless Headset)", "Speakers (Realtek USB2.0 Audio)"];
        string[] mics = ["Headset Microphone (INZONE Buds - Chat)", "Microphone (Steam Streaming Microphone)", "Microphone (Xbox Wireless Headset)", "Line In (Realtek USB2.0 Audio)", "Microphone (Webcam)"];
        foreach (var (name, arrange) in new (string, Func<SoundScene, Task>)[]
        {
            ("muted", async s =>
            {
                await s.Routing.SetOutputMutedAsync("speakers", true);
                await s.Mute.ToggleAsync();
                await s.Processing.SetEnabledAsync(true);
            }),
            ("crowded", s =>
            {
                s.Devices.With([.. outputs.Select((n, i) => Speaker($"out{i}", n)), .. mics.Select((n, i) => Mic($"mic{i}", n))]);
                return s.Audio.RefreshAsync();
            }),
            ("crowded-stopped", async s =>
            {
                s.Devices.With([.. outputs.Select((n, i) => Speaker($"out{i}", n)), .. mics.Select((n, i) => Mic($"mic{i}", n))]);
                s.Devices.Remove("CableIn", "CableOut");
                await s.Audio.RefreshAsync();
                await s.Processing.SetEnabledAsync(true); // stops: no VB-CABLE, so its line takes room from both cards
            }),
            ("no-mic", s =>
            {
                s.Devices.Remove("desk", "CableOut");
                return s.Audio.RefreshAsync();
            }),
        })
        {
            await using var scene = new SoundScene();
            await scene.Start();
            await arrange(scene);
            await Task.Delay(300);
            var work = new Rect(-20000, 0, 1920, 1032);
            var panel = new IslandPanelWindow();
            try
            {
                panel.Initialize();
                panel.SetMedia(new FakeMedia());
                panel.SetTiles(Chippi.Core.Modules.IslandTileContribution.Group(scene.Shell.Registry.Features.SelectMany(e => e.IslandTiles)));
                panel.SetActivities(scene.Shell.Island.Activities);
                panel.SetLastPage(null);
                panel.Open(IslandGeometry.Start(work, 1), new MonitorArea(default, new Rect(-20000, 0, 1920, 1080), work, 1));
                await Task.Delay(500);
                Save(panel, work, Path.Combine(folder, $"sound-top-grid-{name}.png"));
                panel.ShowPage(SoundTile.Key);
                await Task.Delay(500);
                Save(panel, work, Path.Combine(folder, $"sound-top-sound-{name}.png"));
            }
            finally { panel.Close(); }
        }
    });

    /// <summary>Settings pages over the scene's features, laid out unshown at the window's size.</summary>
    [Fact]
    public Task RenderSettings() => SmokeDispatcher.Run(async () =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        await using var scene = new SoundScene();
        await scene.Start();
        foreach (var page in new[] { "island", "page:sound-output", "page:sound-microphone", "module:app-volume" })
        {
            var window = new Chippi.App.Settings.SettingsWindow(scene.Shell, page);
            try
            {
                var root = (FrameworkElement)window.Content;
                foreach (var height in new[] { 720.0, 1600 })
                {
                    root.Measure(new Size(1120, height));
                    root.Arrange(new Rect(0, 0, 1120, height));
                    root.UpdateLayout();
                    await Task.Delay(200);
                    var image = new RenderTargetBitmap(1120, (int)height, 96, 96, PixelFormats.Pbgra32);
                    image.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var stream = File.Create(Path.Combine(folder, $"settings-{page.Replace(':', '-').Replace('/', '-')}-{height:0}.png"));
                    encoder.Save(stream);
                }
            }
            finally { window.Close(); }
        }
    });

    // The island over a dark desktop, with the work area's edge drawn so the dock shows.
    private static void Save(IslandPanelWindow panel, Rect work, string path)
    {
        var window = IslandNative.WindowRect(panel.Handle);
        var region = panel.Footprint;
        region.Inflate(48, 48);
        region.Intersect(window);
        var root = panel.Visual;
        var pixels = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        pixels.Render(root);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(region.Size));
            work.Offset(-region.X, -region.Y);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)), 1), work);
            dc.DrawImage(pixels, new Rect(window.X - region.X, window.Y - region.Y, pixels.PixelWidth, pixels.PixelHeight));
        }
        var image = new RenderTargetBitmap((int)region.Width, (int)region.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
#endif
