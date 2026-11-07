// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

/// <summary>A run of mixed audio: 48 kHz stereo float, interleaved, starting at <see cref="Time"/> in the file.</summary>
public sealed record AudioBlock(TimeSpan Time, float[] Samples)
{
    public int Frames => Samples.Length / AudioMixer.Channels;
}

/// <summary>
/// Mixes system sound and the mic into the file's one audio track (screen-recording.md, Video). Each track's packets
/// arrive already on the file's timeline. A packet within <see cref="Jitter"/> of where its track left off is joined
/// on, so clock jitter never clicks; a later one leaves silence before it, an earlier one loses its overlap. Mixed
/// audio comes out only as far as every track has reached. Loopback sends nothing while nothing plays, so the
/// recorder calls <see cref="Pad"/> on a clock and a silent track is filled with silence. Not thread-safe.
/// </summary>
public sealed class AudioMixer
{
    public const int Rate = 48_000;
    public const int Channels = 2;

    private readonly List<float>[] _pending;
    private long _emitted;

    public AudioMixer(int tracks, TimeSpan? jitter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tracks, 1);
        _pending = [.. Enumerable.Range(0, tracks).Select(_ => new List<float>())];
        Jitter = jitter ?? TimeSpan.FromMilliseconds(20);
    }

    public TimeSpan Jitter { get; }

    /// <summary>Frames already mixed and handed out.</summary>
    public long Emitted => _emitted;

    public static long FrameAt(TimeSpan at) => (long)Math.Round(at.Ticks * (double)Rate / TimeSpan.TicksPerSecond);

    public static TimeSpan TimeOf(long frame) => TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / Rate);

    private long Head(int track) => _emitted + (_pending[track].Count / Channels);

    /// <summary>Adds a packet of <paramref name="track"/> at <paramref name="at"/> in the file.</summary>
    public IReadOnlyList<AudioBlock> Add(int track, TimeSpan at, ReadOnlySpan<float> samples)
    {
        if (samples.Length % Channels != 0) throw new ArgumentException("Samples must be whole stereo frames.", nameof(samples));
        var pending = _pending[track];
        var head = Head(track);
        var start = FrameAt(at);
        var jitter = FrameAt(Jitter);
        if (start > head + jitter) Silence(pending, start - head);
        else if (start < head - jitter) samples = Skip(samples, head - start);
        foreach (var sample in samples) pending.Add(sample);
        return Drain();
    }

    /// <summary>Fills every track that hasn't reached <paramref name="upTo"/> with silence, then mixes.</summary>
    public IReadOnlyList<AudioBlock> Pad(TimeSpan upTo)
    {
        var frame = FrameAt(upTo);
        for (var track = 0; track < _pending.Length; track++)
        {
            var head = Head(track);
            if (head < frame) Silence(_pending[track], frame - head);
        }
        return Drain();
    }

    private IReadOnlyList<AudioBlock> Drain()
    {
        var ready = _pending.Min(p => p.Count);
        if (ready == 0) return [];
        var mixed = new float[ready];
        foreach (var pending in _pending)
        {
            for (var i = 0; i < ready; i++) mixed[i] += pending[i];
            pending.RemoveRange(0, ready);
        }
        if (_pending.Length > 1)
            for (var i = 0; i < ready; i++) mixed[i] = Math.Clamp(mixed[i], -1f, 1f);
        var block = new AudioBlock(TimeOf(_emitted), mixed);
        _emitted += ready / Channels;
        return [block];
    }

    private static ReadOnlySpan<float> Skip(ReadOnlySpan<float> samples, long frames) =>
        frames * Channels >= samples.Length ? [] : samples[(int)(frames * Channels)..];

    private static void Silence(List<float> pending, long frames)
    {
        for (long i = 0; i < frames * Channels; i++) pending.Add(0f);
    }
}
