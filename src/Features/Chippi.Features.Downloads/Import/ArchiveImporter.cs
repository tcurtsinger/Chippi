// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Formats.Tar;
using System.IO.Compression;
using Chippi.Rules;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Downloads.Import;

/// <summary>Limits on what an archive may expand to (architecture.md open question 3: 10,000 / 4 GB / 100:1).</summary>
public sealed record ArchiveLimits
{
    public int MaxEntries { get; init; } = 10_000;

    public long MaxBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Expanded bytes per archive byte.</summary>
    public int MaxRatio { get; init; } = 100;
}

/// <summary>An archive's files, extracted flat into <see cref="Staging"/> and waiting for preview.</summary>
public sealed record ExtractedArchive(string Name, string Staging, IReadOnlyList<string> Files, IReadOnlyList<string> Skipped);

public sealed class ArchiveRejectedException(string message) : Exception(message);

/// <summary>
/// Extracts zip, tgz and tar.gz archives for import. Folders inside the archive are flattened,
/// links and anything that is not a regular file are skipped, names are checked as Windows file
/// names and de-duplicated against each other and the target folder. Nothing here organizes:
/// the result always goes to preview.
/// </summary>
public static class ArchiveImporter
{
    public static bool IsSupported(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts <paramref name="archive"/> into a new folder under <paramref name="stagingRoot"/>.</summary>
    /// <param name="targetFolder">Where the files will be planned as if they were; names already there are avoided.</param>
    /// <exception cref="ArchiveRejectedException">The archive exceeds a limit or cannot be read. Nothing is left behind.</exception>
    public static ExtractedArchive Extract(string archive, string targetFolder, string stagingRoot, ArchiveLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= new ArchiveLimits();
        if (!IsSupported(archive))
            throw new ArchiveRejectedException("Only zip, tgz and tar.gz archives can be imported.");
        long archiveBytes = new FileInfo(archive).Length;
        long budget = Math.Min(limits.MaxBytes, Math.Max(1, archiveBytes) * limits.MaxRatio);
        string staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var taken = new HashSet<string>(
            Directory.Exists(targetFolder) ? Directory.EnumerateFileSystemEntries(targetFolder).Select(p => Path.GetFileName(p)) : [],
            StringComparer.OrdinalIgnoreCase);
        var state = new State(staging, taken, budget, limits);
        try
        {
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ExtractZip(archive, state, ct);
            else
                ExtractTarGz(archive, state, ct);
            return new ExtractedArchive(Path.GetFileName(archive), staging, state.Files, state.Skipped);
        }
        catch (Exception ex)
        {
            DeleteStaging(staging, null);
            if (ex is ArchiveRejectedException or OperationCanceledException)
                throw;
            if (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
                throw new ArchiveRejectedException("The archive could not be read.");
            throw;
        }
    }

    /// <summary>Removes extracted files that were not applied. Failures are logged; the next start retries.</summary>
    public static void DeleteStaging(string staging, ILogger? logger)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not remove {Staging}", staging);
        }
    }

    private static void ExtractZip(string archive, State state, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > state.Limits.MaxEntries)
            throw TooMany(state.Limits);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue; // a folder
            const int unixTypeMask = 0xF000, unixSymlink = 0xA000;
            if (((entry.ExternalAttributes >> 16) & unixTypeMask) == unixSymlink
                || ((FileAttributes)(entry.ExternalAttributes & 0xFFFF)).HasFlag(FileAttributes.ReparsePoint))
            {
                state.Skipped.Add(entry.FullName);
                continue;
            }
            state.Reserve(entry.Length); // declared size first, so an obvious bomb is refused before writing
            using var source = entry.Open();
            state.Write(entry.FullName, source, entry.LastWriteTime.UtcDateTime, ct);
        }
    }

    private static void ExtractTarGz(string archive, State state, CancellationToken ct)
    {
        using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        int entries = 0;
        while (tar.GetNextEntry() is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            if (++entries > state.Limits.MaxEntries)
                throw TooMany(state.Limits);
            if (entry.EntryType is TarEntryType.Directory)
                continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
            {
                state.Skipped.Add(entry.Name); // links, devices, fifos
                continue;
            }
            state.Reserve(entry.Length);
            state.Write(entry.Name, entry.DataStream, entry.ModificationTime.UtcDateTime, ct);
        }
    }

    private static ArchiveRejectedException TooMany(ArchiveLimits limits) =>
        new($"The archive has more than {limits.MaxEntries:N0} entries.");

    private sealed class State(string staging, HashSet<string> taken, long budget, ArchiveLimits limits)
    {
        private long _declared;
        private long _written;

        public ArchiveLimits Limits { get; } = limits;

        public List<string> Files { get; } = [];

        public List<string> Skipped { get; } = [];

        public void Reserve(long bytes)
        {
            _declared += Math.Max(0, bytes);
            if (_declared > budget)
                throw TooBig();
        }

        public void Write(string entryName, Stream source, DateTime modifiedUtc, CancellationToken ct)
        {
            string leaf = entryName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            try
            {
                WindowsPaths.ValidateComponent(leaf);
            }
            catch (ArgumentException)
            {
                Skipped.Add(entryName);
                return;
            }
            string name = Unique(leaf);
            string path = Path.Combine(staging, name);
            using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // Count what actually comes out, not what the header claims.
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    _written += read;
                    if (_written > budget)
                        throw TooBig();
                    target.Write(buffer, 0, read);
                }
            }
            if (modifiedUtc > DateTime.UnixEpoch)
                File.SetLastWriteTimeUtc(path, modifiedUtc);
            Files.Add(path);
        }

        private string Unique(string leaf)
        {
            if (taken.Add(leaf))
                return leaf;
            string stem = Path.GetFileNameWithoutExtension(leaf);
            string extension = Path.GetExtension(leaf);
            for (int n = 2; ; n++)
            {
                string candidate = $"{stem} ({n}){extension}";
                if (taken.Add(candidate))
                    return candidate;
            }
        }

        private ArchiveRejectedException TooBig() =>
            budget < Limits.MaxBytes
                ?new($"The archive expands more than {Limits.MaxRatio}:1, so it was not imported.")
                : new($"The archive expands past {Limits.MaxBytes / (1024 * 1024 * 1024)} GB, so it was not imported.");
    }
}
