// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Chippi.Core.Admin;
using Chippi.Elevated.Protocol;
using Microsoft.Extensions.Logging;

namespace Chippi.UI.Admin;

public interface IAdminConnection : IAsyncDisposable { Stream Stream { get; } }
public interface IAdminSessionFactory { Task<IAdminConnection> OpenAsync(CancellationToken ct); }

/// <summary>One explicitly confirmed batch per helper session. Constructing this service never launches anything.</summary>
public sealed class AdminHelper(IAdminSessionFactory factory, ILogger logger, TimeSpan? replyCeiling = null) : IAdminHelper, IDisposable
{
    /// <summary>How long one package may stay silent: the helper's own ceiling, plus slack for it to answer.</summary>
    public static readonly TimeSpan DefaultReplyCeiling = Messages.PackageCeiling + TimeSpan.FromMinutes(2);
    private readonly TimeSpan _replyCeiling = replyCeiling ?? DefaultReplyCeiling;
    private readonly SemaphoreSlim _gate = new(1);
    private IAdminConnection? _active;
    private volatile bool _disposed;

    public async Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items,
        IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var batch = items.DistinctBy(i => i.PackageId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (batch.Length > 256 || batch.Any(i => !Messages.PackageIdAllowed(i.PackageId) || !Messages.VersionAllowed(i.ExpectedVersion)))
            throw new ArgumentException("Invalid package batch.", nameof(items));
        var ids = batch.Select(i => i.PackageId).ToArray();
        if (ids.Length == 0) return [];
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var results = new List<AdminUpdateResult>();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
            _active = connection;
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pipe = connection.Stream;
            using var hello = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ready = await Messages.ReadReplyAsync(pipe, hello.Token).ConfigureAwait(false);
            if (ready is not { Id: var readyId, State: "Ready", PackageId: null, ExitCode: null, Reason: null } || readyId != Guid.Empty)
                throw new InvalidDataException("Invalid helper greeting.");
            foreach (var item in batch)
            {
                string id = item.PackageId;
                if (ct.IsCancellationRequested) { results.Add(new(id, "Cancelled")); continue; }
                var request = new AdminRequest(Guid.NewGuid(), "UpdatePackage", id, item.ExpectedVersion, item.IncludeUnknown);
                // Cancel never reaches a running installer; a silent helper is cut off by the ceiling so the gate is released.
                using (var send = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    await Messages.WriteAsync(pipe, request, send.Token).ConfigureAwait(false);
                progress?.Report(new(id, results.Count + 1, ids.Length));
                AdminReply reply;
                do
                {
                    using var wait = new CancellationTokenSource(_replyCeiling);
                    reply = await Messages.ReadReplyAsync(pipe, wait.Token).ConfigureAwait(false) ?? throw new EndOfStreamException();
                    if (reply.Id != request.Id || reply.PackageId != id || reply.State is not ("Progress" or "Done" or "Failed" or "Refused"))
                        throw new InvalidDataException("Invalid helper result.");
                } while (reply.State == "Progress");
                var result = new AdminUpdateResult(id, reply.State, reply.ExitCode, reply.Reason);
                results.Add(result);
                progress?.Report(new(id, results.Count, ids.Length, result));
                logger.LogInformation("Admin update {Package}: {State}, exit {ExitCode}", id, reply.State, reply.ExitCode);
            }
            var done = new AdminRequest(Guid.NewGuid(), "Done");
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Messages.WriteAsync(pipe, done, finish.Token).ConfigureAwait(false);
            var answer = await Messages.ReadReplyAsync(pipe, finish.Token).ConfigureAwait(false);
            if (answer?.Id != done.Id || answer.State != "Done") throw new InvalidDataException("Invalid helper shutdown.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logger.LogInformation("Admin prompt declined");
            foreach (string id in ids.Skip(results.Count)) results.Add(new(id, "NeedsAdmin"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin helper failed; no automatic retry");
            foreach (string id in ids.Skip(results.Count)) results.Add(new(id, ct.IsCancellationRequested && ex is OperationCanceledException ? "Cancelled" : "Failed", Reason: "Admin helper failed"));
        }
        finally { _active = null; _gate.Release(); }
        return results;
    }
    public void Dispose() { _disposed = true; _active?.Stream.Dispose(); }
}
