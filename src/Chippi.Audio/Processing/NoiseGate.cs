// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using NAudio.Wave;

namespace Chippi.Audio.Processing;

/// <summary>
/// Downward noise gate (Hopper's): below the threshold the gain falls smoothly to zero, opening in
/// about 5 ms and closing in about 120 ms so it never clicks. Reads its settings live; off passes
/// audio through.
/// </summary>
internal sealed class NoiseGate : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly MicSettings _settings;
    private readonly float _attack;
    private readonly float _release;
    private float _gain;

    public NoiseGate(ISampleProvider source, MicSettings settings)
    {
        _source = source;
        _settings = settings;
        var rate = source.WaveFormat.SampleRate;
        _attack = 1f - MathF.Exp(-1f / (0.005f * rate));
        _release = 1f - MathF.Exp(-1f / (0.120f * rate));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (!_settings.Gate)
        {
            _gain = 1f; // held open, so turning it back on doesn't resume mid-close
            return read;
        }

        var threshold = _settings.GateThreshold;
        for (var i = 0; i < read; i++)
        {
            var sample = buffer[offset + i];
            var target = MathF.Abs(sample) > threshold ? 1f : 0f;
            _gain += (target - _gain) * (target > _gain ? _attack : _release);
            buffer[offset + i] = sample * _gain;
        }
        return read;
    }
}
