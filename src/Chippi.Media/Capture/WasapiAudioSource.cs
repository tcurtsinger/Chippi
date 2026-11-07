// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Chippi.Media.Capture;

/// <summary>
/// System sound (WASAPI loopback of the default output) or the mic (the default input), in shared mode with
/// Windows converting to 48 kHz stereo float, so nothing here resamples (screen-recording.md, Video). Each packet
/// carries the performance-counter time Windows stamped on it, the clock WGC frames use. A device that goes away
/// ends the source. Reads on its own thread every 10 ms; loopback sends nothing while nothing plays.
/// </summary>
public sealed class WasapiAudioSource : IAudioSource
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);

    private readonly bool _loopback;
    private readonly ManualResetEventSlim _stop = new();
    private MMDevice? _device;
    private AudioClient? _client;
    private Thread? _thread;
    private int _disposed;

    private WasapiAudioSource(bool loopback) => _loopback = loopback;

    public event Action<TimeSpan, float[]>? PacketArrived;

    public event Action<string>? Ended;

    /// <summary>What the default output plays.</summary>
    public static WasapiAudioSource SystemSound() => new(loopback: true);

    /// <summary>The default input.</summary>
    public static WasapiAudioSource Mic() => new(loopback: false);

    /// <summary>Opens the device and starts reading; throws when it can't, so the recording doesn't start.</summary>
    public void Start()
    {
        using var devices = new MMDeviceEnumerator();
        _device = devices.GetDefaultAudioEndpoint(_loopback ? DataFlow.Render : DataFlow.Capture, Role.Console);
        _client = _device.AudioClient;
        var flags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality
            | (_loopback ? AudioClientStreamFlags.Loopback : AudioClientStreamFlags.None);
        _client.Initialize(AudioClientShareMode.Shared, flags, TimeSpan.FromMilliseconds(200).Ticks, 0,
            WaveFormat.CreateIeeeFloatWaveFormat(AudioMixer.Rate, AudioMixer.Channels), Guid.Empty);
        var capture = _client.AudioCaptureClient;
        _client.Start();
        _thread = new Thread(() => Read(capture)) { IsBackground = true, Name = _loopback ? "Chippi system sound" : "Chippi mic" };
        _thread.Start();
    }

    private void Read(AudioCaptureClient capture)
    {
        try
        {
            while (!_stop.Wait(Poll))
            {
                while (capture.GetNextPacketSize() > 0)
                {
                    var data = capture.GetBuffer(out var frames, out var flags, out _, out var qpc);
                    var samples = new float[frames * AudioMixer.Channels];
                    if ((flags & AudioClientBufferFlags.Silent) == 0 && frames > 0) Marshal.Copy(data, samples, 0, samples.Length);
                    capture.ReleaseBuffer(frames);
                    // The stamp is the counter in 100 ns units: the same timeline as Stopwatch and WGC.
                    if (frames > 0) PacketArrived?.Invoke(TimeSpan.FromTicks(qpc), samples);
                }
            }
        }
        catch (COMException ex) when (Volatile.Read(ref _disposed) == 0)
        {
            Ended?.Invoke(ex.HResult == DeviceInvalidated ? (_loopback ? "Output device lost" : "Mic lost") : ex.Message);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set();
        if (_thread is not null && _thread != Thread.CurrentThread) _thread.Join();
        try { _client?.Stop(); }
        catch (COMException) { }
        _client?.Dispose();
        _device?.Dispose();
        _stop.Dispose();
    }
}
