// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;

namespace Chippi.App.Island;

/// <summary>
/// The tab equalizer's level while it names a song: the default output's live peak, read through the
/// shared audio service (a lease and a meter watch, 20 a second) only between <see cref="Start"/> and
/// <see cref="Stop"/>, raised on the UI thread. Starting the engine claims no device. When the
/// service can't read, no level comes and the bars hold still.
/// </summary>
internal sealed class TabLevel(AudioService audio)
{
    private IAsyncDisposable? _lease;
    private IDisposable? _watch;
    private string? _endpoint;

    public event EventHandler<float>? Level;

    public void Start()
    {
        if (_lease is not null)
            return;
        _lease = audio.Acquire();
        audio.SnapshotChanged += OnSnapshot;
        audio.MetersChanged += OnMeters;
        OnSnapshot(this, audio.Snapshot);
    }

    public void Stop()
    {
        if (_lease is not { } lease)
            return;
        audio.SnapshotChanged -= OnSnapshot;
        audio.MetersChanged -= OnMeters;
        _watch?.Dispose();
        (_watch, _endpoint, _lease) = (null, null, null);
        _ = lease.DisposeAsync().AsTask(); // the last lease stops the engine on its own thread
    }

    // The default output can change while music plays (headphones plugged in): watch the new one.
    private void OnSnapshot(object? sender, AudioSnapshot snapshot)
    {
        var id = snapshot.Default(AudioFlow.Render, AudioRole.Multimedia) ?? snapshot.Default(AudioFlow.Render, AudioRole.Console);
        if (id == _endpoint)
            return;
        _watch?.Dispose();
        (_watch, _endpoint) = (null, id);
        if (id is null)
            return;
        try
        {
            _watch = audio.WatchMeters([id], []);
        }
        catch (AudioFailure)
        {
            // The engine couldn't start: the bars hold still.
        }
    }

    private void OnMeters(object? sender, MeterReadings readings)
    {
        if (_endpoint is not null && readings.Endpoints.TryGetValue(_endpoint, out float peak))
            Level?.Invoke(this, peak);
    }
}

/// <summary>
/// The tab equalizer's three bar heights (0–1) from a stream of output peaks. The loudest recent peak
/// sets the scale, so quiet listening still fills the bars. The first follows the level quickly, the
/// middle smoothly, and the last jumps with each rise in level (the beat), so the bars move apart.
/// </summary>
internal sealed class EqualizerBars
{
    private const double Quiet = 0.05; // the scale never drops below this, so silence stays flat
    private readonly double[] _bars = new double[3];
    private double _loudest = Quiet;
    private double _last;
    private double _beat;

    public IReadOnlyList<double> Next(float peak)
    {
        _loudest = Math.Max(peak, _loudest * 0.995); // fades by half in about 7s of 20 readings a second
        double level = Math.Clamp(peak / Math.Max(_loudest, Quiet), 0, 1);
        _beat = Math.Max(Math.Clamp((level - _last) * 3, 0, 1), _beat * 0.6);
        _last = level;
        Ease(0, level * 0.8, up: 0.6, down: 0.3);
        Ease(1, level, up: 0.35, down: 0.12);
        Ease(2, Math.Max(_beat, level * 0.5), up: 0.7, down: 0.35);
        return _bars;
    }

    private void Ease(int bar, double target, double up, double down) =>
        _bars[bar] += (target - _bars[bar]) * (target > _bars[bar] ? up : down);
}
