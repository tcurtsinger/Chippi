// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture.Tests;

/// <summary>The selector's clicks, drags and keys over a fake frozen screen (capture-layer.md, Modes and Keys on the overlay).</summary>
public sealed class SelectorTests
{
    private static readonly PixelSize Drag = new(4, 4);
    private static readonly FrozenWindow Editor = new(11, "Notes", @"C:\notes.exe", new PixelRect(100, 100, 800, 600));
    private static readonly FrozenWindow Behind = new(12, "Mail", @"C:\mail.exe", new PixelRect(0, 0, 1200, 900));

    private static Selector New(CaptureMode mode, PixelPoint? at = null, PixelRect? last = null) =>
        new(SelectionTests.Fake([Editor, Behind]), mode, last, at ?? new PixelPoint(50, 50), Drag, 6);

    [Fact]
    public void Region_hover_highlights_the_frontmost_window_and_a_click_takes_it()
    {
        var s = New(CaptureMode.Region);
        Assert.Same(Behind, s.Hover);
        s.Move(new(200, 200));
        Assert.Same(Editor, s.Hover);
        s.Down(new(200, 200), ctrl: false);
        s.Move(new(202, 203)); // under the drag distance: still a click
        s.Up(new(202, 203));
        Assert.Equal(Editor.Frame, s.Selection!.Rect);
        Assert.Same(Editor, s.Selection.Window);
        Assert.Null(s.Hover);
    }

    [Fact]
    public void Region_drag_selects_the_rectangle_and_Ctrl_at_the_start_marks_the_fast_path()
    {
        var s = New(CaptureMode.Region);
        s.Down(new(200, 200), ctrl: true);
        s.Move(new(210, 200));
        Assert.True(s.IsDragging);
        Assert.Null(s.Hover);
        s.Up(new(400, 300));
        Assert.Equal(PixelRect.FromEdges(200, 200, 401, 301), s.Selection!.Rect);
        Assert.True(s.Selection.FastPath);
        Assert.Null(s.Selection.Window);
        Assert.False(s.IsDragging);
    }

    [Fact]
    public void A_drag_across_displays_releases_on_the_display_under_the_pointer()
    {
        var s = New(CaptureMode.Region, new(100, 400));
        s.Down(new(100, 400), false);
        s.Up(new(-100, 600));
        Assert.Equal(PixelRect.FromEdges(-100, 400, 101, 601), s.Selection!.Rect);
        Assert.Equal(@"\\?\DISPLAY#1", s.Selection.Display.DisplayId);
        Assert.Equal(2, s.Selection.Screens.Count);
    }

    [Fact]
    public void Window_mode_takes_a_click_and_ignores_a_drag()
    {
        var s = New(CaptureMode.Window);
        s.Down(new(200, 200), false);
        s.Up(new(400, 400));
        Assert.Null(s.Selection);
        s.Down(new(200, 200), false);
        s.Up(new(201, 201));
        Assert.Same(Editor, s.Selection!.Window);
    }

    [Fact]
    public void Screen_and_All_displays_select_at_once()
    {
        Assert.Equal(new PixelRect(-1920, 360, 1920, 1080), New(CaptureMode.Screen, new(-5, 500)).Selection!.Rect);
        var all = New(CaptureMode.AllDisplays);
        Assert.Equal(PixelRect.FromEdges(-1920, 0, 3440, 1440), all.Selection!.Rect);
        Assert.Equal(@"\\?\DISPLAY#0", all.Selection.Display.DisplayId);
    }

    [Fact]
    public void Last_region_selects_it_again_or_starts_Region_without_one()
    {
        var last = new PixelRect(10, 20, 300, 200);
        Assert.Equal(last, New(CaptureMode.LastRegion, last: last).Selection!.Rect);
        var none = New(CaptureMode.LastRegion);
        Assert.Equal(CaptureMode.Region, none.Mode);
        Assert.Null(none.Selection);
    }

    [Fact]
    public void Freeform_closes_on_release_with_its_mask()
    {
        var s = New(CaptureMode.Freeform, new(100, 100));
        s.Down(new(100, 100), false);
        foreach (var p in new PixelPoint[] { new(200, 100), new(200, 200), new(100, 200) })
            s.Move(p);
        s.Up(new(100, 200));
        Assert.NotNull(s.Selection!.Mask);
        Assert.Equal(new PixelRect(100, 100, 101, 101), s.Selection.Rect);
        Assert.Equal(SelectionHandle.None, s.HandleUnderPointer); // an outline keeps its shape
    }

    [Fact]
    public void Keys_while_choosing_Esc_cancels_Enter_takes_the_hovered_window_arrows_aim_and_the_rest_are_ignored()
    {
        var s = New(CaptureMode.Region, new(200, 200));
        Assert.Equal(SelectorOutcome.None, s.Key(SelectorKey.Right, false, false));
        Assert.Equal(new PixelPoint(201, 200), s.Pointer);
        Assert.Equal(SelectorOutcome.None, s.Key(SelectorKey.Other, false, false));
        Assert.Null(s.Selection);
        Assert.Equal(SelectorOutcome.None, s.Key(SelectorKey.Enter, false, false));
        Assert.Same(Editor, s.Selection!.Window);

        Assert.Equal(SelectorOutcome.Cancel, New(CaptureMode.Region).Key(SelectorKey.Escape, false, false));
    }

    [Fact]
    public void After_release_arrows_move_and_Shift_arrows_resize_and_Esc_Enter_and_other_keys_go_to_the_feature()
    {
        var s = New(CaptureMode.Region);
        s.Down(new(200, 200), false);
        s.Up(new(299, 299));
        var rect = s.Selection!.Rect;
        s.Key(SelectorKey.Left, false, false);
        Assert.Equal(rect.Offset(-1, 0), s.Selection.Rect);
        s.Key(SelectorKey.Down, true, false);
        Assert.Equal(rect.Height + 1, s.Selection.Rect.Height);
        Assert.Equal(SelectorOutcome.PassToFeature, s.Key(SelectorKey.Escape, false, false));
        Assert.Equal(SelectorOutcome.PassToFeature, s.Key(SelectorKey.Enter, false, false));
        Assert.Equal(SelectorOutcome.PassToFeature, s.Key(SelectorKey.Other, false, true)); // Ctrl+C
        Assert.NotNull(s.Selection);
    }

    [Fact]
    public void Ctrl_A_selects_the_display_under_the_pointer_before_and_after_release()
    {
        var s = New(CaptureMode.Region, new(-50, 500));
        s.Key(SelectorKey.A, false, true);
        Assert.Equal(new PixelRect(-1920, 360, 1920, 1080), s.Selection!.Rect);
        s.Move(new(10, 10));
        s.Key(SelectorKey.A, false, true);
        Assert.Equal(new PixelRect(0, 0, 3440, 1440), s.Selection.Rect);
    }

    [Fact]
    public void After_release_handles_resize_the_inside_moves_and_a_markup_tool_keeps_the_inside_for_the_feature()
    {
        var s = New(CaptureMode.Region);
        s.Down(new(200, 200), false);
        s.Up(new(399, 299)); // 200 × 100 at 200, 200
        s.Move(new(399, 250));
        Assert.Equal(SelectionHandle.Right, s.HandleUnderPointer);
        s.Down(new(399, 250), false);
        Assert.True(s.IsDragging);
        s.Move(new(449, 250));
        s.Up(new(449, 250));
        Assert.Equal(PixelRect.FromEdges(200, 200, 450, 300), s.Selection!.Rect);

        s.Move(new(300, 250));
        Assert.Equal(SelectionHandle.Inside, s.HandleUnderPointer);
        s.Tooling = true;
        Assert.Equal(SelectionHandle.None, s.HandleUnderPointer);
        s.Down(new(300, 250), false);
        s.Up(new(320, 260));
        Assert.Equal(PixelRect.FromEdges(200, 200, 450, 300), s.Selection.Rect);
    }

    [Fact]
    public void Pick_a_point_picks_on_click_or_Enter_nudges_one_or_ten_and_a_right_click_cancels()
    {
        var s = New(CaptureMode.PickPoint, new(100, 100));
        Assert.Null(s.Hover);
        s.Key(SelectorKey.Right, true, false);
        Assert.Equal(new PixelPoint(110, 100), s.Pointer);
        s.Key(SelectorKey.Up, false, false);
        Assert.Equal(SelectorOutcome.Picked, s.Key(SelectorKey.Enter, false, false));
        Assert.Equal(new PixelPoint(110, 99), s.Picked);
        Assert.Equal(SelectorOutcome.Picked, s.Down(new(5, 6), false));
        Assert.Equal(new PixelPoint(5, 6), s.Picked);
        Assert.Equal(SelectorOutcome.Cancel, s.RightClick());
        Assert.Equal(SelectorOutcome.None, New(CaptureMode.Region).RightClick());
    }

    [Fact]
    public void The_freezer_reads_every_display_or_fails_the_capture()
    {
        var monitors = new[] { new MonitorBounds(1, new PixelRect(0, 0, 4, 2), 96, true), new MonitorBounds(2, new PixelRect(-2, 0, 2, 2), 144, false) };
        var freezer = new Freezer(new FakeReader(), () => [Editor], m => $"id{m}");
        var freeze = freezer.Take(monitors)!;
        Assert.Equal(["id1", "id2"], freeze.Screens.Select(s => s.DisplayId));
        Assert.Equal([Editor], freeze.Windows);
        Assert.Null(new Freezer(new FakeReader { FailAt = 2 }, () => [], _ => "").Take(monitors));
    }

    private sealed class FakeReader : IScreenReader
    {
        public int FailAt { get; init; } = -1;
        private int _reads;
        public IReadOnlyList<MonitorBounds> Monitors() => [];
        public byte[]? Read(PixelRect bounds) => ++_reads == FailAt ? null : new byte[bounds.Area * 4];
    }
}
