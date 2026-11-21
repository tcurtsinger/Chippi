// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Capture;
using Chippi.Core.Actions;

namespace Chippi.Features.ScreenCapture.Scrolling;

/// <summary>What the scrolling bar says (docs/phase-7/scrolling-capture.md, Copy). <see cref="None"/> shows nothing.</summary>
public enum ScrollingStatus
{
    /// <summary>"Scroll": waiting for the first movement.</summary>
    Scroll,
    /// <summary>Stitching by hand, nothing to say.</summary>
    None,
    /// <summary>"Scrolling": Auto is running.</summary>
    Scrolling,
    /// <summary>"Too fast": the last frame no longer overlaps; the preview marks the gap.</summary>
    TooFast,
    /// <summary>"End reached".</summary>
    EndReached,
    /// <summary>"Limit reached".</summary>
    LimitReached,
}

/// <summary>Time for the controller: the clock it measures a settle by and the delay it polls with. A test holds both.</summary>
public interface IScrollClock
{
    TimeSpan Now { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>The real clock.</summary>
public sealed class SystemScrollClock : IScrollClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    public TimeSpan Now => _watch.Elapsed;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>Everything the bar and preview draw, read at one moment.</summary>
public sealed record ScrollingSnapshot(
    ScrollingStatus Status, int Width, int Height, bool Moved, bool AutoAvailable, bool AutoActive,
    IReadOnlyList<int> Seams, int ViewportTop, int GapRow, bool TooFast, bool Failed);

/// <summary>The step arithmetic of Auto, free of everything else.</summary>
public static class ScrollingSteps
{
    /// <summary>The first step in an area at least <see cref="ShortArea"/> tall: 3 notches.</summary>
    public const int FirstNotches = 3;
    /// <summary>An area shorter than this gets a first step of one notch: three could overshoot it (WinUI pages scroll about 100 px a notch).</summary>
    public const int ShortArea = 600;
    public const int MaxNotches = 40;
    /// <summary>Each new frame overlaps the last by about this share of the area's moving band.</summary>
    public const double Overlap = 0.4;

    /// <summary>
    /// The notches for the next step: the shift that leaves <see cref="Overlap"/> of the band shared, at the pixels per notch
    /// measured so far, clamped to 1 to <paramref name="max"/>. The first step (nothing measured) is <see cref="First"/>.
    /// </summary>
    public static int Notches(double pixelsPerNotch, int bandHeight, int max = MaxNotches)
    {
        if (pixelsPerNotch <= 0 || bandHeight <= 0)
            return Math.Clamp(First(bandHeight), 1, max);
        double wanted = (1 - Overlap) * bandHeight;
        return Math.Clamp((int)Math.Round(wanted / pixelsPerNotch), 1, max);
    }

    /// <summary>The first step, before the pixels a notch scroll are known: 1 notch in a short area, else 3.</summary>
    public static int First(int areaHeight) => areaHeight is > 0 and < ShortArea ? 1 : FirstNotches;
}

/// <summary>
/// Runs a scrolling capture over fakes-able parts: live frames of the area, the stitcher, what Auto would scroll and the
/// shell's action PC, with an injected clock. By hand, every frame is stitched as it comes. Auto posts wheel notches to the
/// window under the area's center, waits for the picture to settle, stitches, and sizes the next step. It never moves the
/// pointer, never activates a window and never scrolls through UI Automation. Events are raised on whatever thread caused
/// them (frames on a worker thread); a window marshals. Nothing in it touches Windows itself.
/// </summary>
public sealed class ScrollingController : IDisposable
{
    public const string AutoNotAvailable = "Auto-scroll not available";
    public const string CaptureFailed = "Scrolling capture failed";

    /// <summary>Auto waits at most this long for the picture to settle after a step.</summary>
    public static readonly TimeSpan SettleMax = TimeSpan.FromMilliseconds(1500);
    /// <summary>Auto looks for a new frame this often while settling.</summary>
    public static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(15);
    /// <summary>The picture has settled when no new frame has come for this long (frames come only when the screen changes).</summary>
    public static readonly TimeSpan SettleQuiet = TimeSpan.FromMilliseconds(60);
    /// <summary>A step that has drawn no frame at all after this long moved nothing.</summary>
    public static readonly TimeSpan NoFrameMax = TimeSpan.FromMilliseconds(600);
    /// <summary>Done waits until no frame has come for this long (at most <see cref="DoneMax"/>), then feeds the newest frame again.</summary>
    public static readonly TimeSpan DoneQuiet = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan DoneMax = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DonePoll = TimeSpan.FromMilliseconds(20);

    private readonly AreaFramesRequest _request;
    private readonly IAreaFrames _frames;
    private readonly IScrollTarget _target;
    private readonly IActionPc _pc;
    private readonly IScrollClock _clock;
    private readonly ScrollStitcher _stitcher;
    private readonly object _gate = new();

    private AreaFrame? _latest;
    private long _seq;
    private TimeSpan _lastFrameAt;
    private long _startedAt, _firstFrameLogged;
    private AreaFrame? _processed;
    private bool _moved, _tooFast, _limit, _endReached, _autoActive, _closed, _failed, _cancelled, _doneTaken, _autoAvailable, _subscribed;
    private ScrollTargetInfo _info = ScrollTargetInfo.None(ScrollAvailability.NoWindow);
    private CancellationTokenSource? _autoCts;
    private Task _autoTask = Task.CompletedTask;

    public ScrollingController(
        AreaFramesRequest request, IAreaFrames frames, IScrollTarget target, IActionPc pc, IScrollClock clock, ScrollLimits? limits = null)
    {
        if (!request.IsValid)
            throw new ArgumentException("The area must be inside its display.", nameof(request));
        _request = request;
        _frames = frames;
        _target = target;
        _pc = pc;
        _clock = clock;
        _stitcher = new ScrollStitcher(request.Area.Width, request.Area.Height, limits);
    }

    /// <summary>Where the controller writes its timing spans (<c>CHIPPI_CAPTURE_TIMINGS</c>'s report), or null for none.</summary>
    internal Action<string>? Timings { get; init; }

    private void Span(string line) => Timings?.Invoke(line);

    /// <summary>Something the bar or preview draws changed. Raised outside the controller's lock, on any thread.</summary>
    public event Action? Changed;

    /// <summary>The capture ended and kept nothing: Windows refused it or the source was lost. Carries the source's reason, for the log.</summary>
    public event Action<string>? Failed;

    /// <summary>Whether Auto is offered, and the tooltip when it isn't (<see cref="AutoNotAvailable"/>).</summary>
    public bool AutoAvailable { get { lock (_gate) return _autoAvailable; } }

    public bool AutoActive { get { lock (_gate) return _autoActive; } }

    public ScrollingStatus Status { get { lock (_gate) return StatusLocked(); } }

    /// <summary>The result's size so far, physical pixels.</summary>
    public (int Width, int Height) Size { get { lock (_gate) return (_stitcher.Width, _stitcher.Height); } }

    /// <summary>Why Auto is off, for a tooltip or a log; Available when it's on.</summary>
    public ScrollAvailability AutoReason { get { lock (_gate) return _info.Availability; } }

    public ScrollingSnapshot Snapshot()
    {
        lock (_gate)
            return new ScrollingSnapshot(
                StatusLocked(), _stitcher.Width, _stitcher.Height, _moved, _autoAvailable, _autoActive,
                [.. _stitcher.Seams], _stitcher.ViewportTop, _stitcher.GapRow, _tooFast, _failed);
    }

    /// <summary>The result so far at <paramref name="width"/> pixels wide, for the preview strip.</summary>
    public CapturedImage Preview(int width)
    {
        lock (_gate)
            return _stitcher.ComposePreview(width);
    }

    /// <summary>
    /// Starts the frames and, alongside them, reads what Auto could scroll (UI Automation is slow, and the first frame must not
    /// wait for it): <see cref="AutoAvailable"/> fills in when the read ends, with a <see cref="Changed"/>. A refusal by
    /// Windows ends the capture (<see cref="Failed"/>) rather than throwing; cancelling throws. Call once.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        long begun = _startedAt = Stopwatch.GetTimestamp();
        lock (_gate) _subscribed = true;
        _frames.Frame += OnFrame;
        _frames.Failed += OnFailed;
        var find = Task.Run(() =>
        {
            ScrollTargetInfo info;
            try { info = _target.Find(_request.Area); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Chippi.Core.Diagnostics.Log.Warn("Scrolling capture: the scroll target couldn't be read", ex);
                info = ScrollTargetInfo.None(ScrollAvailability.NoWindow);
            }
            lock (_gate)
            {
                _info = info;
                _autoAvailable = info.IsAvailable && !_closed;
            }
            Span(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scroll-start find={Stopwatch.GetElapsedTime(begun).TotalMilliseconds:F1} auto={info.Availability}"));
            Raise();
        }, CancellationToken.None);
        try
        {
            await _frames.StartAsync(_request, cancellationToken).ConfigureAwait(false);
            Span(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scroll-start wgc={Stopwatch.GetElapsedTime(begun).TotalMilliseconds:F1}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        Raise();
        await find.ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- By hand

    private void OnFrame(AreaFrame frame)
    {
        lock (_gate)
        {
            if (_closed || _endReached || _limit)
                return;
            if (frame.Width != _stitcher.Width || frame.Height != _stitcher.FrameHeight)
                return;
            _latest = frame;
            _seq++;
            _lastFrameAt = _clock.Now;
            if (_firstFrameLogged == 0 && _startedAt != 0)
            {
                _firstFrameLogged = 1;
                Span(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scroll-start first-frame={Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds:F1}"));
            }
            if (_autoActive)
                return;
            Apply(_stitcher.Add(frame.Bgra), frame);
        }
        Raise();
    }

    private void OnFailed(string reason) => Fail(reason);

    private void Fail(string reason)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            _failed = true;
            cts = _autoCts;
        }
        cts?.Cancel();
        Failed?.Invoke(reason);
        Raise();
    }

    // Takes what the stitcher decided about a frame (call under the lock).
    private void Apply(ScrollStep step, AreaFrame frame)
    {
        _processed = frame;
        switch (step.Status)
        {
            case ScrollStatus.Accepted:
                _tooFast = false;
                if (_stitcher.Height > _stitcher.FrameHeight) _moved = true;
                break;
            case ScrollStatus.NoMove:
                _tooFast = false;
                break;
            case ScrollStatus.Gap:
                // Auto never shows a gap: it steps back and retries, and says so only if it gives up.
                _tooFast = !_autoActive;
                break;
            case ScrollStatus.Limit:
                _limit = true;
                _moved = true;
                _tooFast = false;
                break;
            // Ambiguous: a blank or torn frame. Nothing to say; the reference stays and the next frame with something in it matches.
        }
    }

    private ScrollingStatus StatusLocked()
    {
        if (_limit) return ScrollingStatus.LimitReached;
        if (_endReached) return ScrollingStatus.EndReached;
        if (_autoActive) return ScrollingStatus.Scrolling;
        if (_tooFast) return ScrollingStatus.TooFast;
        return _moved ? ScrollingStatus.None : ScrollingStatus.Scroll;
    }

    // ---------------------------------------------------------------- Auto

    /// <summary>
    /// Starts Auto, when it is offered and a first frame is in. Returns whether it started; the steps run on after this
    /// returns, until the end, <see cref="StopAutoAsync"/>, a failure or the limit.
    /// </summary>
    public Task<bool> StartAutoAsync()
    {
        lock (_gate)
        {
            if (_closed || _autoActive || _endReached || _limit || !_autoAvailable || _stitcher.Height == 0)
                return Task.FromResult(false);
        }
        // What the press found is used as it is: reading UI Automation again would delay the first step, and a pane that went
        // away answers null to the first read, which ends Auto.
        bool started = false;
        lock (_gate)
        {
            var info = _info;
            if (!info.IsAvailable || info.Pane is null)
                _autoAvailable = false;
            else if (!_closed && !_autoActive)
            {
                _autoActive = true;
                var cts = _autoCts = new CancellationTokenSource();
                _autoTask = Task.Run(() => RunAutoAsync(info, cts.Token));
                started = true;
            }
        }
        Raise();
        return Task.FromResult(started);
    }

    /// <summary>Completes when the Auto in progress (if any) has ended, however it ended.</summary>
    public Task WhenAutoEndsAsync()
    {
        lock (_gate) return _autoTask;
    }

    /// <summary>Stops Auto, keeps what was stitched and goes back to scrolling by hand. Completes when the loop has ended.</summary>
    public async Task StopAutoAsync()
    {
        Task task;
        lock (_gate)
        {
            _autoCts?.Cancel();
            task = _autoTask;
        }
        await task.ConfigureAwait(false);
    }

    private async Task RunAutoAsync(ScrollTargetInfo info, CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            await AutoStepsAsync(info, ct).ConfigureAwait(false);
            if (Timings is not null)
                Span(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"auto-run total={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} end={(_endReached ? "end" : _limit ? "limit" : "stopped")}"));
        }
        catch (OperationCanceledException)
        {
        }
        catch (ActionFailure)
        {
            lock (_gate) _autoAvailable = false;
        }
        finally
        {
            AutoEnded();
        }
    }

    // Auto ends: back to scrolling by hand, picking up whatever the screen shows now.
    private void AutoEnded()
    {
        lock (_gate)
        {
            _autoActive = false;
            if (!_closed && !_endReached && !_limit && _latest is { } latest && !ReferenceEquals(latest, _processed))
                Apply(_stitcher.Add(latest.Bgra), latest);
        }
        Raise();
    }

    private async Task AutoStepsAsync(ScrollTargetInfo info, CancellationToken ct)
    {
        var pane = info.Pane!;
        var center = ScrollRules.Center(_request.Area);
        nint window = info.WheelWindow;

        // The picture as it is now, settled, is the reference.
        var current = await SettleAsync(0, initial: true, ct).ConfigureAwait(false);
        if (current is null || pane.Read() is not { } reading)
        {
            lock (_gate) _autoAvailable = false;
            return;
        }
        ScrollStep step;
        lock (_gate)
        {
            step = _stitcher.Add(current.Bgra);
            Apply(step, current);
        }
        Raise();
        // The reading matches the stitcher's reference only when this frame is that reference.
        ScrollPosition? referenceReading = step.Status == ScrollStatus.Accepted || (step.Status == ScrollStatus.NoMove && step.Shift == 0) ? reading : null;
        if (step.Status is ScrollStatus.Gap or ScrollStatus.Ambiguous)
            return;
        if (StopForLimit() || EndIf(reading))
            return;

        double pixelsPerNotch = 0;
        // offset: the notches the page is below the reference frame now (what a step back must undo).
        int noMove = 0, maxNotches = ScrollingSteps.MaxNotches, net = 0, offset = 0, steps = 0;
        bool first = true, retry = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long before, t0 = Stopwatch.GetTimestamp();
            if (!retry)
            {
                int band, area;
                lock (_gate)
                {
                    band = Math.Max(1, _stitcher.FrameHeight - _stitcher.Layout.Header - _stitcher.Layout.Footer);
                    area = _stitcher.FrameHeight;
                    before = _seq;
                }
                net = first ? ScrollingSteps.First(area) : ScrollingSteps.Notches(pixelsPerNotch, band, maxNotches);
                _pc.PostWheel(window, center.X, center.Y, net);
                offset = net;
            }
            else
            {
                // The step overshot: come back up so the picture is half a step from the reference.
                int half = Math.Max(1, offset / 2);
                lock (_gate) before = _seq;
                _pc.PostWheel(window, center.X, center.Y, -(offset - half));
                offset = net = half;
            }

            var settled = await SettleAsync(before, initial: false, ct).ConfigureAwait(false);
            long t1 = Stopwatch.GetTimestamp();
            if (pane.Read() is not { } now)
            {
                // The window is gone, minimized or no longer answers: stop here and keep what was stitched.
                lock (_gate) { _autoAvailable = false; _tooFast = false; }
                return;
            }
            long t2 = Stopwatch.GetTimestamp();

            ScrollStatus status = ScrollStatus.NoMove;
            int shift = 0;
            if (settled is not null)
            {
                int? predicted = referenceReading is { } refRead ? now.ShiftSince(refRead) : null;
                lock (_gate)
                {
                    step = _stitcher.Add(settled.Bgra, predicted);
                    // A reading that is off (a pane whose percent is approximate) must not refuse a frame the stitcher can verify.
                    if (step.Status is ScrollStatus.Gap or ScrollStatus.Ambiguous && predicted is not null && !_stitcher.HasHeld)
                        step = _stitcher.Add(settled.Bgra);
                    Apply(step, settled);
                }
                Raise();
                status = step.Status;
                shift = step.Shift;
            }
            if (Timings is not null)
                Span(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"auto-step n={++steps} notches={net} retry={retry} status={status} shift={shift} settle={Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds:F1} read={Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds:F1} stitch={Stopwatch.GetElapsedTime(t2).TotalMilliseconds:F1}"));

            switch (status)
            {
                case ScrollStatus.Limit:
                    return;
                case ScrollStatus.Accepted when shift > 0:
                    noMove = 0;
                    first = false;
                    if (retry) maxNotches = Math.Max(1, net);
                    retry = false;
                    offset = 0;
                    if (net > 0) pixelsPerNotch = (double)shift / net;
                    referenceReading = now;
                    if (StopForLimit() || EndIf(now))
                        return;
                    break;
                case ScrollStatus.Gap or ScrollStatus.Ambiguous:
                    // Nothing is stitched over a doubt: one retry at half the step. A second miss gives up on Auto, after the
                    // page is brought back to the reference so no gap is left behind.
                    if (!retry && offset > 1)
                    {
                        retry = true;
                        break;
                    }
                    await GiveUpAsync(window, center, offset, ct).ConfigureAwait(false);
                    return;
                default:
                    // The page didn't move. A first step that moves nothing means this window ignores the wheel.
                    if (first)
                    {
                        if (EndIf(now))
                            return;
                        lock (_gate) _autoAvailable = false;
                        return;
                    }
                    noMove++;
                    retry = false;
                    offset = 0;
                    if (EndIf(now))
                        return;
                    if (noMove >= 2)
                    {
                        lock (_gate) _endReached = true;
                        return;
                    }
                    break;
            }
        }
    }

    // Auto can't step this page: the wheel goes back by what was posted since the reference, the picture settles and is checked
    // against the reference (so the gap clears), and Auto turns off ("Auto-scroll not available") for hand scrolling.
    private async Task GiveUpAsync(nint window, PixelPoint center, int offset, CancellationToken ct)
    {
        try
        {
            if (offset > 0)
            {
                long before;
                lock (_gate) before = _seq;
                _pc.PostWheel(window, center.X, center.Y, -offset);
                var settled = await SettleAsync(before, initial: false, ct).ConfigureAwait(false);
                if (settled is not null)
                    lock (_gate) Apply(_stitcher.Add(settled.Bgra), settled);
            }
        }
        finally
        {
            lock (_gate)
            {
                _autoAvailable = false;
                _tooFast = false;
            }
            Raise();
        }
    }

    private bool StopForLimit()
    {
        lock (_gate) return _limit;
    }

    // The pane reads 99.999% or more: the end.
    private bool EndIf(ScrollPosition reading)
    {
        if (!reading.IsEnd)
            return false;
        lock (_gate) _endReached = true;
        return true;
    }

    // Waits for the picture to settle after a step: a frame has come since the step (initially: any frame) and none has come
    // for SettleQuiet (frames come only when the screen changes), or SettleMax is up. Null when no frame came within NoFrameMax
    // (the page didn't move) or, initially, when none has come at all. The frame is the newest.
    private async Task<AreaFrame?> SettleAsync(long afterSeq, bool initial, CancellationToken ct)
    {
        if (initial)
        {
            lock (_gate)
                if (_latest is null) return null;
        }
        var start = _clock.Now;
        while (true)
        {
            await _clock.DelayAsync(SettlePoll, ct).ConfigureAwait(false);
            AreaFrame? newest;
            long seq;
            TimeSpan at;
            lock (_gate)
            {
                newest = _latest;
                seq = _seq;
                at = _lastFrameAt;
            }
            var now = _clock.Now;
            bool fresh = newest is not null && (initial || seq != afterSeq);
            if (fresh && now - at >= SettleQuiet)
                return newest;
            if (now - start >= SettleMax)
                return fresh ? newest : null;
            if (!fresh && now - start >= NoFrameMax)
                return null;
        }
    }

    // ---------------------------------------------------------------- Ending

    /// <summary>
    /// Done: stops Auto and the frames and returns what was stitched (also during "Too fast", which keeps what was stitched
    /// before the gap), or null when nothing was captured or the capture failed. Call from a worker for a tall result.
    /// </summary>
    public async Task<CapturedImage?> DoneAsync()
    {
        long t0 = Stopwatch.GetTimestamp();
        await StopAutoAsync().ConfigureAwait(false);
        long t1 = Stopwatch.GetTimestamp();
        await QuietAsync().ConfigureAwait(false);
        long t2 = Stopwatch.GetTimestamp();
        CapturedImage? image;
        lock (_gate)
        {
            if (_failed || _cancelled)
                return null;
            _closed = true;
            _doneTaken = true;
            image = _stitcher.Height > 0 ? _stitcher.Compose() : null;
        }
        long t3 = Stopwatch.GetTimestamp();
        // The frame source is let go after the image is handed back: stopping its capture thread takes tens to hundreds of ms.
        Unhook(background: true);
        Span(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"scroll-done stop-auto={Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds:F1} quiet={Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds:F1} compose={Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds:F1} size={image?.Width}x{image?.Height}"));
        return image;
    }

    // A repaint in pieces leaves the last frame half drawn, and the frames stop when it finishes. Wait until the stream has been
    // quiet for DoneQuiet (at most DoneMax from now; none at all when the last frame is already older than that), then feed the
    // newest frame again: a settled picture replaces whatever the tail held.
    private async Task QuietAsync()
    {
        long seq;
        TimeSpan lastChange;
        lock (_gate)
        {
            if (_failed || _cancelled || _closed || _endReached || _limit || _stitcher.Height == 0)
                return;
            seq = _seq;
            lastChange = _lastFrameAt;
        }
        var start = _clock.Now;
        while (_clock.Now - lastChange < DoneQuiet && _clock.Now - start < DoneMax)
        {
            await _clock.DelayAsync(DonePoll, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                if (_seq != seq)
                {
                    seq = _seq;
                    lastChange = _lastFrameAt;
                }
            }
        }
        lock (_gate)
        {
            if (!_closed && !_endReached && !_limit && _latest is { } latest)
                Apply(_stitcher.Add(latest.Bgra), latest);
        }
    }

    /// <summary>Cancel: stops everything and keeps nothing.</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _closed = true;
            _cancelled = !_doneTaken;
            cts = _autoCts;
        }
        cts?.Cancel();
        Unhook();
    }

    private void Unhook(bool background = false)
    {
        bool hooked;
        lock (_gate)
        {
            hooked = _subscribed;
            _subscribed = false;
        }
        if (!hooked)
            return;
        _frames.Frame -= OnFrame;
        _frames.Failed -= OnFailed;
        if (background)
            _ = Task.Run(_frames.Dispose);
        else
            _frames.Dispose();
    }

    public void Dispose() => Cancel();

    private void Raise() => Changed?.Invoke();
}
