// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;

namespace Chippi.Media;

[ComVisible(true), Guid("a27003cf-2354-4f2a-8d6a-ab7cff15437e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecordingSampleCallback
{
    [PreserveSig] int GetParameters(out uint flags, out uint queue);
    [PreserveSig] int Invoke(nint result);
}

/// <summary>Fragmented MP4, hardware H.264, and timestamped PCM input. All calls belong to the recorder's worker.</summary>
internal sealed unsafe class RecordingWriter : IDisposable
{
    private IMFSinkWriter* _writer;
    private IMFMediaSink* _sink;
    private IMFByteStream* _stream;
    private IMFDXGIDeviceManager* _manager;
    private readonly bool _audio;
    private readonly long _frameDuration;
    private bool _finished;
    public string Encoder { get; private set; } = "Hardware H.264";

    public RecordingWriter(string path, RecordingGraphics graphics, RecordingOptions options, bool audio, Action<string>? trace = null)
    {
        _audio = audio; _frameDuration = TimeSpan.TicksPerSecond / options.FrameRate;
        IMFMediaType* video = null; IMFMediaType* sound = null; IMFMediaType* input = null; IMFAttributes* attributes = null;
        IMFByteStream* stream = null; IMFMediaSink* sink = null; IMFSinkWriter* writer = null; IMFDXGIDeviceManager* manager = null;
        try
        {
            var size = options.OutputSize(graphics.Width, graphics.Height);
            trace?.Invoke("Configuring MP4 streams");
            video = VideoType(PInvoke.MFVideoFormat_H264, size.Width, size.Height, options.FrameRate);
            video->SetUINT32(in PInvoke.MF_MT_AVG_BITRATE, (uint)Math.Clamp((long)size.Width * size.Height * options.FrameRate / 8, 2_000_000, 50_000_000));
            if (audio) sound = AudioType(PInvoke.MFAudioFormat_AAC);
            PInvoke.MFCreateFile(MF_FILE_ACCESSMODE.MF_ACCESSMODE_WRITE, MF_FILE_OPENMODE.MF_OPENMODE_FAIL_IF_EXIST,
                MF_FILE_FLAGS.MF_FILEFLAGS_NONE, path, &stream).ThrowOnFailure();
            _stream = stream;
            PInvoke.MFCreateFMPEG4MediaSink(stream, video, sound, &sink).ThrowOnFailure(); _sink = sink;
            PInvoke.MFCreateDXGIDeviceManager(out uint token, &manager).ThrowOnFailure(); _manager = manager;
            manager->ResetDevice((IUnknown*)graphics.Device, token);
            PInvoke.MFCreateAttributes(&attributes, 4).ThrowOnFailure();
            attributes->SetUINT32(in PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            attributes->SetUnknown(in PInvoke.MF_SINK_WRITER_D3D_MANAGER, (IUnknown*)manager);
            attributes->SetUINT32(in PInvoke.MF_LOW_LATENCY, 1);
            PInvoke.MFCreateSinkWriterFromMediaSink(sink, attributes, &writer).ThrowOnFailure(); _writer = writer;
            trace?.Invoke("Configuring GPU input");
            input = VideoType(PInvoke.MFVideoFormat_ARGB32, graphics.Width, graphics.Height, options.FrameRate);
            writer->SetInputMediaType(0, input, null); input->Release(); input = null;
            if (audio) { input = AudioType(PInvoke.MFAudioFormat_PCM); writer->SetInputMediaType(1, input, null); }
            trace?.Invoke("Starting encoder"); writer->BeginWriting();
            VerifyHardware(options.AllowSoftwareForFixture);
            trace?.Invoke("Encoder selected: " + Encoder);
        }
        catch { Dispose(); throw; }
        finally { if (input != null) input->Release(); if (attributes != null) attributes->Release(); if (video != null) video->Release(); if (sound != null) sound->Release(); }
    }

    private static IMFMediaType* VideoType(Guid subtype, int width, int height, int fps)
    {
        IMFMediaType* type = null; PInvoke.MFCreateMediaType(&type).ThrowOnFailure();
        try
        {
            type->SetGUID(in PInvoke.MF_MT_MAJOR_TYPE, in PInvoke.MFMediaType_Video);
            type->SetGUID(in PInvoke.MF_MT_SUBTYPE, in subtype);
            type->SetUINT64(in PInvoke.MF_MT_FRAME_SIZE, ((ulong)(uint)width << 32) | (uint)height);
            type->SetUINT64(in PInvoke.MF_MT_FRAME_RATE, ((ulong)(uint)fps << 32) | 1);
            type->SetUINT64(in PInvoke.MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
            type->SetUINT32(in PInvoke.MF_MT_INTERLACE_MODE, 2);
            type->SetUINT32(in PInvoke.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
            return type;
        }
        catch { type->Release(); throw; }
    }

    private static IMFMediaType* AudioType(Guid subtype)
    {
        IMFMediaType* type = null; PInvoke.MFCreateMediaType(&type).ThrowOnFailure();
        try
        {
            type->SetGUID(in PInvoke.MF_MT_MAJOR_TYPE, in PInvoke.MFMediaType_Audio);
            type->SetGUID(in PInvoke.MF_MT_SUBTYPE, in subtype);
            type->SetUINT32(in PInvoke.MF_MT_AUDIO_NUM_CHANNELS, 2);
            type->SetUINT32(in PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, 48_000);
            type->SetUINT32(in PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            type->SetUINT32(in PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, subtype == PInvoke.MFAudioFormat_AAC ? 24_000u : 192_000u);
            if (subtype == PInvoke.MFAudioFormat_PCM) type->SetUINT32(in PInvoke.MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
            else type->SetUINT32(in PInvoke.MF_MT_AAC_PAYLOAD_TYPE, 0);
            return type;
        }
        catch { type->Release(); throw; }
    }

    private void VerifyHardware(bool allowSoftwareForFixture)
    {
        IMFSinkWriterEx* extended = null;
        var id = typeof(IMFSinkWriterEx).GUID;
        _writer->QueryInterface(&id, (void**)&extended).ThrowOnFailure();
        try
        {
            for (uint index = 0; index < 12; index++)
            {
                IMFTransform* transform = null; IMFAttributes* attributes = null;
                try
                {
                    extended->GetTransformForStream(0, index, out var category, &transform);
                    if (category != PInvoke.MFT_CATEGORY_VIDEO_ENCODER) continue;
                    transform->GetAttributes(&attributes);
                    string? hardware = GetString(attributes, PInvoke.MFT_ENUM_HARDWARE_URL_Attribute);
                    if (string.IsNullOrWhiteSpace(hardware) && !allowSoftwareForFixture)
                        throw new NotSupportedException("A hardware H.264 encoder could not be verified. CPU encoding is not enabled.");
                    Encoder = GetString(attributes, PInvoke.MFT_FRIENDLY_NAME_Attribute) ?? (hardware == null ? "Software fixture encoder" : "Hardware H.264");
                    return;
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x80070103) { break; }
                finally { if (attributes != null) attributes->Release(); if (transform != null) transform->Release(); }
            }
            throw new NotSupportedException("No hardware H.264 encoder is available for these recording settings.");
        }
        finally { extended->Release(); }
    }

    private static string? GetString(IMFAttributes* attributes, Guid key)
    {
        try
        {
            attributes->GetAllocatedString(in key, out PWSTR text, out _);
            try { return text.ToString(); } finally { Marshal.FreeCoTaskMem((nint)text.Value); }
        }
        catch (COMException) { return null; }
    }

    public void Video(RecordingGraphics.Surface surface, TimeSpan timestamp)
    {
        IMFTrackedSample* tracked = null; IMFSample* sample = null; IMFMediaBuffer* buffer = null;
        bool assigned = false;
        try
        {
            PInvoke.MFCreateTrackedSample(&tracked).ThrowOnFailure();
            var id = typeof(IMFSample).GUID; tracked->QueryInterface(&id, (void**)&sample).ThrowOnFailure();
            var textureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            PInvoke.MFCreateDXGISurfaceBuffer(in textureId, (IUnknown*)surface.Texture, 0, false, &buffer).ThrowOnFailure();
            buffer->GetMaxLength(out uint length); buffer->SetCurrentLength(length);
            sample->AddBuffer(buffer); sample->SetSampleTime(timestamp.Ticks); sample->SetSampleDuration(_frameDuration);
            var callback = new SampleReturned(surface);
            nint ccw = Marshal.GetComInterfaceForObject(callback, typeof(IRecordingSampleCallback));
            try { tracked->SetAllocator((IMFAsyncCallback*)ccw, null); assigned = true; } finally { Marshal.Release(ccw); }
            _writer->WriteSample(0, sample);
        }
        finally
        {
            if (buffer != null) buffer->Release(); if (sample != null) sample->Release(); if (tracked != null) tracked->Release();
            if (!assigned) surface.Return();
        }
    }

    public void Audio(ReadOnlySpan<byte> pcm, long startFrame)
    {
        if (!_audio || pcm.IsEmpty) return;
        IMFMediaBuffer* buffer = null; IMFSample* sample = null;
        try
        {
            PInvoke.MFCreateMemoryBuffer((uint)pcm.Length, &buffer).ThrowOnFailure();
            byte* target = null; buffer->Lock(&target, null, null);
            try { pcm.CopyTo(new Span<byte>(target, pcm.Length)); } finally { buffer->Unlock(); }
            buffer->SetCurrentLength((uint)pcm.Length);
            PInvoke.MFCreateSample(&sample).ThrowOnFailure(); sample->AddBuffer(buffer);
            sample->SetSampleTime(startFrame * TimeSpan.TicksPerSecond / 48_000);
            sample->SetSampleDuration((pcm.Length / 4) * TimeSpan.TicksPerSecond / 48_000);
            _writer->WriteSample(1, sample);
        }
        finally { if (sample != null) sample->Release(); if (buffer != null) buffer->Release(); }
    }

    public void Finish() { if (_finished) return; _finished = true; _writer->Finalize(); }
    public void Dispose()
    {
        bool sinkOwnedStream = _sink != null;
        if (_writer != null) { _writer->Release(); _writer = null; }
        if (_sink != null) { try { _sink->Shutdown(); } finally { _sink->Release(); _sink = null; } }
        if (_stream != null) { try { if (!sinkOwnedStream) _stream->Close(); } finally { _stream->Release(); _stream = null; } }
        if (_manager != null) { _manager->Release(); _manager = null; }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class SampleReturned(RecordingGraphics.Surface surface) : IRecordingSampleCallback
    {
        private int _returned;
        public int GetParameters(out uint flags, out uint queue) { flags = queue = 0; return unchecked((int)0x80004001); }
        public int Invoke(nint result) { if (Interlocked.Exchange(ref _returned, 1) == 0) surface.Return(); return 0; }
    }
}
