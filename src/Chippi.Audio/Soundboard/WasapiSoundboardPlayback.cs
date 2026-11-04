// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Processing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Chippi.Audio.Soundboard;

/// <summary>
/// Each branch owns its decoder and shared-mode stream. Windows combines the send stream with
/// the voice bridge at CABLE Input, after voice DSP; local monitoring never contains that voice.
/// Prepared WAVs are streamed from disk instead of loading an entire clip into memory.
/// </summary>
internal sealed class WasapiSoundboardPlayback : ISoundboardPlayback
{
    private sealed class Output : IDisposable
    {
        private readonly WaveFileReader _reader;
        private readonly RenderDeviceTransport _transport;
        private readonly VolumeSampleProvider _volume;
        private readonly ClipSampleProvider _clip;
        private readonly IWaveProvider _source;

        public Output(string path, string deviceId, TimeSpan start, TimeSpan end, float volume, Action<Exception?> stopped)
        {
            if (!File.Exists(path)) throw new AudioFailure("Sound file missing");
            _reader = new WaveFileReader(path);
            try
            {
                if (_reader.WaveFormat.Channels is < 1 or > 2 || _reader.WaveFormat.SampleRate is < 8000 or > 192000)
                    throw new AudioFailure("Sound format unsupported");
                if (start < TimeSpan.Zero || end <= start || end > _reader.TotalTime + TimeSpan.FromMilliseconds(1))
                    throw new AudioFailure("Sound trim range is invalid");
                _reader.CurrentTime = start;
                _clip = new(_reader.ToSampleProvider(), end - start);
                _volume = new(_clip) { Volume = volume };
                _transport = new RenderDeviceTransport(deviceId);
                try
                {
                    ISampleProvider source = _volume;
                    var format = _transport.MixFormat;
                    if (source.WaveFormat.SampleRate != format.SampleRate)
                        source = new WdlResamplingSampleProvider(source, format.SampleRate);
                    if (source.WaveFormat.Channels == 2 && format.Channels == 1)
                        source = new StereoToMonoSampleProvider(source);
                    else if (source.WaveFormat.Channels == 1 && format.Channels == 2)
                        source = new MonoToStereoSampleProvider(source);
                    else if (format.Channels > 2)
                        source = new FrontChannelsSampleProvider(source, format.Channels);
                    else if (source.WaveFormat.Channels != format.Channels)
                        throw new AudioFailure("Playback channel count unsupported");
                    _source = source.ToWaveProvider();
                    _transport.Stopped += stopped;
                }
                catch { _transport.Dispose(); throw; }
            }
            catch { _reader.Dispose(); throw; }
        }

        public TimeSpan Position => _clip.Position;
        public float Volume { set => _volume.Volume = value; }
        public void Start() => _transport.Begin(_source);
        public void Dispose()
        {
            try { _transport.Dispose(); }
            finally { _reader.Dispose(); }
        }
    }

    private readonly Output? _send, _monitor;
    private int _remaining, _notified;
    private bool _disposed;

    public static ISoundboardPlayback Create(SoundboardPlaybackRequest request) => new WasapiSoundboardPlayback(request);

    private WasapiSoundboardPlayback(SoundboardPlaybackRequest request)
    {
        _remaining = (request.SendDeviceId is null ? 0 : 1) + (request.MonitorDeviceId is null ? 0 : 1);
        if (_remaining == 0) throw new AudioFailure("No playback device");
        try
        {
            if (request.SendDeviceId is { } send)
                _send = new(request.Path, send, request.Start, request.End, request.Send, OnStopped);
            if (request.MonitorDeviceId is { } monitor)
                _monitor = new(request.Path, monitor, request.Start, request.End, request.Hear, OnStopped);
        }
        catch { _send?.Dispose(); _monitor?.Dispose(); throw; }
    }

    public TimeSpan Position => _send?.Position ?? _monitor?.Position ?? TimeSpan.Zero;
    public event Action<Exception?>? Stopped;

    public void Start()
    {
        _send?.Start();
        _monitor?.Start();
    }

    public void SetLevels(float send, float hear)
    {
        if (_send is not null) _send.Volume = send;
        if (_monitor is not null) _monitor.Volume = hear;
    }

    private void OnStopped(Exception? error)
    {
        if (_disposed) return;
        if ((error is not null || Interlocked.Decrement(ref _remaining) == 0)
            && Interlocked.Exchange(ref _notified, 1) == 0)
            Stopped?.Invoke(error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _send?.Dispose(); }
        finally { _monitor?.Dispose(); }
    }
}

/// <summary>Routes mono/stereo clips to front left/right without filling a surround device's other channels.</summary>
internal sealed class FrontChannelsSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _buffer;

    public FrontChannelsSampleProvider(ISampleProvider source, int channels)
    {
        if (source.WaveFormat.Channels is < 1 or > 2)
            throw new ArgumentException("Only mono and stereo clips are supported.", nameof(source));
        if (channels < 2) throw new ArgumentOutOfRangeException(nameof(channels));
        _source = source;
        _buffer = new float[1024 * source.WaveFormat.Channels];
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int sourceChannels = _source.WaveFormat.Channels;
        int channels = WaveFormat.Channels;
        int written = 0;
        int frames = count / channels;
        while (frames > 0)
        {
            int wanted = Math.Min(frames, _buffer.Length / sourceChannels) * sourceChannels;
            int read = _source.Read(_buffer, 0, wanted);
            int available = read / sourceChannels;
            if (available == 0) break;
            Array.Clear(buffer, offset + written, available * channels);
            for (int frame = 0; frame < available; frame++)
            {
                int from = frame * sourceChannels;
                int to = offset + written + frame * channels;
                buffer[to] = _buffer[from];
                buffer[to + 1] = _buffer[from + sourceChannels - 1];
            }
            written += available * channels;
            frames -= available;
        }
        return written;
    }
}

/// <summary>A bounded, streaming slice. It reads whole audio frames and reports consumed clip time.</summary>
internal sealed class ClipSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly long _samples;
    private long _read;

    public ClipSampleProvider(ISampleProvider source, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _source = source;
        _samples = checked((long)(duration.TotalSeconds * source.WaveFormat.SampleRate)) * source.WaveFormat.Channels;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;
    public TimeSpan Position => TimeSpan.FromSeconds((double)Interlocked.Read(ref _read) / WaveFormat.SampleRate / WaveFormat.Channels);

    public int Read(float[] buffer, int offset, int count)
    {
        long remaining = _samples - Interlocked.Read(ref _read);
        int requested = (int)Math.Min(count, remaining);
        requested -= requested % WaveFormat.Channels;
        if (requested <= 0) return 0;
        int read = _source.Read(buffer, offset, requested);
        Interlocked.Add(ref _read, read);
        return read;
    }
}
