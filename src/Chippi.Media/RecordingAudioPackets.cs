// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Chippi.Media;

/// <summary>One continuous resampler and sample clock per endpoint, independent of packet boundaries.</summary>
internal sealed class RecordingAudioPackets
{
    private readonly WaveFormat _format;
    private readonly bool _floating;
    private readonly (float Left, float Right)[] _gains;
    private readonly WdlResampler _resampler = new();
    private readonly float[] _output;
    private readonly AudioTimeline _timeline;
    private long? _nextFrame, _deviceEnd;
    private double _correction;
    public long TimestampErrors { get; private set; }
    public long Discontinuities { get; private set; }
    public long EndFrame => _nextFrame ?? 0;
    public string Description => $"{_format.SampleRate} Hz/{_format.Channels} ch/{_format.BitsPerSample} bit; uncertain timestamps={TimestampErrors}; discontinuities={Discontinuities}; late frames={_timeline.LateFrames}";

    public RecordingAudioPackets(WaveFormat format, int maximumFrames, AudioTimeline timeline)
    {
        _format = format; _timeline = timeline;
        _floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        if ((!_floating && format.BitsPerSample is not (16 or 24 or 32)) || _floating && format.BitsPerSample != 32)
            throw new NotSupportedException("The selected audio device format cannot be recorded.");
        nint pointer = WaveFormat.MarshalToPtr(format);
        int mask;
        try { mask = format is WaveFormatExtensible ? Marshal.ReadInt32(pointer, 20) : format.Channels switch { 2 => 3, 4 => 0x33, 6 => 0x3f, 8 => 0x63f, _ => 0 }; }
        finally { Marshal.FreeHGlobal(pointer); }
        _gains = new (float, float)[format.Channels]; int speaker = 1;
        for (int channel = 0; channel < format.Channels; channel++)
        {
            while (mask != 0 && speaker > 0 && (mask & speaker) == 0) speaker <<= 1;
            _gains[channel] = format.Channels == 1 ? (1, 1) : RecordingAudio.ChannelGain(mask == 0 ? 0 : speaker, channel);
            if (speaker > 0) speaker <<= 1;
        }
        float scale = Math.Max(1, Math.Max(_gains.Sum(x => x.Left), _gains.Sum(x => x.Right)));
        for (int i = 0; i < _gains.Length; i++) _gains[i] = (_gains[i].Left / scale, _gains[i].Right / scale);
        _resampler.SetMode(true, 0, true, 32, 16); _resampler.SetFeedMode(true); _resampler.SetRates(format.SampleRate, 48_000);
        _output = new float[checked(((int)Math.Ceiling((long)maximumFrames * 48_000d / format.SampleRate * 1.002) + 32) * 2)];
    }

    public void Reset() { _nextFrame = _deviceEnd = null; _correction = 0; _resampler.Reset(); }

    public void Flush()
    {
        if (_nextFrame == null) return;
        int remaining = Math.Min(_output.Length / 2, (int)Math.Round(_resampler.GetCurrentLatency() * 48_000));
        if (remaining <= 0) return;
        _resampler.ResamplePrepare(1, 2, out _, out _);
        int count = _resampler.ResampleOut(_output, 0, 0, remaining, 2);
        _timeline.Write(_nextFrame.Value, _output.AsSpan(0, count * 2)); _nextFrame += count;
    }

    public unsafe void Write(nint data, int frames, AudioClientBufferFlags flags, long devicePosition, TimeSpan? timestamp, TimeSpan received)
    {
        if (frames <= 0) return;
        bool uncertain = (flags & AudioClientBufferFlags.TimestampError) != 0;
        if (uncertain) TimestampErrors++;
        // A timestamp error does not invalidate the samples. Continue from the last packet's position.
        if (uncertain && _deviceEnd is { } expected) devicePosition = expected;
        bool gap = _deviceEnd is { } end && devicePosition != end;
        if (gap) { Discontinuities++; Flush(); Reset(); }
        if (_nextFrame == null)
        {
            var first = timestamp ?? received - TimeSpan.FromSeconds(frames / (double)_format.SampleRate);
            _nextFrame = Math.Max(0, (long)Math.Round(first.TotalSeconds * 48_000));
        }
        else if (timestamp is { } at)
        {
            // Follow slow endpoint-clock drift through resampling, never by punching holes between packets.
            double expectedTime = _nextFrame.Value / 48_000d + _resampler.GetCurrentLatency();
            double target = Math.Clamp((at.TotalSeconds - expectedTime) / 2, -.001, .001);
            _correction += (target - _correction) * Math.Min(1, frames / (double)_format.SampleRate);
        }
        _resampler.SetRates(_format.SampleRate * (1 - _correction), 48_000);
        int available = _resampler.ResamplePrepare(frames, 2, out float[] input, out int offset);
        if (available < frames) throw new InvalidOperationException("The audio resampler could not accept the packet.");
        var samples = input.AsSpan(offset, frames * 2); samples.Clear();
        if ((flags & AudioClientBufferFlags.Silent) == 0)
        {
            for (int frame = 0; frame < frames; frame++)
            for (int channel = 0; channel < _format.Channels; channel++)
            {
                byte* value = (byte*)data + frame * _format.BlockAlign + channel * (_format.BitsPerSample / 8);
                float sample = _floating ? *(float*)value : _format.BitsPerSample switch
                {
                    16 => *(short*)value / 32768f,
                    24 => ((value[0] | value[1] << 8 | value[2] << 16) << 8 >> 8) / 8388608f,
                    _ => *(int*)value / 2147483648f,
                };
                if (!float.IsFinite(sample)) sample = 0;
                samples[frame * 2] += sample * _gains[channel].Left;
                samples[frame * 2 + 1] += sample * _gains[channel].Right;
            }
        }
        int count = _resampler.ResampleOut(_output, 0, frames, _output.Length / 2, 2);
        _timeline.Write(_nextFrame.Value, _output.AsSpan(0, count * 2));
        _nextFrame += count; _deviceEnd = devicePosition + frames;
    }
}
