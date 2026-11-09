// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Win32;

namespace Chippi.Media;

public sealed class WindowsRecordingBackend : IRecordingBackend
{
    private int _busy;
    internal Action<string>? Trace { get; init; }
    internal bool SkipBorderAccessForFixture { get; init; }
    public IReadOnlyList<RecordingSource> Sources(RecordingTarget target) => RecordingSources.Get(target);
    public RecordingDeviceNames Devices() => RecordingAudio.Devices();
    public async Task<IRecordingSession> StartAsync(RecordingSource source, RecordingOptions options, string partialPath, CancellationToken cancellationToken)
    {
        options.Validate();
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new NotSupportedException("The previous recorder is still closing. Try again in a moment.");
        Session? session = null;
        try
        {
        bool borderless = !SkipBorderAccessForFixture && await CaptureInterop.BorderlessAsync(cancellationToken).ConfigureAwait(false);
        Trace?.Invoke("Borderless access: " + borderless);
        session = new Session(source, options, partialPath, borderless, Trace, cancellationToken);
        _ = session.Completion.ContinueWith(done => { _ = done.Exception; Interlocked.Exchange(ref _busy, 0); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        session.Start();
        return await session.Ready.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (session == null) Interlocked.Exchange(ref _busy, 0);
            else _ = CleanupCancelledAsync(session);
            throw;
        }
    }
    private static async Task CleanupCancelledAsync(Session session)
    { try { await session.DisposeAsync().ConfigureAwait(false); } catch { /* The starter reports the error; cleanup still observes the worker. */ } }

    private sealed class Session(RecordingSource source, RecordingOptions options, string path, bool borderless, Action<string>? trace, CancellationToken cancellationToken) : IRecordingSession
    {
        private readonly RecordingClock _clock = new();
        private readonly AutoResetEvent _wake = new(false);
        private readonly object _framesGate = new();
        private readonly TaskCompletionSource<IRecordingSession> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<RecordingResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Direct3D11CaptureFrame? _latest;
        private Direct3D11CaptureFramePool? _pool;
        private GraphicsCaptureSession? _capture;
        private SizeInt32 _captureSize;
        private volatile bool _stopping, _pauseRequested;
        private long _dropped;
        private string? _warning;
        public Task<IRecordingSession> Ready => _ready.Task;
        public Task<RecordingResult> Completion => _done.Task;
        public TimeSpan Elapsed => _clock.Elapsed;
        public bool IsPaused => _pauseRequested;
        public event Action<string>? Warning;
        public event Action? SourceClosed;

        public void Start()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Chippi video encoder" };
            thread.SetApartmentState(ApartmentState.MTA);
            try { thread.Start(); } catch (Exception ex) { _ready.TrySetException(ex); _done.TrySetException(ex); throw; }
        }
        public void Pause() { if (_stopping || _pauseRequested) return; _pauseRequested = true; _clock.Pause(); _wake.Set(); }
        public void Resume() { if (_stopping || !_pauseRequested) return; _pauseRequested = false; _wake.Set(); }
        public Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
        {
            if (_done.Task.IsCompleted) return _done.Task.WaitAsync(cancellationToken);
            _stopping = true; _wake.Set();
            return _done.Task.WaitAsync(cancellationToken);
        }
        private void Report(string message) { _warning = message; Warning?.Invoke(message); }
        private void OnClosed(GraphicsCaptureItem item, object args)
        {
            Report("The recorded window or display closed");
            _stopping = true; _wake.Set(); SourceClosed?.Invoke();
        }
        private void OnFrame(Direct3D11CaptureFramePool pool, object args)
        {
            try
            {
                var frame = pool.TryGetNextFrame();
                if (frame == null) return;
                lock (_framesGate)
                {
                    if (_stopping || _clock.IsPaused) { frame.Dispose(); return; }
                    if (_latest != null) { _latest.Dispose(); Interlocked.Increment(ref _dropped); }
                    _latest = frame;
                }
                _wake.Set();
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException) { if (!_stopping) { Report("Screen capture stopped"); _stopping = true; _wake.Set(); } }
        }
        private Direct3D11CaptureFrame? TakeFrame() { lock (_framesGate) { var frame = _latest; _latest = null; return frame; } }

        private void StartCapture(RecordingGraphics graphics, GraphicsCaptureItem item)
        {
            _captureSize = item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(graphics.Projection, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _captureSize);
            _pool.FrameArrived += OnFrame;
            _capture = _pool.CreateCaptureSession(item);
            _capture.IsCursorCaptureEnabled = options.IncludeCursor;
            if (borderless) CaptureInterop.DisableBorder(_capture);
            CaptureInterop.FrameInterval(_capture, TimeSpan.FromSeconds(1d / options.FrameRate));
            _capture.StartCapture();
        }
        private void CloseCapture()
        {
            if (_pool != null) _pool.FrameArrived -= OnFrame;
            _capture?.Dispose(); _capture = null;
            _pool?.Dispose(); _pool = null;
            TakeFrame()?.Dispose();
        }

        private void Run()
        {
            bool initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(RoInitialize(1)); initialized = true;
                MediaStart();
                RecordingResult result;
                try { result = Record(); } finally { MediaStop(); }
                _done.TrySetResult(result);
            }
            catch (OperationCanceledException) { _ready.TrySetCanceled(cancellationToken); _done.TrySetCanceled(cancellationToken); }
            catch (Exception ex) { _ready.TrySetException(ex); _done.TrySetException(ex); }
            finally
            {
                _stopping = true; CloseCapture();
                if (!_ready.Task.IsCompletedSuccessfully && _done.Task.IsFaulted) _ = _done.Task.Exception;
                if (initialized) RoUninitialize();
            }
        }

        private RecordingResult Record()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows screen capture is not available.");
            var item = CaptureInterop.Item(source);
            trace?.Invoke("Capture item created: " + item.Size);
            item.Closed += OnClosed;
            try
            {
                int width = item.Size.Width & ~1, height = item.Size.Height & ~1;
                if (width < 2 || height < 2) throw new InvalidOperationException("The selected window has no recordable area.");
                using var graphics = new RecordingGraphics(width, height);
                bool audioEnabled = options.SystemAudio || options.MicAudio;
                trace?.Invoke("D3D device ready");
                using var writer = new RecordingWriter(path, graphics, options, audioEnabled, trace);
                using var audio = audioEnabled ? new RecordingAudio(_clock, options) : null;
                if (audio != null) audio.Warning += Report;
                trace?.Invoke("Starting WGC session"); _clock.Start(); StartCapture(graphics, item);
                using var cancelled = cancellationToken.Register(() => { _stopping = true; _wake.Set(); });
                _ready.TrySetResult(this);
                RecordingGraphics.Surface? last = null;
                long frames = 0, audioFrame = 0;
                var frameDuration = TimeSpan.FromSeconds(1d / options.FrameRate);
                TimeSpan lastWritten = -frameDuration;
                long nextVideoFrame = 0;
                try
                {
                    while (!_stopping)
                    {
                        if (_pauseRequested)
                        {
                            audio?.SetPaused(true);
                            if (_capture != null) CloseCapture();
                            _wake.WaitOne(100); continue;
                        }
                        if (_clock.IsPaused) _clock.Resume();
                        audio?.SetPaused(false);
                        if (_capture == null) StartCapture(graphics, item);
                        TimeSpan now = _clock.Elapsed;
                        if (lastWritten >= TimeSpan.Zero && now - lastWritten > TimeSpan.FromSeconds(3))
                        { Report("Recording stopped after the computer was unavailable"); _stopping = true; break; }
                        var due = TimeSpan.FromTicks(nextVideoFrame * TimeSpan.TicksPerSecond / options.FrameRate);
                        using (var frame = now >= due ? TakeFrame() : null)
                        {
                            if (frame != null && frame.ContentSize.Width > 0 && frame.ContentSize.Height > 0)
                            {
                                if (frame.SystemRelativeTime is { } capturedAt && _clock.AtSystemTime(capturedAt) is { })
                                {
                                    var next = graphics.Copy(frame.Surface, frame.ContentSize.Width, frame.ContentSize.Height);
                                    if (next != null) { last?.Return(); last = next; }
                                    else Interlocked.Increment(ref _dropped);
                                }
                            }
                            if (frame != null && frame.ContentSize != _captureSize)
                            {
                                if (source.Target == RecordingTarget.Screen) { Report("The display resolution changed; recording was stopped"); _stopping = true; continue; }
                                _captureSize = frame.ContentSize;
                                if (_captureSize.Width > 0 && _captureSize.Height > 0)
                                    _pool!.Recreate(graphics.Projection, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _captureSize);
                            }
                        }
                        if (last != null && now >= due)
                        {
                            writer.Video(last.Retain(), due); lastWritten = due; frames++; nextVideoFrame++;
                            long currentFrame = now.Ticks * options.FrameRate / TimeSpan.TicksPerSecond;
                            if (currentFrame > nextVideoFrame) { Interlocked.Add(ref _dropped, currentFrame - nextVideoFrame); nextVideoFrame = currentFrame; }
                        }
                        if (audio != null)
                        {
                            long available = Math.Max(0, (now - TimeSpan.FromMilliseconds(80)).Ticks) * 48_000 / TimeSpan.TicksPerSecond;
                            while (audioFrame + 960 <= available)
                            { audio.Read(audioFrame, 960, (bytes, at) => writer.Audio(bytes.Span, at)); audioFrame += 960; }
                        }
                        _wake.WaitOne(8);
                    }
                }
                catch (Exception ex) when (frames > 0 && ex is COMException or IOException)
                { Report("Recording stopped before completion"); }
                finally { CloseCapture(); }
                var duration = _clock.Elapsed;
                if (lastWritten >= TimeSpan.Zero && duration - lastWritten > TimeSpan.FromSeconds(3)) duration = lastWritten + frameDuration;
                try
                {
                    if (last != null && duration > lastWritten)
                    { writer.Video(last.Retain(), duration); frames++; }
                    if (audio != null)
                    {
                        long end = duration.Ticks * 48_000 / TimeSpan.TicksPerSecond;
                        while (audioFrame < end)
                        { int count = (int)Math.Min(960, end - audioFrame); audio.Read(audioFrame, count, (bytes, at) => writer.Audio(bytes.Span, at)); audioFrame += count; }
                    }
                    writer.Finish();
                }
                finally { last?.Return(); }
                if (frames == 0) throw new InvalidOperationException("No video frames were captured. Try a visible window or another display.");
                var output = options.OutputSize(width, height);
                return new(path, duration, output.Width, output.Height, frames, Interlocked.Read(ref _dropped), writer.Encoder, _warning);
            }
            finally { item.Closed -= OnClosed; }
        }

        private static unsafe void MediaStart() => PInvoke.MFStartup(0x20070, 0).ThrowOnFailure();
        private static unsafe void MediaStop() => PInvoke.MFShutdown().ThrowOnFailure();
        private int _disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { await StopAsync().ConfigureAwait(false); }
            finally { _wake.Dispose(); }
        }
        [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
        [DllImport("combase.dll")] private static extern void RoUninitialize();
    }
}
