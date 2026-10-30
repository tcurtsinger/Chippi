// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders the open island off screen with stand-in feature sections, on every edge: the grid, each
/// page and a morph caught halfway. Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class IslandPanelShots
{
    private static readonly MonitorArea Area = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            var media = new FakeMedia
            {
                Sessions = [new MediaTrack("Spotify.exe", "Spotify", "Weekend Mix", "Various Artists", true, true, true, true, true, true,
                    TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null)],
            };
            // The home as the mock shows it: eight buttons, Keep awake on, the volume slider.
            var mic = Section("sound", "Sound", 10, 220, status: "Live");
            var micSpec = mic.Spec with { Toggle = new IslandTileToggle(() => false, () => { }, "Microphone", "Mic", () => "Live"), Volume = new IslandTileVolume(() => 0.62, _ => { }) };
            var awake = Section("keep-awake", "Keep awake", 40, 96, status: "1 hr left");
            var awakeSpec = awake.Spec with { Toggle = new IslandTileToggle(() => true, () => { }) };
            var awakeTile = new IslandTileContribution("keep-awake", awakeSpec);
            awakeTile.SetStatus("1 hr left");
            var tiles = Tiles(
                new IslandTileContribution("sound", micSpec),
                Section("sound.mixer", "App volume", 20, 200, status: "4 apps"),
                awakeTile,
                Section("downloads", "File organizer", 50, 120, status: "Auto"),
                Section("system-monitor", "System monitor", 60, 120, status: "CPU 12%"),
                Section("clipboard", "Clipboard", 70, 120, status: "23 items"),
                Section("scratchpad", "Scratchpad", 80, 120, status: "4 notes"),
                Section("shelf", "Shelf", 90, 120, status: "5 items"));
            foreach (var (edge, tab) in new[]
            {
                (IslandEdge.Top, new Rect(-20000 + 960 - 68, 0, 136, 36)),
                (IslandEdge.Left, new Rect(-20000, 516 - 68, 36, 136)),
                (IslandEdge.Right, new Rect(-20000 + 1920 - 36, 516 - 68, 36, 136)),
            })
            {
                var panel = new IslandPanelWindow();
                try
                {
                    panel.Initialize();
                    panel.SetMedia(media);
                    panel.SetTiles(tiles);
                    panel.SetActivities([], PetMood.Idle);
                    panel.SetLastPage(null);
                    panel.Open(edge, tab, Area);
                    Pump(TimeSpan.FromMilliseconds(500));
                    Save(panel, Path.Combine(folder, $"{edge}-grid.png".ToLowerInvariant()));
                    foreach (var key in panel.PageKeys())
                    {
                        panel.ShowPage(key);
                        Pump(TimeSpan.FromMilliseconds(500));
                        Save(panel, Path.Combine(folder, $"{edge}-{key}.png".ToLowerInvariant()));
                    }
                    panel.ShowPage(null, remember: false);
                    Pump(TimeSpan.FromMilliseconds(500));
                    Save(panel, Path.Combine(folder, $"{edge}-grid-with-back.png".ToLowerInvariant()));
                    if (edge == IslandEdge.Top)
                    {
                        panel.ShowPage(IslandPanelWindow.NowPlayingKey);
                        Pump(TimeSpan.FromMilliseconds(110));
                        Save(panel, Path.Combine(folder, "top-morph-midway.png"));
                    }
                }
                finally
                {
                    panel.Close();
                }
            }
        });
    }

    // A stand-in section: a card of the given height (the island draws the page's name above it).
    private static IslandTileContribution Section(string key, string name, int order, double height, string status = "")
    {
        var part = new IslandTileContribution(key, new IslandTileSpec(key, name, Icon(key), order, 0, () =>
        {
            return new Border { Height = height, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = $"{name} section", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        }));
        part.SetStatus(status);
        return part;
    }

    private static string Icon(string key) => key switch
    {
        "sound" => "AudioLines",
        "sound.mixer" => "Sliders",
        "keep-awake" => "Coffee",
        "system-monitor" => "Activity",
        "clipboard" => "ClipboardList",
        "scratchpad" => "NotebookPen",
        "shelf" => "Inbox",
        _ => "Folder",
    };

    // The island over a dark desktop, with the work area's edge drawn so the dock shows.
    internal static void Save(IslandPanelWindow panel, string path)
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
            var work = Area.Work;
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
