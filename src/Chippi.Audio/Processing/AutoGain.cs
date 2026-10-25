// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using NAudio.Wave;

namespace Chippi.Audio.Processing;

/// <summary>
/// Automatic gain control (Hopper's): brings speech toward the target loudness, measured as a
/// 150 ms RMS, with makeup gain capped at the max. Pulls gain down in about 30 ms and pushes it up
/// in about 300 ms, holds it through silence so the noise floor isn't pumped, and hard-clamps the
/// output. Reads its settings live; off passes audio through.
/// </summary>
internal sealed class AutoGain : ISampleProvider
{
    private const float MinGain = 0.3f;
    private const float SilenceFloorRms = 0.005f;

    private readonly ISampleProvider _source;
    private readonly MicSettings _settings;
    private readonly float _msCoef;
    private readonly float _gainDown;
    private readonly float _gainUp;
    private float _ms;
    private float _gain = 1f;

    public AutoGain(ISampleProvider source, MicSettings settings)
    {
        _source = source;
        _settings = settings;
        var rate = source.WaveFormat.SampleRate;
        _msCoef = 1f - MathF.Exp(-1f / (0.150f * rate));
        _gainDown = 1f - MathF.Exp(-1f / (0.030f * rate));
        _gainUp = 1f - MathF.Exp(-1f / (0.300f * rate));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (!_settings.Agc)
        {
            _gain = 1f;
            _ms = 0f; // turning it back on starts clean
            return read;
        }

        var target = _settings.AgcTarget;
        var maxGain = _settings.AgcMaxGain;
        for (var i = 0; i < read; i++)
        {
            var s = buffer[offset + i];
            _ms += (s * s - _ms) * _msCoef;
            var level = MathF.Sqrt(_ms);
            var desired = level > SilenceFloorRms ? Math.Clamp(target / level, MinGain, maxGain) : _gain;
            _gain += (desired - _gain) * (desired < _gain ? _gainDown : _gainUp);
            buffer[offset + i] = Math.Clamp(s * _gain, -1f, 1f);
        }
        return read;
    }
}
