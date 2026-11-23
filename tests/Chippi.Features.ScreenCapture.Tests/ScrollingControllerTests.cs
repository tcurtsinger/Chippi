// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;
using Chippi.Core.Actions;
using Chippi.Features.ScreenCapture.Scrolling;

namespace Chippi.Features.ScreenCapture.Tests;

/// <summary>
/// The scrolling capture's controller over a fake world: a synthetic page, a fake frame source that emits when the page moves,
/// a posted wheel that moves it (with a glide, or not at all), a UI Automation pane that reads its position, and a clock that
/// only advances when the controller waits. Nothing is shown, captured or sent.
/// </summary>
public sealed class ScrollingControllerTests
{
    private const int W = 48, H = 120, Total = 1000;

    /// <summary>The page: every row unique, drawn from a hash.</summary>
    private static void PageRow(int y, Span<byte> row)
    {
        for (int c = 0; c < W / 4; c++)
        {
            uint h = (uint)y * 0x9E3779B1u ^ (uint)c * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
            byte g = (byte)(h & 0xFF);
            for (int x = c * 4; x < c * 4 + 4; x++)
            {
                row[x * 4] = g; row[x * 4 + 1] = g; row[x * 4 + 2] = g; row[x * 4 + 3] = 255;
            }
        }
    }

    private static byte[] Frame(int offset)
    {
        var bytes = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            PageRow(offset + y, bytes.AsSpan(y * W * 4, W * 4));
        return bytes;
    }

    private static void AssertRows(CapturedImage image, int rows)
    {
        Assert.Equal(W, image.Width);
        Assert.Equal(rows, image.Height);
        var expected = new byte[W * 4];
        for (int y = 0; y < rows; y++)
        {
            PageRow(y, expected);
            Assert.True(expected.AsSpan().SequenceEqual(image.Pixels.AsSpan(y * W * 4, W * 4)), $"row {y} differs");
        }
    }

    private sealed class FakeFrames : IAreaFrames
    {
        public event Action<AreaFrame>? Frame;
        public event Action<string>? Failed;
        public Exception? StartFails;
        public bool Started, Disposed;
        public readonly TaskCompletionSource WhenDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(AreaFramesRequest request, CancellationToken cancellationToken = default)
        {
            Started = true;
            return StartFails is null ? Task.CompletedTask : Task.FromException(StartFails);
        }
        public void Emit(int offset) => Frame?.Invoke(new AreaFrame(W, H, ScrollingControllerTests.Frame(offset), TimeSpan.Zero));
        public void EmitRaw(AreaFrame frame) => Frame?.Invoke(frame);
        public void Lose(string reason) => Failed?.Invoke(reason);
        public void Dispose() { Disposed = true; WhenDisposed.TrySetResult(); }
    }

    private sealed class FakeClock(Action tick) : IScrollClock
    {
        public TimeSpan Now { get; private set; }
        public void Advance(TimeSpan time) => Now += time;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Now += delay;
            tick();
            return Task.CompletedTask;
        }
    }

    private sealed class FakePane(World world) : IScrollPane
    {
        public ScrollPosition? Read()
        {
            if (world.PaneGone) return null;
            double range = Total - H;
            double percent = world.Offset / range * 100 * world.PaneSkew;
            return new ScrollPosition(world.ReportsEnd ? Math.Min(percent, 100) : Math.Min(percent, 90), H * 100.0 / Total, H);
        }
    }

    private sealed class FakeTarget(World world) : IScrollTarget
    {
        public ScrollAvailability Availability = ScrollAvailability.Available;
        public ManualResetEventSlim? Hold;
        public ScrollTargetInfo Find(PixelRect area)
        {
            Hold?.Wait(TimeSpan.FromSeconds(10));
            return Found();
        }

        private ScrollTargetInfo Found() =>
            Availability == ScrollAvailability.Available
                ? new ScrollTargetInfo(Availability, 10, 11, ["Notepad", "Edit"], false, new FakePane(world))
                : ScrollTargetInfo.None(Availability);
    }

    private sealed class FakePc(World world) : IActionPc
    {
        public bool? HasApp(string parsingName) => null;
        public bool Exists(string path) => false;
        public nint WindowUnderPointer() => 0;
        public nint WindowInFront() => 0;
        public void Open(string target) { }
        public void Window(nint window, WindowAction action) { }
        public void Media(MediaAction action) { }
        public Task PressAsync(KeyCombo keys) => Task.CompletedTask;
        public Task TypeAsync(string text) => Task.CompletedTask;
        public void Run(CommandShell shell, string line) { }
        public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => Task.FromResult<(bool, string?)>((false, null));
        public void PostWheel(nint window, int screenX, int screenY, int notches) => world.Wheel(window, screenX, screenY, notches);
    }

    /// <summary>The page, its scroll position, and what drives it.</summary>
    private sealed class World
    {
        public int Offset, Target;
        public int PxPerNotch = 20;
        public int GlideStep = int.MaxValue;
        public bool IgnoreWheel, PaneGone, ReportsEnd = true;
        public int MaxOffset = Total - H;
        public double PaneSkew = 1;
        public readonly List<int> Notches = [];
        public readonly List<nint> Windows = [];
        public Action<int>? OnWheel;
        public readonly FakeFrames Frames = new();
        public readonly FakeClock Clock;
        public readonly FakeTarget Target_;
        public readonly ScrollingController Controller;

        public World(ScrollLimits? limits = null, Action<string>? timings = null)
        {
            Clock = new FakeClock(Tick);
            Target_ = new FakeTarget(this);
            var request = new AreaFramesRequest(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(100, 100, W, H));
            Controller = new ScrollingController(request, Frames, Target_, new FakePc(this), Clock, limits) { Timings = timings };
        }

        public void Wheel(nint window, int x, int y, int notches)
        {
            Windows.Add(window);
            Notches.Add(notches);
            if (!IgnoreWheel)
                Target = Math.Clamp(Target + notches * PxPerNotch, 0, MaxOffset);
            OnWheel?.Invoke(Notches.Count);
        }

        // One poll of the controller: the glide moves a step and the source emits when the screen changed.
        private void Tick()
        {
            if (Offset == Target) return;
            int move = Math.Min(Math.Abs(Target - Offset), GlideStep);
            Offset += Math.Sign(Target - Offset) * move;
            Frames.Emit(Offset);
        }

        public async Task StartAsync()
        {
            await Controller.StartAsync();
            Frames.Emit(0);
        }

        /// <summary>Starts Auto and waits for it to end.</summary>
        public async Task<bool> RunAutoAsync()
        {
            bool started = await Controller.StartAutoAsync();
            await Controller.WhenAutoEndsAsync();
            return started;
        }
    }

    private static async Task<World> Started(ScrollLimits? limits = null)
    {
        var world = new World(limits);
        await world.StartAsync();
        return world;
    }

    // ------------------------------------------------------------ By hand

    [Fact]
    public async Task Says_Scroll_until_the_first_movement_then_nothing_and_stitches_row_for_row()
    {
        var w = await Started();
        Assert.True(w.Frames.Started);
        Assert.Equal(ScrollingStatus.Scroll, w.Controller.Status);
        Assert.Equal((W, H), w.Controller.Size);
        for (int offset = 60; offset <= 600; offset += 60)
            w.Frames.Emit(offset);
        Assert.Equal(ScrollingStatus.None, w.Controller.Status);
        Assert.Equal((W, 720), w.Controller.Size);
        var image = await w.Controller.DoneAsync();
        AssertRows(image!, 720);
        await w.Frames.WhenDisposed.Task.WaitAsync(TimeSpan.FromSeconds(10)); // released after the image is handed back
    }

    [Fact]
    public async Task Scrolling_up_or_holding_still_adds_nothing()
    {
        var w = await Started();
        w.Frames.Emit(60); w.Frames.Emit(120);
        w.Frames.Emit(120); w.Frames.Emit(60); w.Frames.Emit(70);
        Assert.Equal(ScrollingStatus.None, w.Controller.Status);
        Assert.Equal((W, 240), w.Controller.Size);
        w.Frames.Emit(180);
        AssertRows((await w.Controller.DoneAsync())!, 300);
    }

    [Fact]
    public async Task Too_fast_marks_the_gap_and_catching_up_clears_it()
    {
        var w = await Started();
        w.Frames.Emit(60);
        w.Frames.Emit(500); // more than a screen away: no overlap
        Assert.Equal(ScrollingStatus.TooFast, w.Controller.Status);
        var snapshot = w.Controller.Snapshot();
        Assert.True(snapshot.TooFast);
        Assert.Equal((W, 180), w.Controller.Size);
        Assert.Equal(180, snapshot.GapRow);
        w.Frames.Emit(140); // back within reach of the last frame used
        Assert.Equal(ScrollingStatus.None, w.Controller.Status);
        w.Frames.Emit(200);
        AssertRows((await w.Controller.DoneAsync())!, 320);
    }

    [Fact]
    public async Task A_torn_frame_by_hand_changes_nothing_and_the_next_clean_frame_stitches()
    {
        var w = await Started();
        w.Frames.Emit(40); w.Frames.Emit(80);
        var before = w.Controller.Size;
        // The area repainted in pieces: its top rows are at 120, the rest still at 80.
        var torn = Frame(120);
        Buffer.BlockCopy(Frame(80), 70 * W * 4, torn, 70 * W * 4, (H - 70) * W * 4);
        w.Frames.EmitRaw(new AreaFrame(W, H, torn, TimeSpan.Zero));
        Assert.Equal(before, w.Controller.Size);
        Assert.NotEqual(ScrollingStatus.TooFast, w.Controller.Status);
        w.Frames.Emit(120);
        Assert.Equal((W, 240), w.Controller.Size);
        AssertRows((await w.Controller.DoneAsync())!, 240);
    }

    [Fact]
    public async Task Done_feeds_the_settled_picture_so_a_tear_in_the_last_rows_is_replaced()
    {
        var w = await Started();
        w.Frames.Emit(40); w.Frames.Emit(80);
        // The last frame is torn below what its overlap covers (its bottom rows show another position), then the area settles.
        var torn = Frame(120);
        Buffer.BlockCopy(Frame(200), 100 * W * 4, torn, 100 * W * 4, (H - 100) * W * 4);
        w.Frames.EmitRaw(new AreaFrame(W, H, torn, TimeSpan.Zero));
        w.Frames.Emit(120);
        AssertRows((await w.Controller.DoneAsync())!, 240);
    }

    [Fact]
    public async Task Done_during_Too_fast_keeps_what_was_stitched_before_the_gap()
    {
        var w = await Started();
        w.Frames.Emit(60); w.Frames.Emit(120);
        w.Frames.Emit(700);
        Assert.Equal(ScrollingStatus.TooFast, w.Controller.Status);
        AssertRows((await w.Controller.DoneAsync())!, 240);
    }

    [Fact]
    public async Task The_limit_stops_the_capture_keeping_what_fits()
    {
        var w = await Started(new ScrollLimits(MaxHeight: 300));
        for (int offset = 60; offset <= 480; offset += 60)
            w.Frames.Emit(offset);
        Assert.Equal(ScrollingStatus.LimitReached, w.Controller.Status);
        Assert.Equal((W, 300), w.Controller.Size);
        w.Frames.Emit(540); // ignored
        AssertRows((await w.Controller.DoneAsync())!, 300);
    }

    [Fact]
    public async Task Frames_of_another_size_are_ignored()
    {
        var w = await Started();
        w.Frames.Emit(60);
        Assert.Equal((W, 180), w.Controller.Size);
        w.Frames.EmitRaw(new AreaFrame(W, H - 1, new byte[W * (H - 1) * 4], TimeSpan.Zero));
        Assert.Equal((W, 180), w.Controller.Size);
    }

    [Fact]
    public async Task Changes_are_announced_and_the_preview_is_a_strip_of_the_result()
    {
        var w = await Started();
        int changed = 0;
        w.Controller.Changed += () => changed++;
        w.Frames.Emit(60); w.Frames.Emit(120);
        Assert.Equal(2, changed);
        var snapshot = w.Controller.Snapshot();
        Assert.Equal(240, snapshot.Height);
        Assert.Equal([H, H + 60], snapshot.Seams);
        var preview = w.Controller.Preview(12);
        Assert.Equal(12, preview.Width);
        Assert.Equal(60, preview.Height);
    }

    // ------------------------------------------------------------ Failure and Cancel

    [Fact]
    public async Task A_lost_source_ends_the_capture_and_keeps_nothing()
    {
        var w = await Started();
        w.Frames.Emit(60);
        string? reason = null;
        w.Controller.Failed += r => reason = r;
        w.Frames.Lose("Display changed");
        Assert.Equal("Display changed", reason);
        Assert.True(w.Controller.Snapshot().Failed);
        Assert.Null(await w.Controller.DoneAsync());
    }

    [Fact]
    public async Task Windows_refusing_the_start_ends_the_capture_without_throwing()
    {
        var w = new World();
        w.Frames.StartFails = new InvalidOperationException("refused");
        string? reason = null;
        w.Controller.Failed += r => reason = r;
        await w.Controller.StartAsync();
        Assert.Equal("refused", reason);
        Assert.Null(await w.Controller.DoneAsync());
    }

    [Fact]
    public async Task Cancel_keeps_nothing_and_releases_the_source()
    {
        var w = await Started();
        w.Frames.Emit(60);
        w.Controller.Cancel();
        Assert.True(w.Frames.Disposed);
        Assert.Null(await w.Controller.DoneAsync());
    }

    [Fact]
    public void An_area_outside_its_display_is_refused()
    {
        var world = new World();
        var bad = new AreaFramesRequest(1, new PixelRect(0, 0, 100, 100), new PixelRect(50, 50, 100, 100));
        Assert.Throws<ArgumentException>(() => new ScrollingController(bad, world.Frames, world.Target_, new FakePc(world), world.Clock));
    }

    // ------------------------------------------------------------ Auto

    [Fact]
    public async Task Auto_scrolls_to_the_end_and_stitches_every_row_once()
    {
        var w = await Started();
        Assert.True(w.Controller.AutoAvailable);
        Assert.True(await w.RunAutoAsync());
        Assert.Equal(ScrollingStatus.EndReached, w.Controller.Status);
        Assert.False(w.Controller.AutoActive);
        Assert.Equal(1, w.Notches[0]); // the first step: one notch in a 120 px area
        Assert.All(w.Notches, n => Assert.InRange(n, 1, 40));
        Assert.Equal(4, w.Notches[1]); // about 40% overlap at 20 px a notch: 72 / 20
        Assert.All(w.Windows, win => Assert.Equal<nint>(11, win)); // the deepest child
        AssertRows((await w.Controller.DoneAsync())!, Total);
    }

    [Fact]
    public async Task Auto_waits_out_a_glide_before_matching()
    {
        var w = await Started();
        w.GlideStep = 17;
        Assert.True(await w.RunAutoAsync());
        Assert.Equal(ScrollingStatus.EndReached, w.Controller.Status);
        AssertRows((await w.Controller.DoneAsync())!, Total);
    }

    [Fact]
    public async Task Auto_settles_within_a_second_and_a_half()
    {
        var w = await Started();
        w.PxPerNotch = 20;
        w.GlideStep = 2; // a long glide: 60 px takes 30 polls, 1.8 s; the settle gives up at 1.5 s and the stitcher decides
        await w.RunAutoAsync();
        var image = await w.Controller.DoneAsync();
        // Whatever Auto stopped on, what it kept matches the page row for row.
        AssertRows(image!, image!.Height);
        Assert.True(w.Clock.Now > TimeSpan.Zero);
    }

    [Fact]
    public async Task A_first_step_that_moves_nothing_turns_Auto_off_and_hand_scrolling_still_works()
    {
        var w = await Started();
        w.IgnoreWheel = true;
        Assert.True(await w.RunAutoAsync());
        Assert.Single(w.Notches);
        Assert.False(w.Controller.AutoAvailable);
        Assert.Equal(ScrollingStatus.Scroll, w.Controller.Status);
        Assert.False(await w.Controller.StartAutoAsync());
        w.Frames.Emit(60);
        Assert.Equal(ScrollingStatus.None, w.Controller.Status);
        AssertRows((await w.Controller.DoneAsync())!, 180);
    }

    [Fact]
    public async Task Two_steps_in_a_row_that_move_nothing_end_it()
    {
        var w = await Started();
        w.MaxOffset = 500;
        w.ReportsEnd = false; // the pane never says 99.999%
        Assert.True(await w.RunAutoAsync());
        Assert.Equal(ScrollingStatus.EndReached, w.Controller.Status);
        AssertRows((await w.Controller.DoneAsync())!, 500 + H);
    }

    [Fact]
    public async Task A_step_too_big_to_overlap_is_retried_at_half()
    {
        var w = await Started();
        // The first notch scrolls 20 px; after it the pane scrolls 40 px a notch, so the 4 notches that 20 px predicts (160 px)
        // overshoot the 120 px area.
        w.OnWheel = count => { if (count == 1) w.PxPerNotch = 40; };
        Assert.True(await w.RunAutoAsync());
        Assert.Equal(ScrollingStatus.EndReached, w.Controller.Status);
        Assert.Equal([1, 4, -2, 2], w.Notches.Take(4)); // back up to half a step from the reference, then no more than half
        Assert.All(w.Notches.Skip(3), n => Assert.InRange(n, 1, 2));
        AssertRows((await w.Controller.DoneAsync())!, Total);
    }

    [Fact]
    public async Task A_short_area_starts_with_one_notch_and_a_tall_one_with_three()
    {
        Assert.Equal(1, ScrollingSteps.First(500));
        Assert.Equal(1, ScrollingSteps.First(599));
        Assert.Equal(3, ScrollingSteps.First(600));
        Assert.Equal(3, ScrollingSteps.First(1200));
        var w = await Started();
        Assert.True(await w.RunAutoAsync());
        Assert.Equal(1, w.Notches[0]); // the test area is 120 px tall
    }

    [Fact]
    public async Task A_first_step_that_overshoots_goes_back_and_leaves_no_gap()
    {
        var w = await Started();
        w.PxPerNotch = 200; // one notch is more than the 120 px area
        Assert.True(await w.RunAutoAsync());
        Assert.Equal([1, -1], w.Notches); // the step, then back to the reference: nothing smaller to try
        Assert.False(w.Controller.AutoAvailable);
        Assert.False(w.Controller.AutoActive);
        Assert.Equal(ScrollingStatus.Scroll, w.Controller.Status);
        Assert.False(w.Controller.Snapshot().TooFast);
        Assert.Equal(0, w.Offset); // the page is where it was
        AssertRows((await w.Controller.DoneAsync())!, H);
    }

    [Fact]
    public async Task A_second_miss_after_the_retry_goes_back_to_the_reference_and_stops_Auto()
    {
        var w = await Started();
        w.OnWheel = count => { if (count == 1) w.PxPerNotch = 100; }; // a notch is now 100 px: 4 notches and then 2 both miss
        Assert.True(await w.RunAutoAsync());
        Assert.Equal([1, 4, -2, -2], w.Notches);
        Assert.False(w.Controller.AutoAvailable);
        Assert.False(w.Controller.AutoActive);
        Assert.Equal(ScrollingStatus.None, w.Controller.Status); // stitched by hand from here, with no "Too fast" left
        Assert.False(w.Controller.Snapshot().TooFast);
        Assert.Equal(20, w.Offset); // back where the reference was (one notch of the first step)
        AssertRows((await w.Controller.DoneAsync())!, H + 20);
    }

    [Fact]
    public async Task Auto_settles_each_step_after_one_short_quiet_interval()
    {
        var w = await Started();
        Assert.True(await w.RunAutoAsync());
        // 60 ms of quiet plus a poll or two, a step: nowhere near the old 1.5 s.
        double perStep = w.Clock.Now.TotalMilliseconds / w.Notches.Count;
        Assert.True(perStep < 200, $"{perStep:F0} ms a step");
    }

    [Fact]
    public async Task A_step_that_draws_nothing_waits_only_the_no_frame_limit()
    {
        var w = await Started();
        w.IgnoreWheel = true;
        var before = w.Clock.Now;
        Assert.True(await w.RunAutoAsync());
        Assert.InRange((w.Clock.Now - before).TotalMilliseconds, 0, 1000); // 60 ms initial settle + 600 ms for the step
    }

    [Fact]
    public async Task Done_does_not_wait_when_no_frame_has_come_for_a_while()
    {
        var w = await Started();
        w.Frames.Emit(60);
        w.Clock.Advance(TimeSpan.FromSeconds(1));
        var at = w.Clock.Now;
        AssertRows((await w.Controller.DoneAsync())!, 180);
        Assert.Equal(at, w.Clock.Now);
    }

    [Fact]
    public async Task The_scroll_target_is_read_alongside_the_frames_and_Auto_fills_in_when_it_lands()
    {
        var w = new World();
        using var hold = new ManualResetEventSlim(false);
        w.Target_.Hold = hold;
        int changed = 0;
        w.Controller.Changed += () => Interlocked.Increment(ref changed);
        var start = w.Controller.StartAsync();
        // The frames have started while UI Automation is still being read, and Auto is not offered yet.
        for (int i = 0; i < 200 && !w.Frames.Started; i++) await Task.Delay(10);
        Assert.True(w.Frames.Started);
        Assert.False(w.Controller.AutoAvailable);
        Assert.False(start.IsCompleted);
        int before = Volatile.Read(ref changed);
        hold.Set();
        await start;
        Assert.True(w.Controller.AutoAvailable);
        Assert.True(Volatile.Read(ref changed) > before);
    }

    [Fact]
    public async Task The_spans_are_written_when_a_report_is_wanted()
    {
        var lines = new List<string>();
        var w = new World(timings: lines.Add);
        await w.StartAsync();
        Assert.True(await w.RunAutoAsync());
        await w.Controller.DoneAsync();
        Assert.Contains(lines, l => l.StartsWith("scroll-start find="));
        Assert.Contains(lines, l => l.StartsWith("scroll-start wgc="));
        Assert.Contains(lines, l => l.StartsWith("auto-step n=1 ") && l.Contains("settle=") && l.Contains("stitch="));
        Assert.Contains(lines, l => l.StartsWith("scroll-done ") && l.Contains("quiet=") && l.Contains("compose="));
    }

    [Fact]
    public async Task A_pane_that_reads_off_does_not_refuse_a_frame_the_stitcher_can_verify()
    {
        var w = await Started();
        w.PaneSkew = 1.25; // predicts shifts 25% too long, past the search's slack
        Assert.True(await w.RunAutoAsync());
        var image = await w.Controller.DoneAsync();
        AssertRows(image!, image!.Height);
        Assert.True(image.Height > 400);
    }

    [Fact]
    public async Task Stop_keeps_what_was_stitched_and_goes_back_to_scrolling_by_hand()
    {
        var w = await Started();
        w.OnWheel = count =>
        {
            if (count == 4)
                _ = w.Controller.StopAutoAsync();
        };
        Assert.True(await w.RunAutoAsync());
        Assert.False(w.Controller.AutoActive);
        Assert.NotEqual(ScrollingStatus.EndReached, w.Controller.Status);
        Assert.True(w.Controller.AutoAvailable);
        int kept = w.Controller.Size.Height;
        Assert.InRange(kept, H + 1, Total - 1);
        // By hand again: a frame from the current position stitches on.
        w.Frames.Emit(w.Offset + 40 > Total - H ? Total - H : w.Offset + 40);
        Assert.True(w.Controller.Size.Height >= kept);
        var image = await w.Controller.DoneAsync();
        AssertRows(image!, image!.Height);
    }

    [Fact]
    public async Task Auto_is_off_when_the_target_refuses()
    {
        var w = new World();
        w.Target_.Availability = ScrollAvailability.NoScrollPane;
        await w.StartAsync();
        Assert.False(await w.Controller.StartAutoAsync());
        Assert.False(w.Controller.AutoAvailable);
        Assert.Equal(ScrollAvailability.NoScrollPane, w.Controller.AutoReason);
        Assert.Empty(w.Notches);
    }

    [Fact]
    public async Task Auto_is_not_offered_from_the_start_when_the_target_refused()
    {
        var w = new World();
        w.Target_.Availability = ScrollAvailability.Elevated;
        await w.StartAsync();
        Assert.False(w.Controller.AutoAvailable);
        Assert.Equal(ScrollAvailability.Elevated, w.Controller.AutoReason);
        Assert.False(await w.Controller.StartAutoAsync());
        w.Frames.Emit(60);
        AssertRows((await w.Controller.DoneAsync())!, 180);
    }

    [Fact]
    public async Task Auto_needs_a_first_frame()
    {
        var w = new World();
        await w.Controller.StartAsync();
        Assert.False(await w.Controller.StartAutoAsync());
    }

    [Fact]
    public async Task A_window_that_goes_during_Auto_stops_it_and_keeps_what_was_stitched()
    {
        var w = await Started();
        w.OnWheel = count =>
        {
            if (count == 3)
                w.PaneGone = true;
        };
        Assert.True(await w.RunAutoAsync());
        Assert.False(w.Controller.AutoActive);
        Assert.False(w.Controller.AutoAvailable);
        var image = await w.Controller.DoneAsync();
        AssertRows(image!, image!.Height);
        Assert.InRange(image.Height, H, Total - 1);
    }

    [Fact]
    public async Task Auto_stops_at_the_limit_keeping_what_fits()
    {
        var w = await Started(new ScrollLimits(MaxHeight: 400));
        Assert.True(await w.RunAutoAsync());
        Assert.Equal(ScrollingStatus.LimitReached, w.Controller.Status);
        AssertRows((await w.Controller.DoneAsync())!, 400);
    }

    [Fact]
    public async Task Auto_that_ends_picks_up_a_changed_screen_by_hand()
    {
        var w = await Started();
        w.IgnoreWheel = true;
        w.OnWheel = _ => { };
        await w.RunAutoAsync();
        w.Frames.Emit(80);
        Assert.Equal((W, 200), w.Controller.Size);
    }

    // ------------------------------------------------------------ Step sizing

    [Theory]
    [InlineData(0, 600, 3)]      // nothing measured: the first step
    [InlineData(20, 600, 18)]    // 60% of 600 is 360, at 20 px a notch
    [InlineData(1, 600, 40)]     // clamped to 40
    [InlineData(1000, 600, 1)]   // clamped to 1
    [InlineData(20, 0, 3)]
    public void The_step_leaves_about_40_percent_shared_within_1_to_40_notches(double perNotch, int band, int expected)
    {
        Assert.Equal(expected, ScrollingSteps.Notches(perNotch, band));
    }

    [Fact]
    public void The_step_respects_a_lower_cap()
    {
        Assert.Equal(2, ScrollingSteps.Notches(20, 600, max: 2));
    }
}
