// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Net;
using Chippi.Core.Admin;

namespace Chippi.Features.PortManager.Tests;

/// <summary>A PC on paper: tables, processes and services the test sets, and a record of every action asked of it.</summary>
internal sealed class FakeSystem : IPortSystem
{
    public int CurrentPid { get; set; } = 6204;
    public string? CurrentPath { get; set; } = @"C:\Chippi\Chippi.exe";
    public List<PortEndpoint> Endpoints { get; } = [];
    public Dictionary<int, ProcessInfo?> Processes { get; } = [];
    public List<ServiceInfo> Services { get; } = [];
    public Dictionary<string, string?> Owners { get; } = [];
    public HashSet<int> Windows { get; } = [];
    public List<string> Calls { get; } = [];
    public EndResult CloseResult { get; set; } = new(EndOutcome.Done);
    public EndResult TerminateResult { get; set; } = new(EndOutcome.Done);
    /// <summary>What a close request does: ends the process (as an app that obeys it).</summary>
    public bool CloseEndsProcess { get; set; }
    /// <summary>What a terminate does: removes the process and its endpoints.</summary>
    public bool TerminateEndsProcess { get; set; } = true;
    public Exception? ReadFails { get; set; }
    public int Reads { get; private set; }

    /// <summary>The managed thread of every table read, so a test can tell which reads left the caller's thread.</summary>
    public List<int> ReadThreads { get; } = [];

    public IReadOnlyList<PortEndpoint> ReadEndpoints()
    {
        Reads++;
        lock (ReadThreads) ReadThreads.Add(Environment.CurrentManagedThreadId);
        if (ReadFails is { } ex) throw ex;
        return [.. Endpoints];
    }

    public string? OwnerModule(PortEndpoint endpoint) { Calls.Add("owner " + endpoint.LocalPort); return Owners.GetValueOrDefault(endpoint.Key); }
    public ProcessInfo? ReadProcess(int pid, ProcessNames? names = null) => Processes.GetValueOrDefault(pid);
    public IReadOnlyList<ServiceInfo> RunningServices() => [.. Services];
    public bool HasWindow(int pid) => Windows.Contains(pid);

    public EndResult PostClose(int pid, long startTime)
    {
        Calls.Add($"close {pid}@{startTime}");
        if (CloseResult.Outcome == EndOutcome.Done && CloseEndsProcess) Kill(pid);
        return CloseResult;
    }

    public EndResult Terminate(int pid, long startTime)
    {
        Calls.Add($"terminate {pid}@{startTime}");
        if (TerminateResult.Outcome == EndOutcome.Done && TerminateEndsProcess) Kill(pid);
        return TerminateResult;
    }

    /// <summary>Runs after a process is killed: a process that restarts itself starts here.</summary>
    public Action<int>? AfterKill { get; set; }

    public void Kill(int pid)
    {
        Processes[pid] = null;
        Endpoints.RemoveAll(e => e.Pid == pid);
        AfterKill?.Invoke(pid);
    }

    public PortEndpoint Listen(int port, int pid, string address = "127.0.0.1", PortProtocol protocol = PortProtocol.Tcp)
    {
        var ip = IPAddress.Parse(address);
        var e = protocol == PortProtocol.Tcp
            ? new PortEndpoint(protocol, ip, port, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0, TcpState.Listen, pid)
            : new PortEndpoint(protocol, ip, port, null, 0, TcpState.None, pid);
        Endpoints.Add(e);
        return e;
    }

    public PortEndpoint Connect(int port, int pid, TcpState state, string remote = "10.0.0.5", int remotePort = 443)
    {
        var e = new PortEndpoint(PortProtocol.Tcp, IPAddress.Parse("192.168.1.42"), port, IPAddress.Parse(remote), remotePort, state, pid);
        Endpoints.Add(e);
        return e;
    }

    public FakeSystem Process(int pid, string name, long start = 1000, bool canTerminate = true, string? path = null, bool unreadable = false)
    {
        Processes[pid] = new ProcessInfo(pid, start, name, path ?? @"C:\apps\" + name, unreadable, canTerminate);
        return this;
    }
}

internal sealed class FakeAdmin : IAdminHelper
{
    public List<string> Calls { get; } = [];
    public Queue<AdminPortResult> Results { get; } = [];
    public Action<string>? OnCall { get; set; }
    public int EndSessions { get; private set; }
    public Task EndSessionAsync() { EndSessions++; return Task.CompletedTask; }

    private AdminPortResult Next(string call, AdminPortVerb verb)
    {
        Calls.Add(call);
        OnCall?.Invoke(call);
        return Results.Count > 0 ? Results.Dequeue() : new AdminPortResult(verb, AdminPortState.Freed);
    }

    public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) =>
        Task.FromResult(Next($"close {endpoint.Pid}@{endpoint.StartTime} {endpoint.Protocol} {endpoint.LocalAddress}:{endpoint.LocalPort}", AdminPortVerb.Close));
    public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) =>
        Task.FromResult(Next($"end {endpoint.Pid}@{endpoint.StartTime} {endpoint.Protocol} {endpoint.LocalAddress}:{endpoint.LocalPort}", AdminPortVerb.End));
    public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) =>
        Task.FromResult(Next($"stop {serviceName} {endpoint.Pid}@{endpoint.StartTime} {endpoint.LocalAddress}:{endpoint.LocalPort}", AdminPortVerb.Stop));
}
