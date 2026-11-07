// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Media;

namespace Chippi.Features.Recording;

/// <summary>
/// The PC side of a recording: its sources, encoder and files, and the frame and pill on screen.
/// <see cref="WindowsRecordingPc"/> is the real one; tests pass a fake.
/// </summary>
public interface IRecordingPc
{
    /// <summary>Why recording can't work on this PC ("Recording failed", with this in Settings); null when it can.</summary>
    string? Missing { get; }

    IMediaWriterFactory Writers { get; }

    /// <summary>A display, or a region of it in its own physical pixels.</summary>
    IVideoSource Display(nint monitor, Chippi.Media.Capture.PixelRect? region, bool cursor);

    /// <summary>A window, followed as it moves. Throws when Windows refuses it ("Can't record this window").</summary>
    IVideoSource Window(nint window, bool cursor);

    IAudioSource SystemSound();

    IAudioSource Mic();

    /// <summary>A new temporary MP4's path, in a folder only recordings use.</summary>
    string TempFile();

    /// <summary>The folder Save writes to when Settings has none (Videos\Chippi).</summary>
    string DefaultSaveFolder { get; }

    /// <summary>The red frame and the pill around what's being recorded, both left out of the video.</summary>
    IRecordingOverlay ShowOverlay(RecordingTarget target, RecordingController recording);

    /// <summary>The first frame of a finished recording, for the page and Recent; null when it can't be read.</summary>
    BitmapSource? Poster(string path);

    Task TrimAsync(string source, string target, TrimRange span, IProgress<double> progress, CancellationToken cancel);

    Task<GifResult> GifAsync(string source, string target, TrimRange span, IProgress<double> progress, CancellationToken cancel);
}

/// <summary>What's being recorded, as the selector left it (virtual-screen physical pixels).</summary>
/// <param name="Window">The window Record window follows; zero for a region or a display.</param>
public sealed record RecordingTarget(RecordingSource Source, PixelRect Rect, PixelRect Display, nint Monitor, int Dpi, nint Window);

/// <summary>The bar's and the pill's source name.</summary>
public enum RecordingSource { Region, Window, Screen }

/// <summary>The frame and the pill while a recording runs. Dispose takes both away.</summary>
public interface IRecordingOverlay : IDisposable;

/// <summary>What the controller needs from the module and the shell. Called on the UI thread.</summary>
public interface IRecordingShell
{
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);

    /// <summary>A 6-second line on the bar ("Already recording", "Recording saved" over "0:42 · Region").</summary>
    void Feedback(string title, string source, bool failed, Action? clicked = null);

    /// <summary>The live line while recording ("0:42" over "Recording · Region"), or none for null.</summary>
    void Live(string? title, string? source);

    /// <summary>Opens the Capture panel on the recording page; false when it can't (a fullscreen app holds the widget's display).</summary>
    bool OpenReview();

    /// <summary>Adds a saved recording to Recent with its poster and length.</summary>
    void AddToRecent(string file, BitmapSource? poster, TimeSpan length, DateTimeOffset time);

    /// <summary>A recording started (true) or ended: the puck is left out of captures only while one runs.</summary>
    void Recording(bool on);
}
