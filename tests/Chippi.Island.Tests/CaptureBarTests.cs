// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.Island.Tests;

/// <summary>
/// The capture features' bar lines (designs/capture-results v3): a capture's thumbnail in the circle, a picked color's
/// swatch, the count down's ring, and the glyph and Caution lines the bar already draws.
/// </summary>
public sealed class CaptureBarTests
{
    private static IslandActivity Line(string module, string title, string source) =>
        new(module + "-line", IslandActivityKind.Status, title, source) { IsFeedback = true, ModuleId = module };

    /// <summary>A small PNG of a document's top: a title strip over a page, 136 × 77, as a capture's thumbnail.</summary>
    internal static byte[] Thumbnail(Color top, Color page)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(page), null, new Rect(0, 0, 136, 77));
            dc.DrawRectangle(new SolidColorBrush(top), null, new Rect(0, 0, 136, 16));
            var line = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(line, null, new Rect(10, 26 + i * 12, i == 3 ? 60 : 116, 5), 2, 2);
        }
        var bitmap = new RenderTargetBitmap(136, 77, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        png.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void A_picture_fills_the_circle_as_a_songs_art_does() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            var copied = Line("screen-capture", "Copied", "Region · 1280 × 720") with { Picture = Thumbnail(Colors.SlateGray, Colors.White), Icon = "Camera" };
            window.SetActivities([copied], PetMood.Idle);
            Assert.Equal(Visibility.Visible, window.Cover.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Glyph.Visibility);
            Assert.IsType<ImageBrush>(window.CoverArt.Fill);
            Assert.Equal(Visibility.Collapsed, window.CoverGlyph.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Equalizer.Visibility);
            Assert.Equal("Copied · Region · 1280 × 720", window.ChipText);

            // A picture that doesn't decode: the line still reads, with its glyph.
            window.SetActivities([copied with { Picture = [1, 2, 3] }], PetMood.Idle);
            Assert.Equal(Visibility.Collapsed, window.Cover.Visibility);
            Assert.Equal(Visibility.Visible, window.Glyph.Visibility);
            Assert.Same(LucideIcons.Camera, window.GlyphIcon.Data);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_swatch_fills_the_circle_with_the_color_and_no_glyph() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetActivities([Line("color-picker", "#7C5CFF", "Copied as HEX") with { Swatch = "#7C5CFF" }], PetMood.Idle);
            Assert.Equal(Visibility.Visible, window.Glyph.Visibility);
            Assert.Equal(Color.FromRgb(0x7C, 0x5C, 0xFF), Assert.IsType<SolidColorBrush>(window.GlyphPlate.Fill).Color);
            Assert.Equal(Visibility.Collapsed, window.GlyphIcon.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Ring.Visibility);

            // The next line gets the plate's own tint back.
            window.SetActivities([Line("color-picker", "Copy failed", "Color picker") with { Icon = "TriangleAlert", NeedsAttention = true }], PetMood.Idle);
            Assert.Equal(Visibility.Visible, window.GlyphIcon.Visibility);
            Assert.Same(window.FindResource("IslandAccentTintBrush"), window.GlyphPlate.Fill);

            // Not a #RRGGBB color: the glyph shows.
            window.SetActivities([Line("color-picker", "Copied", "") with { Swatch = "violet", Icon = "Pipette" }], PetMood.Idle);
            Assert.Same(LucideIcons.Pipette, window.GlyphIcon.Data);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_count_down_shows_its_ring() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetActivities([Line("screen-capture", "Capturing in 3", "Region") with { Progress = 1 }], PetMood.Idle);
            Assert.Equal(Visibility.Visible, window.Ring.Visibility);
            Assert.Equal(Visibility.Collapsed, window.GlyphIcon.Visibility);
            Assert.Equal("Capturing in 3 · Region", window.ChipText);
        }
        finally { window.Close(); }
    });

#if DEBUG
    /// <summary>Every capture line (capture-bar-&lt;theme&gt;-*.png), when CHIPPI_ISLAND_SHOTS names a folder; the widget shows on the primary display.</summary>
    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        bool light = WpfTestThread.ShotsTheme == "Light";
        var backdrop = light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
        string theme = WpfTestThread.ShotsTheme.ToLowerInvariant();
        byte[] doc = Thumbnail(Color.FromRgb(0xE6, 0xE6, 0xE6), Colors.White);
        byte[] desk = Thumbnail(Color.FromRgb(0x2C, 0x3E, 0x57), Color.FromRgb(0x1B, 0x28, 0x38));
        var lines = new (string Name, IslandActivity Line)[]
        {
            ("copied", Line("screen-capture", "Copied", "Region · 1280 × 720") with { Picture = doc, Icon = "Camera" }),
            ("saved", Line("screen-capture", "Saved", "Screenshots · 1280 × 720") with { Picture = desk, Icon = "Camera" }),
            ("copy-failed", Line("screen-capture", "Copy failed", "Region · 1280 × 720") with { Icon = "TriangleAlert", NeedsAttention = true }),
            ("save-failed", Line("screen-capture", "Save failed", "Folder not found") with { Icon = "TriangleAlert", NeedsAttention = true }),
            ("capture-failed", Line("screen-capture", "Capture failed", "Region") with { Icon = "TriangleAlert", NeedsAttention = true }),
            ("copied-text", Line("screen-text", "Copied text", "31 words") with { Icon = "ScanText" }),
            ("no-text", Line("screen-text", "No text found", "Text and QR") with { Icon = "ScanText", NeedsAttention = true }),
            ("no-qr", Line("screen-text", "No QR code found", "Text and QR") with { Icon = "QrCode", NeedsAttention = true }),
            ("color", Line("color-picker", "#7C5CFF", "Copied as HEX") with { Swatch = "#7C5CFF" }),
            ("countdown", Line("screen-capture", "Capturing in 3", "Region") with { Progress = 1 }),
        };
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            window.PointerAt = () => new Point(-20000, -20000);
            foreach (var (name, line) in lines)
            {
                window.SetActivities([line], PetMood.Idle);
                IslandPanelTests.Pump(TimeSpan.FromMilliseconds(500));
                window.SaveScene(Path.Combine(folder, $"capture-bar-{theme}-{name}.png"), backdrop);
            }
            window.SetActivities([], PetMood.Sleepy);
        }));
    }
#endif
}
