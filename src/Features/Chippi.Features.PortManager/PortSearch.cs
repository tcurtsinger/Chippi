// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chippi.Features.PortManager;

/// <summary>What Search's text means to Port manager: "port 3000" is a port; "port 70000" is an invalid one; anything else is not ours.</summary>
public abstract record PortQuery
{
    public sealed record Port(int Number) : PortQuery;
    public sealed record Invalid : PortQuery;
    public sealed record NotPort : PortQuery;

    public static readonly PortQuery None = new NotPort();

    private static readonly Regex Shape = new(@"^port\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>"port 3000" → 3000; "port 0", "port 65536" and a number too long to be one → invalid; "port abc", "port" and "port 3000 x" → not a port query.</summary>
    public static PortQuery Parse(string text)
    {
        if (Shape.Match(text.Trim()) is not { Success: true } match) return None;
        string token = match.Groups[1].Value;
        if (token.Length == 0 || !token.All(char.IsAsciiDigit)) return None;
        if (token.Length > 6 || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number is < 1 or > 65535) return new Invalid();
        return new Port(number);
    }
}

/// <summary>One line of a Search answer, before it is a command: the words of the approved mockup.</summary>
/// <param name="Name">"Port 3000 · node.exe".</param>
/// <param name="Detail">"PID 4120", "Service · PID 2880", "Windows · PID 4", "IPv4, IPv6 · PID 4120", "2 processes".</param>
/// <param name="Verb">Free, Stop, or Open (Settings › Ports at the row); null for a row that is only text ("Port 3001 not in use").</param>
public sealed record PortAnswer(string Name, string? Detail, string? Verb, bool NeedsAdmin, PortRow? Row, string? IconPath)
{
    /// <summary>Builds the answer for the rows on <paramref name="port"/>: one line, however many sockets (IPv4 and IPv6, several connections).</summary>
    public static PortAnswer For(int port, IReadOnlyList<PortRow> rows)
    {
        if (rows.Count == 0) return new($"Port {port} not in use", null, null, false, null, null);
        var held = rows.Where(r => r.Endpoint.IsHeld).ToArray();
        var pool = held.Length > 0 ? held : rows;
        var pids = pool.Select(r => r.Holder.Pid).Distinct().Order().ToArray();
        if (pids.Length > 1)
        {
            string shown = string.Join(", ", pids.Take(3)) + (pids.Length > 3 ? $" +{pids.Length - 3}" : "");
            return new($"Port {port} · {pids.Length} processes", "PID " + shown, "Open", false, null, null);
        }
        var row = pool[0];
        var holder = row.Holder;
        string families = pool.Select(r => r.Endpoint.IsIPv6).Distinct().Count() > 1 ? "IPv4, IPv6 · " : "";
        string pid = holder.Pid == 0 ? "" : "PID " + holder.Pid;
        if (holder.Pid == 0)
            return new($"Port {port}", StateText(row.Endpoint.State), null, false, row, null);
        string who = holder.Kind switch
        {
            HolderKind.Service => row.ServiceDisplay ?? row.ServiceName ?? holder.Name,
            HolderKind.Chippi => "Chippi",
            _ => holder.Name,
        };
        string name = $"Port {port} · {who}";
        string prefix = holder.Kind switch
        {
            HolderKind.Service => "Service · ",
            HolderKind.Shared => "Shared service · ",
            HolderKind.Windows => "Windows · ",
            _ => families,
        };
        // A row that is only a connection says its state; a listener or a bound endpoint says what the verb does.
        string detail = !row.Endpoint.IsHeld && holder.Kind is HolderKind.Own or HolderKind.Elevated ? StateText(row.Endpoint.State) + " · " + pid : prefix + pid;
        string? verb = row.Verb switch { PortVerb.Free => "Free", PortVerb.Stop => "Stop", _ => "Open" };
        return new(name, detail, verb, row.NeedsAdmin, row, holder.Path);
    }

    public static string StateText(TcpState state) => state switch
    {
        TcpState.Listen => "Listening",
        TcpState.Established => "Established",
        TcpState.TimeWait => "Time wait",
        TcpState.CloseWait => "Close wait",
        TcpState.SynSent => "Syn sent",
        TcpState.SynReceived => "Syn received",
        TcpState.FinWait1 => "Fin wait 1",
        TcpState.FinWait2 => "Fin wait 2",
        TcpState.LastAck => "Last ack",
        TcpState.Closing => "Closing",
        TcpState.None => "Bound",
        _ => "Closed",
    };
}
