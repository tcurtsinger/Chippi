// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows.Threading;
using Chippi.Capture;
using System.Runtime.InteropServices;
using Chippi.UI.Capture;
using Chippi.UI.Displays;

namespace Chippi.Features.ScreenCapture.Scrolling;

/// <summary>A scrolling capture to begin: the area on its display, and that display's DPI.</summary>
internal sealed record ScrollingStart(AreaFramesRequest Request, int Dpi);

/// <summary>The controller and the stage of one scrolling capture. The module builds them; tests pass fakes.</summary>
internal sealed record ScrollingParts(ScrollingController Controller, IScrollingStage Stage);

/// <summary>How a scrolling capture ended.</summary>
internal enum ScrollingOutcome
{
    /// <summary>Done: the stitched image, or none when nothing was captured.</summary>
    Done,

    /// <summary>Cancel: nothing is kept.</summary>
    Cancelled,

    /// <summary>The source was lost or Windows refused it ("Scrolling capture failed"): nothing is kept.</summary>
    Failed,
}

/// <summary>
/// One scrolling capture, from Scroll pressed until Done or Cancel (docs/phase-7/scrolling-capture.md): the stage's bar,
/// preview and frame, driven by the <see cref="ScrollingController"/>. The controller raises from any thread; the stage is
/// updated on the UI thread, at most once per <see cref="Throttle"/>. Everything here runs on the UI thread.
/// </summary>
internal sealed class ScrollingSession : IDisposable
{
    private readonly ScrollingController _controller;
    private readonly IScrollingStage _stage;
    private readonly Dispatcher _dispatcher;
    private readonly Action<ScrollingOutcome, CapturedImage?> _ended;
    private readonly PixelSize _area;
    private readonly CancellationTokenSource _cancel = new();
    private int _dirty;
    private bool _ended0, _keyboardTaken, _autoStarting, _watching;
    private DispatcherTimer? _desktopWatch;
    private int _composedHeight = -1;
    private CapturedImage? _strip;

    public ScrollingSession(ScrollingController controller, IScrollingStage stage, PixelSize area, Dispatcher dispatcher, Action<ScrollingOutcome, CapturedImage?> ended)
    {
        _area = area;
        _controller = controller;
        _stage = stage;
        _dispatcher = dispatcher;
        _ended = ended;
        _controller.Changed += Changed;
        _controller.Failed += OnFailed;
        DisplayChanges.Changed += DisplaysChanged;
        _watching = true;
        _stage.AutoAsked += () => _ = AutoAsync();
        _stage.StopAsked += () => _ = StopAsync();
        _stage.DoneAsked += () => _ = DoneAsync();
        _stage.CancelAsked += Cancel;
    }

    /// <summary>The least time between two redraws of the bar and preview.</summary>
    public TimeSpan Throttle { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>The capture has ended (Done, Cancel or a failure).</summary>
    public bool IsEnded => _ended0;

    /// <summary>True when the input desktop is not ours (a lock, Ctrl+Alt+Del or UAC). Tests replace it.</summary>
    internal Func<bool> InputDesktopLost { get; init; } = () => !InputDesktopIsOurs();

    /// <summary>How often the input desktop is checked.</summary>
    internal TimeSpan DesktopPoll { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The displays changed or the PC resumed: the area is no longer what was frozen, so the capture ends, keeping nothing.</summary>
    internal void DisplaysChanged() => _dispatcher.BeginInvoke(() => End(ScrollingOutcome.Cancelled, null));

    private void WatchDesktop()
    {
        _desktopWatch = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = DesktopPoll };
        _desktopWatch.Tick += (_, _) =>
        {
            if (!_ended0 && InputDesktopLost())
                End(ScrollingOutcome.Cancelled, null);
        };
        _desktopWatch.Start();
    }

    private void StopWatching()
    {
        _desktopWatch?.Stop();
        _desktopWatch = null;
        if (!_watching) return;
        _watching = false;
        DisplayChanges.Changed -= DisplaysChanged;
    }

    private static bool InputDesktopIsOurs()
    {
        nint desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == 0) return false;
        CloseDesktop(desktop);
        return true;
    }

    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);

    /// <summary>Shows the stage and starts the frames. The first live frame follows; a refusal ends the capture as Failed.</summary>
    public void Start()
    {
        // The frames and the scroll target start first, on worker threads, so they run while the windows are made.
        long begun = Stopwatch.GetTimestamp();
        _ = RunAsync();
        _stage.Show();
        WatchDesktop();
        Refresh();
        _timings?.Invoke("scroll-start stage-show=" + ScrollTimings.Ms(begun));
    }

    /// <summary>Where the session's spans go (the capture timings report), or null.</summary>
    internal Action<string>? Timings { init => _timings = value; }

    private readonly Action<string>? _timings;

    /// <summary>When Done was asked (a <see cref="Stopwatch.GetTimestamp"/> reading); 0 before. The module times the result's showing from it.</summary>
    internal long DoneAt { get; private set; }

    private async Task RunAsync()
    {
        try
        {
            // The target is read (UI Automation) and the frames start (Windows Graphics Capture) off the UI thread.
            await Task.Run(() => _controller.StartAsync(_cancel.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Scrolling capture: it couldn't start", ex);
            await _dispatcher.InvokeAsync(() => End(ScrollingOutcome.Failed, null));
        }
        // StartAsync may have decided about Auto: the bar says so, and the log says why (the reason's name only).
        if (!_controller.AutoAvailable) Chippi.Core.Diagnostics.Log.Info("Scrolling capture: Auto off, " + _controller.AutoReason);
        Changed();
    }

    private void OnFailed(string reason)
    {
        Chippi.Core.Diagnostics.Log.Warn("Scrolling capture: " + reason);
        // A display change ends the capture without a notice, as a lock does: the frozen screen is gone with it.
        var outcome = reason == AreaFrameReasons.DisplayChanged ? ScrollingOutcome.Cancelled : ScrollingOutcome.Failed;
        _dispatcher.BeginInvoke(() => End(outcome, null));
    }

    private void Changed()
    {
        if (_ended0 || Interlocked.Exchange(ref _dirty, 1) != 0)
            return;
        if (Throttle <= TimeSpan.Zero)
            _dispatcher.BeginInvoke(Refresh, DispatcherPriority.Render);
        else
            _ = Task.Delay(Throttle).ContinueWith(_ => _dispatcher.BeginInvoke(Refresh, DispatcherPriority.Render), TaskScheduler.Default);
    }

    /// <summary>Draws the controller's state on the stage.</summary>
    public void Refresh()
    {
        Interlocked.Exchange(ref _dirty, 0);
        if (_ended0)
            return;
        var s = _controller.Snapshot();
        var area = _area;
        _stage.SetBar(new ScrollingBarState(Line(s.Status), s.Width > 0 ? s.Width : area.Width, s.Height > 0 ? s.Height : area.Height, s.AutoAvailable, s.AutoActive));
        // The strip is composed again only when rows were added; scrolling about inside the result moves only the frame.
        if (s.Height != _composedHeight)
        {
            _composedHeight = s.Height;
            _strip = s.Height > 0 ? _controller.Preview(_stage.PreviewWidth) : null;
        }
        _stage.SetPreview(new ScrollingPreviewState(_strip, s.Width, s.Height, s.Seams, s.ViewportTop, s.GapRow, s.TooFast));
        if (s.Status == ScrollingStatus.EndReached && !_keyboardTaken)
        {
            // End reached: Done takes the keyboard, so Enter finishes.
            _keyboardTaken = true;
            _stage.TakeKeyboard();
        }
    }

    private static ScrollingLine Line(ScrollingStatus status) => status switch
    {
        ScrollingStatus.Scroll => ScrollingLine.Scroll,
        ScrollingStatus.Scrolling => ScrollingLine.Scrolling,
        ScrollingStatus.TooFast => ScrollingLine.TooFast,
        ScrollingStatus.EndReached => ScrollingLine.EndReached,
        ScrollingStatus.LimitReached => ScrollingLine.LimitReached,
        _ => ScrollingLine.None,
    };

    private async Task AutoAsync()
    {
        if (_ended0 || _autoStarting || _controller.AutoActive)
            return;
        _autoStarting = true;
        try
        {
            if (await _controller.StartAutoAsync())
            {
                Refresh();
                await _controller.WhenAutoEndsAsync();
            }
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Scrolling capture: Auto failed", ex);
        }
        finally
        {
            _autoStarting = false;
        }
        await _dispatcher.InvokeAsync(Refresh);
    }

    private async Task StopAsync()
    {
        if (_ended0)
            return;
        await _controller.StopAutoAsync();
        await _dispatcher.InvokeAsync(Refresh);
    }

    /// <summary>Done: the stage goes, the stitched result is composed off the UI thread and handed on.</summary>
    public async Task DoneAsync()
    {
        if (_ended0)
            return;
        _ended0 = true;
        long begun = DoneAt = Stopwatch.GetTimestamp();
        _stage.Dispose();
        long staged = Stopwatch.GetTimestamp();
        CapturedImage? image = null;
        try
        {
            image = await Task.Run(() => _controller.DoneAsync());
            _timings?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"scroll-done stage-dispose={Stopwatch.GetElapsedTime(begun, staged).TotalMilliseconds:F1} controller={ScrollTimings.Ms(staged)}"));
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Scrolling capture: the result couldn't be composed", ex);
            Finish(ScrollingOutcome.Failed, null);
            return;
        }
        Finish(ScrollingOutcome.Done, image);
    }

    public void Cancel() => End(ScrollingOutcome.Cancelled, null);

    private void End(ScrollingOutcome outcome, CapturedImage? image)
    {
        if (_ended0)
            return;
        _ended0 = true;
        _stage.Dispose();
        Finish(outcome, image);
    }

    private void Finish(ScrollingOutcome outcome, CapturedImage? image)
    {
        StopWatching();
        _controller.Changed -= Changed;
        _controller.Failed -= OnFailed;
        if (outcome != ScrollingOutcome.Done)
            _controller.Cancel();
        _controller.Dispose();
        _cancel.Cancel();
        _ended(outcome, image);
    }

    /// <summary>Ends the capture keeping nothing, without telling anyone (the module is stopping).</summary>
    public void Dispose()
    {
        if (_ended0)
            return;
        _ended0 = true;
        StopWatching();
        _controller.Changed -= Changed;
        _controller.Failed -= OnFailed;
        _stage.Dispose();
        _controller.Dispose();
        _cancel.Cancel();
    }
}
