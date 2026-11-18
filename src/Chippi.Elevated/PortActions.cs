// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Net;
using System.Net.Sockets;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

public enum PortProtocol { Tcp, Udp }
/// <summary>One row of the OWNER_MODULE tables. Raw is the row's bytes, which the owner-module lookup needs; fakes leave it null.</summary>
public sealed record PortEntry(PortProtocol Protocol, IPAddress Address, int Port, int Pid, byte[]? Raw = null);
/// <summary>A live process read by PID. Unreadable means Windows refused even limited information; Protected means it runs at a protection level (PPL), which limited access still opens.</summary>
public sealed record ProcessIdentity(long StartTime, string? Name, string? Path, bool Unreadable = false, bool Protected = false);
public sealed record ServiceEntry(string Name, uint Type, uint State, uint Controls, int Pid);
public enum NativeOutcome { Done, Gone, Changed, Failed }
public readonly record struct NativeResult(NativeOutcome Outcome, int Error = 0);

/// <summary>Everything PortActions asks of Windows, so tests run it on fakes and never touch a real process or service.</summary>
public interface IPortSystem
{
    IReadOnlyList<PortEntry> ReadEndpoints(PortProtocol protocol, AddressFamily family);
    /// <summary>The module name the stack attributes the endpoint to (a service's name for a service endpoint), or null.</summary>
    string? OwnerModule(PortEntry entry);
    /// <summary>Null when the process is gone.</summary>
    ProcessIdentity? Identify(int pid);
    /// <summary>Posts WM_CLOSE to the process's top-level windows, after checking its start time again on the same handle.</summary>
    NativeResult PostClose(int pid, long startTime);
    /// <summary>TerminateProcess on a handle whose start time was checked again.</summary>
    NativeResult Terminate(int pid, long startTime);
    /// <summary>Running Win32 services (never drivers) with the PID that hosts each.</summary>
    IReadOnlyList<ServiceEntry> RunningServices();
    NativeResult StopService(string name);
}

public interface IPortActions { Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct); }

/// <summary>
/// CloseProcess, EndProcess and StopService (docs/phase-7/admin-helper.md). Each reads the confirmed row again and acts only when it still matches;
/// a refusal does nothing and says why in the reply's Reason.
/// </summary>
public sealed class PortActions(IPortSystem system, IReadOnlyCollection<int> protectedPids, string? chippiPath = null) : IPortActions
{
    public const string ChangedReason = "Changed since the scan", ProtectedReason = "Protected process", SharedReason = "Shared Windows service",
        CoreServiceReason = "Core Windows service", NotServiceReason = "Not a running service", NoWindowReason = "No window",
        ServiceHostReason = "Service", FailedReason = "Admin helper failed";
    internal const int DependentServicesRunning = 1051, ServiceNotActive = 1062, NoWindowError = 1400;
    internal const uint ServiceAcceptsStop = 1, ServiceRunning = 4, Win32ServiceTypes = 0x30;

    /// <summary>Processes no verb ends or closes, by image name without the extension. svchost is here: a service is stopped instead.</summary>
    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System", "csrss", "smss", "wininit", "winlogon", "lsass", "lsm", "services", "svchost",
        "MsMpEng", "MsMpEngCP", "NisSrv", "MpDefenderCoreService", "MsSense", "SenseIR", "SecurityHealthService", "SgrmBroker", "fontdrvhost", "dwm",
    };

    /// <summary>Windows' core services, never stopped.</summary>
    public static readonly IReadOnlySet<string> CoreServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "BFE", "mpssvc", "WinDefend", "EventLog", "Dhcp", "Dnscache", "nsi", "PlugPlay", "Power",
        "BrokerInfrastructure", "Winmgmt", "CryptSvc", "Schedule", "ProfSvc", "gpsvc",
    };

    public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) => Task.Run(() =>
    {
        try { return Run(request); }
        catch (Exception)
        {
            // Any failure answers the request: an exception must never end the helper's session. Nothing from it is logged or sent.
            return Reply(request, Messages.PortFailed, FailedReason);
        }
    }, ct);

    internal AdminReply Run(AdminRequest r)
    {
        if (!Messages.RequestAllowed(r) || !Messages.IsPortVerb(r.Verb)) return Reply(r, Messages.PortFailed, "Invalid request");
        int pid = r.Pid!.Value;
        if (pid is 0 or 4 || protectedPids.Contains(pid)) return Reply(r, Messages.PortKept, ProtectedReason);
        if (!IPAddress.TryParse(r.LocalAddress, out var address)) return Reply(r, Messages.PortFailed, "Invalid request");
        var protocol = r.Protocol == "Udp" ? PortProtocol.Udp : PortProtocol.Tcp;
        var rows = system.ReadEndpoints(protocol, address.AddressFamily).Where(e => e.Port == r.LocalPort && SameAddress(e.Address, address)).ToArray();
        if (rows.Length == 0) return Reply(r, Messages.PortGone);
        var entry = rows.FirstOrDefault(e => e.Pid == pid);
        if (entry is null) return Reply(r, Messages.PortChanged, ChangedReason);
        var process = system.Identify(pid);
        if (process is null) return Reply(r, Messages.PortGone);
        if (process.Unreadable || process.Protected) return Reply(r, Messages.PortKept, ProtectedReason);
        if (process.StartTime != r.StartTime) return Reply(r, Messages.PortChanged, ChangedReason);
        if (IsProtected(process, r.Verb == Messages.StopService)) return Reply(r, Messages.PortKept, ProtectedReason);
        // A process that hosts a service is never closed or ended directly: the service is stopped instead.
        if (r.Verb != Messages.StopService && system.RunningServices().Any(s => s.Pid == pid)) return Reply(r, Messages.PortKept, ServiceHostReason);
        return r.Verb switch
        {
            Messages.CloseProcess => Close(r, system.PostClose(pid, r.StartTime!.Value)),
            Messages.EndProcess => Native(r, system.Terminate(pid, r.StartTime!.Value)),
            _ => Stop(r, entry, pid),
        };
    }

    private bool IsProtected(ProcessIdentity process, bool forService)
    {
        if (chippiPath is not null && process.Path is not null && string.Equals(process.Path, chippiPath, StringComparison.OrdinalIgnoreCase)) return true;
        if (process.Name is null) return true;
        if (forService && process.Name.Equals("svchost", StringComparison.OrdinalIgnoreCase)) return false;
        return ProtectedNames.Contains(process.Name);
    }

    private AdminReply Close(AdminRequest r, NativeResult result) => result.Outcome == NativeOutcome.Failed && result.Error == NoWindowError
        ? Reply(r, Messages.PortFailed, NoWindowReason) : Native(r, result);

    private AdminReply Stop(AdminRequest r, PortEntry entry, int pid)
    {
        string name = r.ServiceName!;
        var running = system.RunningServices();
        var service = running.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        // Attribution, as admin-helper.md says: the owner module names the service; failing that the PID hosts exactly this one running service.
        bool attributed = string.Equals(system.OwnerModule(entry), name, StringComparison.OrdinalIgnoreCase);
        if (!attributed)
        {
            var inPid = running.Where(s => s.Pid == pid && s.State == ServiceRunning).ToArray();
            attributed = inPid.Length == 1 && inPid[0].Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        }
        if (!attributed) return Reply(r, Messages.PortKept, SharedReason);
        if (CoreServices.Contains(name)) return Reply(r, Messages.PortKept, CoreServiceReason);
        if (service is null || (service.Type & Win32ServiceTypes) == 0 || service.State != ServiceRunning) return Reply(r, Messages.PortKept, NotServiceReason);
        if (service.Pid != pid) return Reply(r, Messages.PortChanged, ChangedReason);
        if ((service.Controls & ServiceAcceptsStop) == 0) return Reply(r, Messages.PortKept, "Can't stop");
        return Native(r, system.StopService(name));
    }

    private static AdminReply Native(AdminRequest r, NativeResult result) => result.Outcome switch
    {
        NativeOutcome.Done => Reply(r, Messages.PortDone),
        NativeOutcome.Gone => Reply(r, Messages.PortGone),
        NativeOutcome.Changed => Reply(r, Messages.PortChanged, ChangedReason),
        _ when result.Error == ServiceNotActive => Reply(r, Messages.PortGone),
        _ when result.Error == DependentServicesRunning => Reply(r, Messages.PortFailed, "Dependent services running"),
        _ => Reply(r, Messages.PortFailed, "Windows error " + result.Error),
    };

    private static bool SameAddress(IPAddress a, IPAddress b) => a.AddressFamily == b.AddressFamily && a.Equals(b)
        && (a.AddressFamily != AddressFamily.InterNetworkV6 || a.ScopeId == b.ScopeId);

    private static AdminReply Reply(AdminRequest r, string state, string? reason = null) => new(r.Id, state, Reason: reason);
}
