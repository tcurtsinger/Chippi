// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Island;

/// <summary>A result line on the island's bar that clears itself after the notice time; a new line restarts the time.</summary>
public sealed class FeedbackLine(IIslandPublisher island, string key, TimeProvider time, TimeSpan after) : IDisposable
{
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;
    private int _generation;

    public void Show(IslandActivity line)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Dispose();
            island.Publish(line);
            int mine = ++_generation;
            _timer = time.CreateTimer(_ => Expire(mine), null, after, Timeout.InfiniteTimeSpan);
        }
    }

    // A callback already queued when its timer was replaced must not clear the newer line.
    internal void Expire(int generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation) return;
            island.Clear(key);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
