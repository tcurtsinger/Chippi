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
/// </summary>
internal sealed class SelfTriggerGuard(IHistoryStore history)
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2); // FAT and network shares round times

    public bool IsOwnOutput(string path, long bytes, DateTimeOffset lastWriteUtc) =>
        history.LatestOutputAt(path) is { DestinationModifiedUtc: { } produced } record
        && record.Bytes == bytes
        && (lastWriteUtc - produced).Duration() <= Tolerance;
}

/// <summary>One file to move or rename, with what the plan saw so the executor can tell if it changed since.</summary>
/// <param name="ExpectedVersion">The <see cref="FileVersion"/> the preview saw; null when acting right after the readiness check.</param>
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
    string? RecordedSource = null);

internal sealed record ExecutionResult(bool Succeeded, long OperationId, string Source, string Destination, string? Error);

/// <summary>
/// Moves and renames files, recording each in history before and after (architecture.md §2, §3):
/// re-validates the source, never overwrites, uses a verified copy across volumes. Call from one
/// thread at a time (the engine serializes).
/// </summary>
internal sealed class Executor(IHistoryStore history, EngineOptions options)
{
    internal const string PartialSuffix = ".chippi-partial";

    /// <summary>The copy-in-progress name. It carries the operation id, so it is never a file the user already had.</summary>
    internal static string PartialPath(string destination, long operationId) => $"{destination}.{operationId}{PartialSuffix}";

    public ExecutionResult Execute(long runId, ExecutionRequest request)
    {
        string recorded = request.RecordedSource ?? request.Source;
        FileStream source;
        try
        {
            // Held from validation to the move: no one can write the file in between (readers and
            // our own rename/delete are still allowed).
            source = new FileStream(request.Source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
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
            var modified = File.GetLastWriteTimeUtc(request.Source);
            if (source.Length != request.ExpectedBytes
                || new DateTimeOffset(modified, TimeSpan.Zero) != request.ExpectedModifiedUtc
                || request.ExpectedVersion is { } version && FileVersion.Of(source.SafeFileHandle) != version)
                return Fail(runId, request, "The file changed after it was planned.");

            string destination = request.Destination;
            if (Exists(destination))
            {
                if (!request.AllowRenumber)
                    return Fail(runId, request, "Something already has that name.");
                destination = NextFreeName(destination);
            }

            long id = history.Plan(runId, request.Kind, recorded, destination, request.RuleId, request.RuleName, request.ExpectedBytes, request.UndoOf);
            string? partial = null;
            try
            {
                string sha = Fingerprint.Sha256(source);
                history.MarkStarted(id);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (!options.ForceCopy && SameVolume(request.Source, destination))
                {
                    File.Move(request.Source, destination, overwrite: false);
                }
                else
                {
                    string path = PartialPath(destination, id);
                    using (var to = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        partial = path; // created by this operation, so ours to clean up
                        source.Position = 0;
                        source.CopyTo(to);
                        to.Flush(flushToDisk: true);
                    }
                    File.SetLastWriteTimeUtc(partial, modified);
                    if (!string.Equals(Fingerprint.Sha256(partial), sha, StringComparison.Ordinal))
                        throw new IOException("The copy did not match the original.");
                    File.Move(partial, destination, overwrite: false);
                    partial = null;
                    File.Delete(request.Source); // still under our handle, so it cannot have changed
                }

                history.MarkCompleted(id, destination, sha, new DateTimeOffset(File.GetLastWriteTimeUtc(destination), TimeSpan.Zero));
                return new ExecutionResult(true, id, request.Source, destination, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (partial is not null)
                    TryDelete(partial);
                history.MarkFailed(id, ex.Message);
                return new ExecutionResult(false, id, request.Source, destination, ex.Message);
            }
        }
    }

    /// <summary>Settles operations a crash left half-done (architecture.md §3).</summary>
    public int Reconcile()
    {
        int settled = 0;
        foreach (var record in history.Incomplete())
        {
            // Only this operation's own copy, never a file that merely ends in the suffix.
            string partial = PartialPath(record.DestinationPath, record.Id);
            if (File.Exists(partial))
                TryDelete(partial);
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

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next reconcile.
        }
    }
}
