// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.ComponentModel;
using System.Net;

namespace Chippi.Features.PortManager;

/// <summary>
/// The IP Helper tables (GetExtendedTcpTable, GetExtendedUdpTable) in their OWNER_MODULE forms, which carry what the owner-module
/// lookup needs. Row layouts are the documented MIB_*ROW_OWNER_MODULE structs; each is padded by the 16-entry module array.
/// </summary>
public static class PortTables
{
    public const int InsufficientBuffer = 122;
    internal const int TableHeader = 8, Tcp4Row = 160, Tcp6Row = 192, Udp4Row = 160, Udp6Row = 176;

    /// <summary>One native call: fills the buffer and sets the size needed (or used); returns the Win32 result.</summary>
    public delegate int TableCall(byte[] buffer, ref uint size);

    public static int RowSize(PortProtocol protocol, bool ipv6) => (protocol, ipv6) switch
    {
        (PortProtocol.Tcp, false) => Tcp4Row, (PortProtocol.Tcp, true) => Tcp6Row, (PortProtocol.Udp, false) => Udp4Row, _ => Udp6Row,
    };

    /// <summary>Reads a table, asking again with a bigger buffer when it grew between the two calls.</summary>
    public static IReadOnlyList<PortEndpoint> Read(PortProtocol protocol, bool ipv6, TableCall call)
    {
        uint size = 0;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var buffer = new byte[Math.Max(size, 8u)];
            uint offered = (uint)buffer.Length;
            int result = call(buffer, ref offered);
            if (result == InsufficientBuffer)
            {
                size = offered + 4096 + offered / 8;
                continue;
            }
            if (result != 0) throw new Win32Exception(result);
            return Parse(protocol, ipv6, buffer);
        }
        throw new InvalidOperationException("The port table kept changing.");
    }

    public static IReadOnlyList<PortEndpoint> Parse(PortProtocol protocol, bool ipv6, ReadOnlySpan<byte> table)
    {
        int stride = RowSize(protocol, ipv6);
        if (table.Length < TableHeader) throw new InvalidDataException("Invalid port table.");
        uint count = BitConverter.ToUInt32(table[..4]);
        if (count > (uint)((table.Length - TableHeader) / stride)) throw new InvalidDataException("Invalid port table.");
        var entries = new List<PortEndpoint>((int)count);
        for (int i = 0; i < count; i++)
            entries.Add(ParseRow(protocol, ipv6, table.Slice(TableHeader + i * stride, stride)));
        return entries;
    }

    private static PortEndpoint ParseRow(PortProtocol protocol, bool ipv6, ReadOnlySpan<byte> row)
    {
        // A port is in network byte order in the low 16 bits of a DWORD.
        static int Port(ReadOnlySpan<byte> row, int offset) => (row[offset] << 8) | row[offset + 1];
        static int Dword(ReadOnlySpan<byte> row, int offset) => BitConverter.ToInt32(row.Slice(offset, 4));
        IPAddress Address(ReadOnlySpan<byte> r, int offset) => ipv6 ? new IPAddress(r.Slice(offset, 16), (long)BitConverter.ToUInt32(r.Slice(offset + 16, 4))) : new IPAddress(r.Slice(offset, 4));
        var raw = row.ToArray();
        return (protocol, ipv6) switch
        {
            (PortProtocol.Tcp, false) => new PortEndpoint(protocol, Address(row, 4), Port(row, 8), Address(row, 12), Port(row, 16), State(Dword(row, 0)), Dword(row, 20), raw),
            (PortProtocol.Tcp, true) => new PortEndpoint(protocol, Address(row, 0), Port(row, 20), Address(row, 24), Port(row, 44), State(Dword(row, 48)), Dword(row, 52), raw),
            (PortProtocol.Udp, false) => new PortEndpoint(protocol, Address(row, 0), Port(row, 4), null, 0, TcpState.None, Dword(row, 8), raw),
            _ => new PortEndpoint(protocol, Address(row, 0), Port(row, 20), null, 0, TcpState.None, Dword(row, 24), raw),
        };
    }

    private static TcpState State(int value) => value is >= 1 and <= 12 ? (TcpState)value : TcpState.Closed;
}
