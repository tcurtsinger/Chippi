// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Chippi.Audio.Processing;

/// <summary>One running mic chain: a real mic in, cleaned audio out to the transport.</summary>
public interface IMicPipeline : IDisposable
{
    /// <summary>Raw (pre-DSP) peak, 0 to 1.</summary>
    float InputLevel { get; }

    /// <summary>Processed peak, 0 to 1.</summary>
    float OutputLevel { get; }

    /// <summary>
    /// Raised on an audio thread when capture or render stops by itself (the device went away, or
    /// Windows invalidated the stream). Never raised by <see cref="IDisposable.Dispose"/>.
    /// </summary>
    event Action<Exception?>? Failed;

    void Start();
}

/// <summary>Builds a pipeline from a real mic to the transport's render endpoint. Runs on the engine thread.</summary>
public delegate IMicPipeline MicPipelineFactory(string sourceId, string sinkId, MicSettings settings);

/// <summary>
/// Where processed mic audio goes so other apps can record it. VB-CABLE's CABLE Input today
/// (<see cref="RenderDeviceTransport"/>); an own virtual-mic driver could replace it later.
/// </summary>
public interface IMicTransport : IDisposable
{
    string Name { get; }

    /// <summary>The sink's shared-mode mix format, which the pipeline must produce.</summary>
    WaveFormat MixFormat { get; }

    event Action<Exception?>? Stopped;

    void Begin(IWaveProvider processed);
}

/// <summary>Renders processed audio into a WASAPI render endpoint (Hopper's). For VB-CABLE: CABLE Input.</summary>
public sealed class RenderDeviceTransport : IMicTransport
{
    private const int RenderLatencyMs = 50; // render-side buffer, not end-to-end latency

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _device;
    private WasapiOut? _out;

    public RenderDeviceTransport(string deviceId)
    {
        _device = _enumerator.GetDevice(deviceId);
        MixFormat = _device.AudioClient.MixFormat;
    }

    public string Name => _device.FriendlyName;

    public WaveFormat MixFormat { get; }

    public event Action<Exception?>? Stopped;

    public void Begin(IWaveProvider processed)
    {
        _out = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: RenderLatencyMs);
        _out.PlaybackStopped += OnStopped;
        _out.Init(processed);
        _out.Play();
    }

    private void OnStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(e.Exception);

    public void Dispose()
    {
        if (_out is not null)
        {
            _out.PlaybackStopped -= OnStopped;
            try { _out.Stop(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { } // device already gone
            _out.Dispose();
            _out = null;
        }
        _device.Dispose();
        _enumerator.Dispose();
    }
}

/// <summary>
/// Hopper's MicPipeline: capture a real mic, convert to 48 kHz mono, then input meter, RNNoise,
/// gate, AGC, output meter, and the transport's mix format. An NAudio pull chain: the transport's
/// render thread pulls through the DSP from the capture buffer, so DSP runs on one thread.
/// </summary>
public sealed class MicPipeline : IMicPipeline
{
    private const int DspRate = 48000;
    private const double BridgeTargetSeconds = 0.100; // trims standing latency only when capture runs ahead

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _captureDevice;
    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _captureBuffer;
    private readonly IMicTransport _transport;
    private readonly IWaveProvider _processed;
    private readonly RnnoiseSuppressor _suppressor;
    private readonly MeterTap _inputMeter;
    private readonly MeterTap _outputMeter;
    private readonly byte[] _trimScratch = new byte[8192];
    private readonly int _bridgeTargetBytes;
    private volatile bool _disposing;
    private bool _running;

    public MicPipeline(string sourceId, IMicTransport transport, MicSettings settings)
    {
        _transport = transport;
        _captureDevice = _enumerator.GetDevice(sourceId);
        _capture = new WasapiCapture(_captureDevice);
        _captureBuffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500), // headroom; standing latency is trimmed in OnData
            DiscardOnBufferOverflow = true,
        };
        _bridgeTargetBytes = (int)(BridgeTargetSeconds * _capture.WaveFormat.AverageBytesPerSecond);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnCaptureStopped;
        _transport.Stopped += OnTransportStopped;

        ISampleProvider chain = _captureBuffer.ToSampleProvider();
        if (chain.WaveFormat.Channels == 2)
            chain = new StereoToMonoSampleProvider(chain) { LeftVolume = 0.5f, RightVolume = 0.5f };
        else if (chain.WaveFormat.Channels > 2)
            chain = new MultiplexingSampleProvider([chain], 1);
        if (chain.WaveFormat.SampleRate != DspRate)
            chain = new WdlResamplingSampleProvider(chain, DspRate);

        chain = _inputMeter = new MeterTap(chain);
        _suppressor = new RnnoiseSuppressor(chain, settings);
        chain = new NoiseGate(_suppressor, settings);
        chain = new AutoGain(chain, settings);
        chain = _outputMeter = new MeterTap(chain);

        var mix = transport.MixFormat;
        if (chain.WaveFormat.SampleRate != mix.SampleRate)
            chain = new WdlResamplingSampleProvider(chain, mix.SampleRate);
        if (mix.Channels == 2)
            chain = new MonoToStereoSampleProvider(chain);
        else if (mix.Channels > 2)
            throw new NotSupportedException($"The transport mixes {mix.Channels} channels; only mono and stereo are supported.");
        _processed = chain.ToWaveProvider();
    }

    /// <summary>The real thing: VB-CABLE's CABLE Input as the transport.</summary>
    public static MicPipelineFactory Factory { get; } = (source, sink, settings) =>
    {
        var transport = new RenderDeviceTransport(sink);
        try { return new MicPipeline(source, transport, settings); }
        catch
        {
            transport.Dispose();
            throw;
        }
    };

    public float InputLevel => _inputMeter.Level;

    public float OutputLevel => _outputMeter.Level;

    public event Action<Exception?>? Failed;

    public void Start()
    {
        _transport.Begin(_processed);
        _capture.StartRecording();
        _running = true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _captureBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        var blockAlign = _captureBuffer.WaveFormat.BlockAlign;
        var excess = _captureBuffer.BufferedBytes - _bridgeTargetBytes;
        excess -= excess % blockAlign;
        while (excess > 0)
        {
            var read = _captureBuffer.Read(_trimScratch, 0, Math.Min(excess, _trimScratch.Length));
            if (read <= 0)
                break;
            excess -= read;
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (!_disposing)
            Failed?.Invoke(e.Exception);
    }

    private void OnTransportStopped(Exception? error)
    {
        if (!_disposing)
            Failed?.Invoke(error);
    }

    public void Dispose()
    {
        _disposing = true;
        if (_running)
        {
            try { _capture.StopRecording(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { } // device gone
            _running = false;
        }
        _capture.DataAvailable -= OnData;
        _capture.RecordingStopped -= OnCaptureStopped;
        _transport.Stopped -= OnTransportStopped;
        _transport.Dispose();
        _suppressor.Dispose(); // after the transport: its render thread has stopped pulling
        _capture.Dispose();
        _captureDevice.Dispose();
        _enumerator.Dispose();
    }
}
