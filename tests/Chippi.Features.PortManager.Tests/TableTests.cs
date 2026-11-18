// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Chippi.Features.PortManager.Tests;

public sealed class TableTests
{
    /// <summary>A table as IP Helper lays it out: a count, padding, then fixed-size rows.</summary>
    private static byte[] Table(int stride, int rows, Action<Span<byte>, int> fill)
    {
        var table = new byte[PortTables.TableHeader + rows * stride];
        BitConverter.TryWriteBytes(table.AsSpan(0, 4), (uint)rows);
        for (int i = 0; i < rows; i++) fill(table.AsSpan(PortTables.TableHeader + i * stride, stride), i);
        return table;
    }

    private static void Port(Span<byte> row, int offset, int port) { row[offset] = (byte)(port >> 8); row[offset + 1] = (byte)port; }
    private static void Dword(Span<byte> row, int offset, int value) => BitConverter.TryWriteBytes(row.Slice(offset, 4), value);

    private static void Tcp4(Span<byte> row, string local, int port, string remote, int remotePort, int state, int pid)
    {
        Dword(row, 0, state);
        IPAddress.Parse(local).TryWriteBytes(row.Slice(4, 4), out _); Port(row, 8, port);
        IPAddress.Parse(remote).TryWriteBytes(row.Slice(12, 4), out _); Port(row, 16, remotePort);
        Dword(row, 20, pid);
    }

    [Fact]
    public void Tcp_ipv4_rows_give_listening_and_time_wait_with_ports_and_pids()
    {
        var table = Table(PortTables.Tcp4Row, 3, (row, i) =>
        {
            if (i == 0) Tcp4(row, "127.0.0.1", 3000, "0.0.0.0", 0, 2, 4120);
            if (i == 1) Tcp4(row, "192.168.1.42", 50122, "20.190.151.9", 443, 11, 0);
            if (i == 2) Tcp4(row, "192.168.1.42", 49811, "142.250.80.14", 443, 5, 14220);
        });
        var rows = PortTables.Parse(PortProtocol.Tcp, false, table);
        Assert.Equal(3, rows.Count);
        Assert.Equal((IPAddress.Loopback, 3000, TcpState.Listen, 4120), (rows[0].LocalAddress, rows[0].LocalPort, rows[0].State, rows[0].Pid));
        Assert.True(rows[0].IsHeld);
        Assert.Equal((50122, 443, TcpState.TimeWait, 0), (rows[1].LocalPort, rows[1].RemotePort, rows[1].State, rows[1].Pid));
        Assert.False(rows[1].IsHeld);
        Assert.Equal(IPAddress.Parse("142.250.80.14"), rows[2].RemoteAddress);
        Assert.False(rows[2].IsHeld);
        Assert.All(rows, r => Assert.Equal(PortTables.Tcp4Row, r.Raw!.Length));
    }

    [Fact]
    public void Tcp_ipv6_rows_keep_the_address_and_the_later_offsets()
    {
        var table = Table(PortTables.Tcp6Row, 1, (row, _) =>
        {
            IPAddress.IPv6Loopback.TryWriteBytes(row[..16], out _); Dword(row, 16, 0); Port(row, 20, 3000);
            IPAddress.IPv6Any.TryWriteBytes(row.Slice(24, 16), out _); Port(row, 44, 0);
            Dword(row, 48, 2); Dword(row, 52, 4120);
        });
        var row = Assert.Single(PortTables.Parse(PortProtocol.Tcp, true, table));
        Assert.Equal((IPAddress.IPv6Loopback, 3000, TcpState.Listen, 4120), (row.LocalAddress, row.LocalPort, row.State, row.Pid));
        Assert.True(row.IsIPv6);
    }

    [Fact]
    public void Ipv6_link_local_scope_id_is_read()
    {
        var table = Table(PortTables.Tcp6Row, 1, (row, _) =>
        {
            IPAddress.Parse("fe80::1").TryWriteBytes(row[..16], out _); Dword(row, 16, 12); Port(row, 20, 8080); Dword(row, 48, 2); Dword(row, 52, 77);
        });
        var row = Assert.Single(PortTables.Parse(PortProtocol.Tcp, true, table));
        Assert.Equal(12, row.LocalAddress.ScopeId);
    }

    [Fact]
    public void Udp_rows_have_no_state_and_no_remote_for_both_families()
    {
        var v4 = Table(PortTables.Udp4Row, 1, (row, _) => { IPAddress.Any.TryWriteBytes(row[..4], out _); Port(row, 4, 5353); Dword(row, 8, 14220); });
        var v6 = Table(PortTables.Udp6Row, 1, (row, _) => { IPAddress.IPv6Any.TryWriteBytes(row[..16], out _); Port(row, 20, 5353); Dword(row, 24, 14220); });
        var a = Assert.Single(PortTables.Parse(PortProtocol.Udp, false, v4));
        var b = Assert.Single(PortTables.Parse(PortProtocol.Udp, true, v6));
        foreach (var e in new[] { a, b })
        {
            Assert.Equal((5353, TcpState.None, 14220), (e.LocalPort, e.State, e.Pid));
            Assert.Null(e.RemoteAddress);
            Assert.True(e.IsHeld);
        }
        Assert.False(a.IsIPv6);
        Assert.True(b.IsIPv6);
    }

    [Fact]
    public void A_count_past_the_buffer_is_refused()
    {
        var table = Table(PortTables.Tcp4Row, 1, (_, _) => { });
        BitConverter.TryWriteBytes(table.AsSpan(0, 4), 5u);
        Assert.Throws<InvalidDataException>(() => PortTables.Parse(PortProtocol.Tcp, false, table));
        Assert.Throws<InvalidDataException>(() => PortTables.Parse(PortProtocol.Tcp, false, new byte[3]));
    }

    [Fact]
    public void A_table_that_grows_between_the_two_calls_is_read_again_with_the_bigger_buffer()
    {
        int calls = 0;
        var rows = PortTables.Read(PortProtocol.Tcp, false, (byte[] buffer, ref uint size) =>
        {
            calls++;
            // The first answer sizes one row; by the next call the table has forty, which a first retry of the usual padding still doesn't fit.
            uint need = calls == 1 ? (uint)(PortTables.TableHeader + PortTables.Tcp4Row) : (uint)(PortTables.TableHeader + 40 * PortTables.Tcp4Row);
            if (buffer.Length < need) { size = need; return PortTables.InsufficientBuffer; }
            var table = Table(PortTables.Tcp4Row, 40, (row, i) => Tcp4(row, "127.0.0.1", 3000 + i, "0.0.0.0", 0, 2, 100 + i));
            table.CopyTo(buffer, 0);
            size = (uint)table.Length;
            return 0;
        });
        Assert.Equal(40, rows.Count);
        Assert.True(calls >= 3);
        Assert.Equal(Enumerable.Range(3000, 40), rows.Select(r => r.LocalPort));
    }

    [Fact]
    public void A_table_that_never_settles_fails_and_a_win32_error_throws()
    {
        Assert.Throws<InvalidOperationException>(() => PortTables.Read(PortProtocol.Tcp, false, (byte[] b, ref uint size) => { size = (uint)b.Length + 1; return PortTables.InsufficientBuffer; }));
        Assert.Throws<System.ComponentModel.Win32Exception>(() => PortTables.Read(PortProtocol.Tcp, false, (byte[] b, ref uint size) => 5));
    }

    [Fact]
    public void Real_tables_read_without_admin_and_find_a_socket_this_test_opens()
    {
        // Reads only: no process is ended and no service touched.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var system = new WindowsPortSystem();
        var all = system.ReadEndpoints();
        Assert.NotEmpty(all);
        var mine = all.Single(e => e.Protocol == PortProtocol.Tcp && e.LocalPort == port && !e.IsIPv6);
        Assert.Equal((Environment.ProcessId, TcpState.Listen), (mine.Pid, mine.State));
        Assert.Equal(Environment.ProcessId, all.Single(e => e.Protocol == PortProtocol.Udp && e.LocalPort == udpPort && !e.IsIPv6).Pid);

        var scanner = new PortScanner(system);
        var row = scanner.Scan(e => e.LocalPort == port).Single();
        // This process is the current one, so the rule that protects Chippi protects it.
        Assert.Equal((HolderKind.Chippi, PortVerb.None, 1), (row.Holder.Kind, row.Verb, row.Holder.PortCount));
        using var self = Process.GetCurrentProcess();
        Assert.Equal(self.StartTime.ToFileTime(), system.ReadProcess(Environment.ProcessId)!.StartTime);
        Assert.NotEmpty(system.RunningServices());
        // The whole list classifies without throwing; a verb sits only on a held row of a holder the helper would not refuse.
        var everything = scanner.Scan();
        Assert.NotEmpty(everything);
        Assert.All(everything.Where(r => r.Verb != PortVerb.None), r =>
        {
            Assert.True(r.Endpoint.IsHeld);
            Assert.True(r.Holder.Kind is HolderKind.Own or HolderKind.Elevated or HolderKind.Service);
        });
        Assert.All(everything.Where(r => r.Holder.Pid is 0 or 4), r => Assert.Equal(PortVerb.None, r.Verb));
    }
}
