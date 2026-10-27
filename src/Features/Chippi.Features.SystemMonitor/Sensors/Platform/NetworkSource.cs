// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// Network adapters from GetIfTable2, hardware rows only: a filter driver (VPN, packet capture, QoS)
/// adds copies of an adapter that would count its traffic again. Speeds are bytes per second.
/// </summary>
internal sealed class NetworkSource : INetworkSource
{
    // MIB_IF_ROW2 offsets (x64 and Arm64): the table is a count, padding, then the rows.
    private const int TableHeader = 8;
    private const int RowSize = 1352;
    private const int GuidOffset = 12;
    private const int AliasOffset = 28;
    private const int FlagsOffset = 1152;
    private const int OperStatusOffset = 1156;
    private const int InOctetsOffset = 1208;
    private const int OutOctetsOffset = 1280;
    private const byte HardwareInterface = 1, FilterInterface = 2;
    private const int OperStatusUp = 1;

    private readonly Dictionary<Guid, (ulong In, ulong Out, long At)> _previous = [];

    public unsafe IReadOnlyList<NetworkReading> Read()
    {
        int result = GetIfTable2(out nint table);
        if (result != 0)
            throw new System.ComponentModel.Win32Exception(result);
        try
        {
            long now = Environment.TickCount64;
            int count = *(int*)table;
            var readings = new List<NetworkReading>();
            var seen = new HashSet<Guid>();
            for (int i = 0; i < count; i++)
            {
                byte* row = (byte*)table + TableHeader + (long)i * RowSize;
                byte flags = row[FlagsOffset];
                if ((flags & HardwareInterface) == 0 || (flags & FilterInterface) != 0)
                    continue;
                var id = *(Guid*)(row + GuidOffset);
                if (!seen.Add(id))
                    continue;
                string name = new((char*)(row + AliasOffset));
                bool connected = *(int*)(row + OperStatusOffset) == OperStatusUp;
                ulong received = *(ulong*)(row + InOctetsOffset), sent = *(ulong*)(row + OutOctetsOffset);
                double? rx = null, tx = null;
                if (connected && _previous.TryGetValue(id, out var last))
                {
                    double seconds = (now - last.At) / 1000.0;
                    // A counter that went down was reset (adapter restarted): no rate this time.
                    if (seconds > 0 && seconds <= RateClock.StaleAfter.TotalSeconds && received >= last.In && sent >= last.Out)
                    {
                        rx = (received - last.In) / seconds;
                        tx = (sent - last.Out) / seconds;
                    }
                }
                _previous[id] = (received, sent, now);
                readings.Add(new NetworkReading(id.ToString("B"), name, connected, rx, tx));
            }
            foreach (var gone in _previous.Keys.Where(k => !seen.Contains(k)).ToList())
                _previous.Remove(gone);
            return readings;
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    [DllImport("iphlpapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetIfTable2(out nint table);

    [DllImport("iphlpapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void FreeMibTable(nint table);
}
