// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;
using Chippi.Features.ScreenCapture.Scrolling;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class ScrollTargetTests
{
    private static readonly PixelRect Area = new(100, 100, 700, 600);

    [Fact]
    public void A_plain_window_with_a_scroll_pane_offers_Auto()
    {
        var native = new FakeNative();
        native.Window(10, "Notepad", new PixelRect(0, 0, 1000, 800)).Child(11, "NotepadTextBox");
        native.Pane = new FakePane(new ScrollPosition(0, 25, 600));
        var info = new ScrollTargetFinder(native).Find(Area);
        Assert.Equal(ScrollAvailability.Available, info.Availability);
        Assert.Equal<nint>(10, info.TopLevel);
        Assert.Equal<nint>(11, info.Deepest);
        Assert.Equal<nint>(11, info.WheelWindow);
        Assert.Equal(["Notepad", "NotepadTextBox"], info.Classes);
        Assert.NotNull(info.Pane);
    }

    [Fact]
    public void The_input_site_wins_over_a_deeper_child()
    {
        var native = new FakeNative();
        native.Window(10, "WinUIDesktopWin32WindowClass", new PixelRect(0, 0, 1000, 800))
            .Child(11, "Microsoft.UI.Content.DesktopChildSiteBridge").Child(12, "InputSiteWindowClass").Child(13, "Windows.UI.Composition.DesktopWindowContentBridge");
        native.Pane = new FakePane(new ScrollPosition(10, 50, 500));
        var info = new ScrollTargetFinder(native).Find(Area);
        Assert.Equal<nint>(12, info.Deepest);
        Assert.Equal(4, info.Classes.Count);
    }

    [Theory]
    [InlineData("Chrome_RenderWidgetHostHWND")]
    [InlineData("MozillaWindowClass")]
    public void A_browser_render_host_is_offered_Auto_like_any_other_window(string hostClass)
    {
        // Edge honored a posted wheel in the Sandbox: Auto turns itself off when its first step moves nothing, not before.
        var native = new FakeNative();
        native.Window(10, "Chrome_WidgetWin_1", new PixelRect(0, 0, 1000, 800)).Child(11, hostClass).Child(12, "Intermediate D3D Window");
        native.Pane = new FakePane(new ScrollPosition(0, 25, 600));
        var info = new ScrollTargetFinder(native).Find(Area);
        Assert.Equal(ScrollAvailability.Available, info.Availability);
        Assert.Equal(1, native.PaneLookups);
    }

    [Fact]
    public void An_elevated_window_refuses_before_UI_Automation_is_read()
    {
        var native = new FakeNative { Elevated = true };
        native.Window(10, "Notepad", new PixelRect(0, 0, 1000, 800));
        native.Pane = new FakePane(new ScrollPosition(0, 25, 600));
        var info = new ScrollTargetFinder(native).Find(Area);
        Assert.Equal(ScrollAvailability.Elevated, info.Availability);
        Assert.True(info.Elevated);
        Assert.Equal(0, native.PaneLookups);
    }

    [Fact]
    public void An_area_that_is_not_inside_one_window_refuses()
    {
        var native = new FakeNative();
        native.Window(10, "Notepad", new PixelRect(0, 0, 500, 800)); // narrower than the area
        native.Pane = new FakePane(new ScrollPosition(0, 25, 600));
        // The center (450, 400) is inside it, the area's right edge is not.
        Assert.Equal(ScrollAvailability.NotInOneWindow, new ScrollTargetFinder(native).Find(Area).Availability);
    }

    [Fact]
    public void A_window_over_a_corner_of_the_area_refuses()
    {
        var native = new FakeNative();
        native.Window(10, "Notepad", new PixelRect(0, 0, 1000, 800));
        native.Cover(new PixelRect(700, 600, 200, 200), 20);
        native.Pane = new FakePane(new ScrollPosition(0, 25, 600));
        Assert.Equal(ScrollAvailability.NotInOneWindow, new ScrollTargetFinder(native).Find(Area).Availability);
    }

    [Fact]
    public void No_window_under_the_center_is_nothing_to_scroll()
    {
        var info = new ScrollTargetFinder(new FakeNative()).Find(Area);
        Assert.Equal(ScrollAvailability.NoWindow, info.Availability);
        Assert.False(info.IsAvailable);
        Assert.Equal<nint>(0, info.WheelWindow);
    }

    [Fact]
    public void No_scroll_pane_turns_Auto_off()
    {
        var native = new FakeNative();
        native.Window(10, "Notepad", new PixelRect(0, 0, 1000, 800));
        Assert.Equal(ScrollAvailability.NoScrollPane, new ScrollTargetFinder(native).Find(Area).Availability);
        Assert.Equal(1, native.PaneLookups);
    }

    [Fact]
    public void A_loop_in_the_child_chain_ends_the_walk()
    {
        var native = new FakeNative();
        native.Window(10, "Notepad", new PixelRect(0, 0, 1000, 800)).Child(11, "A");
        native.Loop(11, 10);
        native.Pane = new FakePane(new ScrollPosition(0, 25, 600));
        var info = new ScrollTargetFinder(native).Find(Area);
        Assert.Equal<nint>(11, info.Deepest);
    }

    [Fact]
    public void The_wheel_index_prefers_the_deepest_input_site_else_the_deepest_window()
    {
        Assert.Equal(-1, ScrollRules.WheelIndex([]));
        Assert.Equal(2, ScrollRules.WheelIndex(["a", "b", "c"]));
        Assert.Equal(1, ScrollRules.WheelIndex(["a", "InputSiteWindowClass", "c"]));
        Assert.Equal(2, ScrollRules.WheelIndex(["InputSiteWindowClass", "x", "InputSiteWindowClass", "y"]));
    }

    [Fact]
    public void The_center_is_the_areas_middle_in_screen_pixels() =>
        Assert.Equal(new PixelPoint(450, 400), ScrollRules.Center(Area));

    [Fact]
    public void Decide_follows_the_briefs_order()
    {
        Assert.Equal(ScrollAvailability.NoWindow, ScrollRules.Decide(false, true, true, false));
        Assert.Equal(ScrollAvailability.NotInOneWindow, ScrollRules.Decide(true, false, true, false));
        Assert.Equal(ScrollAvailability.Elevated, ScrollRules.Decide(true, true, true, false));
        Assert.Equal(ScrollAvailability.NoScrollPane, ScrollRules.Decide(true, true, false, false));
        Assert.Equal(ScrollAvailability.Available, ScrollRules.Decide(true, true, false, true));
    }


    [Fact]
    public void Browser_top_levels_are_known_so_their_tree_is_not_searched()
    {
        Assert.True(ScrollRules.IsBrowser("Chrome_WidgetWin_1"));
        Assert.True(ScrollRules.IsBrowser("MozillaWindowClass"));
        Assert.False(ScrollRules.IsBrowser("Notepad"));
    }

    [Fact]
    public void Position_reads_predict_the_shift_from_the_percent_and_view_size()
    {
        // A 600 px pane showing 25% of its content: 2,400 px of content, 1,800 px to scroll.
        var before = new ScrollPosition(0, 25, 600);
        var after = new ScrollPosition(10, 25, 600);
        Assert.Equal(1800, after.RangePixels, 6);
        Assert.Equal(180, after.ShiftSince(before));
        Assert.Equal(-180, before.ShiftSince(after));
        Assert.Equal(0, before.ShiftSince(before));
    }

    [Fact]
    public void A_pane_that_shows_everything_cannot_predict()
    {
        var all = new ScrollPosition(0, 100, 600);
        Assert.False(all.CanPredict);
        Assert.Null(all.ShiftSince(all));
        Assert.Null(new ScrollPosition(0, 50, 0).ShiftSince(default));
    }

    [Theory]
    [InlineData(99.998, false)]
    [InlineData(99.999, true)]
    [InlineData(100, true)]
    public void The_end_is_99_999_percent(double percent, bool end) => Assert.Equal(end, new ScrollPosition(percent, 10, 500).IsEnd);

    private sealed class FakePane(ScrollPosition position) : IScrollPane
    {
        public ScrollPosition? Read() => position;
    }

    // Top-level windows are rectangles from the first down; a child chain is followed at any point inside its window.
    private sealed class FakeNative : IScrollNative
    {
        private readonly List<(nint Handle, PixelRect Frame)> _tops = [];
        private readonly Dictionary<nint, nint> _children = [];
        private readonly Dictionary<nint, nint> _loops = [];
        private readonly Dictionary<nint, string> _classes = [];
        public bool Elevated { get; init; }
        public IScrollPane? Pane { get; set; }
        public int PaneLookups { get; private set; }

        public Chain Window(nint handle, string windowClass, PixelRect frame)
        {
            _tops.Add((handle, frame)); _classes[handle] = windowClass;
            return new Chain(this, handle);
        }
        public void Cover(PixelRect frame, nint handle) { _tops.Insert(0, (handle, frame)); _classes[handle] = "Cover"; }
        public void Loop(nint child, nint back) => _loops[child] = back;

        public nint TopLevelAt(PixelPoint at)
        {
            foreach (var (handle, frame) in _tops) if (frame.Contains(at)) return handle;
            return 0;
        }
        public nint ChildAt(nint parent, PixelPoint at) =>
            _loops.TryGetValue(parent, out var back) ? back : _children.GetValueOrDefault(parent);
        public string ClassOf(nint window) => _classes.GetValueOrDefault(window, "");
        public PixelRect? FrameOf(nint window) => _tops.Where(t => t.Handle == window).Select(t => (PixelRect?)t.Frame).FirstOrDefault();
        public bool IsElevated(nint window) => Elevated;
        public IScrollPane? FindPane(nint topLevel, PixelPoint at, PixelRect area) { PaneLookups++; return Pane; }

        public sealed class Chain(FakeNative owner, nint handle)
        {
            public Chain Child(nint child, string windowClass)
            {
                owner._children[handle] = child; owner._classes[child] = windowClass;
                return new Chain(owner, child);
            }
        }
    }
}
