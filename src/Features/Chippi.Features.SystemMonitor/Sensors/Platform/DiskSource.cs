// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// Physical disks from the PhysicalDisk counters (instances "0 C: D:"): active time and transfer
/// rates, with each lettered volume's free space.
/// </summary>
internal sealed class DiskSource : IDiskSource
{
    private PdhQuery? _query;
    private nint _idle, _read, _write;
    private readonly RateClock _clock = new();

    /// <summary>Takes the first sample now, so the first read has rates. A failure here is retried on read.</summary>
    public DiskSource()
    {
        try
        {
            Collect();
        }
        catch (PdhException)
        {
        }
    }

    public IReadOnlyList<DiskReading>? Read()
    {
        // Every counter here is a rate: the instance list itself only comes with the second sample.
        if (!Collect())
            return null;
        var idle = Values(_idle);
        var read = Values(_read);
        var write = Values(_write);
        var disks = new List<DiskReading>();
        foreach (var name in idle.Keys.Union(read.Keys).Union(write.Keys))
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                continue;
            double? active = idle.GetValueOrDefault(name) is { } i ? Math.Clamp(100 - i, 0, 100) : null;
            var volumes = parts.Skip(1).Select(Volume).OfType<VolumeReading>().ToList();
            disks.Add(new DiskReading(number, active, read.GetValueOrDefault(name), write.GetValueOrDefault(name), volumes));
        }
        disks.Sort((a, b) => a.Number.CompareTo(b.Number));
        return disks;
    }

    public void Dispose() => _query?.Dispose();

    private bool Collect()
    {
        if (_query is null)
        {
            var query = new PdhQuery();
            try
            {
                _idle = query.Add(@"\PhysicalDisk(*)\% Idle Time");
                _read = query.Add(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
                _write = query.Add(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
            }
            catch
            {
                query.Dispose();
                throw;
            }
            _query = query;
        }
        return _query.Collect() & _clock.Collected();
    }

    // An instance without a value yet (a disk just attached) is still listed, with null rates.
    private Dictionary<string, double?> Values(nint counter) =>
        _query!.Values(counter).Where(v => v.Name != "_Total")
            .GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.First().Value);

    /// <summary>A letter's volume, or null when it can't be read (a locked or removed volume).</summary>
    private static VolumeReading? Volume(string letter)
    {
        if (letter.Length != 2 || letter[1] != ':' || !char.IsAsciiLetter(letter[0]))
            return null;
        string root = letter + "\\";
        // A drive without media must fail quietly, not show Windows' "insert a disk" box.
        _ = SetThreadErrorMode(FailCriticalErrors, out uint previous);
        try
        {
            if (!GetDiskFreeSpaceExW(root, out _, out ulong total, out ulong free) || total == 0)
                return null;
            var guid = new StringBuilder(64);
            string id = GetVolumeNameForVolumeMountPointW(root, guid, (uint)guid.Capacity) ? guid.ToString() : root;
            return new VolumeReading(id, letter.ToUpperInvariant(), free, total);
        }
        finally
        {
            _ = SetThreadErrorMode(previous, out _);
        }
    }

    private const uint FailCriticalErrors = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint mode, out uint previous);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong freeToCaller, out ulong total, out ulong free);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder name, uint length);
}
