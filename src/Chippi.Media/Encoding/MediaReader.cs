// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Variant;

namespace Chippi.Media.Encoding;

/// <summary>
/// Reads a video file's frames as BGRA (top-down) and its audio as 48 kHz-or-native float, through Media
/// Foundation's source reader with its video processor on. The GIF path reads recordings with it, Media tools
/// reads what Windows can decode, and the tests check what the writer made. One thread at a time.
/// </summary>
public sealed class MediaReader : IDisposable
{
    private const uint FirstVideo = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    private const uint FirstAudio = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
    private const uint AllStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);
    private const uint MediaSource = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_MEDIASOURCE);

    private static readonly Lazy<bool> Started = new(() =>
    {
        // MF_VERSION (MF_SDK_VERSION 0x0002 << 16 | MF_API_VERSION 0x0070), MFSTARTUP_FULL.
        MediaPInvoke.MFStartup(0x0002_0070, 0).ThrowOnFailure();
        return true;
    });

    private readonly IMFSourceReader _reader;

    private readonly int _stride;

    private MediaReader(IMFSourceReader reader, int width, int height, int stride, TimeSpan duration, int sampleRate, int channels)
    {
        _reader = reader;
        _stride = stride;
        Width = width;
        Height = height;
        Duration = duration;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The decoder's row stride in bytes; negative when its frames are bottom-up.</summary>
    public int Stride => _stride;

    public TimeSpan Duration { get; }

    /// <summary>0 when the file has no audio, or audio wasn't asked for.</summary>
    public int SampleRate { get; }

    public int Channels { get; }

    public bool HasAudio => SampleRate > 0;

    /// <exception cref="COMException">Windows can't read the file, or has no decoder for it.</exception>
    public static unsafe MediaReader Open(string path, bool audio = false)
    {
        _ = Started.Value;
        MediaPInvoke.MFCreateAttributes(out var attributes, 1).ThrowOnFailure();
        var processing = MediaPInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
        attributes.SetUINT32(&processing, 1);
        MediaPInvoke.MFCreateSourceReaderFromURL(path, attributes, out var reader).ThrowOnFailure();
        try
        {
            reader.SetStreamSelection(AllStreams, false);
            reader.SetStreamSelection(FirstVideo, true);
            MediaPInvoke.MFCreateMediaType(out var video).ThrowOnFailure();
            var major = MediaPInvoke.MFMediaType_Video;
            var rgb = MediaPInvoke.MFVideoFormat_RGB32;
            var majorKey = MediaPInvoke.MF_MT_MAJOR_TYPE;
            var subtypeKey = MediaPInvoke.MF_MT_SUBTYPE;
            video.SetGUID(&majorKey, &major);
            video.SetGUID(&subtypeKey, &rgb);
            reader.SetCurrentMediaType(FirstVideo, video);
            reader.GetCurrentMediaType(FirstVideo, out var current);
            var sizeKey = MediaPInvoke.MF_MT_FRAME_SIZE;
            current.GetUINT64(&sizeKey, out var size);
            var width = (int)(size >> 32);
            var strideKey = MediaPInvoke.MF_MT_DEFAULT_STRIDE;
            // RGB without a stated stride is bottom-up in Media Foundation; the video processor usually says.
            var stride = -width * 4;
            try { current.GetUINT32(&strideKey, out var stated); stride = unchecked((int)stated); }
            catch (COMException) { }

            int rate = 0, channels = 0;
            if (audio && TrySelectAudio(reader, out rate, out channels)) { }

            var durationKey = MediaPInvoke.MF_PD_DURATION;
            reader.GetPresentationAttribute(MediaSource, &durationKey, out var duration);
            var length = TimeSpan.FromTicks((long)duration.Anonymous.Anonymous.Anonymous.uhVal);
            return new MediaReader(reader, width, (int)(size & 0xFFFF_FFFF), stride, length, rate, channels);
        }
        catch
        {
            Marshal.ReleaseComObject(reader);
            throw;
        }
    }

    private static unsafe bool TrySelectAudio(IMFSourceReader reader, out int rate, out int channels)
    {
        rate = channels = 0;
        try
        {
            reader.SetStreamSelection(FirstAudio, true);
        }
        catch (COMException)
        {
            return false; // No audio stream.
        }
        MediaPInvoke.MFCreateMediaType(out var pcm).ThrowOnFailure();
        var major = MediaPInvoke.MFMediaType_Audio;
        var single = MediaPInvoke.MFAudioFormat_Float;
        var majorKey = MediaPInvoke.MF_MT_MAJOR_TYPE;
        var subtypeKey = MediaPInvoke.MF_MT_SUBTYPE;
        pcm.SetGUID(&majorKey, &major);
        pcm.SetGUID(&subtypeKey, &single);
        reader.SetCurrentMediaType(FirstAudio, pcm);
        reader.GetCurrentMediaType(FirstAudio, out var current);
        var rateKey = MediaPInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND;
        var channelsKey = MediaPInvoke.MF_MT_AUDIO_NUM_CHANNELS;
        current.GetUINT32(&rateKey, out var r);
        current.GetUINT32(&channelsKey, out var c);
        rate = (int)r;
        channels = (int)c;
        return true;
    }

    /// <summary>Moves to <paramref name="at"/>: the next frame read is the key frame at or before it.</summary>
    public void Seek(TimeSpan at)
    {
        var position = default(Windows.Win32.System.Com.StructuredStorage.PROPVARIANT);
        position.Anonymous.Anonymous.vt = VARENUM.VT_I8;
        position.Anonymous.Anonymous.Anonymous.hVal = at.Ticks;
        _reader.SetCurrentPosition(Guid.Empty, position);
    }

    /// <summary>Reads the next frame into <paramref name="bgra"/> (<see cref="Width"/> × <see cref="Height"/> × 4).</summary>
    /// <returns>The frame's time, or null at the end.</returns>
    public TimeSpan? ReadFrame(Span<byte> bgra)
    {
        while (true)
        {
            _reader.ReadSample(FirstVideo, 0, out _, out var flags, out var time, out var sample);
            if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0 || flags.HasError())
            {
                Release(sample);
                return null;
            }
            if (sample is null) continue; // A gap or a format change: read on.
            try
            {
                CopyFrame(sample, bgra);
                return TimeSpan.FromTicks(time);
            }
            finally
            {
                Release(sample);
            }
        }
    }

    /// <summary>Reads the next audio packet, interleaved, with its time; null at the end.</summary>
    public float[]? ReadAudio(out TimeSpan time)
    {
        time = default;
        if (!HasAudio) return null;
        while (true)
        {
            _reader.ReadSample(FirstAudio, 0, out _, out var flags, out var at, out var sample);
            if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0 || flags.HasError())
            {
                Release(sample);
                return null;
            }
            if (sample is null) continue;
            try
            {
                using var locked = new Locked(sample);
                time = TimeSpan.FromTicks(at);
                return MemoryMarshal.Cast<byte, float>(locked.Bytes).ToArray();
            }
            finally
            {
                Release(sample);
            }
        }
    }

    /// <summary>Reads every audio sample from here to the end, interleaved.</summary>
    public float[] ReadAllAudio()
    {
        if (!HasAudio) return [];
        var all = new List<float>();
        while (true)
        {
            _reader.ReadSample(FirstAudio, 0, out _, out var flags, out _, out var sample);
            if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0 || flags.HasError())
            {
                Release(sample);
                return [.. all];
            }
            if (sample is null) continue;
            try
            {
                using var locked = new Locked(sample);
                all.AddRange(MemoryMarshal.Cast<byte, float>(locked.Bytes).ToArray());
            }
            finally
            {
                Release(sample);
            }
        }
    }

    private void CopyFrame(IMFSample sample, Span<byte> bgra)
    {
        var row = Width * 4;
        if (bgra.Length < row * Height) throw new ArgumentException("Buffer is smaller than a frame.", nameof(bgra));
        using var locked = new Locked(sample);
        var bytes = locked.Bytes;
        var stride = Math.Abs(_stride);
        if (stride < row || bytes.Length < stride * Height) throw new InvalidDataException("Frame is smaller than its size.");
        for (var y = 0; y < Height; y++)
        {
            var from = _stride > 0 ? y : Height - 1 - y;
            bytes.Slice(from * stride, row).CopyTo(bgra.Slice(y * row, row));
        }
    }

    /// <summary>A sample's bytes, locked until disposed.</summary>
    private readonly unsafe ref struct Locked
    {
        private readonly IMFMediaBuffer _buffer;
        private readonly byte* _data;
        private readonly int _length;

        public Locked(IMFSample sample)
        {
            sample.ConvertToContiguousBuffer(out _buffer);
            byte* data;
            uint length;
            try
            {
                _buffer.Lock(&data, null, &length);
            }
            catch
            {
                Marshal.ReleaseComObject(_buffer);
                throw;
            }
            _data = data;
            _length = (int)length;
        }

        public ReadOnlySpan<byte> Bytes => new(_data, _length);

        public void Dispose()
        {
            _buffer.Unlock();
            Marshal.ReleaseComObject(_buffer);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null) Marshal.ReleaseComObject(com);
    }

    public void Dispose() => Marshal.ReleaseComObject(_reader);
}

internal static class ReaderFlags
{
    public static bool HasError(this uint flags) => (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0;
}
