// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Island;

/// <summary>
/// Holds every live island activity and picks the one to show. Thread-safe; events are raised
/// on the publishing thread, so UI subscribers marshal to their dispatcher.
/// </summary>
public sealed class IslandActivityHub
{
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];
    private long _sequence;

    /// <summary>Raised after any publish or clear.</summary>
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
    {
        bool removed;
        lock (_gate) removed = _entries.RemoveAll(e => e.Activity.ModuleId == moduleId) > 0;
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<Entry> Ordered() =>
        _entries.OrderBy(e => e.Activity.Kind.Priority()).ThenByDescending(e => e.Sequence);

    private void Publish(string moduleId, IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var tagged = activity with { ModuleId = moduleId };
        lock (_gate)
        {
            int i = _entries.FindIndex(e => e.Activity.ModuleId == moduleId && e.Activity.Key == activity.Key);
            if (i >= 0)
                _entries[i] = _entries[i] with { Activity = tagged }; // update in place, keep its age
            else
                _entries.Add(new Entry(tagged, ++_sequence));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Clear(string moduleId, string key)
    {
        bool removed;
        lock (_gate) removed = _entries.RemoveAll(e => e.Activity.ModuleId == moduleId && e.Activity.Key == key) > 0;
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Entry(IslandActivity Activity, long Sequence);

    private sealed class Publisher(IslandActivityHub hub, string moduleId) : IIslandPublisher
    {
        public void Publish(IslandActivity activity) => hub.Publish(moduleId, activity);

        public void Clear(string key) => hub.Clear(moduleId, key);

        public void ReportSuccess(string? message = null) =>
            hub.Succeeded?.Invoke(hub, new ModuleSucceededEventArgs(moduleId, message));
    }
}
