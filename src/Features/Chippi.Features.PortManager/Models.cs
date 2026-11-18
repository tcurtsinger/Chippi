// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Net;

namespace Chippi.Features.PortManager;

public enum PortProtocol { Tcp, Udp }

/// <summary>MIB_TCP_STATE. UDP endpoints have none.</summary>
public enum TcpState { None = 0, Closed = 1, Listen = 2, SynSent = 3, SynReceived = 4, Established = 5, FinWait1 = 6, FinWait2 = 7, CloseWait = 8, Closing = 9, LastAck = 10, TimeWait = 11, DeleteTcb = 12 }

/// <summary>
/// One row of the IP Helper tables. <see cref="Raw"/> is the row's bytes (an OWNER_MODULE row), which the owner-module lookup
/// needs; fakes leave it null.
/// </summary>
public sealed record PortEndpoint(PortProtocol Protocol, IPAddress LocalAddress, int LocalPort, IPAddress? RemoteAddress, int RemotePort,
    TcpState State, int Pid, byte[]? Raw = null)
{
    /// <summary>A listening TCP socket or a bound UDP endpoint: the only rows a verb is offered on.</summary>
    public bool IsHeld => Protocol == PortProtocol.Udp || State == TcpState.Listen;

    public bool IsIPv6 => LocalAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

    /// <summary>Equal rows across two reads: the same socket, whatever else changed.</summary>
    public string Key => $"{Protocol}|{LocalAddress}|{LocalPort}|{RemoteAddress}|{RemotePort}|{Pid}";
}

/// <summary>A live process. Unreadable means Windows refused even limited information; Protected means it runs at a protection level (PPL), which limited access still opens.</summary>
public sealed record ProcessInfo(int Pid, long StartTime, string Name, string? Path, bool Unreadable = false, bool CanTerminate = false, bool Protected = false);

/// <summary>A running Win32 service (never a driver) and the process that hosts it.</summary>
public sealed record ServiceInfo(string Name, string DisplayName, int Pid);

public enum HolderKind
{
    /// <summary>The owner's own process: Free ends it directly.</summary>
    Own,
    /// <summary>An elevated or another user's process: Free goes through the admin helper.</summary>
    Elevated,
    /// <summary>A service whose ownership of this endpoint is attributed: Stop goes through the admin helper.</summary>
    Service,
    /// <summary>A process hosting several services, none attributed to this endpoint: no verb.</summary>
    Shared,
    /// <summary>System (PID 4), a closing connection (PID 0), a protected process or a core service: no verb.</summary>
    Windows,
    /// <summary>Chippi itself: no verb.</summary>
    Chippi,
    /// <summary>The process has gone since the table was read: no verb.</summary>
    Gone,
}

public enum PortVerb { None, Free, Stop }

/// <summary>
/// The process that holds a port. A process on several ports is one holder with a port count.
/// </summary>
public sealed record PortHolder(int Pid, long StartTime, string Name, string? Path, HolderKind Kind, int PortCount);

/// <summary>One endpoint with its holder and what can be done about it.</summary>
public sealed record PortRow(PortEndpoint Endpoint, PortHolder Holder, PortVerb Verb, string? ServiceName = null, string? ServiceDisplay = null)
{
    /// <summary>Free or Stop asks Windows for admin: it goes through the helper (the UAC shield beside the verb).</summary>
    public bool NeedsAdmin => Verb == PortVerb.Stop || (Verb == PortVerb.Free && Holder.Kind == HolderKind.Elevated);

    public string Key => Endpoint.Key;
}

public enum PortMark { None, New, Ended }

/// <summary>A row of the live list: new rows and ended ones are marked for a few seconds.</summary>
public sealed record PortListRow(PortRow Row, PortMark Mark = PortMark.None);
