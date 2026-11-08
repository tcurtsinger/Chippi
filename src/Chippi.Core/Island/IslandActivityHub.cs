// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Island;

/// <summary>
/// Holds every live island activity and picks the one to show. Thread-safe; events run on the
/// publishing, clearing or timer thread, so UI subscribers marshal to their dispatcher.
/// </summary>
public sealed class IslandActivityHub(TimeProvider? time = null, TimeSpan? feedbackDuration = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _feedbackDuration = feedbackDuration ?? TimeSpan.FromSeconds(6);
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];
    private long _sequence;

    /// <summary>Raised after any publish, clear or feedback expiry.</summary>
    public event EventHandler? Changed;

    public event EventHandler<ModuleSucceededEventArgs>? Succeeded;

    /// <summary>The activity the live pill shows: highest priority, newest first within a priority. Null when resting.</summary>
    public IslandActivity? Current
    {
        get
        {
            lock (_gate) return Ordered().FirstOrDefault()?.Activity;
        }
    }

    /// <summary>All live activities, in display priority order.</summary>
    public IReadOnlyList<IslandActivity> Activities
    {
        get
        {
            lock (_gate) return [.. Ordered().Select(e => e.Activity)];
        }
    }

    /// <summary>A publisher whose activities are tagged with <paramref name="moduleId"/>. The debug menu can use its own id.</summary>
    public IIslandPublisher CreatePublisher(string moduleId) => new Publisher(this, moduleId);

    /// <summary>Removes everything a module published. The registry calls this after the module stops.</summary>
    public void ClearModule(string moduleId)
        => Remove(e => e.Activity.ModuleId == moduleId);

    private IEnumerable<Entry> Ordered() =>
        _entries.OrderBy(e => e.Activity.Priority()).ThenByDescending(e => e.Sequence);

    private void Publish(string moduleId, IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var tagged = activity with { ModuleId = moduleId };
        lock (_gate)
        {
            int i = _entries.FindIndex(e => e.Activity.ModuleId == moduleId && e.Activity.Key == activity.Key);
            var entry = new Entry(tagged, i >= 0 ? _entries[i].Sequence : ++_sequence);
            if (i >= 0)
            {
                _entries[i].Timer?.Dispose();
                _entries[i] = entry; // update in place, keep its age
            }
            else
                _entries.Add(entry);
            if (tagged.IsFeedback)
            {
                entry.Timer = _time.CreateTimer(_ => Remove(e => ReferenceEquals(e, entry)), null,
                    Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                entry.Timer.Change(_feedbackDuration, Timeout.InfiniteTimeSpan);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Clear(string moduleId, string key)
        => Remove(e => e.Activity.ModuleId == moduleId && e.Activity.Key == key);

    private void Remove(Predicate<Entry> matches)
    {
        bool removed;
        lock (_gate)
            removed = _entries.RemoveAll(e =>
            {
                if (!matches(e)) return false;
                e.Timer?.Dispose();
                return true;
            }) > 0;
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Entry(IslandActivity Activity, long Sequence)
    {
        public ITimer? Timer { get; set; }
    }

    private sealed class Publisher(IslandActivityHub hub, string moduleId) : IIslandPublisher
    {
        public void Publish(IslandActivity activity) => hub.Publish(moduleId, activity);

        public void Clear(string key) => hub.Clear(moduleId, key);

        public void ReportSuccess(string? message = null) =>
            hub.Succeeded?.Invoke(hub, new ModuleSucceededEventArgs(moduleId, message));
    }
}
