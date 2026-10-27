// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Sensors.Platform;
using Chippi.Features.SystemMonitor.Views;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.SystemMonitor;

/// <summary>
/// The System monitor (Phase 4): an island tile whose status is one reading, and a page of every
/// reading. The sampler reads only for what's on screen (the tile, the page) and the alerts that are on.
/// </summary>
public sealed class SystemMonitorModule : IFeatureModule
{
    /// <summary>The island tile's key and the notice-click verb's page.</summary>
    public const string TileKey = "system-monitor";

    /// <summary>A notice click's verb (<c>--system-monitor</c>): opens the page.</summary>
    public const string OpenVerb = "system-monitor";

    private readonly Func<SensorSources> _createSources;
    private readonly Lock _gate = new();
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private SensorWatch? _tileWatch;

    public SystemMonitorModule() : this(WindowsSources.Create) { }

    /// <param name="createSources">Called on each start; tests pass fakes.</param>
    public SystemMonitorModule(Func<SensorSources> createSources) => _createSources = createSources;

    public FeatureDescriptor Descriptor { get; } = new("system-monitor", "System monitor", "CPU, GPU, memory, network and battery, with alerts", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Activity),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
        Badges = [FeatureBadge.Overlap("Overlaps Task Manager")],
    };

    /// <summary>The settings in use, normalized.</summary>
    public SystemMonitorSettings Settings { get; private set; } = new();

    /// <summary>The running sampler; null while stopped.</summary>
    public SensorSampler? Sampler { get; private set; }

    /// <summary>Whether the page's logical processor strip is open: for the session only (design notes #12).</summary>
    internal bool ProcessorsOpen { get; set; }

    /// <summary>GPUs that have reported load this session; the GPU card lists only these (design notes #13).</summary>
    internal HashSet<string> GpusWithLoad { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after <see cref="Save"/>, on the caller's thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether an alert that's on is past its threshold for <paramref name="target"/> (a GPU or
    /// volume ID): its value turns Caution on the page (design notes #18).
    /// </summary>
    internal bool Alerting(string kind, string target) => false;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        Settings = context.Settings.Get<SystemMonitorSettings>().Normalized();
        Sampler = new SensorSampler(_createSources(), log: context.Logger) { Interval = Settings.UpdateInterval };
        Sampler.Updated += OnUpdated;
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(TileKey, Descriptor.Name, nameof(LucideIcons.Activity), Order: 60, SectionOrder: 0,
            () => new SystemMonitorView(this))
        {
            PageWidth = SystemMonitorView.PageWidth,
        });
        _tile.OnScreenChanged += OnTileScreen;
        _tile.SetPageNote(Format.Span(Settings.UpdateInterval));
        context.Activation.Register(OpenVerb, _ => context.OpenIslandPage(TileKey));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_tile is not null)
            _tile.OnScreenChanged -= OnTileScreen;
        lock (_gate)
        {
            _tileWatch?.Dispose();
            _tileWatch = null;
        }
        if (Sampler is not null)
        {
            Sampler.Updated -= OnUpdated;
            Sampler.Dispose();
            Sampler = null;
        }
        _tile = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>Saves and applies <paramref name="settings"/> (Settings › System monitor).</summary>
    public void Save(SystemMonitorSettings settings)
    {
        Settings = settings.Normalized();
        _context?.Settings.Save(Settings);
        if (Sampler is { } sampler)
            sampler.Interval = Settings.UpdateInterval;
        _tile?.SetPageNote(Format.Span(Settings.UpdateInterval));
        lock (_gate)
        {
            if (_tileWatch is not null)
                _tileWatch.Need = TileNeed(Settings);
        }
        if (Sampler?.Latest is { } latest)
            UpdateTile(latest);
        Changed?.Invoke();
    }

    /// <summary>What the tile's status needs read; none when no reading it can show is on.</summary>
    internal static SensorNeed TileNeed(SystemMonitorSettings settings) => settings.EffectiveTileStatus() switch
    {
        TileStatus.Cpu => SensorNeed.Cpu,
        TileStatus.Memory => SensorNeed.Memory,
        TileStatus.Gpu => SensorNeed.GpuLoad,
        TileStatus.Network => SensorNeed.Network,
        _ => SensorNeed.None,
    };

    /// <summary>The tile's status for <paramref name="snapshot"/>: "CPU 12%", "↓ 1.2 MiB/s"; null when the snapshot doesn't have it.</summary>
    internal static string? TileText(SystemSnapshot snapshot, SystemMonitorSettings settings, string? gpuId, string? adapterId) =>
        settings.EffectiveTileStatus() switch
        {
            TileStatus.Cpu => snapshot.Cpu.Value is { } cpu ? "CPU " + Format.Percent(cpu.Utility) : null,
            TileStatus.Memory => snapshot.Memory.Value is { } memory ? "Memory " + Format.Percent(memory.UsedPercent) : null,
            TileStatus.Gpu => Pick.Gpu(snapshot.Gpus.Value, gpuId, null)?.Load is { } load ? "GPU " + Format.Percent(load) : null,
            TileStatus.Network => Pick.Adapter(snapshot.Network.Value, adapterId) is { Connected: true, ReceiveBytesPerSecond: { } rx }
                ? "↓ " + Format.Rate(rx, settings.NetworkSpeed)
                : null,
            _ => null,
        };

    private void OnTileScreen(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_tile is null || Sampler is null)
                return;
            if (_tile.OnScreen)
                _tileWatch ??= Sampler.Watch(TileNeed(Settings));
            else
            {
                _tileWatch?.Dispose();
                _tileWatch = null;
            }
        }
    }

    // On the sampler thread. A reading not in this snapshot leaves the last status (a rate starting over).
    private void OnUpdated(SystemSnapshot snapshot) => UpdateTile(snapshot);

    private void UpdateTile(SystemSnapshot snapshot)
    {
        if (Settings.EffectiveTileStatus() is null)
            _tile?.SetStatus("");
        else if (TileText(snapshot, Settings, Settings.GpuId, Settings.NetworkAdapterId) is { } status)
            _tile?.SetStatus(status);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Sampler?.Resumed();
    }
}
