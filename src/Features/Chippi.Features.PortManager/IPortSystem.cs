// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.PortManager;

public enum EndOutcome { Done, Gone, Changed, Failed }
public readonly record struct EndResult(EndOutcome Outcome, int Error = 0);

/// <summary>
/// Everything the engine asks of Windows, so tests run it on fakes and never read, close or end a real process.
/// Reads never need admin; the two actions are only called on the owner's own processes.
/// </summary>
public interface IPortSystem
{
    /// <summary>Chippi's own PID and image path: never ended.</summary>
    int CurrentPid { get; }
    string? CurrentPath { get; }

    /// <summary>Every TCP and UDP endpoint, IPv4 and IPv6, with its owning PID.</summary>
    IReadOnlyList<PortEndpoint> ReadEndpoints();

    /// <summary>The module name the stack attributes the endpoint to (a service's name for a service endpoint), or null.</summary>
    string? OwnerModule(PortEndpoint endpoint);

    /// <summary>
    /// The process, or null when it has gone. <paramref name="names"/> is the scan's own process-name list (null: a private one), so
    /// concurrent scans never share a name map.
    /// </summary>
    ProcessInfo? ReadProcess(int pid, ProcessNames? names = null);

    /// <summary>Running Win32 services (never drivers) with the PID that hosts each.</summary>
    IReadOnlyList<ServiceInfo> RunningServices();

    /// <summary>Whether the process has a visible top-level window, which a close request can reach.</summary>
    bool HasWindow(int pid);

    /// <summary>WM_CLOSE to the process's visible windows, after checking its start time again on the same handle.</summary>
    EndResult PostClose(int pid, long startTime);

    /// <summary>TerminateProcess on a handle whose start time was checked again.</summary>
    EndResult Terminate(int pid, long startTime);
}

/// <summary>
/// The process list's names and start times for one scan, read once and lazily (only a protected or unreadable process needs it). Each scan
/// makes its own, so two scans on different threads never share or overwrite a map. The list needs no access to any process: it is how a
/// service process a standard user can't open still gets the start time the helper checks.
/// </summary>
public sealed class ProcessNames
{
    private readonly Func<IReadOnlyDictionary<int, string>> _load;
    private readonly Func<IReadOnlyDictionary<int, long>> _starts;
    private IReadOnlyDictionary<int, string>? _map;
    private IReadOnlyDictionary<int, long>? _startMap;
    private SystemProcesses.Snapshot? _snapshot;

    public ProcessNames(Func<IReadOnlyDictionary<int, string>>? load = null, Func<IReadOnlyDictionary<int, long>>? starts = null)
    {
        _load = load ?? (() => Snap().Names);
        _starts = starts ?? (() => Snap().Starts);
    }

    private SystemProcesses.Snapshot Snap() => _snapshot ??= SystemProcesses.Read();

    /// <summary>The process list is read the first time a name is asked for.</summary>
    public string? Get(int pid) => (_map ??= _load()).GetValueOrDefault(pid);

    /// <summary>The process's creation time (FILETIME) from the list, or 0 when it isn't there.</summary>
    public long StartOf(int pid) => (_startMap ??= _starts()).GetValueOrDefault(pid);
}
