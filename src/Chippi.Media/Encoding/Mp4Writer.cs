// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Chippi.Media.Encoding;

/// <summary>A frame the MP4 writer can hand to Media Foundation: a GPU surface or a buffer of BGRA pixels.</summary>
public interface IEncodableFrame : IVideoFrame
{
    /// <summary>The frame the writer queues: this one, or a copy when this one belongs to its source (a WGC frame
    /// goes back to the pool once its event returns). Called on the source's thread, during its event.</summary>
    IEncodableFrame Keep();

    MediaStreamSample ToSample(TimeSpan at);
}

/// <summary>A frame of BGRA pixels, top-down, in memory: fixtures, and anything not on the GPU.</summary>
public sealed class BgraFrame(TimeSpan time, byte[] pixels, int width) : IEncodableFrame
{
    public TimeSpan Time { get; } = time;

    public byte[] Pixels { get; } = pixels;

    public int Width { get; } = width;

    public IEncodableFrame Keep() => this;

    /// <summary>Media Foundation reads an uncompressed RGB buffer bottom-up (measured: a top-down buffer came out
    /// upside down), so the rows go in reversed. GPU surfaces are top-down and need nothing.</summary>
    public MediaStreamSample ToSample(TimeSpan at)
    {
        var row = Width * 4;
        var height = Pixels.Length / row;
        var flipped = new byte[row * height];
        for (var y = 0; y < height; y++)
            Pixels.AsSpan(y * row, row).CopyTo(flipped.AsSpan((height - 1 - y) * row, row));
        return MediaStreamSample.CreateFromBuffer(flipped.AsBuffer(), at);
    }

    public void Dispose() { }
}

public sealed class Mp4WriterFactory : IMediaWriterFactory
{
    public IMediaWriter Create(string path, VideoFormat format) => new Mp4Writer(path, format);
}

/// <summary>
/// H.264 and AAC in MP4 through Media Foundation (screen-recording.md, Video; architecture.md section 3): frames and
/// mixed audio go into a <see cref="MediaStreamSource"/> that a <see cref="MediaTranscoder"/> pulls from, the pattern of
/// Microsoft's SimpleRecorder sample (MIT). Windows picks a hardware encoder when there is one. Writes queue; a queue
/// that backs up drops new frames rather than holding GPU memory (a 3440 x 1440 frame is 20 MB).
/// </summary>
public sealed class Mp4Writer : IMediaWriter
{
    private readonly Channel<(IEncodableFrame Frame, TimeSpan At)> _video;
    private readonly Channel<AudioBlock> _audio = Channel.CreateUnbounded<AudioBlock>(new() { SingleReader = true });
    private readonly CancellationTokenSource _abort = new();
    private readonly FileStream _file;
    private readonly Task _transcode;
    private readonly VideoFormat _format;
    private readonly object _gate = new();
    private Held? _last;
    private TimeSpan _lastAt;
    private long _written;
    private long _dropped;
    private bool _closed;
    private int _failed;

    /// <param name="queue">Frames that may wait for the encoder before new ones are dropped.</param>
    /// <param name="live">False for re-encoding a file (trim, Media tools): <see cref="WriteVideoAsync"/> waits for
    /// the encoder instead of dropping.</param>
    public Mp4Writer(string path, VideoFormat format, int queue = 20, bool live = true)
    {
        if (format.Width < 2 || format.Height < 2 || format.Width % 2 != 0 || format.Height % 2 != 0)
            throw new ArgumentException("H.264 needs an even width and height.", nameof(format));
        _format = format;
        _video = Channel.CreateBounded<(IEncodableFrame, TimeSpan)>(
            new BoundedChannelOptions(queue) { FullMode = live ? BoundedChannelFullMode.DropWrite : BoundedChannelFullMode.Wait, SingleReader = true },
            dropped => { Interlocked.Increment(ref _dropped); dropped.Item1.Dispose(); });

        var video = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8,
            (uint)format.Width, (uint)format.Height));
        video.EncodingProperties.FrameRate.Numerator = (uint)format.Fps;
        video.EncodingProperties.FrameRate.Denominator = 1;
        var source = format.HasAudio
            ? new MediaStreamSource(video, new AudioStreamDescriptor(FloatPcm()))
            : new MediaStreamSource(video);
        source.BufferTime = TimeSpan.Zero;
        source.CanSeek = false;
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        _file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _transcode = Run(source, Profile(format));
        _transcode.ContinueWith(t =>
        {
            bool closed;
            lock (_gate) closed = _closed;
            if (!closed && Interlocked.Exchange(ref _failed, 1) == 0) Failed?.Invoke(t.Exception!.GetBaseException());
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public event Action<Exception>? Failed;

    /// <summary>Frames dropped because the encoder fell behind.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public static MediaEncodingProfile Profile(VideoFormat format)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)format.Width;
        profile.Video.Height = (uint)format.Height;
        profile.Video.FrameRate.Numerator = (uint)format.Fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        // Screen text needs more than a camera: about 0.1 bit a pixel a frame, 15 Mbps at 3440 x 1440 and 30 fps.
        profile.Video.Bitrate = (uint)Math.Clamp((long)format.Width * format.Height * format.Fps / 10, 2_000_000, 40_000_000);
        profile.Audio = format.HasAudio ? AudioEncodingProperties.CreateAac(AudioMixer.Rate, AudioMixer.Channels, 192_000) : null;
        return profile;
    }

    private static AudioEncodingProperties FloatPcm()
    {
        var pcm = AudioEncodingProperties.CreatePcm(AudioMixer.Rate, AudioMixer.Channels, 32);
        pcm.Subtype = MediaEncodingSubtypes.Float;
        return pcm;
    }

    private async Task Run(MediaStreamSource source, MediaEncodingProfile profile)
    {
        // The transcoder starts on the thread pool: preparing it can take a moment, and writes queue meanwhile.
        await Task.Yield();
        using var output = _file.AsRandomAccessStream();
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, profile).AsTask(_abort.Token);
        if (!prepared.CanTranscode) throw new NotSupportedException($"Recording failed: {prepared.FailureReason}");
        await prepared.TranscodeAsync().AsTask(_abort.Token);
    }

    public void WriteVideo(IVideoFrame frame, TimeSpan at)
    {
        if (frame is not IEncodableFrame encodable)
        {
            frame.Dispose();
            throw new ArgumentException("The MP4 writer takes GPU or BGRA frames.", nameof(frame));
        }
        lock (_gate)
        {
            if (_closed) { frame.Dispose(); return; }
            var kept = encodable.Keep();
            if (!ReferenceEquals(kept, encodable)) encodable.Dispose();
            if (!_video.Writer.TryWrite((kept, at))) { kept.Dispose(); return; }
            _lastAt = at;
        }
    }

    /// <summary>Queues <paramref name="frame"/>, waiting while the encoder is behind (a writer made with live off).</summary>
    public async ValueTask WriteVideoAsync(IEncodableFrame frame, TimeSpan at, CancellationToken cancel)
    {
        try
        {
            await _video.Writer.WriteAsync((frame, at), cancel).ConfigureAwait(false);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
        lock (_gate) _lastAt = at;
    }

    public void WriteAudio(AudioBlock block)
    {
        if (!_format.HasAudio) return;
        lock (_gate)
        {
            if (!_closed) _audio.Writer.TryWrite(block);
        }
    }

    public async Task<WriteResult> FinishAsync(TimeSpan duration)
    {
        lock (_gate)
        {
            if (_closed) throw new InvalidOperationException("Already finished.");
            _closed = true;
            // Hold the last frame to the end: a still screen sends nothing, and the file should last as long as the recording.
            var interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _format.Fps);
            if (_written + _video.Reader.Count > 0 && duration - _lastAt > interval + TimeSpan.FromMilliseconds(1))
                _video.Writer.TryWrite((Repeat.Instance, duration - interval));
            _video.Writer.TryComplete();
            _audio.Writer.TryComplete();
        }
        try
        {
            await _transcode.ConfigureAwait(false);
        }
        finally
        {
            Close();
        }
        return new WriteResult(Interlocked.Read(ref _written), _written > 0);
    }

    public async Task AbortAsync()
    {
        lock (_gate)
        {
            _closed = true;
            _video.Writer.TryComplete();
            _audio.Writer.TryComplete();
        }
        _abort.Cancel();
        try { await _transcode.ConfigureAwait(false); }
        catch (Exception) { /* Cancelled, or it had already failed: either way the file is the caller's to delete. */ }
        finally { Close(); }
    }

    private void Close()
    {
        while (_video.Reader.TryRead(out var queued)) queued.Frame.Dispose();
        lock (_gate)
        {
            _last?.Release();
            _last = null;
        }
        _file.Dispose();
    }

    private async void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e)
    {
        var request = e.Request;
        var deferral = request.GetDeferral();
        try
        {
            request.Sample = request.StreamDescriptor is VideoStreamDescriptor ? await NextVideo() : await NextAudio();
        }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
        {
            request.Sample = null;
        }
        catch (Exception ex)
        {
            request.Sample = null;
            if (Interlocked.Exchange(ref _failed, 1) == 0) Failed?.Invoke(ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<MediaStreamSample?> NextVideo()
    {
        if (!await _video.Reader.WaitToReadAsync(_abort.Token).ConfigureAwait(false)) return null;
        if (!_video.Reader.TryRead(out var next)) return null;
        Held held;
        lock (_gate)
        {
            if (next.Frame is Repeat)
            {
                if (_last is null) return null;
                held = _last;
                held.AddRef();
            }
            else
            {
                held = new Held(next.Frame);
                _last?.Release();
                _last = held;
                held.AddRef();
            }
        }
        var sample = held.Frame.ToSample(next.At);
        sample.Duration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _format.Fps);
        sample.Processed += (_, _) => held.Release();
        Interlocked.Increment(ref _written);
        return sample;
    }

    private async Task<MediaStreamSample?> NextAudio()
    {
        if (!await _audio.Reader.WaitToReadAsync(_abort.Token).ConfigureAwait(false)) return null;
        if (!_audio.Reader.TryRead(out var block)) return null;
        var bytes = new byte[block.Samples.Length * sizeof(float)];
        Buffer.BlockCopy(block.Samples, 0, bytes, 0, bytes.Length);
        var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), block.Time);
        sample.Duration = AudioMixer.TimeOf(block.Frames);
        return sample;
    }

    public void Dispose()
    {
        if (!_transcode.IsCompleted) _abort.Cancel();
        _abort.Dispose();
    }

    /// <summary>A frame held by the writer as the last one (to repeat at the end) and by samples in flight.</summary>
    private sealed class Held(IEncodableFrame frame)
    {
        private int _refs = 1;

        public IEncodableFrame Frame { get; } = frame;

        public void AddRef() => Interlocked.Increment(ref _refs);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refs) == 0) Frame.Dispose();
        }
    }

    /// <summary>Queued at Finish: repeat the last frame.</summary>
    private sealed class Repeat : IEncodableFrame
    {
        public static readonly Repeat Instance = new();
        public TimeSpan Time => TimeSpan.Zero;
        public IEncodableFrame Keep() => this;
        public MediaStreamSample ToSample(TimeSpan at) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
