// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;

namespace Chippi.Features.PortManager;

/// <summary>
/// Freed: the action was carried out and nothing of that process holds the endpoint now. StillRunning: a close request went out and the
/// process is still there after the wait, so the page offers Force end. Changed and AlreadyClosed: the row no longer matches, nothing
/// ended. Kept: refused (Reason says why). Failed: Windows refused. NeedsAdmin: the prompt was declined or the helper couldn't start.
/// </summary>
public enum PortEndState { Freed, StillRunning, AlreadyClosed, Changed, Kept, Failed, NeedsAdmin, Cancelled }

public sealed record PortEndResult(PortEndState State, PortVerb Verb, string? Reason = null)
{
    /// <summary>The status words of the approved mockup.</summary>
    public string Text => State switch
    {
        PortEndState.Freed => "Freed",
        PortEndState.StillRunning => "Still running",
        PortEndState.AlreadyClosed => "Already closed",
        PortEndState.Changed => "Changed",
        PortEndState.Kept => "Kept",
        PortEndState.NeedsAdmin => "Needs admin",
        PortEndState.Cancelled => "Cancelled",
        _ => Verb == PortVerb.Stop ? "Stop failed" : "End failed",
    };
}

/// <summary>
/// Frees a port after the owner confirmed (docs/phase-7/port-manager.md). It reads the confirmed row again first: the endpoint must still
/// be held by that PID with that start time, and the holder must still classify the same, or nothing ends. Own processes end directly:
/// a close request when the process has a window, then three seconds, then <c>force</c> ends it; with no window it ends at once. An
/// elevated or other user's process goes the same way through the admin helper, and a service is stopped (never its host) through it.
/// Never ends anything on its own: every call is one confirmed row.
/// </summary>
public sealed class PortEnder(IPortSystem system, IAdminHelper? admin = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<long>? nowMs = null)
{
    /// <summary>How long a close request gets before the page offers Force end.</summary>
    public static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Func<long> _now = nowMs ?? (() => Environment.TickCount64);

    /// <summary>Free for an own or elevated holder. <paramref name="force"/> skips the close request (the Force end step).</summary>
    public async Task<PortEndResult> FreeAsync(PortRow row, bool force = false, CancellationToken ct = default)
    {
        try { return await FreeCoreAsync(row, force, ct); }
        catch (OperationCanceledException) { return new(PortEndState.Cancelled, PortVerb.Free); }
    }

    /// <summary>Stop for an attributed service: only that service, never the process that hosts it.</summary>
    public async Task<PortEndResult> StopAsync(PortRow row, CancellationToken ct = default)
    {
        try { return await StopCoreAsync(row, ct); }
        catch (OperationCanceledException) { return new(PortEndState.Cancelled, PortVerb.Stop); }
    }

    private async Task<PortEndResult> FreeCoreAsync(PortRow row, bool force, CancellationToken ct)
    {
        if (row.Verb != PortVerb.Free || row.Holder.Kind is not (HolderKind.Own or HolderKind.Elevated)) return Refused(row, PortVerb.Free);
        if (await RecheckAsync(row, PortVerb.Free, ct) is { } refusal) return refusal;
        var holder = row.Holder;
        bool elevated = holder.Kind == HolderKind.Elevated;
        if (elevated && admin is null) return new(PortEndState.NeedsAdmin, PortVerb.Free);
        if (!force && system.HasWindow(holder.Pid))
        {
            var closed = elevated ? Map(await Close(row, ct), PortVerb.Free) : Map(system.PostClose(holder.Pid, holder.StartTime), PortVerb.Free);
            if (closed.State == PortEndState.Freed)
            {
                if (await WaitForExitAsync(holder, ct)) return await VerifiedAsync(row, PortVerb.Free, ct);
                return new(PortEndState.StillRunning, PortVerb.Free);
            }
            // A window that went away between the check and the request: nothing to ask, so end it.
            if (!(closed.State == PortEndState.Failed && closed.Reason == PortActionsText.NoWindow)) return closed;
        }
        var ended = elevated ? Map(await End(row, ct), PortVerb.Free) : Map(system.Terminate(holder.Pid, holder.StartTime), PortVerb.Free);
        return ended.State == PortEndState.Freed ? await VerifiedAsync(row, PortVerb.Free, ct) : ended;
    }

    private async Task<PortEndResult> StopCoreAsync(PortRow row, CancellationToken ct)
    {
        if (row.Verb != PortVerb.Stop || row.Holder.Kind != HolderKind.Service || row.ServiceName is not { Length: > 0 } service) return Refused(row, PortVerb.Stop);
        if (await RecheckAsync(row, PortVerb.Stop, ct) is { } refusal) return refusal;
        if (admin is null) return new(PortEndState.NeedsAdmin, PortVerb.Stop);
        var result = Map(await Guard(() => admin.StopServiceAsync(service, Endpoint(row), ct), PortVerb.Stop), PortVerb.Stop);
        return result.State == PortEndState.Freed ? await VerifiedAsync(row, PortVerb.Stop, ct) : result;
    }

    /// <summary>Ends the helper session a port action left open (the flow is over); a no-op without an admin helper.</summary>
    public async Task EndAdminSessionAsync()
    {
        if (admin is null) return;
        try { await admin.EndPortSessionAsync(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException) { }
    }

    private Task<AdminPortResult> Close(PortRow row, CancellationToken ct) => Guard(() => admin!.CloseProcessAsync(Endpoint(row), ct), PortVerb.Free);
    private Task<AdminPortResult> End(PortRow row, CancellationToken ct) => Guard(() => admin!.EndProcessAsync(Endpoint(row), ct), PortVerb.Free);

    private static async Task<AdminPortResult> Guard(Func<Task<AdminPortResult>> call, PortVerb verb)
    {
        try { return await call(); }
        catch (ArgumentException) { return new(verb == PortVerb.Stop ? AdminPortVerb.Stop : AdminPortVerb.End, AdminPortState.Failed, "Invalid request"); }
    }

    internal static AdminEndpoint Endpoint(PortRow row) =>
        new(row.Endpoint.Protocol == PortProtocol.Tcp ? AdminProtocol.Tcp : AdminProtocol.Udp, row.Endpoint.LocalAddress.ToString(),
            row.Endpoint.LocalPort, row.Holder.Pid, row.Holder.StartTime);

    private static PortEndResult Refused(PortRow row, PortVerb verb) => row.Holder.Kind switch
    {
        HolderKind.Gone => new(PortEndState.AlreadyClosed, verb),
        HolderKind.Shared => new(PortEndState.Kept, verb, PortActionsText.Shared),
        _ => new(PortEndState.Kept, verb, PortActionsText.Protected),
    };

    /// <summary>Reads the row again: the endpoint still held by that PID and start time, classified the same. Null when it still matches.</summary>
    private async Task<PortEndResult?> RecheckAsync(PortRow row, PortVerb verb, CancellationToken ct)
    {
        var e = row.Endpoint;
        IReadOnlyList<PortRow> now;
        try { now = await Task.Run(() => new PortScanner(system).Scan(r => r.Protocol == e.Protocol && r.LocalPort == e.LocalPort && r.LocalAddress.Equals(e.LocalAddress) && r.RemotePort == e.RemotePort && Equals(r.RemoteAddress, e.RemoteAddress)), ct); }   // off the UI thread: a full read of the tables
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or InvalidDataException)
        { return new(PortEndState.Failed, verb, "Windows error"); }
        if (now.Count == 0) return new(PortEndState.AlreadyClosed, verb);
        var same = now.FirstOrDefault(r => r.Endpoint.Pid == row.Holder.Pid);
        if (same is null) return new(PortEndState.Changed, verb, PortActionsText.Changed);
        if (same.Holder.Kind == HolderKind.Gone) return new(PortEndState.AlreadyClosed, verb);
        if (same.Holder.StartTime != row.Holder.StartTime || same.Holder.Kind != row.Holder.Kind || same.ServiceName != row.ServiceName)
            return new(PortEndState.Changed, verb, PortActionsText.Changed);
        if (same.Verb != row.Verb) return Refused(same, verb);
        return null;
    }

    /// <summary>
    /// The endpoint is free of that process. Windows releases a dying process's sockets a moment after the call returns, so it reads again
    /// for up to two seconds; a child that keeps the socket still holds it after that.
    /// </summary>
    private async Task<PortEndResult> VerifiedAsync(PortRow row, PortVerb verb, CancellationToken ct)
    {
        long deadline = _now() + 2000;
        while (true)
        {
            try
            {
                var e = row.Endpoint;
                bool held = await Task.Run(() => system.ReadEndpoints().Any(r => r.Protocol == e.Protocol && r.LocalPort == e.LocalPort && r.LocalAddress.Equals(e.LocalAddress)
                    && r.Pid == row.Holder.Pid && r.RemotePort == e.RemotePort && Equals(r.RemoteAddress, e.RemoteAddress)), ct);
                if (!held) return new(PortEndState.Freed, verb);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or InvalidDataException)
            { return new(PortEndState.Freed, verb); }
            if (_now() >= deadline) return new(PortEndState.Failed, verb, "Port still held");
            await _delay(Poll, ct);
        }
    }

    /// <summary>Polls for the process to go, up to <see cref="CloseWait"/> on the injected clock.</summary>
    private async Task<bool> WaitForExitAsync(PortHolder holder, CancellationToken ct)
    {
        long deadline = _now() + (long)CloseWait.TotalMilliseconds;
        while (true)
        {
            var process = await Task.Run(() => system.ReadProcess(holder.Pid), ct);
            if (process is null || process.StartTime != holder.StartTime) return true;
            if (_now() >= deadline) return false;
            await _delay(Poll, ct);
        }
    }

    private static PortEndResult Map(EndResult result, PortVerb verb) => result.Outcome switch
    {
        EndOutcome.Done => new(PortEndState.Freed, verb),
        EndOutcome.Gone => new(PortEndState.AlreadyClosed, verb),
        EndOutcome.Changed => new(PortEndState.Changed, verb, PortActionsText.Changed),
        _ when result.Error == 1400 => new(PortEndState.Failed, verb, PortActionsText.NoWindow),
        _ => new(PortEndState.Failed, verb, $"Windows error {result.Error}"),
    };

    private static PortEndResult Map(AdminPortResult result, PortVerb verb) => result.State switch
    {
        AdminPortState.Freed => new(PortEndState.Freed, verb),
        AdminPortState.AlreadyClosed => new(PortEndState.AlreadyClosed, verb),
        AdminPortState.Changed => new(PortEndState.Changed, verb, result.Reason),
        AdminPortState.Kept => new(PortEndState.Kept, verb, result.Reason),
        AdminPortState.NeedsAdmin => new(PortEndState.NeedsAdmin, verb),
        AdminPortState.Cancelled => new(PortEndState.Cancelled, verb),
        _ => new(PortEndState.Failed, verb, result.Reason),
    };
}

/// <summary>The reasons a result carries, as the helper words them.</summary>
public static class PortActionsText
{
    public const string Changed = "Changed since the scan", Protected = "Protected process", Shared = "Shared Windows service", NoWindow = "No window";
}
