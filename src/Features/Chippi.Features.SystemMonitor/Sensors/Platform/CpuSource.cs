// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// "% Processor Utility" (Task Manager's CPU figure) for the total and each logical processor, and the
/// effective clock from "Processor Frequency" × "% Processor Performance".
/// </summary>
internal sealed class CpuSource : ICpuSource
{
    private PdhQuery? _query;
    private nint _utility, _performance, _frequency;
    private readonly RateClock _clock = new();

    /// <summary>The query is opened on the first read, on the sampler thread; that read is Pending.</summary>
    public CpuReading? Read(bool perProcessor)
    {
        if (!Collect())
            return null;
        var query = _query!;
        var utility = query.Values(_utility);
        if (utility.FirstOrDefault(u => u.Name == "_Total").Value is not { } total)
            return null;
        // Instances are "group,index"; "_Total" and "0,_Total" are totals.
        var processors = utility.Where(u => !u.Name.Contains("_Total", StringComparison.Ordinal))
            .Select(u => (Key: Key(u.Name), u.Value)).OrderBy(u => u.Key).ToList();
        double? performance = query.Values(_performance).FirstOrDefault().Value;
        double? frequency = query.Values(_frequency).FirstOrDefault().Value;
        double? mhz = performance > 0 && frequency > 0 ? frequency * performance / 100 : null;
        int count = processors.Count > 0 ? processors.Count : Environment.ProcessorCount;
        IReadOnlyList<double>? each = perProcessor && processors.All(p => p.Value is not null)
            ? [.. processors.Select(p => p.Value!.Value)]
            : null;
        return new CpuReading(total, count, mhz, each);
    }

    public void Dispose() => _query?.Dispose();

    /// <summary>Collects a sample; true when the one before it gives a current rate.</summary>
    private bool Collect()
    {
        if (_query is null)
        {
            var query = new PdhQuery();
            try
            {
                _utility = query.Add(@"\Processor Information(*)\% Processor Utility");
                _performance = query.Add(@"\Processor Information(_Total)\% Processor Performance");
                _frequency = query.Add(@"\Processor Information(_Total)\Processor Frequency");
            }
            catch
            {
                query.Dispose();
                throw;
            }
            _query = query;
        }
        _query.Collect();
        return _clock.Collected();
    }

    private static (int Group, int Index) Key(string name)
    {
        int comma = name.IndexOf(',');
        return comma < 0
            ? (0, Parse(name))
            : (Parse(name[..comma]), Parse(name[(comma + 1)..]));
    }

    private static int Parse(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;
}

/// <summary>
/// When a source was last collected. A rate over a long pause (nothing watched for a while) isn't
/// current, so the first collection after one is only a new start.
/// </summary>
internal sealed class RateClock
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    private long _last = long.MinValue;

    /// <summary>Records a collection; true when the one before it was recent enough to give a rate.</summary>
    public bool Collected()
    {
        long now = Environment.TickCount64;
        bool fresh = _last != long.MinValue && now - _last <= StaleAfter.TotalMilliseconds;
        _last = now;
        return fresh;
    }
}
