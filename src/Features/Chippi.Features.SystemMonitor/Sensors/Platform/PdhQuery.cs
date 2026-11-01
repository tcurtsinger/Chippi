// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// A PDH query with English counter paths, so it works on any display language. Rate counters need
/// two collections; until then their values are null. Wildcard counters are read into one buffer the
/// query keeps, so a read of hundreds of instances (GPU Engine) allocates nothing.
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
    // Pinned for its life: the instance names PDH writes into it are pointers into it.
    private byte[] _buffer = [];

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
    public List<(string Name, double? Value)> Values(nint counter)
    {
        var instances = Instances(counter);
        var list = new List<(string, double?)>(instances.Count);
        foreach (var instance in instances)
            list.Add((instance.Name.ToString(), instance.Value));
        return list;
    }

    /// <summary>
    /// Every instance of a wildcard counter, its name read in place: valid until the next read. None
    /// while the counter has no data (a rate's first collection), however PDH says so.
    /// </summary>
    public unsafe InstanceList Instances(nint counter)
    {
        const uint format = FormatDouble | FormatNoCap100;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint size = (uint)_buffer.Length;
            uint status;
            uint count;
            fixed (byte* p = _buffer)
                status = PdhGetFormattedCounterArrayW(counter, format, ref size, out count, p);
            if (status == 0)
                return new InstanceList(_buffer, (int)count);
            if (status is InvalidData or CounterInvalidData or NegativeDenominator or NoData)
                return default;
            if (status != MoreData)
                Check(status, "PdhGetFormattedCounterArray");
            // Too small, or the first read. PDH says the size it needs reliably only when asked with
            // no buffer, and instances come and go between calls, so leave room.
            size = 0;
            status = PdhGetFormattedCounterArrayW(counter, format, ref size, out _, null);
            if (status is InvalidData or CounterInvalidData or NegativeDenominator or NoData || status == 0 || size == 0)
                return default;
            if (status != MoreData)
                Check(status, "PdhGetFormattedCounterArray");
            _buffer = GC.AllocateUninitializedArray<byte>(checked((int)(size + size / 4)), pinned: true);
        }
        throw new PdhException("PdhGetFormattedCounterArray", MoreData);
    }

    /// <summary>A wildcard counter's instances, in the query's buffer.</summary>
    public readonly ref struct InstanceList(byte[]? buffer, int count)
    {
        public int Count => buffer is null ? 0 : count;

        public Enumerator GetEnumerator() => new(buffer, Count);

        public unsafe ref struct Enumerator(byte[]? buffer, int count)
        {
            private int _index = -1;

            public bool MoveNext() => ++_index < count;

            public readonly Instance Current
            {
                get
                {
                    var item = ((Item*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer!)))[_index];
                    // CStatus 0 (valid) or 1 (new data); anything else is no value yet or a failed instance.
                    return new Instance(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(item.Name), item.Status <= 1 ? item.Value : null);
                }
            }
        }
    }

    /// <summary>One instance: its name ("pid_4_luid_…_engtype_3D", "0,3") and value.</summary>
    public readonly ref struct Instance(ReadOnlySpan<char> name, double? value)
    {
        public ReadOnlySpan<char> Name { get; } = name;

        public double? Value { get; } = value;
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
