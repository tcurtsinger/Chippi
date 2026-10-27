// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// A PDH query with English counter paths, so it works on any display language. Rate counters need
/// two collections; until then their values are null.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    public const uint NoData = 0x800007D5;
    public const uint NoObject = 0xC0000BB8;
    public const uint NoCounter = 0xC0000BB9;
    private const uint MoreData = 0x800007D2;
    // What a rate counter returns before its second collection.
    private const uint InvalidData = 0xC0000BC6;
    private const uint CounterInvalidData = 0xC0000BBA;
    private const uint NegativeDenominator = 0x800007D6;
    private const uint FormatDouble = 0x00000200;
    private const uint FormatNoCap100 = 0x00008000;

    private nint _query;

    public PdhQuery() => Check(PdhOpenQueryW(null, 0, out _query), "PdhOpenQuery");

    /// <summary>Adds a counter; throws <see cref="PdhException"/> when the object or counter doesn't exist.</summary>
    public nint Add(string path)
    {
        Check(PdhAddEnglishCounterW(_query, path, 0, out var counter), path);
        return counter;
    }

    /// <summary>Collects every counter. False when there is nothing to collect (no instances).</summary>
    public bool Collect()
    {
        uint status = PdhCollectQueryData(_query);
        if (status == NoData)
            return false;
        Check(status, "PdhCollectQueryData");
        return true;
    }

    /// <summary>Every instance of a wildcard counter; an instance without a valid value yet has a null value.</summary>
    public unsafe List<(string Name, double? Value)> Values(nint counter)
    {
        const uint format = FormatDouble | FormatNoCap100;
        uint size = 0;
        uint status = PdhGetFormattedCounterArrayW(counter, format, ref size, out _, null);
        if (status is InvalidData or CounterInvalidData or NegativeDenominator or NoData || size == 0)
            return [];
        if (status != MoreData)
            Check(status, "PdhGetFormattedCounterArray");
        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            Check(PdhGetFormattedCounterArrayW(counter, format, ref size, out uint count, p), "PdhGetFormattedCounterArray");
            var items = (Item*)p;
            var list = new List<(string, double?)>((int)count);
            for (int i = 0; i < count; i++)
            {
                // CStatus 0 (valid) or 1 (new data); anything else is no value yet or a failed instance.
                double? value = items[i].Status <= 1 ? items[i].Value : null;
                list.Add((new string(items[i].Name), value));
            }
            return list;
        }
    }

    public void Dispose()
    {
        if (_query != 0)
            _ = PdhCloseQuery(_query);
        _query = 0;
    }

    private static void Check(uint status, string what)
    {
        if (status != 0)
            throw new PdhException(what, status);
    }

    // PDH_FMT_COUNTERVALUE_ITEM_W: the instance name, then CStatus and the double (8-aligned).
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Item
    {
        public char* Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhOpenQueryW(string? source, nint user, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nint user, out nint counter);

    [DllImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern unsafe uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, byte* items);

    [DllImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCloseQuery(nint query);
}

internal sealed class PdhException(string what, uint status) : Exception($"{what} failed: 0x{status:X8}")
{
    public uint Status { get; } = status;
}
