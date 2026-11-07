// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows.Controls;
using Chippi.Capture;
using Chippi.Features.Recording.Overlay;
using Chippi.Island.Tests;

namespace Chippi.Features.Recording.Tests;

/// <summary>The red frame and the pill (screen-recording.md, Frame and pill).</summary>
public sealed partial class OverlayTests
{
    private static readonly PixelRect Display = new(0, 0, 3440, 1440);
    private static readonly PixelSize Pill = new(160, 40);

    [Fact]
    public void The_frame_sits_just_outside_the_recording_and_inside_where_it_meets_the_display_s_edge()
    {
        Assert.Equal(PixelRect.FromEdges(98, 98, 742, 462), OverlayPlacement.Frame(new PixelRect(100, 100, 640, 360), Display, 2));
        Assert.Equal(Display, OverlayPlacement.Frame(Display, Display, 2));
        Assert.Equal(PixelRect.FromEdges(0, 98, 742, 462), OverlayPlacement.Frame(new PixelRect(0, 100, 740, 360), Display, 2));
    }

    [Fact]
    public void The_pill_goes_under_the_recording_at_its_right_edge()
    {
        Assert.Equal(new PixelPoint(580, 470), OverlayPlacement.Pill(new PixelRect(100, 100, 640, 360), Display, Pill, 10));
    }

    [Fact]
    public void The_pill_goes_above_when_there_s_no_room_under()
    {
        Assert.Equal(new PixelPoint(580, 950), OverlayPlacement.Pill(new PixelRect(100, 1000, 640, 400), Display, Pill, 10));
    }

    [Fact]
    public void The_pill_goes_inside_the_bottom_right_corner_of_a_whole_display()
    {
        Assert.Equal(new PixelPoint(3270, 1390), OverlayPlacement.Pill(Display, Display, Pill, 10));
    }

    [Fact]
    public void The_pill_stays_on_the_display_by_a_narrow_recording_at_its_left_edge()
    {
        var at = OverlayPlacement.Pill(new PixelRect(0, 100, 40, 40), Display, Pill, 10);
        Assert.Equal(new PixelPoint(10, 150), at);
        var left = new PixelRect(-1920, 360, 1920, 1080);
        Assert.Equal(new PixelPoint(-1910, 510), OverlayPlacement.Pill(new PixelRect(-1920, 400, 50, 100), left, Pill, 10));
    }

    [Fact]
    public void The_frame_and_the_pill_are_left_out_of_captures_and_never_take_the_keyboard()
    {
        WpfTestThread.Run(() =>
        {
            var target = new RecordingTarget(RecordingSource.Region, new PixelRect(200, 200, 320, 180), Display, 0, 96, 0);
            using var overlay = new RecordingOverlay(target, new Border { Width = 120, Height = 32 });
            foreach (var hwnd in new[] { overlay.FrameHandle, overlay.PillHandle })
            {
                Assert.True(GetWindowDisplayAffinity(hwnd, out uint affinity));
                Assert.Equal(0x11u, affinity); // WDA_EXCLUDEFROMCAPTURE
                Assert.NotEqual(0, GetWindowLongPtr(hwnd, -20) & 0x08000000); // WS_EX_NOACTIVATE
            }
            Assert.NotEqual(0, GetWindowLongPtr(overlay.FrameHandle, -20) & 0x20); // the frame lets clicks through
            Assert.Equal(0, GetWindowLongPtr(overlay.PillHandle, -20) & 0x20); // the pill takes them
            Assert.True(GetWindowRect(overlay.FrameHandle, out var f));
            Assert.Equal((198, 198, 522, 382), (f.Left, f.Top, f.Right, f.Bottom));
            Assert.True(GetWindowRect(overlay.PillHandle, out var p));
            Assert.Equal((520, 390), (p.Right, p.Top)); // under, right edges lined up, 2 + 8 clear
        });
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint hwnd, out Rect rect);
}
