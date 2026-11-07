// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture.Tests;

/// <summary>
/// Recording's record mode (screen-recording.md, Start; surfaces.md, One display per recording): the selection stays on
/// the display where the drag started. Main is 0,0 3440×1440; Left is -1920,360 1920×1080.
/// </summary>
public sealed class RecordModeTests
{
    private static readonly PixelRect Main = new(0, 0, 3440, 1440);
    private static readonly FrozenWindow Across = new(21, "Browser", @"C:\browser.exe", new PixelRect(-200, 400, 600, 300));

    private static Selector New(CaptureMode mode, PixelPoint at, bool oneDisplay = true) =>
        new(SelectionTests.Fake([Across]), mode, null, at, new PixelSize(4, 4), 6, oneDisplay);

    [Fact]
    public void A_drag_stops_at_the_edge_of_the_display_it_started_on()
    {
        var s = New(CaptureMode.Region, new(100, 600));
        s.Down(new(100, 600), false);
        s.Move(new(-300, 900));
        Assert.Equal(PixelRect.FromEdges(0, 600, 101, 901), s.Rect);
        s.Up(new(-500, 1000));
        Assert.Equal(PixelRect.FromEdges(0, 600, 101, 1001), s.Selection!.Rect);
        Assert.Equal(Main, s.Selection.Display.Bounds);
        Assert.Single(s.Selection.Screens);
    }

    [Fact]
    public void A_screenshot_drag_still_spans_displays()
    {
        var s = New(CaptureMode.Region, new(100, 600), oneDisplay: false);
        s.Down(new(100, 600), false);
        s.Up(new(-300, 900));
        Assert.Equal(2, s.Selection!.Screens.Count);
    }

    [Fact]
    public void A_window_over_two_displays_is_cut_to_the_one_clicked()
    {
        var s = New(CaptureMode.Window, new(-100, 500));
        s.Down(new(-100, 500), false);
        s.Up(new(-100, 500));
        Assert.Equal(PixelRect.FromEdges(-200, 400, 0, 700), s.Selection!.Rect);
        Assert.Same(Across, s.Selection.Window);

        s = New(CaptureMode.Window, new(200, 500));
        s.Down(new(200, 500), false);
        s.Up(new(200, 500));
        Assert.Equal(PixelRect.FromEdges(0, 400, 400, 700), s.Selection!.Rect);
    }

    [Fact]
    public void After_release_moves_and_resizes_stop_at_the_same_display()
    {
        var s = New(CaptureMode.Region, new(10, 10));
        s.Down(new(10, 10), false);
        s.Up(new(110, 110));
        var start = s.Selection!.Rect;
        // Drag the inside left, past the main display's edge.
        s.Down(new(50, 50), false);
        s.Move(new(-500, 50));
        s.Up(new(-500, 50));
        Assert.Equal(start with { X = 0 }, s.Selection!.Rect);
        // The left handle can't pull it across either.
        s.Down(new(0, 60), false);
        s.Up(new(-400, 60));
        Assert.Equal(0, s.Selection!.Rect.Left);
        for (int i = 0; i < 3; i++)
            s.Key(SelectorKey.Left, shift: false, ctrl: false);
        Assert.Equal(0, s.Selection!.Rect.Left);
        Assert.Equal(Main, s.Selection.Display.Bounds);
    }

    [Fact]
    public void Screen_mode_and_Ctrl_A_take_one_display()
    {
        var s = New(CaptureMode.Screen, new(-100, 500));
        Assert.Equal(new PixelRect(-1920, 360, 1920, 1080), s.Selection!.Rect);

        s = New(CaptureMode.Region, new(500, 500));
        s.Key(SelectorKey.A, shift: false, ctrl: true);
        Assert.Equal(Main, s.Selection!.Rect);
        s.Key(SelectorKey.Left, shift: false, ctrl: false);
        Assert.Equal(Main, s.Selection!.Rect);
    }
}
