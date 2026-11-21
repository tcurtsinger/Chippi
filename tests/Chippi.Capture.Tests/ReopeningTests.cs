// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture.Tests;

/// <summary>A finished capture reopened for Annotate (docs/phase-7/capture-layer.md, Reopening a capture).</summary>
public sealed class ReopeningTests
{
    private static readonly PixelRect Main = new(0, 0, 3440, 1440);
    private static readonly PixelRect Left = new(-1920, 360, 1920, 1080);

    private static CapturedImage Image(int w, int h, Func<int, int, (byte B, byte G, byte R, byte A)> pixel)
    {
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (b, g, r, a) = pixel(x, y);
                int i = (y * w + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, a);
            }
        return new CapturedImage(w, h, pixels);
    }

    [Fact]
    public void An_image_that_fits_shows_at_its_own_size_centred_on_the_pointers_display()
    {
        var live = SelectionTests.Fake(null, Main, Left);
        var image = Image(400, 300, (x, y) => ((byte)x, (byte)y, 200, 255));
        var (freeze, rect, scale, display) = Reopening.Place(live, image, new PixelPoint(-1000, 800), 32);

        Assert.Equal(1, scale);
        Assert.Equal(Left, display.Bounds);
        Assert.Equal(new PixelRect(-1920 + 760, 360 + 390, 400, 300), rect);
        // The display shows the image's pixels one for one; the other display and the live freeze are untouched.
        Assert.Equal(0xFFC80000u | 5 << 8 | 7, freeze.PixelAt(new PixelPoint(rect.X + 7, rect.Y + 5)));
        Assert.Same(live.Screens[0], freeze.Screens[0]);
        Assert.NotEqual(live.PixelAt(rect.TopLeft), freeze.PixelAt(rect.TopLeft));
        Assert.Empty(freeze.Windows);
    }

    [Fact]
    public void An_image_bigger_than_its_display_shrinks_inside_the_margin_and_keeps_its_shape()
    {
        var live = SelectionTests.Fake(null, Main, Left);
        var image = Image(5360, 1440, (_, _) => (10, 20, 30, 255)); // an All displays capture
        var (freeze, rect, scale, _) = Reopening.Place(live, image, new PixelPoint(100, 100), 32);

        Assert.Equal((3440 - 64) / 5360.0, scale, 6);
        Assert.Equal(3376, rect.Width);
        Assert.Equal((int)Math.Round(1440 * scale), rect.Height);
        Assert.True(new PixelRect(32, 32, 3376, 1376).Contains(rect));
        Assert.Equal(0xFF1E140Au, freeze.PixelAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
    }

    [Fact]
    public void Transparent_pixels_show_the_screen_under_them()
    {
        var live = SelectionTests.Fake(null, Main);
        var image = Image(10, 10, (x, _) => x < 5 ? ((byte)0, (byte)0, (byte)0, (byte)0) : ((byte)9, (byte)9, (byte)9, (byte)255));
        var (freeze, rect, _, _) = Reopening.Place(live, image, new PixelPoint(5, 5), 32);

        Assert.Equal(live.PixelAt(rect.TopLeft), freeze.PixelAt(rect.TopLeft));
        Assert.Equal(0xFF090909u, freeze.PixelAt(new PixelPoint(rect.X + 7, rect.Y)));
    }

    [Fact]
    public void The_selection_returns_the_image_itself_and_stays_where_it_is()
    {
        var live = SelectionTests.Fake(null, Main);
        var image = Image(5000, 2000, (x, y) => ((byte)x, (byte)y, 1, 255));
        var (freeze, rect, scale, display) = Reopening.Place(live, image, new PixelPoint(100, 100), 32);
        var selector = new Selector(freeze, rect, display, image, scale, new PixelPoint(100, 100));

        var selection = Assert.IsType<Selection>(selector.Selection);
        Assert.True(selector.IsReleased && selector.Fixed);
        Assert.Same(image, selection.Image());
        Assert.Equal(scale, selection.Scale);
        Assert.Equal(rect, selection.Rect);

        // No handles, no move, no resize, no Ctrl+A; a press inside isn't a drag.
        selector.Move(rect.TopLeft);
        Assert.Equal(SelectionHandle.None, selector.HandleUnderPointer);
        Assert.Equal(SelectorOutcome.None, selector.Key(SelectorKey.Right, false, false));
        Assert.Equal(SelectorOutcome.None, selector.Key(SelectorKey.Down, true, false));
        Assert.Equal(SelectorOutcome.None, selector.Key(SelectorKey.A, false, true));
        selector.Down(new PixelPoint(rect.X + 50, rect.Y + 50), false);
        selector.Move(new PixelPoint(rect.X + 300, rect.Y + 300));
        selector.Up(new PixelPoint(rect.X + 300, rect.Y + 300));
        Assert.Same(selection, selector.Selection);

        // Everything else is the feature's: Esc, Enter, Ctrl+C, tool letters.
        Assert.Equal(SelectorOutcome.PassToFeature, selector.Key(SelectorKey.Escape, false, false));
        Assert.Equal(SelectorOutcome.PassToFeature, selector.Key(SelectorKey.Enter, false, false));
        Assert.Equal(SelectorOutcome.PassToFeature, selector.Key(SelectorKey.Other, false, true));
    }

    [Fact]
    public void Shrinking_averages_by_alpha()
    {
        var image = Image(2, 1, (x, _) => x == 0 ? ((byte)200, (byte)200, (byte)200, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        var shown = Reopening.Shrink(image, 1, 1);
        Assert.Equal(new byte[] { 200, 200, 200, 127 }, shown);
    }

    [Fact]
    public void An_opaque_image_shrinks_to_the_same_pixels_as_the_alpha_weighted_path()
    {
        var rng = new Random(7);
        var pixels = new byte[97 * 211 * 4];
        rng.NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var weighted = Reopening.Shrink(new CapturedImage(97, 211, pixels), 13, 29);
        var opaque = Reopening.Shrink(new CapturedImage(97, 211, pixels) { Opaque = true }, 13, 29);
        Assert.Equal(weighted, opaque);
        Assert.All(Enumerable.Range(0, 13 * 29), i => Assert.Equal(255, opaque[i * 4 + 3]));
    }

    [Fact]
    public void A_stitched_result_20000_rows_tall_is_composed_and_placed_within_the_budget()
    {
        // Done to the result shown (docs/phase-7/scrolling-capture.md): composing the stitched rows once and drawing them,
        // shrunk, into the display's pixels is what runs here; the screen read and the first frame follow it.
        const int w = 2560, h = 1440, step = 600;
        var page = new SyntheticPage(w);
        var o = new Overlay(Header: 64, Footer: 48, Left: 220, Right: 16);
        var stitcher = new ScrollStitcher(w, h);
        for (int offset = 0; stitcher.Height < 20_000; offset += step)
            Assert.Equal(ScrollStatus.Accepted, stitcher.Add(Frames.Render(page, offset, h, o, 40000)).Status);

        var compose = System.Diagnostics.Stopwatch.StartNew();
        var image = stitcher.Compose();
        compose.Stop();
        Assert.True(image.Opaque);
        Assert.True(image.Height >= 20_000);

        var live = SelectionTests.Fake(null, new PixelRect(0, 0, 2560, 1440));
        var place = System.Diagnostics.Stopwatch.StartNew();
        var (freeze, rect, scale, _) = Reopening.Place(live, image, new PixelPoint(100, 100), 32);
        place.Stop();
        Assert.True(scale < 0.1);
        Assert.True(new PixelRect(0, 0, 2560, 1440).Contains(rect));
        Assert.NotNull(freeze);

        var weighted = System.Diagnostics.Stopwatch.StartNew();
        Reopening.Shrink(image with { Opaque = false }, rect.Width, rect.Height); // the alpha-weighted path, parallel
        weighted.Stop();
        if (Environment.GetEnvironmentVariable("CHIPPI_SCROLL_TIMINGS") is { Length: > 0 } report)
            File.AppendAllText(report, $"scroll result {image.Width}x{image.Height}: compose {compose.Elapsed.TotalMilliseconds:F0} ms, place (shrink to {rect.Width}x{rect.Height}) {place.Elapsed.TotalMilliseconds:F0} ms, alpha-weighted shrink {weighted.Elapsed.TotalMilliseconds:F0} ms{Environment.NewLine}");
        // Generous, for a shared CI runner: the old single-threaded shrink alone took about as long on a fast PC.
        Assert.True(compose.Elapsed + place.Elapsed < TimeSpan.FromMilliseconds(2500), $"compose {compose.ElapsedMilliseconds} ms + place {place.ElapsedMilliseconds} ms");
    }
}
