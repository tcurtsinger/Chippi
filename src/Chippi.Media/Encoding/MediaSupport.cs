// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace Chippi.Media.Encoding;

/// <summary>
/// Whether Windows can record (screen-recording.md, Video): a Windows N edition without the Media Feature Pack has no
/// H.264 or AAC encoder, and Recording says "Recording failed" with this cause in Settings.
/// </summary>
public static class MediaSupport
{
    private static readonly Lazy<string?> Missing = new(Find);

    /// <summary>Null when MP4 recording works; otherwise what's missing.</summary>
    public static string? MissingEncoder => Missing.Value;

    /// <summary>
    /// Whether the GPU's encoders may be used (the default). <c>CHIPPI_MEDIA_SOFTWARE=1</c> keeps to Windows' software
    /// ones: in Windows Sandbox, loading the host GPU's H.264 encoder takes the whole Sandbox down (0x80370106, 2026-11-07).
    /// </summary>
    public static bool Hardware { get; internal set; } = Environment.GetEnvironmentVariable("CHIPPI_MEDIA_SOFTWARE") != "1";

    private static string? Find()
    {
        try
        {
            if (Count(MediaPInvoke.MFT_CATEGORY_VIDEO_ENCODER, MediaPInvoke.MFVideoFormat_H264) == 0) return "No H.264 encoder";
            if (Count(MediaPInvoke.MFT_CATEGORY_AUDIO_ENCODER, MediaPInvoke.MFAudioFormat_AAC) == 0) return "No AAC encoder";
            return null;
        }
        catch (DllNotFoundException)
        {
            return "Media Foundation isn't installed";
        }
        catch (EntryPointNotFoundException)
        {
            return "Media Foundation isn't installed";
        }
    }

    private static unsafe uint Count(Guid category, Guid output)
    {
        var type = new MFT_REGISTER_TYPE_INFO { guidMajorType = category == MediaPInvoke.MFT_CATEGORY_VIDEO_ENCODER
            ? MediaPInvoke.MFMediaType_Video : MediaPInvoke.MFMediaType_Audio, guidSubtype = output };
        uint count = 0;
        IMFActivate_unmanaged** found;
        var flags = MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_ASYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER
            | (Hardware ? MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE : 0);
        if (MediaPInvoke.MFTEnumEx(category, flags, null, &type, out found, &count).Failed) return 0;
        for (var i = 0; i < count; i++) found[i]->Release();
        MediaPInvoke.CoTaskMemFree(found);
        return count;
    }
}
