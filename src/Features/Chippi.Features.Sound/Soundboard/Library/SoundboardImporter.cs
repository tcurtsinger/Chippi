// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Chippi.Features.Sound.Soundboard.Library;

/// <summary>Imports into an isolated draft, then normalizes audio once so every later play is local.</summary>
public sealed class SoundboardImporter : ISoundboardImporter, IDisposable
{
    public const long MaxSourceBytes = 250L * 1024 * 1024;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);
    private readonly string _draftRoot;
    private readonly ISoundboardDecoder _decoder;
    private readonly ISoundboardLinkDownloader _downloader;
    private readonly bool _ownsDownloader;

    public SoundboardImporter(string dataDirectory, ISoundboardDecoder? decoder = null, ISoundboardLinkDownloader? downloader = null)
    {
        _draftRoot = Path.Combine(Path.GetFullPath(dataDirectory), "imports");
        _decoder = decoder ?? new SoundboardDecoder();
        _ownsDownloader = downloader is null;
        _downloader = downloader ?? new SoundboardLinkDownloader();
    }

    public async Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(source.Value)) throw new SoundboardException("Choose a sound file or enter a link");
        string directory = Path.Combine(_draftRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SoundDownload input;
            if (source.IsUrl)
            {
                var uri = SoundboardLinkDownloader.ValidateUrl(source.Value);
                input = await _downloader.DownloadAsync(uri, directory, progress, cancellationToken).ConfigureAwait(false);
                // A downloader may only supply a file inside this draft, never an arbitrary executable/path.
                string path = Path.GetFullPath(input.LocalPath);
                if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new SoundboardException("Import returned an invalid file");
            }
            else
            {
                string sourcePath = Path.GetFullPath(source.Value);
                var info = new FileInfo(sourcePath);
                if (!info.Exists) throw new SoundboardException("Sound file was not found");
                if (info.Length > MaxSourceBytes) throw new SoundboardException("Choose a sound under 250 MB");
                string destination = Path.Combine(directory, "source" + SafeExtension(info.Extension));
                progress?.Report(new("Copying", 0));
                using var original = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                using var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                await CopyBoundedAsync(original, copy, info.Length, progress, cancellationToken).ConfigureAwait(false);
                input = new(destination, Path.GetFileNameWithoutExtension(sourcePath), "File");
            }
            cancellationToken.ThrowIfCancellationRequested();
            string wavePath = Path.Combine(directory, "sound.wav");
            var decoded = await _decoder.DecodeAsync(input.LocalPath, wavePath, progress, cancellationToken).ConfigureAwait(false);
            SoundboardStore.ValidateRange(decoded.DurationSeconds, 0, decoded.DurationSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("Ready", 1));
            return new(wavePath, SoundboardStore.CleanName(input.Title), input.Source, decoded.DurationSeconds, decoded.Waveform,
                () => DeleteDraft(directory));
        }
        catch (OperationCanceledException) { DeleteDraft(directory); throw; }
        catch (SoundboardException) { DeleteDraft(directory); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException
            or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            DeleteDraft(directory);
            throw new SoundboardException("Import failed. Choose a playable audio file or try the link again.", ex);
        }
    }

    private static string SafeExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".wav" or ".mp3" or ".m4a" or ".aac" or ".wma" or ".flac" or ".aif" or ".aiff" or ".mp4" => extension.ToLowerInvariant(),
        _ => ".audio",
    };

    internal static async Task CopyBoundedAsync(Stream input, Stream output, long? length, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[65536];
        long count = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            count += read;
            if (count > MaxSourceBytes) throw new SoundboardException("Choose a sound under 250 MB");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(new("Preparing", length > 0 ? Math.Clamp((double)count / length.Value, 0, 1) : null));
        }
        if (count == 0) throw new SoundboardException("The sound file is empty");
    }

    private void DeleteDraft(string directory)
    {
        string absolute = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(absolute), _draftRoot, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(absolute), "N", out _))
            throw new InvalidOperationException("Draft directory is outside the import folder.");
        try { if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_ownsDownloader && _downloader is IDisposable disposable) disposable.Dispose();
    }
}

/// <summary>File-only decoding to canonical 48 kHz stereo PCM16. It never accesses audio endpoints.</summary>
public sealed class SoundboardDecoder : ISoundboardDecoder
{
    public Task<SoundDecodedAudio> DecodeAsync(string inputPath, string wavePath, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Decode(inputPath, wavePath, progress, cancellationToken), cancellationToken);

    private static SoundDecodedAudio Decode(string inputPath, string wavePath, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken)
    {
        using WaveStream reader = Path.GetExtension(inputPath).ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(inputPath),
            ".aif" or ".aiff" => new AiffFileReader(inputPath),
            _ => new MediaFoundationReader(inputPath),
        };
        if (reader.TotalTime > SoundboardImporter.MaxDuration)
            throw new SoundboardException("Choose a sound under 30 minutes");
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        if (samples.WaveFormat.Channels != 2) throw new SoundboardException("Choose a mono or stereo sound");
        if (samples.WaveFormat.SampleRate != 48000) samples = new WdlResamplingSampleProvider(samples, 48000);
        using var writer = new WaveFileWriter(wavePath, new WaveFormat(48000, 16, 2));
        var buffer = new float[8192];
        var peaks = new List<float>();
        long count = 0;
        int inBucket = 0;
        float peak = 0;
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count += read;
            if (count > SoundboardImporter.MaxDuration.TotalSeconds * 96000)
                throw new SoundboardException("Choose a sound under 30 minutes");
            for (int i = 0; i < read; i++)
            {
                buffer[i] = float.IsFinite(buffer[i]) ? Math.Clamp(buffer[i], -1, 1) : 0;
                peak = Math.Max(peak, Math.Abs(buffer[i]));
                if (++inBucket == 9600) { peaks.Add(peak); peak = 0; inBucket = 0; }
            }
            writer.WriteSamples(buffer, 0, read);
            progress?.Report(new("Preparing", reader.TotalTime.TotalSeconds > 0
                ? Math.Clamp(count / 96000d / reader.TotalTime.TotalSeconds, 0, 1) : null));
        }
        if (inBucket > 0) peaks.Add(peak);
        if (count < 1920) throw new SoundboardException("Choose a sound at least 0.02 seconds long");
        int bars = Math.Min(128, peaks.Count);
        var waveform = new float[bars];
        for (int i = 0; i < peaks.Count; i++) waveform[i * bars / peaks.Count] = Math.Max(waveform[i * bars / peaks.Count], peaks[i]);
        return new(count / 96000d, waveform);
    }
}
