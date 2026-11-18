// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.PortManager;

/// <summary>
/// What no verb ends or stops. The admin helper refuses the same (src/Chippi.Elevated/PortActions.cs: keep the lists equal); the page
/// never offers a verb the helper would refuse.
/// </summary>
public static class PortRules
{
    /// <summary>Processes never ended, by image name without the extension. svchost is handled by its services, not listed here.</summary>
    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System", "csrss", "smss", "wininit", "winlogon", "lsass", "lsm", "services",
        "MsMpEng", "MsMpEngCP", "NisSrv", "MpDefenderCoreService", "MsSense", "SenseIR", "SecurityHealthService", "SgrmBroker", "fontdrvhost", "dwm",
    };

    /// <summary>Windows' core services, never stopped.</summary>
    public static readonly IReadOnlySet<string> CoreServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "BFE", "mpssvc", "WinDefend", "EventLog", "Dhcp", "Dnscache", "nsi", "PlugPlay", "Power",
        "BrokerInfrastructure", "Winmgmt", "CryptSvc", "Schedule", "ProfSvc", "gpsvc",
    };

    public static string BaseName(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    public static bool IsSvchost(string name) => string.Equals(BaseName(name), "svchost", StringComparison.OrdinalIgnoreCase);
}
