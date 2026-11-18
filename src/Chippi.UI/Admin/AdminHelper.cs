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
    TimeSpan? sessionIdle = null, TimeProvider? time = null, TimeSpan? categoryReplyCeiling = null) : IAdminHelper, IDisposable
{
    /// <summary>How long one package may stay silent: the helper's own ceiling, plus slack for it to answer.</summary>
    public static readonly TimeSpan DefaultReplyCeiling = Messages.PackageCeiling + TimeSpan.FromMinutes(2);
    private readonly TimeSpan _replyCeiling = replyCeiling ?? DefaultReplyCeiling;
    /// <summary>A port action is quick; the helper waits up to 10 s for a service to stop.</summary>
    public static readonly TimeSpan DefaultPortReplyCeiling = TimeSpan.FromSeconds(45);
    private readonly TimeSpan _portReplyCeiling = portReplyCeiling ?? DefaultPortReplyCeiling;
    /// <summary>A helper session stays open this long after its last action for the next one; the helper itself exits after 60 s idle.</summary>
    public static readonly TimeSpan DefaultSessionIdle = TimeSpan.FromSeconds(50);
    private readonly TimeSpan _sessionIdle = sessionIdle ?? DefaultSessionIdle;
    /// <summary>One category may take a while (Windows Update caches can be many GB); the helper answers when it is done.</summary>
    public static readonly TimeSpan DefaultCategoryReplyCeiling = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _categoryReplyCeiling = categoryReplyCeiling ?? DefaultCategoryReplyCeiling;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1);
    private HelperSessionState? _session;
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
            await EndSessionCoreAsync().ConfigureAwait(false);
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
    public async Task EndSessionAsync()
    {
        Interlocked.Increment(ref _generation);
        try { await _gate.WaitAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        try { await EndSessionCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task EndSessionCoreAsync()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
        if (_session is { } session) await FinishAsync(session).ConfigureAwait(false);
    }

    private sealed class HelperSessionState(IAdminConnection connection)
    {
        public IAdminConnection Connection { get; } = connection;
        public long LastUsed;
    }

    /// <summary>
    /// One confirmed request. The helper session it opens (one UAC prompt) stays open for the next port action within the idle time
    /// (Close, then End; Close, then Force end), and ends with Done when that passes or the flow calls <see cref="EndSessionAsync"/>.
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
            var (session, reused) = await AcquireAsync(ct).ConfigureAwait(false);
            try { await SendAsync(session, request).ConfigureAwait(false); }
            catch (Exception ex) when (reused && ex is IOException or ObjectDisposedException)
            {
                // The helper went away while idle and never saw the request: one fresh session, one prompt.
                await DiscardAsync(session).ConfigureAwait(false);
                session = await OpenSessionAsync(ct).ConfigureAwait(false);
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
            await DiscardAsync(_session).ConfigureAwait(false);
            return new(verb, AdminPortState.NeedsAdmin);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin helper failed; no automatic retry");
            await DiscardAsync(_session).ConfigureAwait(false);
            return ct.IsCancellationRequested && ex is OperationCanceledException
                ? new(verb, AdminPortState.Cancelled) : new(verb, AdminPortState.Failed, "Admin helper failed");
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The open session if it is still inside its idle time, else a fresh one (one prompt). Called with the gate held. A session that went
    /// quiet is finished with Done first. Whoever acquires it arms the idle timer again after the job.
    /// </summary>
    private async Task<(HelperSessionState Session, bool Reused)> AcquireAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _generation);
        _idleTimer?.Dispose();
        _idleTimer = null;
        var session = _session;
        if (session is not null && _time.GetElapsedTime(session.LastUsed) >= _sessionIdle)
        {
            await FinishAsync(session).ConfigureAwait(false);
            session = null;
        }
        bool reused = session is not null;
        return (session ?? await OpenSessionAsync(ct).ConfigureAwait(false), reused);
    }

    public Task<IReadOnlyList<AdminCategoryResult>> MeasureCategoriesAsync(IReadOnlyList<AdminCategory> names, CancellationToken ct = default) =>
        RunCategoriesAsync(Messages.MeasureCategory, names, null, endAfter: false, ct);

    public Task<IReadOnlyList<AdminCategoryResult>> CleanCategoriesAsync(IReadOnlyList<AdminCategory> names,
        IProgress<AdminCategoryProgress>? progress = null, CancellationToken ct = default) =>
        RunCategoriesAsync(Messages.CleanCategory, names, progress, endAfter: true, ct);

    /// <summary>
    /// Each name in turn, in the session the first job opened. A measure leaves the session open for the clean that follows (Cancel then calls
    /// <see cref="EndSessionAsync"/>); a clean ends it. Cancel during a clean sends Done: the helper stops between files and answers with
    /// what it freed, then the rest are Cancelled. A declined prompt is NeedsAdmin for every name left; nothing ran.
    /// </summary>
    private async Task<IReadOnlyList<AdminCategoryResult>> RunCategoriesAsync(string verb, IReadOnlyList<AdminCategory> names,
        IProgress<AdminCategoryProgress>? progress, bool endAfter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(names);
        var list = names.Distinct().ToArray();
        if (list.Length == 0) return [];
        if (list.Any(n => !Enum.IsDefined(n))) throw new ArgumentException("Invalid category.", nameof(names));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var results = new List<AdminCategoryResult>();
        HelperSessionState? session = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            bool reused;
            (session, reused) = await AcquireAsync(ct).ConfigureAwait(false);
            bool cancelled = false;
            foreach (var category in list)
            {
                if (cancelled || ct.IsCancellationRequested) { cancelled = true; results.Add(new(category, AdminCategoryState.Cancelled)); continue; }
                var request = new AdminRequest(Guid.NewGuid(), verb, Category: category.ToString());
                try { await SendAsync(session, request).ConfigureAwait(false); }
                catch (Exception ex) when (reused && results.Count == 0 && ex is IOException or ObjectDisposedException)
                {
                    // The helper went away while idle and never saw the request: one fresh session, one prompt.
                    await DiscardAsync(session).ConfigureAwait(false);
                    session = await OpenSessionAsync(ct).ConfigureAwait(false);
                    await SendAsync(session, request).ConfigureAwait(false);
                }
                reused = true;
                progress?.Report(new(category, results.Count + 1, list.Length));
                var (reply, stopped) = await ReadCategoryReplyAsync(session, request, ct).ConfigureAwait(false);
                var result = ToResult(category, reply);
                if (stopped)
                {
                    cancelled = true;
                    if (result.State is AdminCategoryState.Cleaned or AdminCategoryState.Measured) result = result with { State = AdminCategoryState.Cancelled };
                }
                results.Add(result);
                progress?.Report(new(category, results.Count, list.Length, result));
                logger.LogInformation("Admin {Verb} {Category}: {State}, {Bytes} bytes, {Files} files, {InUse} in use", verb, category, reply.State, reply.Bytes, reply.Files, reply.InUse);
            }
            session.LastUsed = _time.GetTimestamp();
            if (cancelled && _session is null) { /* ReadCategoryReplyAsync already ended the session */ }
            else if (endAfter || cancelled) await FinishAsync(session).ConfigureAwait(false);
            else ArmIdleTimer();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logger.LogInformation("Admin prompt declined");
            await DiscardAsync(session ?? _session).ConfigureAwait(false);
            foreach (var category in list.Skip(results.Count)) results.Add(new(category, AdminCategoryState.NeedsAdmin));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin helper failed; no automatic retry");
            await DiscardAsync(session ?? _session).ConfigureAwait(false);
            bool cancel = ct.IsCancellationRequested && ex is OperationCanceledException;
            foreach (var category in list.Skip(results.Count))
                results.Add(cancel ? new(category, AdminCategoryState.Cancelled) : new(category, AdminCategoryState.Failed, Reason: "Admin helper failed"));
        }
        finally { _gate.Release(); }
        return results;
    }

    /// <summary>
    /// Waits for the category's answer. Cancel sends Done once (the helper stops between files and still answers), then the wait goes on for that
    /// answer; stopped says Cancel was the reason it ended early. A helper that stays silent past the ceiling is cut off.
    /// </summary>
    private async Task<(AdminReply Reply, bool Stopped)> ReadCategoryReplyAsync(HelperSessionState session, AdminRequest request, CancellationToken ct)
    {
        var pipe = session.Connection.Stream;
        using var wait = new CancellationTokenSource(_categoryReplyCeiling, _time);
        // Exactly one Done is written, whichever of Cancel or the reply's own reason asks first.
        var doneSent = new Lazy<Task>(() => SendDoneAsync(pipe), LazyThreadSafetyMode.ExecutionAndPublication);
        using var registration = ct.Register(() => _ = doneSent.Value);
        var reply = await Messages.ReadReplyAsync(pipe, wait.Token).ConfigureAwait(false) ?? throw new EndOfStreamException();
        if (reply.Id != request.Id || reply.Category != request.Category || reply.PackageId is not null || reply.ExitCode is not null
            || !Messages.CategoryStateAllowed(reply.State) || reply.Bytes is < 0 || reply.Files is < 0 || reply.InUse is < 0
            || (reply.State == Messages.CategoryMeasured && request.Verb != Messages.MeasureCategory)
            || (reply.State == Messages.CategoryCleaned && request.Verb != Messages.CleanCategory))
            throw new InvalidDataException("Invalid helper result.");
        bool stopped = ct.IsCancellationRequested || reply.Reason == Messages.CancelledReason;
        if (stopped) _ = doneSent.Value;
        if (doneSent.IsValueCreated)
        {
            await doneSent.Value.ConfigureAwait(false);
            // The Done's own answer follows the category's. The session is over either way, and the connection is closed.
            try
            {
                using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(5), _time);
                await Messages.ReadReplyAsync(pipe, finish.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ObjectDisposedException or OperationCanceledException) { }
            await DiscardAsync(session).ConfigureAwait(false);
        }
        return (reply, stopped);
    }

    private static async Task SendDoneAsync(Stream pipe)
    {
        try
        {
            using var send = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Messages.WriteAsync(pipe, new AdminRequest(Guid.NewGuid(), "Done"), send.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
    }

    private static AdminCategoryResult ToResult(AdminCategory category, AdminReply reply) => reply.State switch
    {
        Messages.CategoryMeasured => new(category, AdminCategoryState.Measured, reply.Bytes ?? 0, reply.Files ?? 0, reply.InUse ?? 0, reply.Reason),
        Messages.CategoryCleaned => new(category, AdminCategoryState.Cleaned, reply.Bytes ?? 0, reply.Files ?? 0, reply.InUse ?? 0, reply.Reason),
        Messages.CategoryUnavailable => new(category, AdminCategoryState.Unavailable, Reason: reply.Reason),
        _ => new(category, AdminCategoryState.Failed, Reason: reply.Reason),
    };

    private async Task<HelperSessionState> OpenSessionAsync(CancellationToken ct)
    {
        var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var session = new HelperSessionState(connection) { LastUsed = _time.GetTimestamp() };
        _session = session;
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

    private static async Task SendAsync(HelperSessionState session, AdminRequest request)
    {
        using var send = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Messages.WriteAsync(session.Connection.Stream, request, send.Token).ConfigureAwait(false);
    }

    /// <summary>Done, answered or not: a best effort, bounded to 5 seconds, and the connection is closed either way.</summary>
    private async Task FinishAsync(HelperSessionState session)
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
    private async Task DiscardAsync(HelperSessionState? session)
    {
        if (session is null) return;
        if (ReferenceEquals(_session, session)) { _session = null; _active = null; }
        try { await session.Connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void ArmIdleTimer()
    {
        int generation = Interlocked.Increment(ref _generation);
        _idleTimer?.Dispose();
        _idleTimer = _time.CreateTimer(_ => _ = EndWhenIdleAsync(generation), null, _sessionIdle, Timeout.InfiniteTimeSpan);
    }

    private async Task EndWhenIdleAsync(int generation)
    {
        try { await _gate.WaitAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        try
        {
            // Any later action or end call moved the generation on; only the quiet session is ended here.
            if (generation == Volatile.Read(ref _generation) && _session is { } session) await FinishAsync(session).ConfigureAwait(false);
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
