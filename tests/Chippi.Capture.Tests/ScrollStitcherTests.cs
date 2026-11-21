// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;

namespace Chippi.Capture.Tests;

/// <summary>
/// A synthetic tall page: lines of random "words" with a coloured stripe in the margin that encodes the line number, drawn
/// row by row from a hash so a page of any height costs nothing to hold. <paramref name="repeat"/> makes the text repeat
/// every that many lines; <paramref name="stripe"/> off makes a fully repeating page.
/// </summary>
internal sealed class SyntheticPage(int width, int repeat = 0, bool stripe = true, int blankFrom = -1, int blankTo = -1)
{
    public const int LineHeight = 18;

    public int Width { get; } = width;

    internal static uint H(uint a, uint b, uint c)
    {
        uint h = a * 0x9E3779B1u ^ b * 0x85EBCA77u ^ c * 0xC2B2AE3Du;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        return h ^ (h >> 15);
    }

    private static void Set(Span<byte> row, int x, int b, int g, int r)
    {
        row[x * 4] = (byte)b;
        row[x * 4 + 1] = (byte)g;
        row[x * 4 + 2] = (byte)r;
        row[x * 4 + 3] = 255;
    }

    public void Row(int y, Span<byte> row)
    {
        row.Fill(255);
        if (y < 0 || (y >= blankFrom && y < blankTo))
            return;
        int line = y / LineHeight, sub = y % LineHeight;
        uint tl = (uint)(repeat > 0 ? line % repeat : line);
        if (stripe)
        {
            uint c = H((uint)line, 5, 0);
            if (sub is >= 1 and < 17)
                for (int x = 4; x < 12; x++)
                    Set(row, x, (int)(c & 0xFF), (int)(c >> 8 & 0xFF), (int)(c >> 16 & 0xFF));
        }
        if (sub is < 3 or >= 15)
            return;
        int n = 3 + (int)(H(tl, 1, 0) % 4);
        int cursor = 20 + (int)(H(tl, 2, 0) % 20);
        for (int k = 0; k < n; k++)
        {
            int w = 20 + (int)(H(tl, 3, (uint)k) % 50);
            if (cursor + w > Width - 20)
                break;
            for (int x = cursor; x < cursor + w; x++)
                if (H(tl, (uint)x, (uint)sub) % 4 != 0)
                {
                    int v = 20 + (int)(H(tl, (uint)x + 7, (uint)sub) % 80);
                    Set(row, x, v, v, v);
                }
            cursor += w + 8 + (int)(H(tl, 4, (uint)k) % 20);
        }
    }
}

/// <summary>What sits over the page in every frame: sticky bars, a fixed sidebar, a scrollbar.</summary>
internal sealed record Overlay(int Header = 0, int Footer = 0, int Left = 0, int Right = 0)
{
    public static readonly Overlay None = new();
    public const uint SidebarBg = 0xFFF5EBEB; // BGRA bytes 235,235,245 (B=EB G=EB R=F5)
}

internal static class Frames
{
    private static void Set(byte[] f, int w, int y, int x, int b, int g, int r)
    {
        int i = (y * w + x) * 4;
        f[i] = (byte)b;
        f[i + 1] = (byte)g;
        f[i + 2] = (byte)r;
        f[i + 3] = 255;
    }

    /// <summary>The area at page <paramref name="offset"/>, with the overlay drawn on top; <paramref name="total"/> sizes the scrollbar's thumb.</summary>
    public static byte[] Render(SyntheticPage page, int offset, int h, Overlay o, int total)
    {
        int w = page.Width;
        var f = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            page.Row(offset + y, f.AsSpan(y * w * 4, w * 4));
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < o.Left; x++)
            {
                bool ink = y % 24 is >= 6 and < 16 && x >= 10 && x < o.Left - 10 && SyntheticPage.H((uint)y, (uint)x, 3) % 3 == 0;
                if (ink)
                    Set(f, w, y, x, 60, 60, 70);
                else
                    Set(f, w, y, x, 0xEB, 0xEB, 0xF5);
            }
            for (int x = w - o.Right; x < w; x++)
                Set(f, w, y, x, 225, 225, 225);
        }
        if (o.Right > 0)
        {
            int thumb = Math.Max(12, (int)((long)h * h / total));
            int top = total > h ? (int)((long)offset * (h - thumb) / (total - h)) : 0;
            for (int y = Math.Max(0, top); y < Math.Min(h, top + thumb); y++)
                for (int x = w - o.Right; x < w; x++)
                    Set(f, w, y, x, 120, 120, 120);
        }
        for (int y = 0; y < o.Header; y++)
            for (int x = 0; x < w - o.Right; x++)
                Set(f, w, y, x, 60, 40, 30);
        for (int y = 0; y < o.Header; y++)
            for (int x = 10; x < w - 10; x++)
                if (y % 12 is >= 3 and < 9 && SyntheticPage.H((uint)y, (uint)x, 11) % 3 == 0)
                    Set(f, w, y, x, 220, 220, 220);
        for (int y = h - o.Footer; y < h; y++)
            for (int x = 0; x < w - o.Right; x++)
            {
                bool ink = (y - (h - o.Footer)) % 10 is >= 2 and < 7 && x >= 10 && x < w - 10 && SyntheticPage.H((uint)y, (uint)x, 13) % 3 == 0;
                if (ink)
                    Set(f, w, y, x, 230, 230, 230);
                else
                    Set(f, w, y, x, 40, 90, 40);
            }
        return f;
    }
}

public sealed class ScrollStitcherTests
{
    private const int W = 320;
    private const int FH = 400;

    /// <summary>
    /// Every row of the result against the source: content columns from the page, the sticky bars from the first frame, the
    /// fixed columns from the first frame for the first screen and their own background below it. Returns the first
    /// mismatch, or null.
    /// </summary>
    private static string? Check(ScrollStitcher s, SyntheticPage page, Overlay o, int firstOffset, byte[] first)
    {
        var img = s.Compose();
        int r = img.Height;
        if (img.Width != page.Width)
            return "width";
        var expect = new byte[page.Width * 4];
        for (int y = 0; y < r; y++)
        {
            for (int x = 0; x < page.Width; x++)
            {
                bool fixedCol = x < o.Left || x >= page.Width - o.Right;
                int footerRow = y - (r - o.Footer);
                (byte b, byte g, byte rr)? want;
                if (footerRow >= 0)
                    want = Px(first, page.Width, FH - o.Footer + footerRow, x);
                else if (y < o.Header)
                    want = Px(first, page.Width, y, x);
                else if (fixedCol)
                    want = y < FH - o.Footer ? Px(first, page.Width, y, x) : null;
                else
                {
                    page.Row(firstOffset + y, expect);
                    want = (expect[x * 4], expect[x * 4 + 1], expect[x * 4 + 2]);
                }
                if (want is null)
                {
                    // Below the first screen a fixed column is its own background.
                    var (eb, eg, er) = x < o.Left ? ((byte)0xEB, (byte)0xEB, (byte)0xF5) : ((byte)225, (byte)225, (byte)225);
                    want = (eb, eg, er);
                }
                int i = (y * img.Width + x) * 4;
                if (img.Pixels[i] != want.Value.b || img.Pixels[i + 1] != want.Value.g || img.Pixels[i + 2] != want.Value.rr)
                    return $"row {y} col {x} of {r}: got {img.Pixels[i]},{img.Pixels[i + 1]},{img.Pixels[i + 2]} want {want.Value.b},{want.Value.g},{want.Value.rr}";
            }
        }
        return null;
    }

    /// <summary>Sticky rows and the scrollbar exactly; a sidebar up to its last drawn column (its flat edge is left to the content).</summary>
    private static void AssertLayout(ScrollLayout want, ScrollLayout got)
    {
        Assert.Equal(want.Header, got.Header);
        Assert.Equal(want.Footer, got.Footer);
        Assert.Equal(want.Right, got.Right);
        Assert.InRange(got.Left, Math.Max(0, want.Left - 12), want.Left);
    }

    private static (byte B, byte G, byte R) Px(byte[] f, int w, int y, int x) => (f[(y * w + x) * 4], f[(y * w + x) * 4 + 1], f[(y * w + x) * 4 + 2]);

    private static ScrollStep Feed(ScrollStitcher s, SyntheticPage page, Overlay o, int offset, int total, int? predicted = null)
        => s.Add(Frames.Render(page, offset, FH, o, total), predicted);

    private static void Walk(ScrollStitcher s, SyntheticPage page, Overlay o, IEnumerable<int> offsets, int total, bool predict = false)
    {
        int? prev = null;
        foreach (int off in offsets)
        {
            var step = Feed(s, page, o, off, total, predict && prev is { } p ? off - p : null);
            Assert.Equal(ScrollStatus.Accepted, step.Status);
            prev = off;
        }
    }

    [Fact]
    public void A_plain_page_stitches_row_for_row_and_the_first_frame_is_the_top()
    {
        var page = new SyntheticPage(W);
        var s = new ScrollStitcher(W, FH);
        Assert.Equal(0, s.Height);
        Walk(s, page, Overlay.None, Enumerable.Range(0, 8).Select(i => i * 150), 20000);

        Assert.Equal(7 * 150 + FH, s.Height);
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
        // One seam per appended strip, each at the bottom of what was there.
        Assert.Equal(Enumerable.Range(0, 7).Select(i => FH + i * 150), s.Seams);
        Assert.Equal(default, s.Layout);
        Assert.Equal(ScrollStatus.Accepted, s.Status);
    }

    [Fact]
    public void The_first_frame_need_not_be_the_pages_top()
    {
        var page = new SyntheticPage(W);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, Overlay.None, [1000, 1180, 1400], 20000);
        Assert.Equal(400 + 400, s.Height);
        Assert.Null(Check(s, page, Overlay.None, 1000, Frames.Render(page, 1000, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void A_sticky_header_and_footer_go_in_once()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 40, Footer: 30);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, Enumerable.Range(0, 9).Select(i => i * 120), 20000);

        Assert.Equal(new ScrollLayout(40, 30, 0, 0), s.Layout);
        Assert.Equal(8 * 120 + FH, s.Height);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 20000)));
    }

    [Fact]
    public void A_fixed_sidebar_and_a_scrollbar_are_found_and_filled_below_the_first_screen()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 36, Footer: 28, Left: 60, Right: 12);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, Enumerable.Range(0, 9).Select(i => i * 140), 2500);

        AssertLayout(new ScrollLayout(36, 28, 60, 12), s.Layout);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 2500)));
    }

    [Fact]
    public void A_sidebar_alone_is_found_without_any_sticky_rows()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Left: 70);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, [0, 160, 320, 480], 9000);
        AssertLayout(new ScrollLayout(0, 0, 70, 0), s.Layout);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 9000)));
    }

    [Fact]
    public void Repeating_text_with_a_numbered_stripe_stitches_exactly()
    {
        // The same five lines over and over, told apart only by the stripe that encodes the line number.
        var page = new SyntheticPage(W, repeat: 5);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, Overlay.None, Enumerable.Range(0, 8).Select(i => i * 130), 20000);
        Assert.Equal(7 * 130 + FH, s.Height);
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void A_fully_repeating_page_is_refused_never_stitched_wrong()
    {
        var page = new SyntheticPage(W, repeat: 1, stripe: false);
        var s = new ScrollStitcher(W, FH);
        Feed(s, page, Overlay.None, 0, 20000);
        // 100 is not a multiple of the 18-row period, so every period-shifted candidate fits as well as the true one.
        var step = Feed(s, page, Overlay.None, 100, 20000);
        Assert.NotEqual(ScrollStatus.Accepted, step.Status);
        Assert.Equal(FH, s.Height);
        Assert.True(s.Lost);
    }

    [Fact]
    public void A_predicted_shift_resolves_a_repeating_page_the_hand_could_not()
    {
        var page = new SyntheticPage(W, repeat: 1, stripe: false);
        var s = new ScrollStitcher(W, FH);
        Feed(s, page, Overlay.None, 0, 20000);
        var step = Feed(s, page, Overlay.None, 100, 20000, predicted: 100);
        Assert.Equal(ScrollStatus.Accepted, step.Status);
        Assert.Equal(100, step.Shift);
        Assert.Equal(FH + 100, s.Height);
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void A_blank_stretch_is_not_crossed_by_hand_and_the_stitched_part_stays_right()
    {
        // Text to row 1100, then 1,500 blank rows, taller than any overlap.
        var page = new SyntheticPage(W, blankFrom: 1100, blankTo: 2600);
        var o = Overlay.None;
        var s = new ScrollStitcher(W, FH);
        var statuses = new List<ScrollStatus>();
        foreach (int off in Enumerable.Range(0, 30).Select(i => i * 100))
            statuses.Add(Feed(s, page, o, off, 20000).Status);

        Assert.Contains(ScrollStatus.Accepted, statuses);
        Assert.True(s.Lost);
        Assert.True(s.Height < 29 * 100 + FH);
        // What was kept is exactly the page's rows from the top: nothing repeated, nothing dropped.
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 20000)));
        Assert.DoesNotContain(ScrollStatus.NoMove, statuses.Skip(1));
    }

    [Fact]
    public void A_blank_stretch_is_crossed_on_the_predicted_shift()
    {
        var page = new SyntheticPage(W, blankFrom: 1100, blankTo: 2600);
        var o = Overlay.None;
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, Enumerable.Range(0, 30).Select(i => i * 100), 20000, predict: true);
        Assert.Equal(29 * 100 + FH, s.Height);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 20000)));
    }

    [Fact]
    public void A_torn_frame_caught_mid_glide_is_refused_and_the_next_clean_one_stitches()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 30, Footer: 20);
        var s = new ScrollStitcher(W, FH);
        Feed(s, page, o, 0, 20000);
        Feed(s, page, o, 120, 20000);

        // The compositor presented the top 200 rows at the old offset and the rest at the new one.
        var a = Frames.Render(page, 240, FH, o, 20000);
        var b = Frames.Render(page, 300, FH, o, 20000);
        var torn = (byte[])a.Clone();
        Buffer.BlockCopy(b, 200 * W * 4, torn, 200 * W * 4, (FH - 200) * W * 4);
        var step = s.Add(torn);
        // The tear is below what the earlier frame verified, so the frame may be taken as provisional; the next clean one replaces it.


        // A 50/50 blend of two offsets is no frame either.
        var blend = new byte[a.Length];
        for (int i = 0; i < blend.Length; i++)
            blend[i] = (byte)((a[i] + b[i]) / 2);
        s.Add(blend);

        Assert.Equal(ScrollStatus.Accepted, Feed(s, page, o, 300, 20000).Status);
        Assert.Equal(300 + FH, s.Height);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 20000)));
    }

    [Fact]
    public void A_held_torn_frame_is_flagged_and_a_clean_frame_with_a_prediction_still_stitches()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 30, Footer: 20);
        var s = new ScrollStitcher(W, FH);
        Feed(s, page, o, 0, 20000);
        Feed(s, page, o, 120, 20000);
        Assert.False(s.HasHeld);

        var a = Frames.Render(page, 240, FH, o, 20000);
        var b = Frames.Render(page, 300, FH, o, 20000);
        var torn = (byte[])a.Clone();
        Buffer.BlockCopy(b, 200 * W * 4, torn, 200 * W * 4, (FH - 200) * W * 4);
        var step = s.Add(torn, 120);
        Assert.Equal(ScrollStatus.Ambiguous, step.Status);
        Assert.True(s.HasHeld); // held, waiting for the next frame: a caller must not feed the same frame again

        // The clean frame, with a prediction measured from the reference before the torn one: it still lands on the page.
        var clean = s.Add(b, 180);
        Assert.NotEqual(ScrollStatus.Gap, clean.Status);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 20000)));
    }

    [Fact]
    public void Scrolling_up_adds_nothing_and_scrolling_down_again_catches_up()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 30, Footer: 20);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, [0, 150, 300], 20000);
        int height = s.Height;

        var up = Feed(s, page, o, 180, 20000);
        Assert.Equal(ScrollStatus.NoMove, up.Status);
        Assert.Equal(-120, up.Shift);
        Assert.Equal(height, s.Height);
        Assert.Equal(300 - 120, s.ViewportTop);

        // Still above where it was: nothing, and the reference hasn't moved.
        Assert.Equal(ScrollStatus.NoMove, Feed(s, page, o, 250, 20000).Status);
        Assert.Equal(height, s.Height);

        var down = Feed(s, page, o, 420, 20000);
        Assert.Equal(ScrollStatus.Accepted, down.Status);
        Assert.Equal(120, down.Shift);
        Assert.Equal(420 + FH, s.Height);
        Assert.Equal(420, s.ViewportTop);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 20000)));
    }

    [Fact]
    public void A_fast_scroll_that_leaves_a_gap_is_refused_and_scrolling_back_catches_up()
    {
        var page = new SyntheticPage(W);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, Overlay.None, [0, 200], 20000);

        var gap = Feed(s, page, Overlay.None, 900, 20000); // 700 rows: no overlap with a 400-row frame
        Assert.Equal(ScrollStatus.Gap, gap.Status);
        Assert.True(s.Lost);
        Assert.Equal(200 + FH, s.Height);
        Assert.Equal(s.Height, s.GapRow);
        // Done now would keep exactly what was stitched.
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));

        // Back until the area overlaps the last frame used again: the gap clears.
        var back = Feed(s, page, Overlay.None, 480, 20000);
        Assert.Equal(ScrollStatus.Accepted, back.Status);
        Assert.False(s.Lost);
        Assert.Equal(480 + FH, s.Height);
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void A_frame_identical_to_the_last_adds_nothing()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 30, Footer: 20);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, [0, 150], 20000);
        var frame = Frames.Render(page, 150, FH, o, 20000);
        var step = s.Add(frame);
        Assert.Equal(ScrollStatus.NoMove, step.Status);
        Assert.Equal(0, step.Shift);
        Assert.Equal(150 + FH, s.Height);

        // A blinking caret is still no movement.
        var caret = (byte[])frame.Clone();
        for (int y = 200; y < 214; y++)
            for (int x = 100; x < 102; x++)
                caret[(y * W + x) * 4] ^= 0x80;
        Assert.Equal(ScrollStatus.NoMove, s.Add(caret).Status);
        Assert.Equal(150 + FH, s.Height);
    }

    [Fact]
    public void A_page_that_grows_while_scrolled_keeps_stitching_through_the_scrollbar_resizing()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 30, Footer: 20, Right: 12);
        var s = new ScrollStitcher(W, FH);
        int[] offsets = [0, 130, 260, 390, 520, 650, 780, 910];
        for (int i = 0; i < offsets.Length; i++)
        {
            // The page was 3,000 rows and loads 4,000 more at the fourth frame: the thumb jumps.
            int total = i < 4 ? 3000 : 7000;
            Assert.Equal(ScrollStatus.Accepted, Feed(s, page, o, offsets[i], total).Status);
        }
        Assert.Equal(910 + FH, s.Height);
        Assert.Null(Check(s, page, o, 0, Frames.Render(page, 0, FH, o, 3000)));
    }

    [Fact]
    public void The_height_limit_keeps_exactly_what_fits_and_ignores_the_rest()
    {
        var page = new SyntheticPage(W);
        var s = new ScrollStitcher(W, FH, new ScrollLimits(MaxHeight: 1000));
        var statuses = new List<ScrollStatus>();
        foreach (int off in Enumerable.Range(0, 8).Select(i => i * 200))
            statuses.Add(Feed(s, page, Overlay.None, off, 20000).Status);

        Assert.Equal(1000, s.Height);
        Assert.True(s.LimitReached);
        Assert.Equal(ScrollStatus.Limit, statuses[^1]);
        Assert.Equal(ScrollStatus.Limit, statuses[3]); // 400 + 3 × 200 would be 1,000: the fourth strip fits, the fifth is cut
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void The_megapixel_limit_counts_the_width()
    {
        var page = new SyntheticPage(W);
        var s = new ScrollStitcher(W, FH, new ScrollLimits(MaxHeight: 30_000, MaxPixels: W * 900L));
        foreach (int off in Enumerable.Range(0, 8).Select(i => i * 200))
            Feed(s, page, Overlay.None, off, 20000);
        Assert.Equal(900, s.Height);
        Assert.True(s.LimitReached);
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void Frames_with_a_row_pitch_stitch_like_tight_ones()
    {
        var page = new SyntheticPage(W);
        var s = new ScrollStitcher(W, FH);
        foreach (int off in new[] { 0, 170, 340 })
        {
            var tight = Frames.Render(page, off, FH, Overlay.None, 20000);
            int stride = W * 4 + 64;
            var padded = new byte[stride * FH];
            for (int y = 0; y < FH; y++)
                Buffer.BlockCopy(tight, y * W * 4, padded, y * stride, W * 4);
            Assert.Equal(ScrollStatus.Accepted, s.AddRows(padded, stride).Status);
        }
        Assert.Equal(340 + FH, s.Height);
        Assert.Null(Check(s, page, Overlay.None, 0, Frames.Render(page, 0, FH, Overlay.None, 20000)));
    }

    [Fact]
    public void Compose_in_bands_matches_whole_and_the_preview_is_a_downscale_of_it()
    {
        var page = new SyntheticPage(W);
        var o = new Overlay(Header: 30, Footer: 20);
        var s = new ScrollStitcher(W, FH);
        Walk(s, page, o, Enumerable.Range(0, 6).Select(i => i * 140), 20000);
        var whole = s.Compose();

        var banded = new byte[whole.Pixels.Length];
        for (int top = 0; top < whole.Height; top += 97)
        {
            int rows = Math.Min(97, whole.Height - top);
            s.CopyRows(top, rows, banded.AsSpan(top * W * 4));
        }
        Assert.Equal(whole.Pixels, banded);

        var preview = s.ComposePreview(96);
        Assert.Equal(96, preview.Width);
        Assert.Equal((int)((long)whole.Height * 96 / W), preview.Height);
        // The mean gray over the preview tracks the full picture's.
        double Mean(CapturedImage im)
        {
            long sum = 0;
            for (int i = 0; i < im.Pixels.Length; i += 4)
                sum += im.Pixels[i] + im.Pixels[i + 1] + im.Pixels[i + 2];
            return sum / (3.0 * im.Pixels.Length / 4);
        }
        Assert.InRange(Mean(preview), Mean(whole) - 6, Mean(whole) + 6);
        Assert.Equal(0, new ScrollStitcher(W, FH).ComposePreview(96).Height);
    }

    [Fact]
    public void A_2560_by_1440_area_stitches_exactly()
    {
        const int w = 2560, h = 1440;
        var page = new SyntheticPage(w);
        var o = new Overlay(Header: 64, Footer: 48, Left: 220, Right: 16);
        var s = new ScrollStitcher(w, h);
        int[] offsets = [0, 600, 1200, 1800];
        foreach (int off in offsets)
            Assert.Equal(ScrollStatus.Accepted, s.Add(Frames.Render(page, off, h, o, 12000)).Status);
        AssertLayout(new ScrollLayout(64, 48, 220, 16), s.Layout);
        Assert.Equal(1800 + h, s.Height);
        var first = Frames.Render(page, 0, h, o, 12000);
        var img = s.Compose();
        var expect = new byte[w * 4];
        for (int y = 0; y < img.Height; y += 7)
        {
            page.Row(y, expect);
            int footerRow = y - (img.Height - 48);
            for (int x = 220; x < w - 16; x += 3)
            {
                int i = (y * w + x) * 4;
                if (footerRow >= 0 || y < 64)
                    continue;
                Assert.True(img.Pixels[i] == expect[x * 4] && img.Pixels[i + 1] == expect[x * 4 + 1], $"row {y} col {x}");
            }
        }
        Assert.Equal(first.AsSpan(0, 64 * w * 4).ToArray(), img.Pixels.AsSpan(0, 64 * w * 4).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void Any_scroll_sequence_stitches_row_for_row_or_is_refused(int seed) => RandomSequence(seed);

    [Fact]
    public void More_random_sequences()
    {
        // A wider sweep when CHIPPI_SCROLL_SOAK names how many seeds.
        int n = int.TryParse(Environment.GetEnvironmentVariable("CHIPPI_SCROLL_SOAK"), out int v) ? v : 40;
        for (int seed = 21; seed < 21 + n; seed++)
            RandomSequence(seed);
    }

    private static void RandomSequence(int seed)
    {
        // Random bars, sidebar, page kind, steps (up, down, jumps past the area) and predictions: whatever the stitcher
        // accepts is exactly the source, and the height only ever grows by what a frame showed.
        var rng = new Random(seed);
        var o = new Overlay(
            Header: rng.Next(3) == 0 ? 0 : rng.Next(10, 50),
            Footer: rng.Next(3) == 0 ? 0 : rng.Next(10, 40),
            Left: rng.Next(3) == 0 ? 0 : 60);
        var page = rng.Next(4) switch
        {
            0 => new SyntheticPage(W, repeat: 5),
            1 => new SyntheticPage(W, blankFrom: 1500, blankTo: 2300),
            _ => new SyntheticPage(W),
        };
        var s = new ScrollStitcher(W, FH);
        int offset = 0, previous = 0, accepted = 0, refOffset = 0;
        byte[]? first = null;
        int firstOffset = -1;
        for (int i = 0; i < 24; i++)
        {
            int step = rng.Next(6) switch { 0 => -rng.Next(10, 300), 1 => rng.Next(450, 900), _ => rng.Next(40, 260) };
            offset = Math.Max(0, offset + step);
            var frame = Frames.Render(page, offset, FH, o, 20000);
            if (first is null)
            {
                first = frame;
                firstOffset = offset;
            }
            int? predicted = rng.Next(3) == 0 && i > 0 ? offset - refOffset : null;
            var result = s.Add(frame, predicted);
            if (result.Status is ScrollStatus.Accepted or ScrollStatus.Limit)
                refOffset = offset;
            if (result.Status == ScrollStatus.Accepted)
                accepted++;
            Assert.True(s.Height >= previous, $"seed {seed} frame {i}: the result shrank");
            Assert.True(s.Height - previous <= FH, $"seed {seed} frame {i}: grew by more than a frame");
            previous = s.Height;
            Assert.Equal(ScrollStatus.Limit == result.Status ? s.Height : result.Height, s.Height);
        }
        Assert.True(accepted >= 1);
        Assert.Null(Check(s, page, o, firstOffset, first!));
    }

    [Fact]
    public void Timing_report()
    {
        string? report = Environment.GetEnvironmentVariable("CHIPPI_SCROLL_TIMINGS");
        if (string.IsNullOrEmpty(report))
            return;
        const int w = 2560, h = 1440, step = 600;
        var page = new SyntheticPage(w);
        var o = new Overlay(Header: 64, Footer: 48, Left: 220, Right: 16);
        var s = new ScrollStitcher(w, h);
        var times = new List<double>();
        int offset = 0;
        while (s.Height < 20_000)
        {
            var frame = Frames.Render(page, offset, h, o, 40000);
            var sw = Stopwatch.StartNew();
            var st = s.Add(frame);
            sw.Stop();
            Assert.Equal(ScrollStatus.Accepted, st.Status);
            times.Add(sw.Elapsed.TotalMilliseconds);
            offset += step;
        }
        var previewWatch = Stopwatch.StartNew();
        var preview = s.ComposePreview(144);
        previewWatch.Stop();
        var composeWatch = Stopwatch.StartNew();
        var image = s.Compose();
        composeWatch.Stop();
        var sorted = times.Skip(1).OrderBy(t => t).ToList(); // the first frame is a copy
        File.AppendAllText(report,
            $"scroll stitcher {w}x{h}, step {step}: frames {times.Count}, first {times[0]:F1} ms, stitch per frame median {sorted[sorted.Count / 2]:F1} ms, max {sorted[^1]:F1} ms{Environment.NewLine}" +
            $"scroll stitcher result {image.Width}x{image.Height}: compose {composeWatch.Elapsed.TotalMilliseconds:F0} ms, preview {preview.Width}x{preview.Height} in {previewWatch.Elapsed.TotalMilliseconds:F1} ms{Environment.NewLine}");
    }
}
