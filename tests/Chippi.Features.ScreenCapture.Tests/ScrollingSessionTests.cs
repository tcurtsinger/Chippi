// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Capture;
using Chippi.Core.Actions;
using Chippi.Features.ScreenCapture.Scrolling;
using Chippi.Island.Tests;
using Chippi.UI.Capture;

namespace Chippi.Features.ScreenCapture.Tests;

/// <summary>
/// A scrolling capture's session over a fake stage and a controller on fakes: the stage shows, follows the controller, takes
/// Done and Cancel, and ends on a failure. No window is shown, no screen is read and no input is sent.
/// </summary>
public sealed class ScrollingSessionTests
{
    private const int W = 48, H = 120;

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

    private static AreaFrame Frame(int offset)
    {
        var bytes = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            PageRow(offset + y, bytes.AsSpan(y * W * 4, W * 4));
        return new AreaFrame(W, H, bytes, TimeSpan.Zero);
    }

    private sealed class Frames : IAreaFrames
    {
        public event Action<AreaFrame>? Frame;
        public event Action<string>? Failed;
        public Exception? StartFails;
        public bool Disposed;
        public TaskCompletionSource Started = new();
        public Task StartAsync(AreaFramesRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return StartFails is null ? Task.CompletedTask : Task.FromException(StartFails);
        }
        public void Emit(int offset) => Frame?.Invoke(ScrollingSessionTests.Frame(offset));
        public void Lose(string reason) => Failed?.Invoke(reason);
        public void Dispose() => Disposed = true;
    }

    private sealed class NoTarget : IScrollTarget
    {
        public ScrollTargetInfo Find(PixelRect area) => ScrollTargetInfo.None(ScrollAvailability.NoWindow);
    }

    private sealed class Pc : IActionPc
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
    }

    private sealed class Stage : IScrollingStage
    {
        public int PreviewWidth => 12;
        public bool Shown, Disposed;
        public int Keyboard;
        public readonly List<ScrollingBarState> Bars = [];
        public readonly List<ScrollingPreviewState> Previews = [];
        public event Action? AutoAsked, StopAsked, DoneAsked, CancelAsked;
        public void Show() => Shown = true;
        public void SetBar(ScrollingBarState state) => Bars.Add(state);
        public void SetPreview(ScrollingPreviewState state) => Previews.Add(state);
        public void TakeKeyboard() => Keyboard++;
        public void Dispose() => Disposed = true;
        public void PressDone() => DoneAsked?.Invoke();
        public void PressCancel() => CancelAsked?.Invoke();
        public void PressStop() => StopAsked?.Invoke();
        public void PressAuto() => AutoAsked?.Invoke();
    }

    private sealed class Rig : IDisposable
    {
        public readonly Frames Frames = new();
        public readonly Stage Stage = new();
        public readonly ScrollingSession Session;
        public readonly TaskCompletionSource<(ScrollingOutcome Outcome, CapturedImage? Image)> Ended = new();

        public bool DesktopLost;

        public Rig()
        {
            var request = new AreaFramesRequest(1, new PixelRect(0, 0, 200, 300), new PixelRect(10, 10, W, H));
            var controller = new ScrollingController(request, Frames, new NoTarget(), new Pc(), new SystemScrollClock());
            Session = new ScrollingSession(controller, Stage, request.Area.Size, Dispatcher.CurrentDispatcher, (outcome, image) => Ended.TrySetResult((outcome, image)))
            { Throttle = TimeSpan.Zero, InputDesktopLost = () => DesktopLost, DesktopPoll = TimeSpan.FromMilliseconds(20) };
        }

        public void Dispose() => Session.Dispose();

        /// <summary>Lets what the session posted to the UI thread run.</summary>
        public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    [Fact]
    public void Start_shows_the_stage_with_Scroll_and_the_areas_size_then_follows_the_frames() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        Assert.True(rig.Stage.Shown);
        var first = rig.Stage.Bars[^1];
        Assert.Equal(new ScrollingBarState(ScrollingLine.Scroll, W, H, false, false), first);
        WpfTestThread.Wait(rig.Frames.Started.Task);

        rig.Frames.Emit(0);
        rig.Session.Refresh();
        Assert.Equal(ScrollingLine.Scroll, rig.Stage.Bars[^1].Line); // nothing has moved yet
        Assert.Equal((W, H), (rig.Stage.Bars[^1].Width, rig.Stage.Bars[^1].Height));

        rig.Frames.Emit(60);
        rig.Frames.Emit(120);
        rig.Session.Refresh();
        var bar = rig.Stage.Bars[^1];
        Assert.Equal(new ScrollingBarState(ScrollingLine.None, W, 240, false, false), bar);
        var preview = rig.Stage.Previews[^1];
        Assert.Equal(240, preview.Height);
        Assert.Equal(W, preview.Width);
        Assert.NotEmpty(preview.Seams);
        Assert.False(preview.TooFast);
        Assert.NotNull(preview.Strip);
        Assert.Equal(12, preview.Strip!.Width); // composed at the stage's pixel width
    });

    [Fact]
    public void Too_fast_marks_the_gap_and_catching_up_clears_it() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Emit(60);
        rig.Frames.Emit(400); // more than a screen away: no overlap
        rig.Session.Refresh();
        Assert.Equal(ScrollingLine.TooFast, rig.Stage.Bars[^1].Line);
        Assert.True(rig.Stage.Previews[^1].TooFast);
        Assert.Equal(180, rig.Stage.Bars[^1].Height); // nothing was stitched over the gap

        rig.Frames.Emit(120);
        rig.Session.Refresh();
        Assert.Equal(ScrollingLine.None, rig.Stage.Bars[^1].Line);
        Assert.False(rig.Stage.Previews[^1].TooFast);
    });

    [Fact]
    public void Done_takes_the_stage_down_and_hands_on_the_stitched_rows() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Emit(60);
        rig.Frames.Emit(120);
        rig.Stage.PressDone();
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var (outcome, image) = rig.Ended.Task.Result;
        Assert.Equal(ScrollingOutcome.Done, outcome);
        Assert.Equal(W, image!.Width);
        Assert.Equal(240, image.Height);
        Assert.True(rig.Stage.Disposed);
        Assert.True(rig.Frames.Disposed);
        Assert.True(rig.Session.IsEnded);
    });

    [Fact]
    public void Done_before_the_first_frame_keeps_nothing() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Stage.PressDone();
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var (outcome, image) = rig.Ended.Task.Result;
        Assert.Equal(ScrollingOutcome.Done, outcome);
        Assert.Null(image);
    });

    [Fact]
    public void Cancel_keeps_nothing_and_lets_go_of_the_frames() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Emit(60);
        rig.Stage.PressCancel();
        var (outcome, image) = rig.Ended.Task.Result;
        Assert.Equal(ScrollingOutcome.Cancelled, outcome);
        Assert.Null(image);
        Assert.True(rig.Stage.Disposed);
        Assert.True(rig.Frames.Disposed);
        rig.Stage.PressDone(); // nothing more happens
        Assert.True(rig.Ended.Task.IsCompleted);
    });

    [Fact]
    public void A_lost_source_ends_the_capture_as_failed() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Lose("Capture failed");
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var (outcome, image) = rig.Ended.Task.Result;
        Assert.Equal(ScrollingOutcome.Failed, outcome);
        Assert.Null(image);
        Assert.True(rig.Stage.Disposed);
    });

    [Fact]
    public void A_display_change_ends_the_capture_silently_keeping_nothing() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Emit(60);
        rig.Session.DisplaysChanged(); // the displays settled after a change or a resume
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var (outcome, image) = rig.Ended.Task.Result;
        Assert.Equal(ScrollingOutcome.Cancelled, outcome);
        Assert.Null(image);
        Assert.True(rig.Stage.Disposed);
        Assert.True(rig.Frames.Disposed);
    });

    [Fact]
    public void The_sources_own_display_change_is_no_failure() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Lose(AreaFrameReasons.DisplayChanged);
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ScrollingOutcome.Cancelled, rig.Ended.Task.Result.Outcome);
    });

    [Fact]
    public void A_lock_or_secure_desktop_ends_the_capture_keeping_nothing() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Frames.Emit(0);
        rig.Frames.Emit(60);
        rig.DesktopLost = true; // Win+L: the input desktop is no longer ours
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var (outcome, image) = rig.Ended.Task.Result;
        Assert.Equal(ScrollingOutcome.Cancelled, outcome);
        Assert.Null(image);
        Assert.True(rig.Stage.Disposed);
    });

    [Fact]
    public void Windows_refusing_the_capture_ends_it_as_failed() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.Frames.StartFails = new InvalidOperationException("refused");
        rig.Session.Start();
        WpfTestThread.Wait(rig.Ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ScrollingOutcome.Failed, rig.Ended.Task.Result.Outcome);
        Assert.True(rig.Stage.Disposed);
    });

    [Fact]
    public void Disposing_the_session_lets_go_without_telling_anyone() => WpfTestThread.Run(() =>
    {
        var rig = new Rig();
        rig.Session.Start();
        WpfTestThread.Wait(rig.Frames.Started.Task);
        rig.Session.Dispose();
        Assert.True(rig.Stage.Disposed);
        Assert.True(rig.Frames.Disposed);
        Assert.False(rig.Ended.Task.IsCompleted);
    });
}
