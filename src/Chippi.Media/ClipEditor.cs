// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Media.Encoding;
using Chippi.Media.Gif;

namespace Chippi.Media;

/// <param name="Frames">GIF frames written; held frames are joined into one.</param>
/// <param name="Cut">The span was longer than <see cref="GifPlan.Limit"/>, so only its first 60 seconds went in.</param>
public sealed record GifResult(int Frames, bool Cut);

/// <summary>
/// The recording page's re-encodes (screen-recording.md, Recording page and GIF): Trim writes a span of a recording
/// to a new MP4, Export GIF writes it as a looping GIF at 15 fps. Both run off the caller's thread, report progress
/// from 0 to 1, and on cancel or failure delete only the file they were writing.
/// </summary>
public static class ClipEditor
{
    public static Task TrimAsync(string source, string target, TrimRange span, IProgress<double>? progress = null,
        CancellationToken cancel = default) =>
        Guard(target, () => Trim(source, target, span, progress, cancel));

    public static Task<GifResult> ExportGifAsync(string source, string target, TrimRange span, IProgress<double>? progress = null,
        CancellationToken cancel = default) =>
        Guard(target, () => Task.FromResult(Gif(source, target, span, progress, cancel)));

    private static async Task<T> Guard<T>(string target, Func<Task<T>> work)
    {
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        catch
        {
            try { File.Delete(target); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static Task Guard(string target, Func<Task> work) => Guard(target, async () => { await work().ConfigureAwait(false); return 0; });

    private static async Task Trim(string source, string target, TrimRange span, IProgress<double>? progress, CancellationToken cancel)
    {
        using var reader = MediaReader.Open(source, audio: true);
        // Recordings are 48 kHz stereo; audio in any other shape isn't carried over.
        var audio = reader.HasAudio && reader.SampleRate == AudioMixer.Rate && reader.Channels == AudioMixer.Channels;
        var format = new VideoFormat(reader.Width, reader.Height, 30, audio);
        var writer = new Mp4Writer(target, format, live: false);
        try
        {
            var size = reader.Width * reader.Height * 4;
            reader.Seek(span.Start);
            byte[]? before = null;
            var wroteFirst = false;
            var audioDone = !audio;
            var audioAt = TimeSpan.Zero;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var pixels = new byte[size];
                if (reader.ReadFrame(pixels) is not { } time || time >= span.End) break;
                if (time < span.Start)
                {
                    before = pixels; // The seek lands on the key frame before the span; the last of these opens it.
                    continue;
                }
                var at = time - span.Start;
                if (!wroteFirst && at > TimeSpan.Zero && before is not null)
                    await writer.WriteVideoAsync(new BgraFrame(span.Start, before, reader.Width), TimeSpan.Zero, cancel).ConfigureAwait(false);
                wroteFirst = true;
                // Audio goes in ahead of the video, so the encoder never waits on it.
                while (!audioDone && audioAt <= time + TimeSpan.FromMilliseconds(500))
                    audioDone = !CopyAudio(reader, writer, span, ref audioAt);
                await writer.WriteVideoAsync(new BgraFrame(time, pixels, reader.Width), at, cancel).ConfigureAwait(false);
                progress?.Report(Math.Clamp(at / span.Length, 0, 1));
            }
            while (!audioDone) audioDone = !CopyAudio(reader, writer, span, ref audioAt);
            cancel.ThrowIfCancellationRequested();
            var result = await writer.FinishAsync(span.Length).ConfigureAwait(false);
            if (!result.Playable) throw new InvalidDataException("Trim failed: no frames in the span.");
            progress?.Report(1);
        }
        catch
        {
            await writer.AbortAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Copies the next audio packet's part inside <paramref name="span"/>.</summary>
    /// <returns>False at the end of the audio or of the span.</returns>
    private static bool CopyAudio(MediaReader reader, Mp4Writer writer, TrimRange span, ref TimeSpan at)
    {
        if (reader.ReadAudio(out var time) is not { } samples) return false;
        var frames = samples.Length / AudioMixer.Channels;
        at = time + AudioMixer.TimeOf(frames);
        if (time >= span.End) return false;
        var first = time < span.Start ? AudioMixer.FrameAt(span.Start - time) : 0;
        var last = Math.Min(frames, AudioMixer.FrameAt(span.End - time));
        if (last <= first) return true;
        var cut = samples.AsSpan((int)(first * AudioMixer.Channels), (int)((last - first) * AudioMixer.Channels)).ToArray();
        writer.WriteAudio(new AudioBlock(time + AudioMixer.TimeOf(first) - span.Start, cut));
        return true;
    }

    private static GifResult Gif(string source, string target, TrimRange range, IProgress<double>? progress, CancellationToken cancel)
    {
        using var reader = MediaReader.Open(source);
        var (span, cut) = GifPlan.Span(range);
        var count = GifPlan.FrameCount(span.Length);
        var size = reader.Width * reader.Height * 4;
        var diff = new GifFrameDiff(reader.Width, reader.Height);
        using var gif = new GifWriter(target, reader.Width, reader.Height);
        reader.Seek(span.Start);

        // The frame shown at each GIF slot is the last one at or before it.
        var shown = new byte[size];
        var next = new byte[size];
        var nextTime = reader.ReadFrame(next);
        var any = false;
        GifPatch? pending = null;
        var pendingFirst = 0;
        for (var i = 0; i < count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var slot = span.Start + GifPlan.FrameTime(i);
            while (nextTime is { } t && (t <= slot || !any))
            {
                (shown, next) = (next, shown);
                any = true;
                nextTime = reader.ReadFrame(next);
            }
            if (!any) break;
            if (diff.Next(shown) is { } patch)
            {
                if (pending is not null) gif.Add(pending, GifPlan.Delay(pendingFirst, i - pendingFirst));
                pending = patch;
                pendingFirst = i;
            }
            progress?.Report((i + 1) / (double)count);
        }
        if (pending is null) throw new InvalidDataException("Export GIF failed: no frames in the span.");
        gif.Add(pending, GifPlan.Delay(pendingFirst, count - pendingFirst));
        gif.Finish();
        return new GifResult(gif.Frames, cut);
    }
}
