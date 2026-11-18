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
/// The process list's names for one scan, read once and lazily (only a protected process needs it). Each scan makes its own, so two
/// scans on different threads never share or overwrite a map.
/// </summary>
public sealed class ProcessNames(Func<IReadOnlyDictionary<int, string>>? load = null)
{
    private readonly Func<IReadOnlyDictionary<int, string>> _load = load ?? Read;
    private IReadOnlyDictionary<int, string>? _map;

    /// <summary>The process list is read the first time a name is asked for.</summary>
    public string? Get(int pid) => (_map ??= _load()).GetValueOrDefault(pid);

    private static Dictionary<int, string> Read()
    {
        var map = new Dictionary<int, string>();
        foreach (var process in System.Diagnostics.Process.GetProcesses())
            using (process)
                map[process.Id] = process.ProcessName;
        return map;
    }
}
