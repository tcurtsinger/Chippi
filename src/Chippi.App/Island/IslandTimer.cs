// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;

namespace Chippi.App.Island;

/// <summary>A one-shot delay on the island's thread: the 250 ms drag open and the 500 ms drag close. Its tick stops it first.</summary>
internal interface IIslandTimer
{
    void Start();

    void Stop();
}

/// <summary>The real timer, a <see cref="DispatcherTimer"/> that stops before it ticks.</summary>
internal sealed class DispatcherIslandTimer : IIslandTimer
{
    private readonly DispatcherTimer _timer;

    public DispatcherIslandTimer(TimeSpan interval, Action tick)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            tick();
        };
    }

    public void Start()
    {
        _timer.Stop(); // a restart counts from now
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
