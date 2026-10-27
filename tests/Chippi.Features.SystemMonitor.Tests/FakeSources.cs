// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>Every source as a settable function, counting reads. Set a function to throw to fail a read.</summary>
internal sealed class FakeSources :
    ICpuSource, IMemorySource, IGpuSource, INetworkSource, IDiskSource, IBatterySource, ITemperatureSource
{
    private int _cpuReads, _memoryReads, _gpuReads, _networkReads;

    public Func<bool, CpuReading?> Cpu { get; set; } = _ => new CpuReading(25, 4, 3600);
    public Func<MemoryReading> Memory { get; set; } = () => new MemoryReading(8, 32, 10, 40);
    public Func<bool, IReadOnlyList<GpuReading>> Gpu { get; set; } = load => [new GpuReading("PCI\\VEN_10DE&DEV_2704\\1", "GPU", Load: load ? 40 : null, TemperatureC: 50)];
    public Func<IReadOnlyList<NetworkReading>> Network { get; set; } = () => [new NetworkReading("{A}", "Ethernet", true, 1000, 500)];
    public Func<IReadOnlyList<DiskReading>?> Disks { get; set; } = () => [new DiskReading(0, 5, 0, 0, [new VolumeReading("\\\\?\\Volume{1}\\", "C:", 50, 100)])];
    public Func<BatteryReading?> Battery { get; set; } = () => null;
    public Func<IReadOnlyList<ThermalZoneReading>> Zones { get; set; } = () => [];

    public int CpuReads => Volatile.Read(ref _cpuReads);
    public int MemoryReads => Volatile.Read(ref _memoryReads);
    public int GpuReads => Volatile.Read(ref _gpuReads);
    public int NetworkReads => Volatile.Read(ref _networkReads);
    public bool Disposed { get; private set; }

    public SensorSources Sources => new(this, this, this, this, this, this, this);

    CpuReading? ICpuSource.Read(bool perProcessor)
    {
        Interlocked.Increment(ref _cpuReads);
        return Cpu(perProcessor);
    }

    MemoryReading IMemorySource.Read()
    {
        Interlocked.Increment(ref _memoryReads);
        return Memory();
    }

    IReadOnlyList<GpuReading> IGpuSource.Read(bool load)
    {
        Interlocked.Increment(ref _gpuReads);
        return Gpu(load);
    }

    IReadOnlyList<NetworkReading> INetworkSource.Read()
    {
        Interlocked.Increment(ref _networkReads);
        return Network();
    }

    IReadOnlyList<DiskReading>? IDiskSource.Read() => Disks();

    BatteryReading? IBatterySource.Read() => Battery();

    IReadOnlyList<ThermalZoneReading> ITemperatureSource.Read() => Zones();

    public void Dispose() => Disposed = true;
}
