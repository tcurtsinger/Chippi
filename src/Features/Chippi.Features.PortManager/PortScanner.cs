// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.PortManager;

/// <summary>
/// Reads the tables and classifies each row's holder (docs/phase-7/port-manager.md): own process, elevated or other user, an
/// attributed service, an ambiguous shared host, Windows or Chippi itself. A verb is only offered on a listening TCP row or a
/// bound UDP endpoint, and never on a holder the admin helper would refuse.
/// </summary>
public sealed class PortScanner(IPortSystem system)
{
    /// <summary>Reads the tables, and for rows passing <paramref name="where"/> (all, when null) their holders.</summary>
    public IReadOnlyList<PortRow> Scan(Func<PortEndpoint, bool>? where = null)
    {
        var endpoints = system.ReadEndpoints();
        var rows = where is null ? endpoints : [.. endpoints.Where(where)];
        if (rows.Count == 0) return [];
        var processes = new Dictionary<int, ProcessInfo?>();
        var names = new ProcessNames();   // this scan's own: scans run on several threads at once
        foreach (int pid in rows.Select(r => r.Pid).Distinct())
            processes[pid] = pid is 0 or 4 ? null : SafeProcess(pid, names);
        ILookup<int, ServiceInfo>? services = null;
        var ports = rows.Where(r => r.IsHeld).GroupBy(r => r.Pid).ToDictionary(g => g.Key, g => g.Select(r => (r.Protocol, r.LocalPort)).Distinct().Count());
        var result = new List<PortRow>(rows.Count);
        foreach (var row in rows.OrderBy(r => r.LocalPort).ThenBy(r => r.Protocol).ThenBy(r => r.LocalAddress.ToString(), StringComparer.Ordinal).ThenBy(r => r.RemotePort))
        {
            services ??= Services();
            result.Add(Classify(row, processes[row.Pid], services[row.Pid].ToArray(), ports.GetValueOrDefault(row.Pid, 0)));
        }
        return result;
    }

    private ProcessInfo? SafeProcess(int pid, ProcessNames names)
    {
        try { return system.ReadProcess(pid, names); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException) { return new ProcessInfo(pid, 0, "", null, Unreadable: true); }
    }

    private ILookup<int, ServiceInfo> Services()
    {
        try { return system.RunningServices().ToLookup(s => s.Pid); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { return Array.Empty<ServiceInfo>().ToLookup(s => s.Pid); }
    }

    private PortRow Classify(PortEndpoint row, ProcessInfo? process, ServiceInfo[] services, int portCount)
    {
        PortRow With(HolderKind kind, PortVerb verb, string name, ServiceInfo? service = null) =>
            new(row, new PortHolder(row.Pid, process?.StartTime ?? 0, name, process?.Path, kind, portCount), row.IsHeld ? verb : PortVerb.None,
                service?.Name, service?.DisplayName);
        if (row.Pid == 0) return With(HolderKind.Windows, PortVerb.None, "");
        if (row.Pid == 4) return With(HolderKind.Windows, PortVerb.None, "System");
        if (process is null) return With(HolderKind.Gone, PortVerb.None, "");
        string name = process.Name;
        if (row.Pid == system.CurrentPid || (system.CurrentPath is { Length: > 0 } own && string.Equals(process.Path, own, StringComparison.OrdinalIgnoreCase)))
            return With(HolderKind.Chippi, PortVerb.None, name);
        if (process.Protected || PortRules.ProtectedNames.Contains(PortRules.BaseName(name))) return With(HolderKind.Windows, PortVerb.None, name);
        // A service process is usually one the owner's standard token can't open (LocalSystem, NetworkService): the service list and the process
        // list's start time are enough to offer Stop, which the helper attributes again before it stops anything.
        if (services.Length > 0 && !(process.Unreadable && process.StartTime == 0))
        {
            ServiceInfo? match;
            if (row.IsHeld)
            {
                // The endpoint's owner module names its service; failing that, a process hosting exactly one service owns the endpoint.
                string? owner = SafeOwner(row);
                match = (owner is null ? null : services.FirstOrDefault(s => string.Equals(s.Name, owner, StringComparison.OrdinalIgnoreCase)))
                    ?? (services.Length == 1 ? services[0] : null);
            }
            else match = services.Length == 1 ? services[0] : null;
            if (match is null) return With(HolderKind.Shared, PortVerb.None, name);
            if (PortRules.CoreServices.Contains(match.Name)) return With(HolderKind.Windows, PortVerb.None, name, match);
            return With(HolderKind.Service, PortVerb.Stop, name, match);
        }
        if (process.Unreadable || PortRules.IsSvchost(name)) return With(HolderKind.Windows, PortVerb.None, name);
        return process.CanTerminate ? With(HolderKind.Own, PortVerb.Free, name) : With(HolderKind.Elevated, PortVerb.Free, name);
    }

    private string? SafeOwner(PortEndpoint row)
    {
        try { return system.OwnerModule(row); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }
}
