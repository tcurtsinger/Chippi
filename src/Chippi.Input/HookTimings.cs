// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Chippi.Input;

/// <summary>
/// How long the hooks take per event, from entry to return, when <c>CHIPPI_INPUT_TIMINGS</c> names a report: written every
/// 10 seconds while new events come and at exit, for each device, as count, median, p99, p99.9 and the longest, in
/// microseconds. Only the hub's thread adds and writes.
/// </summary>
internal sealed class HookTimings(string report)
{
    private const int Capacity = 1 << 21;

    private readonly long[] _keys = new long[Capacity / 8];
    private readonly long[] _mouse = new long[Capacity];
    private int _keyCount, _mouseCount, _written, _ticks;

    /// <summary>Once a second, from the hub's timer.</summary>
    public void Tick()
    {
        if (++_ticks % 10 != 0 || _keyCount + _mouseCount == _written)
            return;
        Write();
    }

    public void Add(long ticks, bool keyboard)
    {
        if (keyboard)
        {
            if (_keyCount < _keys.Length)
                _keys[_keyCount++] = ticks;
        }
        else if (_mouseCount < _mouse.Length)
        {
            _mouse[_mouseCount++] = ticks;
        }
    }

    public void Write()
    {
        var text = new StringBuilder();
        text.AppendLine("device,count,p50_us,p99_us,p999_us,max_us");
        Line(text, "keyboard", _keys, _keyCount);
        Line(text, "mouse", _mouse, _mouseCount);
        File.WriteAllText(report, text.ToString());
        _written = _keyCount + _mouseCount;
    }

    private static void Line(StringBuilder text, string device, long[] all, int count)
    {
        if (count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{device},0,,,,");
            return;
        }
        var sorted = all.AsSpan(0, count).ToArray();
        Array.Sort(sorted);
        string At(double q) => Micro(sorted[Math.Min(count - 1, (int)Math.Ceiling(q * count) - 1)]);
        text.AppendLine(CultureInfo.InvariantCulture, $"{device},{count},{At(0.5)},{At(0.99)},{At(0.999)},{Micro(sorted[^1])}");
    }

    private static string Micro(long ticks) => (ticks * 1_000_000.0 / Stopwatch.Frequency).ToString("0.0", CultureInfo.InvariantCulture);
}
