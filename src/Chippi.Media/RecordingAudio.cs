// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Chippi.Media;

/// <summary>Bounded timestamped stereo rings. Missing packets are silence, not an ever-growing capture backlog.</summary>
internal sealed class AudioTimeline
{
    private const int Capacity = 48_000 * 2;
    private readonly float[] _samples = new float[Capacity * 2];
    private readonly long[] _positions = Enumerable.Repeat(-1L, Capacity).ToArray();
    private readonly object _gate = new();
    private long _read;

    public void Write(long start, ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            for (int i = 0; i < samples.Length / 2; i++)
            {
                long frame = start + i;
                if (frame < _read || frame >= _read + Capacity) continue;
                int slot = (int)(frame % Capacity);
                _samples[slot * 2] = samples[i * 2]; _samples[slot * 2 + 1] = samples[i * 2 + 1]; _positions[slot] = frame;
            }
        }
    }
    public void Mix(long start, Span<float> destination)
    {
        lock (_gate)
        {
            int frames = destination.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                long position = start + i; int slot = (int)(position % Capacity);
                if (_positions[slot] != position) continue;
                destination[i * 2] += _samples[slot * 2]; destination[i * 2 + 1] += _samples[slot * 2 + 1];
                _positions[slot] = -1;
            }
            _read = start + frames;
        }
    }
}

internal sealed class RecordingAudio : IDisposable
{
    private readonly List<Input> _inputs = [];
    private readonly float[] _mixed = new float[960 * 2];
    private readonly byte[] _pcm = new byte[960 * 4];
    public event Action<string>? Warning;

    public RecordingAudio(RecordingClock clock, RecordingOptions options)
    {
        try
        {
            if (options.SystemAudio) _inputs.Add(new Input(clock, loopback: true, message => Warning?.Invoke(message)));
            if (options.MicAudio) _inputs.Add(new Input(clock, loopback: false, message => Warning?.Invoke(message)));
        }
        catch { Dispose(); throw; }
    }

    public static RecordingDeviceNames Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? Name(DataFlow flow)
        {
            try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); return device.FriendlyName; }
            catch (COMException) { return null; }
        }
        return new(Name(DataFlow.Render), Name(DataFlow.Capture));
    }

    public void Read(long startFrame, int count, Action<ReadOnlyMemory<byte>, long> write)
    {
        var mixed = _mixed.AsSpan(0, count * 2); mixed.Clear();
        foreach (var input in _inputs) input.Timeline.Mix(startFrame, mixed);
        for (int i = 0; i < mixed.Length; i++)
        {
            short value = (short)Math.Clamp((int)(mixed[i] * 32767), short.MinValue, short.MaxValue);
            _pcm[i * 2] = (byte)value; _pcm[i * 2 + 1] = (byte)(value >> 8);
        }
        write(_pcm.AsMemory(0, count * 4), startFrame);
    }
    public void Dispose() { foreach (var input in _inputs) input.Dispose(); _inputs.Clear(); }
    public void SetPaused(bool paused) { foreach (var input in _inputs) input.SetPaused(paused); }

    private sealed class Input : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AutoResetEvent _control = new(false);
        private volatile bool _paused;
        public void SetPaused(bool paused) { if (_paused == paused) return; _paused = paused; _control.Set(); }
        public AudioTimeline Timeline { get; } = new();

        public Input(RecordingClock clock, bool loopback, Action<string> warning)
        {
            _thread = new Thread(() => Run(clock, loopback, warning)) { IsBackground = true, Name = loopback ? "Chippi recording sound" : "Chippi recording mic" };
            _thread.Start();
            try { _ready.Task.GetAwaiter().GetResult(); } catch { Dispose(); throw; }
        }

        private unsafe void Run(RecordingClock clock, bool loopback, Action<string> warning)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(loopback ? DataFlow.Render : DataFlow.Capture, Role.Multimedia);
                using var client = device.AudioClient;
                var format = client.MixFormat;
                nint formatPointer = WaveFormat.MarshalToPtr(format);
                int channelMask;
                try { channelMask = format is WaveFormatExtensible ? Marshal.ReadInt32(formatPointer, 20) : format.Channels switch { 2 => 3, 4 => 0x33, 6 => 0x3f, 8 => 0x63f, _ => 0 }; }
                finally { Marshal.FreeHGlobal(formatPointer); }
                bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
                if ((!floating && format.BitsPerSample is not (16 or 24 or 32)) || floating && format.BitsPerSample != 32)
                    throw new NotSupportedException("The selected audio device format cannot be recorded.");
                client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback | (loopback ? AudioClientStreamFlags.Loopback : AudioClientStreamFlags.None), 2_000_000, 0, format, Guid.Empty);
                using var arrived = new AutoResetEvent(false);
                client.SetEventHandle(arrived.SafeWaitHandle.DangerousGetHandle());
                var handles = new[] { _stop.Token.WaitHandle, arrived, _control };
                var converted = new float[checked((int)((long)client.BufferSize * 48_000 / format.SampleRate + 2) * 2)];
                using var capture = client.AudioCaptureClient;
                bool running = true;
                client.Start(); _ready.TrySetResult();
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        if (_paused)
                        {
                            if (running) { client.Stop(); client.Reset(); running = false; }
                            WaitHandle.WaitAny(handles, 100); continue;
                        }
                        if (!running) { client.Start(); running = true; }
                        while (capture.GetNextPacketSize() > 0)
                        {
                            nint buffer = capture.GetBuffer(out int frames, out var flags, out _, out long qpc);
                            try
                            {
                                if ((flags & AudioClientBufferFlags.TimestampError) != 0) continue;
                                bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
                                var at = clock.AtSystemTime(TimeSpan.FromTicks(qpc));
                                if (at == null) continue;
                                long first = at.Value.Ticks * 48_000 / TimeSpan.TicksPerSecond;
                                int output = (int)((long)frames * 48_000 / format.SampleRate);
                                for (int i = 0; i < output; i++)
                                {
                                    double position = i * (double)format.SampleRate / 48_000;
                                    int a = Math.Min((int)position, frames - 1), b = Math.Min(a + 1, frames - 1);
                                    float fraction = (float)(position - a);
                                    var av = silent ? (0f, 0f) : Stereo((byte*)buffer + a * format.BlockAlign, format, floating, channelMask);
                                    var bv = silent ? (0f, 0f) : Stereo((byte*)buffer + b * format.BlockAlign, format, floating, channelMask);
                                    converted[i * 2] = av.Item1 + (bv.Item1 - av.Item1) * fraction;
                                    converted[i * 2 + 1] = av.Item2 + (bv.Item2 - av.Item2) * fraction;
                                }
                                Timeline.Write(first, converted.AsSpan(0, output * 2));
                            }
                            finally { capture.ReleaseBuffer(frames); }
                        }
                        WaitHandle.WaitAny(handles, 100);
                    }
                }
                finally { if (running) client.Stop(); }
            }
            catch (Exception ex)
            {
                if (!_ready.TrySetException(ex) && !_stop.IsCancellationRequested)
                    warning(loopback ? "System audio disconnected" : "Microphone disconnected");
            }
        }

        private static unsafe (float, float) Stereo(byte* frame, WaveFormat format, bool floating, int mask)
        {
            float Sample(int channel)
            {
                byte* value = frame + channel * (format.BitsPerSample / 8);
                if (floating) return float.IsFinite(*(float*)value) ? *(float*)value : 0;
                return format.BitsPerSample switch
                {
                    16 => *(short*)value / 32768f,
                    24 => ((value[0] | value[1] << 8 | value[2] << 16) << 8 >> 8) / 8388608f,
                    _ => *(int*)value / 2147483648f,
                };
            }
            if (format.Channels == 1) { float mono = Sample(0); return (mono, mono); }
            float left = 0, right = 0;
            int speaker = 1;
            for (int channel = 0; channel < format.Channels; channel++)
            {
                while (mask != 0 && speaker > 0 && (mask & speaker) == 0) speaker <<= 1;
                var gain = ChannelGain(mask == 0 ? 0 : speaker, channel);
                float value = Sample(channel); left += value * gain.Left; right += value * gain.Right;
                if (speaker > 0) speaker <<= 1;
            }
            return (Math.Clamp(left, -1, 1), Math.Clamp(right, -1, 1));
        }

        public void Dispose()
        {
            _stop.Cancel();
            if (Thread.CurrentThread != _thread) _thread.Join();
            _stop.Dispose();
            _control.Dispose();
        }
    }

    internal static (float Left, float Right) ChannelGain(int speaker, int index) => speaker switch
    {
        1 => (1, 0), 2 => (0, 1), 8 => (0, 0),
        0x10 or 0x40 or 0x200 or 0x1000 or 0x8000 => (.5f, 0),
        0x20 or 0x80 or 0x400 or 0x4000 or 0x20000 => (0, .5f),
        0 => index % 2 == 0 ? (.5f, 0) : (0, .5f),
        _ => (.5f, .5f),
    };
}
