// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.Sound.Soundboard.Library;

/// <summary>Atomic library metadata and a closed media directory; imported originals are never modified.</summary>
public sealed class SoundboardStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _file;
    private readonly string _media;

    public SoundboardStore(string dataDirectory)
    {
        Root = Path.GetFullPath(dataDirectory);
        _file = Path.Combine(Root, "library.json");
        _media = Path.Combine(Root, "media");
    }

    public string Root { get; }
    public string? RecoveryNotice { get; private set; }

    public SoundboardLibrary Load()
    {
        lock (_gate)
        {
            RecoveryNotice = null;
            if (!File.Exists(_file)) return new();
            try { return Read(_file); }
            catch (Exception ex) when (ex is JsonException or IOException or SoundboardException)
            {
                if (File.Exists(_file + ".bak"))
                {
                    try
                    {
                        var recovered = Read(_file + ".bak");
                        RecoveryNotice = "Library restored from backup";
                        return recovered;
                    }
                    catch (Exception backup) when (backup is JsonException or IOException or SoundboardException) { }
                }
                throw new SoundboardException("Library could not be read. Your sound files are still saved.", ex);
            }
        }
    }

    private static SoundboardLibrary Read(string file)
    {
        if (new FileInfo(file).Length > 16 * 1024 * 1024)
            throw new SoundboardException("Library is too large");
        return Normalize(JsonSerializer.Deserialize<SoundboardLibrary>(File.ReadAllBytes(file), Json)
            ?? throw new SoundboardException("Library is empty"));
    }

    public void Save(SoundboardLibrary library)
    {
        var normalized = Normalize(library);
        lock (_gate)
        {
            Directory.CreateDirectory(Root);
            string pending = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, normalized, Json);
                    stream.Flush(flushToDisk: true);
                }
                // Preserve a known-good previous library, never a corrupt primary after recovery.
                if (File.Exists(_file) && RecoveryNotice is null)
                    File.Copy(_file, _file + ".bak", overwrite: true);
                File.Move(pending, _file, overwrite: true);
                RecoveryNotice = null;
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }

    public SoundClip CommitDraft(SoundImportDraft draft, string name, string collectionId, double startSeconds, double endSeconds, string? imagePath = null, bool favorite = false)
    {
        ObjectDisposedException.ThrowIf(draft.IsDisposed, draft);
        ValidateRange(draft.DurationSeconds, startSeconds, endSeconds);
        string id = Guid.NewGuid().ToString("N");
        var clip = new SoundClip
        {
            Id = id, Name = CleanName(name), CollectionId = collectionId,
            AudioFile = id + ".wav", Source = draft.Source,
            DurationSeconds = draft.DurationSeconds, StartSeconds = startSeconds, EndSeconds = endSeconds,
            Favorite = favorite, Waveform = CleanWaveform(draft.Waveform),
        };
        Directory.CreateDirectory(_media);
        try
        {
            File.Copy(draft.LocalAudioPath, AudioPath(clip), overwrite: false);
            if (!string.IsNullOrWhiteSpace(imagePath)) clip = clip with { ImageFile = ImportCover(imagePath) };
            return clip;
        }
        catch
        {
            DeleteMedia(clip);
            throw;
        }
    }

    public string AudioPath(SoundClip clip) => MediaPath(clip.AudioFile, ".wav");
    public string? ImagePath(SoundClip clip) => clip.ImageFile is null ? null : MediaPath(clip.ImageFile, ".png");

    /// <summary>Call only after metadata no longer references the clip; malformed paths never reach the filesystem.</summary>
    public void DeleteMedia(SoundClip clip)
    {
        foreach (var path in new[] { AudioPath(clip), ImagePath(clip) }.OfType<string>())
        {
            try { File.Delete(path); }
            catch (IOException) { } // A still-closing preview may release it shortly; metadata remains valid.
            catch (UnauthorizedAccessException) { }
        }
    }

    public void DisposeDraft(SoundImportDraft draft) => draft.Dispose();

    public void DeleteCover(string? ownedFileName)
    {
        if (ownedFileName is null) return;
        string path = MediaPath(ownedFileName, ".png");
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string ImportCover(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new SoundboardException("Choose an image under 20 MB");
        string name = Guid.NewGuid().ToString("N") + ".png";
        string destination = MediaPath(name, ".png");
        using var input = File.OpenRead(path);
        // Read image headers before asking WIC to allocate decoded pixels. Constrain both axes,
        // including tall/narrow images which a width-only thumbnail would otherwise enlarge.
        var header = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = header.Frames.FirstOrDefault() ?? throw new SoundboardException("Choose a readable image");
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || width > 16000 || height > 16000 || (long)width * height > 64_000_000)
            throw new SoundboardException("Choose an image under 64 megapixels");
        double scale = Math.Min(1, 256d / Math.Max(width, height));
        input.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = Math.Max(1, (int)Math.Round(width * scale));
        image.DecodePixelHeight = Math.Max(1, (int)Math.Round(height * scale));
        image.StreamSource = input;
        image.EndInit();
        image.Freeze();
        if (image.PixelHeight > 256 || image.PixelWidth > 256 || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            throw new SoundboardException("Choose a smaller image");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(_media);
        try
        {
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoder.Save(output);
            return name;
        }
        catch { File.Delete(destination); throw; }
    }

    private string MediaPath(string name, string extension)
    {
        if (!IsOwnedName(name, extension)) throw new SoundboardException("Invalid sound file path");
        return Path.Combine(_media, name);
    }

    private static bool IsOwnedName(string? name, string extension) => name is not null
        && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
        && name.Length == 32 + extension.Length && Guid.TryParseExact(name[..32], "N", out _);

    public static string CleanName(string? name)
    {
        string printable = new((name ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        string value = string.Join(" ", printable.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length == 0 ? "Sound" : value[..Math.Min(value.Length, 100)];
    }

    public static void ValidateRange(double duration, double start, double end)
    {
        if (!double.IsFinite(duration) || !double.IsFinite(start) || !double.IsFinite(end)
            || duration <= 0 || duration > SoundboardImporter.MaxDuration.TotalSeconds
            || start < 0 || end > duration + .001 || end - start < .02)
            throw new SoundboardException("Choose a valid start and end time");
    }

    public static SoundboardLibrary Normalize(SoundboardLibrary library)
    {
        if (library.Version != 1 || library.Collections is null || library.Clips is null)
            throw new SoundboardException("Library format is not supported");
        if (library.Collections.Count > 1000 || library.Clips.Count > 10000)
            throw new SoundboardException("Library is too large");
        var collections = library.Collections.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Id)
                && c.Id != SoundboardLibrary.FavoritesId).DistinctBy(c => c.Id)
            .Select(c => c with { Name = CleanName(c.Name) }).ToList();
        if (collections.Count == 0) collections.Add(new(SoundboardLibrary.DefaultCollectionId, "Sounds"));
        var ids = collections.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var clips = new List<SoundClip>();
        foreach (var clip in library.Clips.DistinctBy(c => c?.Id))
        {
            if (clip is null || string.IsNullOrWhiteSpace(clip.Id) || !IsOwnedName(clip.AudioFile, ".wav")
                || (clip.ImageFile is not null && !IsOwnedName(clip.ImageFile, ".png")))
                throw new SoundboardException("Invalid sound entry");
            ValidateRange(clip.DurationSeconds, clip.StartSeconds, clip.EndSeconds);
            clips.Add(clip with { Name = CleanName(clip.Name), CollectionId = ids.Contains(clip.CollectionId) ? clip.CollectionId : collections[0].Id,
                Waveform = CleanWaveform(clip.Waveform) });
        }
        return library with
        {
            Collections = collections, Clips = clips,
            SelectedCollectionId = library.SelectedCollectionId == SoundboardLibrary.FavoritesId || ids.Contains(library.SelectedCollectionId ?? "")
                ? library.SelectedCollectionId : collections[0].Id,
            SendVolume = float.IsFinite(library.SendVolume) ? Math.Clamp(library.SendVolume, 0, 1) : .8f,
            HearVolume = float.IsFinite(library.HearVolume) ? Math.Clamp(library.HearVolume, 0, 1) : .6f,
        };
    }

    private static float[] CleanWaveform(float[]? peaks) => (peaks ?? []).Take(128)
        .Select(p => float.IsFinite(p) ? Math.Clamp(p, 0, 1) : 0).ToArray();
}
