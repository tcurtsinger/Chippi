// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio.Soundboard;

public sealed record SoundboardPlaybackState(string? ClipId, bool IsPlaying, bool IsPreview,
    TimeSpan Position, TimeSpan Duration, string? Error = null)
{
    public static SoundboardPlaybackState Idle { get; } = new(null, false, false, TimeSpan.Zero, TimeSpan.Zero);
}

public interface ISoundboardPlayer : IAsyncDisposable
{
    SoundboardPlaybackState State { get; }
    event Action? Changed;
    Task InitializeAsync(CancellationToken ct = default);
    Task PlayAsync(string clipId, string path, TimeSpan start, TimeSpan end, bool preview = false, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SetLevelsAsync(float send, float hear, CancellationToken ct = default);
}

/// <summary>The route contains only explicit output IDs. A local preview never receives a cable ID.</summary>
public sealed record SoundboardPlaybackRequest(string Path, TimeSpan Start, TimeSpan End,
    string? SendDeviceId, string? MonitorDeviceId, float Send, float Hear);

/// <summary>All members except Stopped run on the shared audio engine thread.</summary>
public interface ISoundboardPlayback : IDisposable
{
    TimeSpan Position { get; }
    event Action<Exception?>? Stopped;
    void Start();
    void SetLevels(float send, float hear);
}

public delegate ISoundboardPlayback SoundboardPlaybackFactory(SoundboardPlaybackRequest request);

/// <summary>
/// One prepared clip, with independent send and monitor streams. Commands, device handoffs and
/// stream teardown share AudioService's engine thread; audio callbacks only enqueue work.
/// </summary>
public sealed class SoundboardPlayer : ISoundboardPlayer
{
    private sealed record Clip(string Id, string Path, TimeSpan Start, TimeSpan End, bool Preview);

    private readonly AudioService _audio;
    private readonly RoleTarget _voiceSource;
    private readonly SoundboardPlaybackFactory _factory;
    private readonly SynchronizationContext? _context;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ITimer _timer;
    private IAsyncDisposable? _lease;
    private ISoundboardPlayback? _playback;
    private Clip? _clip;
    private string? _sendDevice, _monitorDevice;
    private TimeSpan _offset;
    private float _send = .8f, _hear = .6f;
    private volatile bool _disposed;
    private volatile SoundboardPlaybackState _state = SoundboardPlaybackState.Idle;
    private int _polling;
    private long _requestRevision;

    public SoundboardPlayer(AudioService audio, RoleTarget? voiceSource = null,
        SoundboardPlaybackFactory? factory = null, TimeProvider? time = null)
    {
        _audio = audio;
        _voiceSource = voiceSource ?? (active => active.FirstOrDefault()?.Id);
        _factory = factory ?? WasapiSoundboardPlayback.Create;
        _context = SynchronizationContext.Current;
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => Poll(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public SoundboardPlaybackState State => _state;
    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lease is not null) return;
            ct.ThrowIfCancellationRequested();
            _lease = _audio.Acquire();
            try
            {
                await _audio.SetSoundboardVoiceAsync(_voiceSource).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                _audio.SnapshotChanged += OnSnapshot;
            }
            catch
            {
                try { await _audio.SetSoundboardVoiceAsync(null).ConfigureAwait(false); }
                finally { await _lease.DisposeAsync().ConfigureAwait(false); _lease = null; }
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task PlayAsync(string clipId, string path, TimeSpan start, TimeSpan end, bool preview = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (start < TimeSpan.Zero || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "Choose a nonempty clip range.");
        long revision = Interlocked.Increment(ref _requestRevision);
        await InitializeAsync(ct).ConfigureAwait(false);
        await _audio.InvokeAsync(_ =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (revision != Interlocked.Read(ref _requestRevision)) return false;
            try
            {
                StopCore();
                _clip = new(clipId, path, start, end, preview);
                _offset = TimeSpan.Zero;
                StartCore(Route(_audio.Snapshot, preview));
                ct.ThrowIfCancellationRequested();
                Publish(new(clipId, true, preview, TimeSpan.Zero, end - start));
                _timer.Change(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            }
            catch (OperationCanceledException)
            {
                try { StopCore(); }
                finally { Publish(SoundboardPlaybackState.Idle); }
                throw;
            }
            catch (Exception ex)
            {
                try { StopCore(); }
                finally { Publish(new(clipId, false, preview, TimeSpan.Zero, end - start, AudioErrors.Reason(ex))); }
                throw;
            }
            return true;
        }).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        long revision = Interlocked.Increment(ref _requestRevision);
        if (_disposed || _lease is null) return;
        await _audio.InvokeAsync(_ =>
        {
            ct.ThrowIfCancellationRequested();
            if (revision != Interlocked.Read(ref _requestRevision)) return false;
            try { StopCore(); }
            catch (Exception ex)
            {
                Publish(_state with { IsPlaying = false, Error = AudioErrors.Reason(ex) });
                throw;
            }
            Publish(SoundboardPlaybackState.Idle);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task SetLevelsAsync(float send, float hear, CancellationToken ct = default)
    {
        if (!float.IsFinite(send) || !float.IsFinite(hear)) throw new ArgumentOutOfRangeException(nameof(send));
        await InitializeAsync(ct).ConfigureAwait(false);
        await _audio.InvokeAsync(_ =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            _send = Math.Clamp(send, 0, 1);
            _hear = Math.Clamp(hear, 0, 1);
            _playback?.SetLevels(_send, _hear);
            return true;
        }).ConfigureAwait(false);
    }

    private static (string? Send, string? Monitor) Route(AudioSnapshot snapshot, bool preview)
    {
        string? monitor = snapshot.Default(AudioFlow.Render, AudioRole.Multimedia)
            ?? snapshot.Default(AudioFlow.Render, AudioRole.Console);
        if (monitor is null || snapshot.Find(monitor) is not { IsActive: true } output || VbCable.IsCable(output))
            monitor = null; // Never guess another speaker or feed a monitor stream back into the cable.
        if (preview)
            return monitor is null ? throw new AudioFailure("No playback device") : (null, monitor);

        var (state, pair) = VbCable.Find(snapshot.Endpoints);
        if (state != CableState.Ready)
            throw new AudioFailure(state switch
            {
                CableState.Disabled => "Virtual mic is disabled in Windows",
                CableState.Ambiguous => "Choose one virtual mic in Windows",
                _ => "Set up the virtual mic to play sounds",
            });
        if (snapshot.Levels.TryGetValue(pair!.CaptureId, out var capture) && capture.Muted)
            throw new AudioFailure("Virtual mic is muted in Windows");
        if (snapshot.Levels.TryGetValue(pair.RenderId, out var render) && render.Muted)
            throw new AudioFailure("Virtual mic input is muted in Windows");
        return (pair.RenderId, monitor);
    }

    private void StartCore((string? Send, string? Monitor) route)
    {
        var clip = _clip!;
        var playback = _factory(new(clip.Path, clip.Start + _offset, clip.End,
            route.Send, route.Monitor, _send, _hear));
        _playback = playback;
        _sendDevice = route.Send;
        _monitorDevice = route.Monitor;
        playback.Stopped += error => Complete(playback, error);
        playback.Start();
    }

    private void OnSnapshot(object? sender, AudioSnapshot snapshot) => ReconcileRoute();

    private async void ReconcileRoute()
    {
        if (_disposed || !_state.IsPlaying) return;
        try
        {
            await _audio.InvokeAsync(_ =>
            {
                if (_disposed || _playback is null || _clip is null) return false;
                try
                {
                    var route = Route(_audio.Snapshot, _clip.Preview);
                    if (PriorityPolicy.Eq(route.Send, _sendDevice) && PriorityPolicy.Eq(route.Monitor, _monitorDevice)) return false;
                    var position = Position();
                    DisposePlayback();
                    _offset = position;
                    if (_clip.Start + _offset >= _clip.End) Finish(null);
                    else StartCore(route);
                }
                catch (Exception ex) { Finish(ex); }
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { if (!_disposed) Publish(_state with { IsPlaying = false, Error = AudioErrors.Reason(ex) }); }
    }

    private async void Complete(ISoundboardPlayback playback, Exception? error)
    {
        if (_disposed) return;
        try
        {
            await _audio.InvokeAsync(_ =>
            {
                if (!_disposed && ReferenceEquals(_playback, playback)) Finish(error);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { if (!_disposed) Publish(_state with { IsPlaying = false, Error = AudioErrors.Reason(ex) }); }
    }

    private async void Poll()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            await _audio.InvokeAsync(_ =>
            {
                if (!_disposed && _playback is not null && _clip is not null)
                    Publish(_state with { Position = Position() });
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { if (!_disposed) Publish(_state with { IsPlaying = false, Error = AudioErrors.Reason(ex) }); }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private TimeSpan Position() => _clip is null ? TimeSpan.Zero
        : TimeSpan.FromTicks(Math.Clamp((_offset + (_playback?.Position ?? TimeSpan.Zero)).Ticks, 0, (_clip.End - _clip.Start).Ticks));

    private void Finish(Exception? error)
    {
        var state = _state;
        var position = Position();
        StopCore();
        Publish(state with { IsPlaying = false, Position = error is null ? state.Duration : position,
            Error = error is null ? null : AudioErrors.Reason(error) });
    }

    private void DisposePlayback()
    {
        var previous = _playback;
        _playback = null; // A stopped callback from teardown cannot finish a replacement clip.
        previous?.Dispose();
    }

    private void StopCore()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try { DisposePlayback(); }
        finally
        {
            _clip = null;
            _sendDevice = _monitorDevice = null;
            _offset = TimeSpan.Zero;
        }
    }

    private void Publish(SoundboardPlaybackState state)
    {
        _state = state;
        if (_context is null) Changed?.Invoke();
        else _context.Post(_ => { if (!_disposed) Changed?.Invoke(); }, null);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _audio.SnapshotChanged -= OnSnapshot;
            try
            {
                if (_lease is not null)
                {
                    try
                    {
                        try { await _audio.InvokeAsync(_ => { StopCore(); return true; }).ConfigureAwait(false); }
                        finally { await _audio.SetSoundboardVoiceAsync(null).ConfigureAwait(false); }
                    }
                    finally { await _lease.DisposeAsync().ConfigureAwait(false); _lease = null; }
                }
            }
            finally
            {
                _state = SoundboardPlaybackState.Idle;
                _timer.Dispose();
            }
        }
        finally { _lifecycle.Release(); }
    }
}
