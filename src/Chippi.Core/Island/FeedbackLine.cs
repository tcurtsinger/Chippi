// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Island;

/// <summary>A result line on the island's bar that clears itself after the notice time; a new line restarts the time.</summary>
public sealed class FeedbackLine(IIslandPublisher island, string key, TimeProvider time, TimeSpan after) : IDisposable
{
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public void Show(IslandActivity line)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Dispose();
            island.Publish(line);
            _timer = time.CreateTimer(_ => island.Clear(key), null, after, Timeout.InfiniteTimeSpan);
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
