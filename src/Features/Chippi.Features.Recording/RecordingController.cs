// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Media;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Features.Recording;

/// <summary>Where a recording is, from the starter to the page closing.</summary>
public enum RecordingPhase
{
    Idle,

    /// <summary>The selector is up in record mode.</summary>
    Choosing,

    /// <summary>Selected: the start bar shows MP4 or GIF, System sound, Mic, Start and Cancel. Nothing records yet.</summary>
    Ready,

    /// <summary>The 3-second count after Start; Esc cancels.</summary>
    Counting,

    Recording,

    Paused,

    /// <summary>Stop was pressed: the file is being finished.</summary>
    Finishing,

    /// <summary>Stopped: the recording page has it (poster, play, trim, Discard, Export GIF, Save as, Save).</summary>
    Review,
}

/// <summary>
/// One recording at a time (docs/phase-7/screen-recording.md), and the model every recording screen binds to: the start
/// bar, the pill, the Capture panel's recording card and the recording page. Call on the UI thread; <see cref="Changed"/>
/// is raised there.
/// </summary>
public sealed class RecordingController : IDisposable
{
    /// <summary>The count between Start and the first frame.</summary>
    public const int CountSeconds = 3;

    private readonly ICaptureLayer _capture;
    private readonly IRecordingPc _pc;
    private readonly IRecordingShell _shell;
    private readonly TimeProvider _time;
    private readonly Func<RecordingSettings> _settings;
    private readonly Recorder _recorder;
    private readonly ITimer _tick;
    private ICaptureSession? _session;
    private Selection? _selection;
    private Countdown? _countdown;
    private IRecordingOverlay? _overlay;
    private CancellationTokenSource? _work;
    private int _shown = -1;
    private DateTimeOffset _started;

    public RecordingController(ICaptureLayer capture, IRecordingPc pc, IRecordingShell shell, Func<RecordingSettings> settings, TimeProvider? time = null)
    {
        _capture = capture;
        _pc = pc;
        _shell = shell;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        _recorder = new Recorder(pc.Writers, _time);
        _recorder.StoppedItself += result => _shell.Post(() => _ = FinishedAsync(result));
        _tick = _time.CreateTimer(_ => _shell.Post(ShowElapsed), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public RecordingPhase Phase { get; private set; }

    public RecordingSource Source { get; private set; }

    public RecordingFormat Format { get; private set; }

    /// <summary>Off at every start (task 487).</summary>
    public bool SystemSound { get; private set; }

    /// <summary>Off at every start (task 487).</summary>
    public bool Mic { get; private set; }

    /// <summary>The seconds left while <see cref="RecordingPhase.Counting"/>.</summary>
    public int CountLeft { get; private set; }

    /// <summary>The file's length so far; it stands still while paused.</summary>
    public TimeSpan Elapsed => _recorder.Elapsed;

    /// <summary>On the page: the recording's length.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>On the page: its first frame.</summary>
    public BitmapSource? Poster { get; private set; }

    /// <summary>On the page: the temporary MP4, for Play.</summary>
    public string? File { get; private set; }

    /// <summary>On the page: what Save, Save as and Export GIF write.</summary>
    public TrimRange Trim { get; private set; }

    /// <summary>A GIF of the trimmed span takes only its first 60 seconds: the page says "GIF is limited to 60 seconds" first.</summary>
    public bool GifCut => Trim.Length > Chippi.Media.Gif.GifPlan.Limit;

    /// <summary>"Trimming" or "Exporting" while a re-encode runs (with <see cref="Progress"/> and Cancel); null otherwise.</summary>
    public string? Working { get; private set; }

    public double Progress { get; private set; }

    /// <summary>Where Save last wrote this recording; null until saved.</summary>
    public string? SavedTo { get; private set; }

    /// <summary>Why the last start failed, for Settings ("Recording failed" on the bar); null after one works.</summary>
    public string? LastError { get; private set; }

    public bool IsRecording => Phase is RecordingPhase.Recording or RecordingPhase.Paused or RecordingPhase.Finishing;

    /// <summary>A screenshot's freeze would cover the region being recorded: the panel dims those verbs.</summary>
    public bool Busy => Phase is not (RecordingPhase.Idle or RecordingPhase.Review);

    public event Action? Changed;

    /// <summary>"0:42", "1:02:05".</summary>
    public static string Clock(TimeSpan t) => t.TotalHours >= 1
        ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");

    public static string SourceName(RecordingSource source) => source switch
    {
        RecordingSource.Window => "Window",
        RecordingSource.Screen => "Screen",
        _ => "Region",
    };

    // ---- Start ----

    /// <summary>
    /// A starter: the selector in record mode. "Already recording" while one runs; nothing while a capture is up. A
    /// recording still on the page is saved first, so a new one never loses it.
    /// </summary>
    public async Task<bool> BeginAsync(RecordingSource source, bool gif, CaptureDoor door)
    {
        if (Phase is RecordingPhase.Recording or RecordingPhase.Paused or RecordingPhase.Finishing or RecordingPhase.Counting)
        {
            _shell.Feedback("Already recording", SourceName(Source), failed: false);
            return false;
        }
        if (Phase is RecordingPhase.Choosing or RecordingPhase.Ready || _capture.Current is not null)
            return false;
        if (_pc.Missing is { } missing)
        {
            LastError = missing;
            _shell.Feedback("Recording failed", SourceName(source), failed: true);
            Raise();
            return false;
        }
        if (Phase == RecordingPhase.Review)
            await LeaveReviewAsync();
        var mode = source switch { RecordingSource.Window => CaptureMode.Window, RecordingSource.Screen => CaptureMode.Screen, _ => CaptureMode.Region };
        var session = _capture.Start(new CaptureRequest(mode) { Door = door, OneDisplay = true });
        if (session is null)
            return false;
        _session = session;
        _selection = null;
        Source = source;
        Format = gif ? RecordingFormat.Gif : _settings().Format;
        SystemSound = Mic = false;
        Phase = RecordingPhase.Choosing;
        session.Selected += selection =>
        {
            _selection = selection;
            if (Phase != RecordingPhase.Choosing) return;
            Phase = RecordingPhase.Ready;
            Raise();
            BarsWanted?.Invoke(session);
        };
        session.KeyDown += OnKey;
        session.Closed += _ =>
        {
            if (!ReferenceEquals(_session, session)) return;
            _session = null;
            if (Phase is RecordingPhase.Choosing or RecordingPhase.Ready or RecordingPhase.Counting)
                Cancel();
        };
        Raise();
        // Screen mode is selected as the overlay shows.
        if (session.Selection is { } now)
            _selection = now;
        return true;
    }

    /// <summary>
    /// The selection is released: the module puts the start bar under it (<see cref="ICaptureSession.SetBars"/>). Lumen's
    /// start bar binds to this controller.
    /// </summary>
    public event Action<ICaptureSession>? BarsWanted;

    private void OnKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Phase == RecordingPhase.Ready:
                e.Handled = true;
                Start();
                break;
            case Key.Escape:
                e.Handled = true;
                Cancel();
                break;
        }
    }

    public void SetFormat(RecordingFormat format)
    {
        if (Phase != RecordingPhase.Ready || Format == format) return;
        Format = format;
        Raise();
    }

    public void SetSystemSound(bool on)
    {
        if (Phase != RecordingPhase.Ready || SystemSound == on) return;
        SystemSound = on;
        Raise();
    }

    public void SetMic(bool on)
    {
        if (Phase != RecordingPhase.Ready || Mic == on) return;
        Mic = on;
        Raise();
    }

    /// <summary>Start on the bar, or Enter: the count, then the recording.</summary>
    public void Start()
    {
        if (Phase != RecordingPhase.Ready || _selection is null) return;
        Phase = RecordingPhase.Counting;
        _countdown = new Countdown(CountSeconds, _time,
            left => _shell.Post(() => { if (Phase != RecordingPhase.Counting) return; CountLeft = left; Raise(); }),
            () => _shell.Post(Go));
    }

    /// <summary>Cancel on the bar, or Esc, before the recording starts: the overlay closes and nothing is kept.</summary>
    public void Cancel()
    {
        if (Phase is not (RecordingPhase.Choosing or RecordingPhase.Ready or RecordingPhase.Counting)) return;
        _countdown?.Cancel();
        _countdown = null;
        Phase = RecordingPhase.Idle;
        CountLeft = 0;
        var session = _session;
        _session = null;
        session?.Close(CaptureEnd.Cancelled);
        Raise();
    }

    private void Go()
    {
        if (Phase != RecordingPhase.Counting || _selection is not { } selection) return;
        _countdown = null;
        CountLeft = 0;
        var session = _session;
        _session = null;
        // The overlays are left out of captures, so the recording can start as they go.
        session?.Close(CaptureEnd.Finished);
        var target = TargetOf(selection);
        var settings = _settings();
        IVideoSource video;
        try
        {
            video = target.Window != 0
                ? _pc.Window(target.Window, settings.Cursor)
                : _pc.Display(target.Monitor, Relative(target), settings.Cursor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Refuse(target.Window != 0 ? "Can't record this window" : "Recording failed", ex.Message);
            return;
        }
        var audio = new List<IAudioSource>();
        try
        {
            if (SystemSound) audio.Add(_pc.SystemSound());
            if (Mic) audio.Add(_pc.Mic());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            video.Dispose();
            foreach (var a in audio) a.Dispose();
            Refuse("Recording failed", ex.Message);
            return;
        }
        var path = _pc.TempFile();
        int fps = Array.IndexOf(RecordingSettings.FrameRates, settings.FrameRate) >= 0 ? settings.FrameRate : 30;
        var started = _recorder.Start(new RecordingRequest(video, audio, path, fps));
        if (started != StartResult.Started)
        {
            video.Dispose();
            foreach (var a in audio) a.Dispose();
            Refuse("Recording failed", _recorder.LastError ?? "The recording couldn't start.");
            return;
        }
        LastError = null;
        _started = _time.GetUtcNow();
        Phase = RecordingPhase.Recording;
        _shown = -1;
        _shell.Recording(true);
        _overlay = _pc.ShowOverlay(target, this);
        _tick.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        Raise();
    }

    private void Refuse(string title, string why)
    {
        LastError = why;
        Phase = RecordingPhase.Idle;
        _shell.Feedback(title, SourceName(Source), failed: true);
        Raise();
    }

    internal static RecordingTarget TargetOf(Selection selection)
    {
        var source = selection.Mode switch
        {
            CaptureMode.Window => RecordingSource.Window,
            CaptureMode.Screen => RecordingSource.Screen,
            _ => RecordingSource.Region,
        };
        var display = selection.Display;
        // Only Record window follows a window; a window clicked in Region mode is recorded as the region it covered.
        nint window = source == RecordingSource.Window && selection.Window is { } w ? w.Handle : 0;
        return new RecordingTarget(source, selection.Rect.Intersect(display.Bounds), display.Bounds, display.Monitor, display.Dpi, window);
    }

    /// <summary>The region in its display's own pixels; null for the whole display.</summary>
    internal static Chippi.Media.Capture.PixelRect? Relative(RecordingTarget target)
    {
        if (target.Rect == target.Display) return null;
        return new Chippi.Media.Capture.PixelRect(target.Rect.X - target.Display.X, target.Rect.Y - target.Display.Y, target.Rect.Width, target.Rect.Height);
    }

    // ---- While recording ----

    public void Pause()
    {
        if (Phase != RecordingPhase.Recording || !_recorder.Pause()) return;
        Phase = RecordingPhase.Paused;
        ShowElapsed();
        Raise();
    }

    public void Resume()
    {
        if (Phase != RecordingPhase.Paused || !_recorder.Resume()) return;
        Phase = RecordingPhase.Recording;
        ShowElapsed();
        Raise();
    }

    /// <summary>Stop on the pill, the card, or Stop recording: "Not recording" when nothing is.</summary>
    public async Task StopAsync()
    {
        if (Phase is RecordingPhase.Counting)
        {
            Cancel();
            return;
        }
        if (Phase is not (RecordingPhase.Recording or RecordingPhase.Paused))
        {
            if (Phase != RecordingPhase.Finishing)
                _shell.Feedback("Not recording", "Recording", failed: false);
            return;
        }
        Phase = RecordingPhase.Finishing;
        EndLive();
        Raise();
        if (await _recorder.StopAsync() is { } result)
            await FinishedAsync(result);
    }

    /// <summary>
    /// Discard, after "Discard recording?": while recording, or on the page. Deletes the temporary file only; a copy
    /// Save already wrote stays.
    /// </summary>
    public async Task DiscardAsync()
    {
        if (Phase is RecordingPhase.Recording or RecordingPhase.Paused)
        {
            Phase = RecordingPhase.Finishing;
            EndLive();
            Raise();
            await _recorder.DiscardAsync();
            Phase = RecordingPhase.Idle;
            Raise();
            return;
        }
        if (Phase != RecordingPhase.Review) return;
        _work?.Cancel();
        DropReview();
        Raise();
    }

    private void ShowElapsed()
    {
        if (Phase is not (RecordingPhase.Recording or RecordingPhase.Paused)) return;
        var elapsed = Elapsed;
        bool paused = Phase == RecordingPhase.Paused;
        // The second, and whether it's paused, as the line last showed them.
        int shown = (int)elapsed.TotalSeconds * 2 + (paused ? 1 : 0);
        if (shown == _shown) return;
        _shown = shown;
        _shell.Live(Clock(elapsed), (paused ? "Paused · " : "Recording · ") + SourceName(Source));
        Raise();
    }

    private void EndLive()
    {
        _tick.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _overlay?.Dispose();
        _overlay = null;
        _shell.Live(null, null);
        _shell.Recording(false);
    }

    // The recorder finished the file: after Stop, or by itself (the window closed, the display changed, the disk filled).
    private async Task FinishedAsync(RecordingResult result)
    {
        bool itself = result.Stopped is not null;
        if (itself)
        {
            if (Phase is not (RecordingPhase.Recording or RecordingPhase.Paused)) return;
            EndLive();
        }
        if (!result.Playable || result.Frames == 0)
        {
            TryDelete(result.Path);
            LastError = result.Stopped;
            Phase = RecordingPhase.Idle;
            _shell.Feedback("Recording failed", SourceName(Source), failed: true);
            Raise();
            return;
        }
        File = result.Path;
        Duration = result.Duration;
        Trim = TrimRange.Whole(result.Duration);
        Poster = _pc.Poster(result.Path);
        SavedTo = null;
        Phase = RecordingPhase.Review;
        Raise();
        string line = $"{Clock(Duration)} · {SourceName(Source)}";
        if (itself)
            _shell.Feedback("Recording stopped", line, failed: true, clicked: () => _shell.OpenReview());
        // Over a fullscreen app nothing opens: it's saved and in Recent, and the bar says so (surfaces.md).
        if (!_shell.OpenReview() && await SaveAsync() && !itself)
            _shell.Feedback("Recording saved", line, failed: false, clicked: () => _shell.OpenReview());
    }

    // ---- The recording page ----

    /// <summary>The trim bar's handles moved.</summary>
    public void SetTrim(TrimRange span)
    {
        if (Phase != RecordingPhase.Review || Working is not null) return;
        Trim = TrimRange.Clamp(span.Start, span.End, Duration);
        Raise();
    }

    /// <summary>The name Save gives this recording in the Save to folder: "Recording 2026-11-07 142530.mp4".</summary>
    public string FileName(RecordingFormat format) =>
        string.Create(CultureInfo.InvariantCulture, $"Recording {_started.ToLocalTime():yyyy-MM-dd HHmmss}{(format == RecordingFormat.Gif ? ".gif" : ".mp4")}");

    public string SaveFolder => _settings().SaveTo is { Length: > 0 } folder ? folder : _pc.DefaultSaveFolder;

    /// <summary>Save: the recording's format, trimmed span, to the Save to folder without asking.</summary>
    public async Task<bool> SaveAsync()
    {
        if (Phase != RecordingPhase.Review) return false;
        string folder = SaveFolder;
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.Feedback("Save failed", SourceName(Source), failed: true);
            return false;
        }
        return await WriteAsync(Unique(Path.Combine(folder, FileName(Format))), Format);
    }

    /// <summary>Save as: <paramref name="path"/> from the dialog; its extension picks MP4 or GIF.</summary>
    public Task<bool> SaveAsAsync(string path) =>
        WriteAsync(path, string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase) ? RecordingFormat.Gif : RecordingFormat.Mp4);

    /// <summary>Export GIF: the trimmed span (its first 60 seconds) as a GIF beside where Save puts recordings.</summary>
    public async Task<bool> ExportGifAsync()
    {
        if (Phase != RecordingPhase.Review) return false;
        try { Directory.CreateDirectory(SaveFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.Feedback("Export failed", SourceName(Source), failed: true);
            return false;
        }
        return await WriteAsync(Unique(Path.Combine(SaveFolder, FileName(RecordingFormat.Gif))), RecordingFormat.Gif);
    }

    /// <summary>Cancel while trimming or exporting: only the partial file goes.</summary>
    public void CancelWork() => _work?.Cancel();

    private async Task<bool> WriteAsync(string target, RecordingFormat format)
    {
        if (Phase != RecordingPhase.Review || File is not { } source || Working is not null) return false;
        var span = Trim;
        bool whole = span.IsWhole(Duration);
        using var work = _work = new CancellationTokenSource();
        Working = format == RecordingFormat.Gif ? "Exporting" : "Trimming";
        Progress = 0;
        Raise();
        var progress = new Progress<double>(p => _shell.Post(() => { if (Working is null) return; Progress = p; Raise(); }));
        bool ok;
        try
        {
            if (format == RecordingFormat.Gif)
                await _pc.GifAsync(source, target, span, progress, work.Token);
            else if (whole)
                await Task.Run(() => System.IO.File.Copy(source, target, overwrite: true), work.Token);
            else
                await _pc.TrimAsync(source, target, span, progress, work.Token);
            ok = true;
        }
        catch (OperationCanceledException)
        {
            ok = false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ok = false;
            _shell.Feedback(format == RecordingFormat.Gif ? "Export failed" : "Save failed", SourceName(Source), failed: true);
        }
        _work = null;
        Working = null;
        Progress = 0;
        if (ok)
        {
            SavedTo = target;
            _shell.AddToRecent(target, Poster, span.Length > Chippi.Media.Gif.GifPlan.Limit && format == RecordingFormat.Gif ? Chippi.Media.Gif.GifPlan.Limit : span.Length, _started);
        }
        Raise();
        return ok;
    }

    private async Task LeaveReviewAsync()
    {
        if (SavedTo is null)
            await SaveAsync();
        DropReview();
    }

    private void DropReview()
    {
        if (File is { } file) TryDelete(file);
        File = null;
        Poster = null;
        SavedTo = null;
        Duration = TimeSpan.Zero;
        Trim = default;
        Phase = RecordingPhase.Idle;
    }

    /// <summary>The page closed with the recording kept or discarded: its temporary file goes.</summary>
    public void Close()
    {
        if (Phase != RecordingPhase.Review || Working is not null) return;
        DropReview();
        Raise();
    }

    private static string Unique(string path)
    {
        if (!System.IO.File.Exists(path)) return path;
        string folder = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            string next = Path.Combine(folder, $"{name} ({n}){ext}");
            if (!System.IO.File.Exists(next)) return next;
        }
    }

    private static void TryDelete(string path)
    {
        try { System.IO.File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Raise() => Changed?.Invoke();

    /// <summary>
    /// Quitting Chippi or removing the feature: a running recording is finished and saved (the quit confirm, "Stop
    /// recording and quit?", asks first); one on the page is kept only if it was saved.
    /// </summary>
    public async Task ShutDownAsync()
    {
        Cancel();
        if (Phase is RecordingPhase.Recording or RecordingPhase.Paused)
        {
            Phase = RecordingPhase.Finishing;
            EndLive();
            if (await _recorder.StopAsync() is { Playable: true, Frames: > 0 } result)
            {
                File = result.Path;
                Duration = result.Duration;
                Trim = TrimRange.Whole(result.Duration);
                Phase = RecordingPhase.Review;
                await SaveAsync();
            }
        }
        _work?.Cancel();
        if (Phase == RecordingPhase.Review && Working is null)
            DropReview();
        Phase = RecordingPhase.Idle;
    }

    public void Dispose()
    {
        _tick.Dispose();
        _countdown?.Cancel();
        _overlay?.Dispose();
        _recorder.Dispose();
    }
}
