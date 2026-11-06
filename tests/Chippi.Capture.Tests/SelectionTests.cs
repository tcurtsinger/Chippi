// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture.Tests;

/// <summary>The selector's geometry over fake displays (docs/phase-7/capture-layer.md, Modes and After release).</summary>
public sealed class SelectionTests
{
    // Travis's layout, with the 1920 display left of the main one and lower, so there are negative origins and gaps.
    private static readonly PixelRect Main = new(0, 0, 3440, 1440);
    private static readonly PixelRect Left = new(-1920, 360, 1920, 1080);
    private static readonly PixelRect[] Displays = [Main, Left];

    /// <summary>Each display's pixels encode where they are: B = x mod 256, G = y mod 256, R = 1 for the main display, 2 for the left one.</summary>
    internal static Freeze Fake(IReadOnlyList<FrozenWindow>? windows = null, params PixelRect[] displays)
    {
        if (displays.Length == 0)
            displays = Displays;
        var screens = displays.Select((d, n) =>
        {
            var pixels = new byte[d.Area * 4];
            for (int y = 0; y < d.Height; y++)
                for (int x = 0; x < d.Width; x++)
                {
                    int i = (y * d.Width + x) * 4;
                    pixels[i] = (byte)(d.X + x);
                    pixels[i + 1] = (byte)(d.Y + y);
                    pixels[i + 2] = (byte)(n + 1);
                    pixels[i + 3] = 255;
                }
            return new CapturedScreen($@"\\?\DISPLAY#{n}", n + 1, d, n == 0 ? 96 : 144, pixels);
        }).ToArray();
        return new Freeze(screens, windows ?? [], DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void A_drag_spans_both_corner_pixels_whichever_way_it_went()
    {
        Assert.Equal(new PixelRect(10, 20, 91, 31), PixelRect.Spanning(new(100, 50), new(10, 20)));
        Assert.Equal(new PixelRect(-5, -5, 1, 1), PixelRect.Spanning(new(-5, -5), new(-5, -5)));
    }

    [Fact]
    public void A_click_is_under_the_drag_distance_on_both_axes()
    {
        Assert.False(SelectionMath.IsDrag(new(10, 10), new(13, 13), 4, 4));
        Assert.True(SelectionMath.IsDrag(new(10, 10), new(14, 10), 4, 4));
        Assert.True(SelectionMath.IsDrag(new(10, 10), new(10, 6), 4, 4));
    }

    [Fact]
    public void A_region_across_two_displays_cuts_each_displays_own_pixels_and_the_gap_is_transparent()
    {
        var freeze = Fake();
        var rect = PixelRect.FromEdges(-10, 350, 10, 370); // 10 px of the left display (from y 360), 20 px of the main
        var image = freeze.Cut(rect);
        Assert.Equal((20, 20), (image.Width, image.Height));

        uint At(int x, int y)
        {
            int i = ((y - rect.Y) * rect.Width + x - rect.X) * 4;
            return BitConverter.ToUInt32(image.Pixels, i);
        }
        Assert.Equal(0u, At(-10, 350)); // left display starts at y 360: a gap
        static uint Pixel(int display, int x, int y) => 0xFF00_0000u | (uint)display << 16 | (uint)(y & 0xFF) << 8 | (uint)(x & 0xFF);
        Assert.Equal(Pixel(2, -5, 365), At(-5, 365)); // left display's own
        Assert.Equal(Pixel(1, 5, 355), At(5, 355)); // main display's own
        Assert.Equal(freeze.PixelAt(new(-5, 365)), At(-5, 365));
    }

    [Fact]
    public void A_window_is_the_frontmost_frame_under_the_pointer_clipped_to_the_displays()
    {
        var back = new FrozenWindow(1, "Back", @"C:\back.exe", new PixelRect(0, 0, 800, 600));
        var front = new FrozenWindow(2, "Front", @"C:\front.exe", new PixelRect(500, 400, 800, 600));
        var offEdge = new FrozenWindow(3, "Edge", @"C:\edge.exe", new PixelRect(3000, 1300, 800, 600));
        var freeze = Fake([front, back, offEdge]);
        Assert.Same(front, freeze.WindowAt(new(600, 500)));
        Assert.Same(back, freeze.WindowAt(new(100, 100)));
        Assert.Null(freeze.WindowAt(new(2000, 100)));
        Assert.Equal(PixelRect.FromEdges(3000, 1300, 3440, 1440), SelectionMath.WindowRect(Displays, offEdge));
    }

    [Fact]
    public void Ctrl_A_and_Screen_take_the_display_under_the_pointer_or_the_nearest_one_from_a_gap()
    {
        Assert.Equal(Left, SelectionMath.DisplayAt(Displays, new(-100, 400)));
        Assert.Equal(Main, SelectionMath.DisplayAt(Displays, new(100, 100)));
        Assert.Equal(Left, SelectionMath.DisplayAt(Displays, new(-100, 300))); // in the gap above the left display, nearer it
        Assert.Equal(Main, SelectionMath.DisplayAt(Displays, new(-100, 100))); // in the gap, nearer the main one
        Assert.Equal(new PixelPoint(0, 100), SelectionMath.OnDisplays(Displays, new(-1, 100)));
        Assert.Equal(new PixelPoint(-50, 360), SelectionMath.OnDisplays(Displays, new(-50, 340)));
        Assert.Equal(PixelRect.FromEdges(-1920, 0, 3440, 1440), SelectionMath.Around(Displays));
    }

    [Fact]
    public void Arrows_move_a_pixel_and_Shift_arrows_resize_from_the_right_and_bottom_stopping_at_the_edges()
    {
        var r = new PixelRect(3400, 1400, 40, 40); // at the main display's bottom right corner
        Assert.Equal(r, SelectionMath.Move(Displays, r, 1, 1));
        Assert.Equal(r with { X = 3399 }, SelectionMath.Move(Displays, r, -1, 0));
        Assert.Equal(r, SelectionMath.Grow(Displays, r, 1, 1));
        Assert.Equal(r with { Width = 39 }, SelectionMath.Grow(Displays, r, -1, 0));
        Assert.Equal(1, SelectionMath.Grow(Displays, r with { Width = 1 }, -1, 0).Width);
        var far = new PixelRect(-1920, 0, 10, 10);
        Assert.Equal(far, SelectionMath.Move(Displays, far, -1, -1));
    }

    [Fact]
    public void Handles_resize_the_held_edges_flip_past_the_opposite_ones_and_the_inside_moves_it()
    {
        var r = new PixelRect(100, 100, 200, 100);
        Assert.Equal(SelectionHandle.TopLeft, SelectionMath.HandleAt(r, new(102, 98), 4));
        Assert.Equal(SelectionHandle.Right, SelectionMath.HandleAt(r, new(299, 150), 4));
        Assert.Equal(SelectionHandle.Inside, SelectionMath.HandleAt(r, new(200, 120), 4));
        Assert.Equal(SelectionHandle.None, SelectionMath.HandleAt(r, new(50, 50), 4));

        Assert.Equal(PixelRect.FromEdges(100, 100, 350, 200), SelectionMath.Drag(Displays, r, SelectionHandle.Right, 50, 0));
        Assert.Equal(PixelRect.FromEdges(300, 100, 350, 200), SelectionMath.Drag(Displays, r, SelectionHandle.Left, 250, 0)); // flipped
        Assert.Equal(r.Offset(10, -5), SelectionMath.Drag(Displays, r, SelectionHandle.Inside, 10, -5));
        Assert.Equal(PixelRect.FromEdges(100, 0, 300, 200), SelectionMath.Drag(Displays, r, SelectionHandle.Top, 0, -500)); // stops at the top
    }

    [Fact]
    public void Last_region_is_kept_on_screen_cut_to_the_part_still_there_or_dropped()
    {
        var last = new PixelRect(-500, 500, 1000, 200);
        Assert.Equal(last, SelectionMath.LastRegion(Displays, last));
        Assert.Null(SelectionMath.LastRegion(Displays, null));
        // The left display was unplugged: the part on the main display stays selected.
        Assert.Equal(new PixelRect(0, 500, 500, 200), SelectionMath.LastRegion([Main], last));
        Assert.Null(SelectionMath.LastRegion([Main], new PixelRect(-500, 500, 400, 200)));
        // Across the gap above the left display: only the parts on a display count.
        Assert.False(SelectionMath.OnScreen(Displays, new PixelRect(-10, 300, 20, 100)));
    }

    [Fact]
    public void Freeform_closes_the_outline_and_masks_the_pixels_outside_it()
    {
        // A right triangle: (0,0) (9,0) (0,9).
        var closed = Freeform.Close([new(0, 0), new(9, 0), new(0, 9)]);
        Assert.NotNull(closed);
        var (bounds, mask) = closed.Value;
        Assert.Equal(new PixelRect(0, 0, 10, 10), bounds);
        Assert.True(mask.Contains(new(1, 1)));
        Assert.False(mask.Contains(new(8, 8)));
        Assert.Null(Freeform.Close([new(0, 0), new(5, 5)]));
        Assert.Null(Freeform.Close([new(0, 0), new(5, 5), new(10, 10)])); // a line encloses nothing

        var image = Fake().Cut(bounds, mask);
        Assert.Equal(255, image.Pixels[(1 * 10 + 1) * 4 + 3]);
        Assert.Equal(0, image.Pixels[(8 * 10 + 8) * 4 + 3]);
    }

    [Fact]
    public void A_selection_lists_the_displays_it_touches()
    {
        var freeze = Fake();
        var both = new Selection(PixelRect.FromEdges(-10, 400, 10, 410), freeze, CaptureMode.Region, null, null, false, freeze.Screens[0]);
        Assert.Equal([@"\\?\DISPLAY#0", @"\\?\DISPLAY#1"], both.Screens.Select(s => s.DisplayId));
        Assert.Equal([144], new Selection(new PixelRect(-100, 400, 10, 10), freeze, CaptureMode.Region, null, null, false, freeze.Screens[1]).Screens.Select(s => s.Dpi));
    }

    [Fact]
    public void Every_read_pixel_is_opaque()
    {
        var pixels = new byte[4 * 37];
        ScreenReader.Opaque(pixels);
        Assert.All(Enumerable.Range(0, 37), i => Assert.Equal(255, pixels[i * 4 + 3]));
        Assert.All(Enumerable.Range(0, 37), i => Assert.Equal(0, pixels[i * 4]));
    }
}
