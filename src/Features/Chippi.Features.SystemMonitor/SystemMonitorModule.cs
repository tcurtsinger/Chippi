// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Alerts;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Sensors.Platform;
using Chippi.Features.SystemMonitor.Views;
using Chippi.UI.Controls;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.SystemMonitor;

/// <summary>
/// System (Phase 4, the System monitor until 2026-10-31): an island tile whose status is one reading, and
/// a page of widgets that each open a detail page. The sampler reads only for what's on screen (the tile,
/// the page) and the alerts that are on.
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
    private MonitorAlerts? _alerts;
    private SystemView? _view;

    public SystemMonitorModule() : this(WindowsSources.Create) { }

    /// <param name="createSources">Called on each start and one-time notification discovery; each call must return fresh sources. Tests pass fakes.</param>
    public SystemMonitorModule(Func<SensorSources> createSources) => _createSources = createSources;

    public FeatureDescriptor Descriptor { get; } = new("system-monitor", "System", "CPU, GPU, memory, network, drives and battery, with alerts", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Activity),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
    };

    /// <summary>Fresh, read-only discovery for Notifications. No watch or sampler state is retained.</summary>
    public IReadOnlySet<string> AvailableNoticeKinds
    {
        get
        {
            var available = AlertEvaluator.Kinds.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
            using var sources = _createSources();
            try { if (sources.Battery.Read() is null) available.Remove("low-battery"); }
            catch (Exception error) { _context?.Logger.LogWarning(error, "Battery availability check failed"); }
            try { if (!sources.Gpu.Read(load: false).Any(gpu => gpu.TemperatureC is not null)) available.Remove("gpu-temperature"); }
            catch (Exception error) { _context?.Logger.LogWarning(error, "GPU availability check failed"); }
            return available;
        }
    }

    /// <summary>The settings in use, normalized.</summary>
    public SystemMonitorSettings Settings { get; private set; } = new();

    /// <summary>The running sampler; null while stopped.</summary>
    public SensorSampler? Sampler { get; private set; }


    /// <summary>GPUs that have reported load this session; the GPU card lists only these (design notes #13).</summary>
    internal HashSet<string> GpusWithLoad { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last readings a page showed beyond CPU and memory, which the next page opens with until its own arrive.</summary>
    internal SystemSnapshot? PageSnapshot { get; set; }

    /// <summary>Raised after <see cref="Save"/>, on the caller's thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether an alert that's on is past its threshold for <paramref name="target"/> (a GPU or
    /// volume ID): its value turns Caution on the page (design notes #18).
    /// </summary>
    internal bool Alerting(string kind, string target) =>
        _alerts?.Current.Conditions.Any(c => c.Kind == kind && string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase)) == true;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        Settings = context.Settings.Get<SystemMonitorSettings>().Normalized();
        Sampler = new SensorSampler(_createSources(), log: context.Logger) { Interval = Settings.UpdateInterval };
        Sampler.Updated += OnUpdated;
        context.Ui.SetSettingsPage(() => new SystemMonitorSettingsPage(this));
        // The alerts watch only what their switched-on alerts need; while one holds, its reading is
        // the tile's status in Caution (design notes #4).
        _alerts = new MonitorAlerts(Sampler, context.Notifier, context.Island, Settings);
        _alerts.Changed += OnAlertsChanged;
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(TileKey, Descriptor.Name, nameof(LucideIcons.Activity), Order: 60, SectionOrder: 0, View)
        {
            CreateHeader = () => View().Header,
            HandleBack = () => _view?.Back() == true, // a detail page returns to the overview
            HandleEscape = () => _view?.Back() == true,
        });
        _tile.OnScreenChanged += OnTileScreen;
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
        if (_alerts is not null)
        {
            _alerts.Changed -= OnAlertsChanged;
            _alerts.Dispose(); // before the sampler, which it watches
            _alerts = null;
        }
        if (Sampler is not null)
        {
            Sampler.Updated -= OnUpdated;
            Sampler.Dispose();
            Sampler = null;
        }
        _tile = null;
        _view = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>The island page, built once and kept, as the island keeps every page.</summary>
    private SystemView View() => _view ??= new SystemView(this);

    /// <summary>Opens Settings on this feature's page (the island page's empty state).</summary>
    public void OpenSettings() => _context?.OpenSettings(null);

    /// <summary>Saves and applies <paramref name="settings"/> (Settings › System).</summary>
    public void Save(SystemMonitorSettings settings)
    {
        Settings = settings.Normalized();
        _context?.Settings.Save(Settings);
        if (Sampler is { } sampler)
            sampler.Interval = Settings.UpdateInterval;
        _alerts?.Configure(Settings);
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

    // The alerts evaluate after the tile on each snapshot, so the tile looks again once they have.
    private void OnAlertsChanged(AlertEvaluation evaluation)
    {
        if (Sampler?.Latest is { } latest)
            UpdateTile(latest);
    }

    // A reading this snapshot didn't read (the tile off screen) leaves the status; one that was read
    // but has no value (failed, a rate starting over, an unplugged adapter) clears it: never a stale number.
    private void UpdateTile(SystemSnapshot snapshot)
    {
        if (_alerts?.Current.First is { } alert)
            _tile?.SetStatus(alert.TileText);
        else if (Settings.EffectiveTileStatus() is not { } shown)
            _tile?.SetStatus("");
        else if (TileStatusOf(snapshot, shown) != ReadingStatus.NotRead)
            _tile?.SetStatus(TileText(snapshot, Settings, Settings.GpuId, Settings.NetworkAdapterId) ?? "");
    }

    private static ReadingStatus TileStatusOf(SystemSnapshot snapshot, TileStatus shown) => shown switch
    {
        TileStatus.Memory => snapshot.Memory.Status,
        TileStatus.Gpu => snapshot.Gpus.Status,
        TileStatus.Network => snapshot.Network.Status,
        _ => snapshot.Cpu.Status,
    };

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;
        Sampler?.Resumed();
        _alerts?.Resumed(); // resume never counts as sustained
    }
}
