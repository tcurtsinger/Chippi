// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using NAudio.Wave;

namespace Chippi.Audio.Processing;

/// <summary>
/// Passthrough that tracks the recent peak for a level meter (Hopper's MeterTap). <see cref="Level"/>
/// jumps to new peaks and decays 0.85 per read block between them. Written on the audio thread, read
/// on the engine thread; a float read is atomic.
/// </summary>
internal sealed class MeterTap(ISampleProvider source) : ISampleProvider
{
    private const float Decay = 0.85f;
    private float _level;

    public WaveFormat WaveFormat => source.WaveFormat;

    public float Level => _level;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        var peak = 0f;
        for (var i = 0; i < read; i++)
            peak = MathF.Max(peak, MathF.Abs(buffer[offset + i]));
        _level = peak > _level ? peak : _level * Decay;
        return read;
    }
}
