// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture.Tests;

/// <summary>
/// A page built like the Sandbox's long text window (tests/Chippi.Input.Tests/Sandbox/scrolling-fixture.ps1): lines 22 px
/// high, "Line NNNN lorem ipsum dolor ..." whose length cycles every 3 lines, and a left stripe of four 14 px cells spelling
/// the line number in base 8. Neighbouring lines differ only in the last stripe cell and one digit glyph, and the digits
/// share one base shape that differs by a handful of pixels, as real digits do.
/// </summary>
internal static class FixturePage
{
    public const int Line = 22;
    public const int Width = 560;
    private static readonly (int B, int G, int R)[] Palette =
    [
        (255, 255, 255), (60, 60, 230), (60, 200, 60), (230, 120, 40), (40, 200, 220), (200, 60, 200), (140, 140, 140), (30, 30, 30),
    ];

    private static bool Glyph(char c, int gx, int gy)
    {
        if (c == ' ')
            return false;
        if (c is >= '0' and <= '9')
        {
            bool px = SyntheticPage.H(99, (uint)gx, (uint)gy) % 3 != 0;
            int d = c - '0';
            for (int k = 0; k < 5; k++)
            {
                int fx = (int)(SyntheticPage.H((uint)d, (uint)k, 1) % 8), fy = (int)(SyntheticPage.H((uint)d, (uint)k, 2) % 12);
                if (fx == gx && fy == gy)
                    px = !px;
            }
            return px;
        }
        return SyntheticPage.H((uint)c, (uint)gx, (uint)gy) % 2 == 0;
    }

    public static string Text(int line) => "Line " + line.ToString("0000") + " " + string.Concat(Enumerable.Repeat("lorem ipsum dolor ", 1 + line % 3));

    public static void Row(int y, Span<byte> row, bool stripe = true)
    {
        row.Fill(255);
        if (y < 0)
            return;
        int line = y / Line, sub = y % Line;
        for (int cell = 0; stripe && cell < 4; cell++)
        {
            var (b, g, r) = Palette[line >> (3 * (3 - cell)) & 7];
            for (int x = cell * 14; x < cell * 14 + 14; x++)
            {
                row[x * 4] = (byte)b;
                row[x * 4 + 1] = (byte)g;
                row[x * 4 + 2] = (byte)r;
            }
        }
        if (sub is < 4 or >= 18)
            return;
        string text = Text(line);
        for (int i = 0; i < text.Length; i++)
        {
            int x0 = 62 + i * 10;
            if (x0 + 8 > Width)
                break;
            for (int gx = 0; gx < 8; gx++)
                if (Glyph(text[i], gx, sub - 4))
                {
                    int x = x0 + gx;
                    row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = 20;
                }
        }
    }

    /// <summary>The area at page offset <paramref name="top"/>; with a tear, rows from <paramref name="tearRow"/> down come from page offset <paramref name="tornTop"/> instead.</summary>
    public static byte[] Frame(int top, int h, int? tearRow = null, int tornTop = 0, bool stripe = true)
    {
        var f = new byte[Width * 4 * h];
        for (int y = 0; y < h; y++)
        {
            int src = tearRow is { } t && y >= t ? tornTop + y : top + y;
            Row(src, f.AsSpan(y * Width * 4, Width * 4), stripe);
        }
        return f;
    }
}

public sealed class ScrollFixtureTests
{
    private const int FH = 300;

    // Every row the stitcher has vouched for (all but its provisional tail) is already the page's.
    private static void AssertVouchedRowsRight(ScrollStitcher s, int firstTop, bool stripe, string what)
    {
        var img = s.Compose();
        var want = new byte[FixturePage.Width * 4];
        for (int y = 0; y < img.Height - s.ProvisionalRows; y++)
        {
            FixturePage.Row(firstTop + y, want, stripe);
            Assert.True(img.Pixels.AsSpan(y * img.Width * 4, img.Width * 4).SequenceEqual(want), $"{what}: row {y} of {img.Height}");
        }
    }

    private static void AssertMatchesPage(ScrollStitcher s, int firstTop, bool stripe = true)
    {
        var img = s.Compose();
        var want = new byte[FixturePage.Width * 4];
        for (int y = 0; y < img.Height; y++)
        {
            FixturePage.Row(firstTop + y, want, stripe);
            Assert.True(img.Pixels.AsSpan(y * img.Width * 4, img.Width * 4).SequenceEqual(want), $"row {y} of {img.Height} differs from the page");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Frames_scrolled_by_whole_lines_stitch_exactly(int lines)
    {
        foreach (int startLine in new[] { 0, 37, 291, 640 })
        {
            int top0 = startLine * FixturePage.Line;
            var s = new ScrollStitcher(FixturePage.Width, FH);
            s.Add(FixturePage.Frame(top0, FH));
            int top = top0;
            for (int i = 0; i < 12; i++)
            {
                top += lines * FixturePage.Line;
                var step = s.Add(FixturePage.Frame(top, FH));
                Assert.Equal(ScrollStatus.Accepted, step.Status);
                Assert.Equal(lines * FixturePage.Line, step.Shift);
            }
            AssertMatchesPage(s, top0);
        }
    }

    [Fact]
    public void Frames_scrolled_by_odd_pixel_amounts_stitch_exactly()
    {
        var s = new ScrollStitcher(FixturePage.Width, FH);
        int top0 = 291 * FixturePage.Line;
        s.Add(FixturePage.Frame(top0, FH));
        int top = top0;
        foreach (int d in new[] { 15, 44, 67, 120, 21, 100, 3, 131, 88, 45 })
        {
            top += d;
            var step = s.Add(FixturePage.Frame(top, FH));
            Assert.Equal(ScrollStatus.Accepted, step.Status);
        }
        AssertMatchesPage(s, top0);
    }

    // A tear: the area repainted in pieces, so a frame shows the new position above a row and the old one below it (or the
    // reverse). The overlap with the reference then disagrees in a band of rows; it is never to be appended.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    public void A_torn_frame_is_never_stitched_and_the_next_clean_frame_is(int lines, bool oldAbove)
    {
        int top0 = 291 * FixturePage.Line;
        int move = lines * FixturePage.Line;
        int[] tears = [1, 3, 9, 30, 57, 100, 101, 133, 150, 177, 200, 233, 260, 275, 290, 296];
        foreach (int t in tears)
        {
            var s = new ScrollStitcher(FixturePage.Width, FH);
            s.Add(FixturePage.Frame(top0, FH));
            int cur = top0;
            for (int i = 0; i < 2; i++)
            {
                cur += move;
                Assert.Equal(ScrollStatus.Accepted, s.Add(FixturePage.Frame(cur, FH)).Status);
            }
            int next = cur + move;
            byte[] torn = oldAbove
                ? FixturePage.Frame(cur, FH, t, next)   // old above the tear, new below
                : FixturePage.Frame(next, FH, t, cur);  // new above the tear, old below
            s.Add(torn);
            // A tear below what the overlap covers may be taken as provisional rows; the next clean frame replaces them.
            var cleanFrame = FixturePage.Frame(next, FH);
            s.Add(cleanFrame);
            var clean = s.Add(cleanFrame); // the picture holds still, as it does when the capture ends
            Assert.True(clean.Status is ScrollStatus.Accepted or ScrollStatus.NoMove, $"tear {t}: clean frame {clean.Status}");
            AssertMatchesPage(s, top0);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Two_torn_frames_in_a_row_then_a_clean_one_give_the_page(int lines, bool oldAbove)
    {
        int top0 = 291 * FixturePage.Line;
        int move = lines * FixturePage.Line;
        foreach (int t in new[] { 20, 99, 150, 211, 250, 281, 297, 299 })
        {
            var s = new ScrollStitcher(FixturePage.Width, FH);
            s.Add(FixturePage.Frame(top0, FH));
            int cur = top0;
            for (int i = 0; i < 2; i++)
            {
                cur += move;
                Assert.Equal(ScrollStatus.Accepted, s.Add(FixturePage.Frame(cur, FH)).Status);
            }
            int a = cur + move, b = a + move;
            // Two repaints in pieces, one after the other, then the settled picture.
            s.Add(oldAbove ? FixturePage.Frame(cur, FH, t, a) : FixturePage.Frame(a, FH, t, cur));
            s.Add(oldAbove ? FixturePage.Frame(a, FH, Math.Min(FH - 1, t + 7), b) : FixturePage.Frame(b, FH, Math.Min(FH - 1, t + 7), a));
            var cleanFrame = FixturePage.Frame(b, FH);
            s.Add(cleanFrame);
            var clean = s.Add(cleanFrame);
            Assert.True(clean.Status is ScrollStatus.Accepted or ScrollStatus.NoMove, $"tear {t}: clean frame {clean.Status}");
            AssertMatchesPage(s, top0);
        }
    }

    [Fact]
    public void A_tear_below_the_overlap_is_replaced_by_the_next_clean_frame()
    {
        // The top 250 rows are a clean scroll of 66; the rows below are another position: the overlap sees nothing wrong.
        int top0 = 291 * FixturePage.Line;
        var s = new ScrollStitcher(FixturePage.Width, FH);
        s.Add(FixturePage.Frame(top0, FH));
        Assert.Equal(ScrollStatus.Accepted, s.Add(FixturePage.Frame(top0 + 66, FH)).Status);
        int next = top0 + 66 + 66;
        var torn = FixturePage.Frame(next, FH, 250, next + 90);
        Assert.Equal(ScrollStatus.Accepted, s.Add(torn).Status);
        var settled = FixturePage.Frame(next + 66, FH);
        s.Add(settled);
        Assert.True(s.Add(settled).Status is ScrollStatus.Accepted or ScrollStatus.NoMove);
        AssertMatchesPage(s, top0);
    }

    [Fact]
    public void A_band_of_rows_still_showing_the_old_position_is_refused_not_averaged_in()
    {
        int top0 = 291 * FixturePage.Line;
        foreach (int lines in new[] { 2, 3 })
            foreach (int bandRows in new[] { 2, 5, 12, 22 })
            {
                var s = new ScrollStitcher(FixturePage.Width, FH);
                s.Add(FixturePage.Frame(top0, FH));
                int next = top0 + lines * FixturePage.Line;
                var f = FixturePage.Frame(next, FH);
                var old = FixturePage.Frame(top0, FH);
                int start = 100 + bandRows % 7;
                Buffer.BlockCopy(old, start * FixturePage.Width * 4, f, start * FixturePage.Width * 4, bandRows * FixturePage.Width * 4);
                var step = s.Add(f);
                Assert.NotEqual(ScrollStatus.Accepted, step.Status);
                Assert.Equal(ScrollStatus.Accepted, s.Add(FixturePage.Frame(next, FH)).Status);
                AssertMatchesPage(s, top0);
            }
    }

    // Without the stripe, lines three apart are the same but for one digit: the alias differs from the truth in a few pixels
    // per row. Whatever is accepted must be right; a clean frame at the true shift may be refused, never misread.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Without_the_stripe_a_whole_line_alias_is_never_accepted(int lines)
    {
        int top0 = 291 * FixturePage.Line;
        var s = new ScrollStitcher(FixturePage.Width, FH);
        s.Add(FixturePage.Frame(top0, FH, stripe: false));
        int top = top0;
        for (int i = 0; i < 10; i++)
        {
            top += lines * FixturePage.Line;
            var step = s.Add(FixturePage.Frame(top, FH, stripe: false));
            if (step.Status == ScrollStatus.Accepted)
                Assert.Equal(lines * FixturePage.Line, step.Shift);
        }
        AssertMatchesPage(s, top0, false);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void Scrolling_by_hand_with_repaints_in_pieces_never_adds_a_wrong_row(int seed, bool stripe)
    {
        var rng = new Random(seed);
        int top0 = 100 * FixturePage.Line;
        var s = new ScrollStitcher(FixturePage.Width, FH);
        s.Add(FixturePage.Frame(top0, FH, stripe: stripe));
        int shown;
        int accepted = 0;
        for (int i = 0; i < 150; i++)
        {
            shown = top0 + s.Height - FH; // where the reference is: the old rows of a torn frame are its rows in place
            int next = shown + FixturePage.Line * rng.Next(1, 7) + (rng.Next(3) == 0 ? rng.Next(0, 22) : 0);
            int kind = rng.Next(4);
            int t = rng.Next(1, FH); // anywhere, the last row included
            byte[] frame = kind switch
            {
                0 => FixturePage.Frame(next, FH, t, shown, stripe), // new above the tear
                1 => FixturePage.Frame(shown, FH, t, next, stripe), // old above the tear
                _ => FixturePage.Frame(next, FH, null, 0, stripe),
            };
            var step = s.Add(frame);
            AssertVouchedRowsRight(s, top0, stripe, $"seed {seed} step {i} torn {kind}/{t}");
            if (step.Status == ScrollStatus.Accepted)
                accepted++;
            if (kind < 2)
            {
                // The repaint finishes: the next frame is the clean one.
                if (s.Add(FixturePage.Frame(next, FH, null, 0, stripe)).Status == ScrollStatus.Accepted)
                    accepted++;
            }
        }
        // The sequence ends with a clean frame: whatever tail the last tears left is replaced.
        shown = top0 + s.Height - FH;
        var lastFrame = FixturePage.Frame(shown + 3 * FixturePage.Line, FH, null, 0, stripe);
        s.Add(lastFrame);
        var last = s.Add(lastFrame);
        Assert.True(!stripe || accepted > 60, $"only {accepted} accepted"); // a page that repeats may be refused, never misread
        if (!stripe && last.Status is not (ScrollStatus.Accepted or ScrollStatus.NoMove))
            return;
        AssertMatchesPage(s, top0, stripe);
    }

    [Fact]
    public void A_blinking_caret_in_one_row_does_not_refuse_the_frame()
    {
        int top0 = 291 * FixturePage.Line;
        var s = new ScrollStitcher(FixturePage.Width, FH);
        s.Add(FixturePage.Frame(top0, FH));
        var f = FixturePage.Frame(top0 + 3 * FixturePage.Line, FH);
        for (int x = 300; x < 304; x++)
            f[(140 * FixturePage.Width + x) * 4] = f[(140 * FixturePage.Width + x) * 4 + 1] = f[(140 * FixturePage.Width + x) * 4 + 2] = 0;
        Assert.Equal(ScrollStatus.Accepted, s.Add(f).Status);
    }
}
