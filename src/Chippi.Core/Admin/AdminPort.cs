// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Core.Admin;

public enum AdminProtocol { Tcp, Udp }
public enum AdminPortVerb { Close, End, Stop }
/// <summary>
/// What the owner confirmed: the row's protocol, local address (IPv4 or IPv6 literal), local port, owning PID and that process's start time
/// (FILETIME ticks, as Windows' GetProcessTimes gives it). The helper reads the row again and refuses anything that no longer matches.
/// </summary>
public sealed record AdminEndpoint(AdminProtocol Protocol, string LocalAddress, int LocalPort, int Pid, long StartTime);

/// <summary>
/// Freed: the helper did the action (the page's next read says whether the port is free). AlreadyClosed: the endpoint or process was gone.
/// Changed: the PID or start time no longer matches. Kept: the helper refused (Reason says why). Failed: Windows refused or the helper failed.
/// NeedsAdmin: the prompt was declined or the helper couldn't start.
/// </summary>
public enum AdminPortState { Freed, AlreadyClosed, Changed, Kept, Failed, NeedsAdmin, Cancelled }

public sealed record AdminPortResult(AdminPortVerb Verb, AdminPortState State, string? Reason = null)
{
    /// <summary>The status words of the approved mockup.</summary>
    public string Text => State switch
    {
        AdminPortState.Freed => "Freed",
        AdminPortState.AlreadyClosed => "Already closed",
        AdminPortState.Changed => "Changed",
        AdminPortState.Kept => "Kept",
        AdminPortState.NeedsAdmin => "Needs admin",
        AdminPortState.Cancelled => "Cancelled",
        _ => Verb == AdminPortVerb.Stop ? "Stop failed" : "End failed",
    };
}
