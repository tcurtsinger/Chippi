// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Island.Tests;

/// <summary>
/// The capture layer's session paths that show no window: the reader fails before any overlay is shown, and the island
/// door's fake return window is 0, so nothing takes the foreground.
/// </summary>
public sealed class CaptureLayerTests
{
    [Fact]
    public void A_read_that_throws_after_a_delay_closes_the_capture_as_failed() => WpfTestThread.Run(() =>
    {
        var shell = new Shell();
        var time = new StepTime();
        var layer = new CaptureLayer(shell, new ThrowingReader(), time);
        var session = layer.Start(new CaptureRequest(CaptureMode.Region) { Delay = 3, Door = CaptureDoor.Island });
        Assert.NotNull(session);
        CaptureEnd? end = null;
        session.Closed += e => end = e;
        IslandPanelTests.Pump(TimeSpan.FromMilliseconds(50));
        Assert.Equal("Capturing in 3", shell.Lines[^1]?.Title);

        for (int i = 0; i < 3; i++)
            time.Fire();
        IslandPanelTests.Pump(TimeSpan.FromMilliseconds(100));

        Assert.Equal(CaptureEnd.Failed, end);
        Assert.Null(layer.Current);
        Assert.True(shell.Lines[^1] is { Title: "Capture failed", Failed: true });

        // The next starter runs: nothing was left stuck.
        Assert.NotNull(layer.Start(new CaptureRequest(CaptureMode.Region) { Delay = 3, Door = CaptureDoor.Island }));
        layer.Current!.Close(CaptureEnd.Cancelled, handBack: false);
    });

    private sealed class Shell : ICaptureShell
    {
        public List<CaptureBarLine?> Lines { get; } = [];

        public nint IslandReturnWindow() => 0;

        public Task CloseIslandAsync() => Task.CompletedTask;

        public void Bar(CaptureBarLine? line) => Lines.Add(line);
    }

    private sealed class ThrowingReader : IScreenReader
    {
        public IReadOnlyList<MonitorBounds> Monitors() => throw new InvalidOperationException("No displays");

        public byte[]? Read(PixelRect bounds) => throw new InvalidOperationException("No pixels");
    }

    /// <summary>One timer whose ticks a test fires by hand.</summary>
    private sealed class StepTime : TimeProvider
    {
        private TimerCallback? _callback;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            return new Timer();
        }

        public void Fire() => _callback?.Invoke(null);

        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
