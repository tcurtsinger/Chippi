// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Chippi.Features.Downloads.Storage;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Features.Downloads.Engine;

/// <summary>Timings and limits. Defaults follow brief §2; tests shorten them.</summary>
public sealed record EngineOptions
{
    /// <summary>Size and last-write time must hold still this long between two samples.</summary>
    public TimeSpan StabilityInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How often due files are re-checked.</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Retry delays for a file that is not ready yet; the last repeats.</summary>
    public IReadOnlyList<TimeSpan> Backoff { get; init; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5)];

    /// <summary>After this a not-ready file stops being retried and shows as Waiting (brief §2: 24 h).</summary>
    public TimeSpan GiveUpAfter { get; init; } = TimeSpan.FromHours(24);

    /// <summary>A preview can be approved for this long, once.</summary>
    public TimeSpan PreviewLifetime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Tests: take the cross-volume copy path even on one volume.</summary>
    public bool ForceCopy { get; init; }

    public const string IgnoreFileName = ".chippiignore";
}

internal static class Fingerprint
{
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Sha256(stream);
    }

    public static string Sha256(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
}

/// <summary>
/// A file's version: NTFS's change time, which moves on every write and every metadata change,
/// including someone setting the last-write time back. Size and last-write time alone cannot
/// tell a same-size edit from the original (Karl's P2-B review).
/// </summary>
internal static class FileVersion
{
    /// <summary>The version of <paramref name="path"/>, or null when it cannot be opened to read it.</summary>
    public static long? Of(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Of(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static long? Of(SafeFileHandle handle) =>
        GetFileInformationByHandleEx(handle, FileBasicInfoClass, out var info, (uint)Marshal.SizeOf<FileBasicInfo>()) ? info.ChangeTime : null;

    private const int FileBasicInfoClass = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileBasicInfo info, uint size);
}

/// <summary>
/// The self-trigger guard (brief §2, from the P2-A review): a file Chippi itself produced (a
/// move, a rename that stayed in the watched folder, an undo restore) is not organized again
/// while its size and last-write time are unchanged. Backed by history, so it holds across
/// restarts and rescans; a real change to the file makes it new again.
/// Explicit previews allow undo restores so the person can organize them again.
/// </summary>
internal sealed class SelfTriggerGuard(IHistoryStore history)
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2); // FAT and network shares round times

    public bool IsOwnOutput(string path, long bytes, DateTimeOffset lastWriteUtc, bool allowRestores = false) =>
        history.LatestOutputAt(path) is { DestinationModifiedUtc: { } produced } record
        && (!allowRestores || record.Kind != HistoryOperationKind.Restore)
        && record.Bytes == bytes
        && (lastWriteUtc - produced).Duration() <= Tolerance;
}

/// <summary>One file to move or rename, with what the plan saw so the executor can tell if it changed since.</summary>
/// <param name="ExpectedVersion">The <see cref="FileVersion"/> the preview saw; null when acting right after the readiness check.</param>
/// <param name="ExpectedSha256">Undo: the content history recorded. Checked on the bytes actually moved.</param>
/// <param name="RecordedSource">What history calls the source, when it is not <paramref name="Source"/> (an import's staged file).</param>
internal sealed record ExecutionRequest(
    HistoryOperationKind Kind,
    string Source,
    string Destination,
    string? RuleId,
    string? RuleName,
    long ExpectedBytes,
    DateTimeOffset ExpectedModifiedUtc,
    bool AllowRenumber,
    long? UndoOf = null,
    long? ExpectedVersion = null,
    string? RecordedSource = null,
    string? ExpectedSha256 = null);

internal sealed record ExecutionResult(bool Succeeded, long OperationId, string Source, string Destination, string? Error);

/// <summary>
/// Moves and renames files, recording each in history before and after (architecture.md §2, §3):
/// re-validates the source, never overwrites, uses a verified copy across volumes. Every check,
/// rename and delete goes through one open handle (<see cref="FileHandles"/>), so the file that was
/// validated is the file that moves. Call from one thread at a time (the engine serializes).
/// </summary>
internal sealed class Executor(IHistoryStore history, EngineOptions options)
{
    internal const string PartialSuffix = ".chippi-partial";

    internal const string ChangedSinceOrganized = "The file changed since it was organized, so it was left alone.";

    internal static string PartialPath(string destination, long operationId) => $"{destination}.{operationId}{PartialSuffix}";

    public ExecutionResult Execute(long runId, ExecutionRequest request)
    {
        string recorded = request.RecordedSource ?? request.Source;
        SafeFileHandle source;
        try
        {
            // Held from validation to the move. Others can read it, but nobody can write, rename,
            // delete or replace it at this path until it is closed.
            source = FileHandles.OpenToMove(request.Source);
        }
        catch (FileNotFoundException)
        {
            return Fail(runId, request, "The file is gone.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(runId, request, "The file is in use.");
        }

        using (source)
        {
            var modified = File.GetLastWriteTimeUtc(source);
            if (RandomAccess.GetLength(source) != request.ExpectedBytes
                || new DateTimeOffset(modified, TimeSpan.Zero) != request.ExpectedModifiedUtc
                || request.ExpectedVersion is { } version && FileVersion.Of(source) != version)
                return Fail(runId, request, request.ExpectedSha256 is null ? "The file changed after it was planned." : ChangedSinceOrganized);

            string sha = FileHandles.Sha256(source);
            if (request.ExpectedSha256 is { } expected && !string.Equals(sha, expected, StringComparison.Ordinal))
                return Fail(runId, request, ChangedSinceOrganized);

            string destination = request.Destination;
            if (Exists(destination))
            {
                if (!request.AllowRenumber)
                    return Fail(runId, request, "Something already has that name.");
                destination = NextFreeName(destination);
            }

            long id = history.Plan(runId, request.Kind, recorded, destination, request.RuleId, request.RuleName, request.ExpectedBytes, request.UndoOf);
            try
            {
                history.MarkStarted(id);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                bool moved = !options.ForceCopy && FileHandles.TryRename(source, destination);
                if (!moved)
                    CopyAcross(id, source, destination, modified, sha);
                history.MarkCompleted(id, destination, sha, new DateTimeOffset(File.GetLastWriteTimeUtc(destination), TimeSpan.Zero));
                return new ExecutionResult(true, id, request.Source, destination, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                history.MarkFailed(id, ex.Message);
                return new ExecutionResult(false, id, request.Source, destination, ex.Message);
            }
        }
    }

    /// <summary>
    /// Another volume: copy to a new partial file this operation creates and records, verify it,
    /// rename it into place by its handle, then delete the original by its handle.
    /// </summary>
    private void CopyAcross(long id, SafeFileHandle source, string destination, DateTime modified, string sha)
    {
        string path = PartialPath(destination, id);
        using var partial = FileHandles.CreateExclusive(path); // fails if anything is already there
        history.MarkPartial(id, path); // proof for a restart that this file is ours
        bool placed = false;
        try
        {
            FileHandles.Copy(source, partial);
            File.SetLastWriteTimeUtc(partial, modified);
            if (!string.Equals(FileHandles.Sha256(partial), sha, StringComparison.Ordinal))
                throw new IOException("The copy did not match the original.");
            if (!FileHandles.TryRename(partial, destination))
                throw new IOException("The copy could not be put in place.");
            placed = true;
            history.MarkPartial(id, null);
            FileHandles.DeleteOnClose(source); // the validated original, gone when the handle closes
        }
        finally
        {
            if (!placed)
            {
                try
                {
                    FileHandles.DeleteOnClose(partial);
                    history.MarkPartial(id, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still recorded, so the next reconcile removes it.
                }
            }
        }
    }

    /// <summary>
    /// Settles operations a crash left half-done (architecture.md §3) and removes partial files
    /// they recorded creating. A partial that cannot be deleted yet (locked) stays recorded, on
    /// whatever row owns it, and is tried again at the next start. Returns how many rows it touched.
    /// </summary>
    public int Reconcile()
    {
        int settled = 0;
        // Only a partial file an operation recorded creating; never inferred from a name.
        foreach (var record in history.WithPartial())
        {
            if (TryDelete(record.PartialPath!))
            {
                history.MarkPartial(record.Id, null);
                settled++;
            }
        }
        foreach (var record in history.Incomplete())
        {
            bool atDestination = Exists(record.DestinationPath);
            bool atSource = Exists(record.SourcePath);

            if (atDestination && !atSource && record.State == OperationState.Started)
            {
                string sha = Fingerprint.Sha256(record.DestinationPath);
                history.MarkCompleted(record.Id, record.DestinationPath, sha, new DateTimeOffset(File.GetLastWriteTimeUtc(record.DestinationPath), TimeSpan.Zero));
            }
            else if (atSource)
            {
                history.MarkFailed(record.Id, "Interrupted before it finished; the file was left where it was.");
            }
            else
            {
                history.MarkFailed(record.Id, "Interrupted; the file was found in neither place.");
            }
            settled++;
        }
        return settled;
    }

    internal static string NextFreeName(string path)
    {
        string folder = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(folder, $"{stem} ({n}){extension}");
            if (!Exists(candidate))
                return candidate;
        }
    }

    private ExecutionResult Fail(long runId, ExecutionRequest request, string error)
    {
        // Failures before anything moved are still history: the user sees why an approved file stayed.
        long id = history.Plan(runId, request.Kind, request.RecordedSource ?? request.Source, request.Destination, request.RuleId, request.RuleName, request.ExpectedBytes, request.UndoOf);
        history.MarkFailed(id, error);
        return new ExecutionResult(false, id, request.Source, request.Destination, error);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>True once the file is gone (including when it already was).</summary>
    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
