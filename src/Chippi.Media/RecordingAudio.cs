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
    public long LateFrames { get; private set; }

    public void Write(long start, ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            for (int i = 0; i < samples.Length / 2; i++)
            {
                long frame = start + i;
                if (frame < _read) { LateFrames++; continue; }
                if (frame >= _read + Capacity) continue;
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
    internal static readonly TimeSpan BufferDelay = TimeSpan.FromMilliseconds(250);
    private readonly List<Input> _inputs = [];
    private readonly float[] _mixed = new float[960 * 2];
    private readonly byte[] _pcm = new byte[960 * 4];
    public event Action<string>? Warning;
    public string Diagnostics => string.Join("; ", _inputs.Select(x => x.Description));

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
        ToPcm(mixed, _pcm, _inputs.Count);
        write(_pcm.AsMemory(0, count * 4), startFrame);
    }
    public void Dispose() { foreach (var input in _inputs) input.Dispose(); _inputs.Clear(); }
    public void Complete() { foreach (var input in _inputs) input.Dispose(); }
    public void SetPaused(bool paused) { foreach (var input in _inputs) input.SetPaused(paused); }

    internal static void ToPcm(ReadOnlySpan<float> mixed, Span<byte> pcm, int sources)
    {
        float gain = 1f / Math.Max(1, sources);
        for (int i = 0; i < mixed.Length; i++)
        {
            float sample = mixed[i] * gain;
            short value = (short)(Math.Clamp(float.IsNaN(sample) ? 0 : sample, -1, 1) * 32767);
            pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
        }
    }

    private sealed class Input : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AutoResetEvent _control = new(false);
        private volatile bool _paused;
        private int _disposed;
        private RecordingAudioPackets? _packets;
        private readonly bool _loopback;
        public string Description => (_loopback ? "System: " : "Mic: ") + _packets?.Description;
        public void SetPaused(bool paused) { if (_paused == paused) return; _paused = paused; _control.Set(); }
        public AudioTimeline Timeline { get; } = new();

        public Input(RecordingClock clock, bool loopback, Action<string> warning)
        {
            _loopback = loopback;
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
                client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback | (loopback ? AudioClientStreamFlags.Loopback : AudioClientStreamFlags.None), 2_000_000, 0, format, Guid.Empty);
                using var arrived = new AutoResetEvent(false);
                client.SetEventHandle(arrived.SafeWaitHandle.DangerousGetHandle());
                var handles = new[] { _stop.Token.WaitHandle, arrived, _control };
                var packets = _packets = new RecordingAudioPackets(format, client.BufferSize, Timeline);
                using var capture = client.AudioCaptureClient;
                void Drain(bool finishing = false)
                {
                    while ((finishing || !_stop.IsCancellationRequested && !_paused) && capture.GetNextPacketSize() > 0)
                    {
                        nint buffer = capture.GetBuffer(out int frames, out var flags, out long position, out long qpc);
                        try
                        {
                            var received = clock.ActiveElapsed;
                            if (received == null && !finishing) continue;
                            bool uncertain = (flags & AudioClientBufferFlags.TimestampError) != 0;
                            if (uncertain && clock.IsPaused) continue;
                            var packet = uncertain ? null : clock.AudioPacket(TimeSpan.FromTicks(qpc), frames, format.SampleRate);
                            if (!uncertain && packet == null) continue;
                            packets.Write(buffer, packet?.Frames ?? frames, flags, position, packet?.Time, received ?? clock.Elapsed);
                        }
                        finally { capture.ReleaseBuffer(frames); }
                    }
                }
                bool running = true;
                client.Start(); _ready.TrySetResult();
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        if (_paused)
                        {
                            if (running) { client.Stop(); Drain(finishing: true); packets.Flush(); client.Reset(); packets.Reset(); running = false; }
                            WaitHandle.WaitAny(handles, 100); continue;
                        }
                        if (!running) { client.Start(); running = true; }
                        Drain();
                        WaitHandle.WaitAny(handles, 100);
                    }
                }
                finally { if (running) { client.Stop(); Drain(finishing: true); packets.Flush(); } }
            }
            catch (Exception ex)
            {
                if (!_ready.TrySetException(ex) && !_stop.IsCancellationRequested)
                    warning(loopback ? "System audio disconnected" : "Microphone disconnected");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
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
