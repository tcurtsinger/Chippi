// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>
/// A slow read (a WinRT device query, the Recycle Bin on every drive) kept between page reads and redone at most once per
/// <paramref name="every"/>. Callers share one load in flight; a failed load is retried on the next read after the interval.
/// </summary>
internal sealed class Refreshed<T>(Func<Task<T>> load, TimeSpan every)
{
    private readonly Lock _gate = new();
    private Task<T>? _value;
    private long _loadedAt;

    public Task<T> GetAsync()
    {
        lock (_gate)
        {
            if (_value is null || (_value.IsCompleted && Stopwatch.GetElapsedTime(_loadedAt) > every))
            {
                _loadedAt = Stopwatch.GetTimestamp();
                _value = Task.Run(load);
            }
            return _value;
        }
    }

    /// <summary>Loads again on the next read, even while a load is in flight (it may have started before the change).</summary>
    public void Expire()
    {
        lock (_gate)
            _value = null;
    }
}
