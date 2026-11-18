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
public sealed class AdminHelper(IAdminSessionFactory factory, ILogger logger, TimeSpan? replyCeiling = null, TimeSpan? portReplyCeiling = null,
    TimeSpan? portIdle = null, TimeProvider? time = null) : IAdminHelper, IDisposable
{
    /// <summary>How long one package may stay silent: the helper's own ceiling, plus slack for it to answer.</summary>
    public static readonly TimeSpan DefaultReplyCeiling = Messages.PackageCeiling + TimeSpan.FromMinutes(2);
    private readonly TimeSpan _replyCeiling = replyCeiling ?? DefaultReplyCeiling;
    /// <summary>A port action is quick; the helper waits up to 10 s for a service to stop.</summary>
    public static readonly TimeSpan DefaultPortReplyCeiling = TimeSpan.FromSeconds(45);
    private readonly TimeSpan _portReplyCeiling = portReplyCeiling ?? DefaultPortReplyCeiling;
    /// <summary>A port session stays open this long after its last action for the next one; the helper itself exits after 60 s idle.</summary>
    public static readonly TimeSpan DefaultPortIdle = TimeSpan.FromSeconds(50);
    private readonly TimeSpan _portIdle = portIdle ?? DefaultPortIdle;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1);
    private PortSession? _port;
    private ITimer? _idleTimer;
    private int _generation;
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
            await EndPortSessionCoreAsync().ConfigureAwait(false);
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

    public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) =>
        RunPortAsync(AdminPortVerb.Close, Messages.CloseProcess, endpoint, null, ct);
    public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) =>
        RunPortAsync(AdminPortVerb.End, Messages.EndProcess, endpoint, null, ct);
    public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) =>
        RunPortAsync(AdminPortVerb.Stop, Messages.StopService, endpoint, serviceName, ct);

    /// <summary>
    /// Ends the open port session (Done), if there is one: the flow that asked has finished or was cancelled. Safe to call at any time;
    /// it waits for an action in flight, which is bounded by the reply ceiling.
    /// </summary>
    public async Task EndPortSessionAsync()
    {
        Interlocked.Increment(ref _generation);
        try { await _gate.WaitAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        try { await EndPortSessionCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task EndPortSessionCoreAsync()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
        if (_port is { } session) await FinishAsync(session).ConfigureAwait(false);
    }

    private sealed class PortSession(IAdminConnection connection)
    {
        public IAdminConnection Connection { get; } = connection;
        public long LastUsed;
    }

    /// <summary>
    /// One confirmed request. The helper session it opens (one UAC prompt) stays open for the next port action within the idle time
    /// (Close, then End; Close, then Force end), and ends with Done when that passes or the flow calls <see cref="EndPortSessionAsync"/>.
    /// Cancel before the prompt answers skips it; once sent, the helper finishes the request even if the caller stops waiting.
    /// </summary>
    private async Task<AdminPortResult> RunPortAsync(AdminPortVerb verb, string name, AdminEndpoint endpoint, string? service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var request = new AdminRequest(Guid.NewGuid(), name, Protocol: endpoint.Protocol.ToString(), LocalAddress: endpoint.LocalAddress,
            LocalPort: endpoint.LocalPort, Pid: endpoint.Pid, StartTime: endpoint.StartTime, ServiceName: service);
        if (!Messages.RequestAllowed(request)) throw new ArgumentException("Invalid endpoint.", nameof(endpoint));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _generation);
            _idleTimer?.Dispose();
            _idleTimer = null;
            var session = _port;
            if (session is not null && _time.GetElapsedTime(session.LastUsed) >= _portIdle)
            {
                await FinishAsync(session).ConfigureAwait(false);
                session = null;
            }
            bool reused = session is not null;
            session ??= await OpenPortAsync(ct).ConfigureAwait(false);
            try { await SendAsync(session, request).ConfigureAwait(false); }
            catch (Exception ex) when (reused && ex is IOException or ObjectDisposedException)
            {
                // The helper went away while idle and never saw the request: one fresh session, one prompt.
                await DiscardAsync(session).ConfigureAwait(false);
                session = await OpenPortAsync(ct).ConfigureAwait(false);
                await SendAsync(session, request).ConfigureAwait(false);
            }
            var pipe = session.Connection.Stream;
            AdminReply reply;
            do
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(_portReplyCeiling);
                reply = await Messages.ReadReplyAsync(pipe, wait.Token).ConfigureAwait(false) ?? throw new EndOfStreamException();
                if (reply.Id != request.Id || reply.PackageId is not null || reply.ExitCode is not null
                    || (reply.State != "Progress" && !Messages.PortStateAllowed(reply.State)))
                    throw new InvalidDataException("Invalid helper result.");
            } while (reply.State == "Progress");
            logger.LogInformation("Admin {Verb} port {Port}: {State}", name, endpoint.LocalPort, reply.State);
            session.LastUsed = _time.GetTimestamp();
            ArmIdleTimer();
            var state = reply.State switch
            {
                Messages.PortDone => AdminPortState.Freed,
                Messages.PortGone => AdminPortState.AlreadyClosed,
                Messages.PortChanged => AdminPortState.Changed,
                Messages.PortKept => AdminPortState.Kept,
                _ => AdminPortState.Failed,
            };
            return new(verb, state, reply.Reason);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logger.LogInformation("Admin prompt declined");
            await DiscardAsync(_port).ConfigureAwait(false);
            return new(verb, AdminPortState.NeedsAdmin);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin helper failed; no automatic retry");
            await DiscardAsync(_port).ConfigureAwait(false);
            return ct.IsCancellationRequested && ex is OperationCanceledException
                ? new(verb, AdminPortState.Cancelled) : new(verb, AdminPortState.Failed, "Admin helper failed");
        }
        finally { _gate.Release(); }
    }

    private async Task<PortSession> OpenPortAsync(CancellationToken ct)
    {
        var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var session = new PortSession(connection) { LastUsed = _time.GetTimestamp() };
        _port = session;
        _active = connection;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var hello = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ready = await Messages.ReadReplyAsync(connection.Stream, hello.Token).ConfigureAwait(false);
            if (ready is not { Id: var readyId, State: "Ready", PackageId: null, ExitCode: null, Reason: null } || readyId != Guid.Empty)
                throw new InvalidDataException("Invalid helper greeting.");
        }
        catch
        {
            await DiscardAsync(session).ConfigureAwait(false);
            throw;
        }
        return session;
    }

    private static async Task SendAsync(PortSession session, AdminRequest request)
    {
        using var send = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Messages.WriteAsync(session.Connection.Stream, request, send.Token).ConfigureAwait(false);
    }

    /// <summary>Done, answered or not: a best effort, bounded to 5 seconds, and the connection is closed either way.</summary>
    private async Task FinishAsync(PortSession session)
    {
        try
        {
            var done = new AdminRequest(Guid.NewGuid(), "Done");
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Messages.WriteAsync(session.Connection.Stream, done, finish.Token).ConfigureAwait(false);
            await Messages.ReadReplyAsync(session.Connection.Stream, finish.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // The helper ends its session on its own when the pipe closes or goes idle.
        }
        await DiscardAsync(session).ConfigureAwait(false);
    }

    /// <summary>Closes the connection without a Done (a failed or cancelled action: the helper sees the pipe close and ends).</summary>
    private async Task DiscardAsync(PortSession? session)
    {
        if (session is null) return;
        if (ReferenceEquals(_port, session)) { _port = null; _active = null; }
        try { await session.Connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void ArmIdleTimer()
    {
        int generation = Interlocked.Increment(ref _generation);
        _idleTimer?.Dispose();
        _idleTimer = _time.CreateTimer(_ => _ = EndWhenIdleAsync(generation), null, _portIdle, Timeout.InfiniteTimeSpan);
    }

    private async Task EndWhenIdleAsync(int generation)
    {
        try { await _gate.WaitAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        try
        {
            // Any later action or end call moved the generation on; only the quiet session is ended here.
            if (generation == Volatile.Read(ref _generation) && _port is { } session) await FinishAsync(session).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        _idleTimer?.Dispose();
        _active?.Stream.Dispose();
    }
}
