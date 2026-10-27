// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Notifications;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Alerts;

/// <summary>
/// Connects alert demand and policy to a sampler, the existing notifier and persistent island Caution.
/// Does not own the sampler. Dispose before the sampler. Call Resumed together with SensorSampler.Resumed.
/// </summary>
public sealed class MonitorAlerts : IDisposable
{
    private const string ActivityKey = "system-monitor-alert";
    private readonly Lock _gate = new();
    private readonly SensorSampler _sampler;
    private readonly INotifier _notifier;
    private readonly IIslandPublisher _island;
    private readonly AlertEvaluator _evaluator;
    private readonly SensorWatch _watch;
    private SystemMonitorSettings _settings;
    private bool _disposed;
    private AlertEvaluation _current = new([], []);

    /// <summary>Raised on the calling/sampler thread. UI consumers marshal to their dispatcher.</summary>
    public event Action<AlertEvaluation>? Changed;

    public MonitorAlerts(SensorSampler sampler, INotifier notifier, IIslandPublisher island,
        SystemMonitorSettings settings, TimeProvider? time = null)
    {
        _sampler = sampler;
        _notifier = notifier;
        _island = island;
        _settings = settings.Normalized();
        _evaluator = new(time);
        _notifier.Declare(AlertEvaluator.Kinds);
        _watch = sampler.Watch(SensorNeed.None);
        sampler.Updated += Updated;
        _watch.Need = AlertEvaluator.Need(_settings);
    }

    public AlertEvaluation Current { get { lock (_gate) return _current; } }

    public void Configure(SystemMonitorSettings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _settings = settings.Normalized();
            Clear();
            _watch.Need = AlertEvaluator.Need(_settings);
        }
    }

    public void Resumed()
    {
        lock (_gate)
        {
            if (!_disposed) Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sampler.Updated -= Updated;
            _watch.Dispose();
            Clear();
        }
    }

    private void Clear()
    {
        _evaluator.ResetSustain();
        _current = new([], []);
        _island.Clear(ActivityKey);
        Changed?.Invoke(_current);
    }

    private void Updated(SystemSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _current = _evaluator.Evaluate(snapshot, _settings);
            if (_current.First is { } first)
                _island.Publish(new(ActivityKey, IslandActivityKind.Status, first.TileText, "System monitor")
                    { NeedsAttention = true, TileKey = SystemMonitorModule.TileKey });
            else
                _island.Clear(ActivityKey);
            foreach (var notice in _current.Notices)
                _notifier.Show(new(notice.Kind, notice.Text)
                {
                    IslandKind = IslandActivityKind.Status, NeedsAttention = true,
                    Activation = AlertEvaluator.Activation,
                });
            Changed?.Invoke(_current);
        }
    }
}
