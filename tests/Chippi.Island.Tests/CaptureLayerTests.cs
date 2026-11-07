// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
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

    [Fact]
    public async Task Use_from_a_background_thread_makes_the_overlays_on_the_UI_thread()
    {
        // Modules start off the UI thread once an earlier one has awaited (ModuleRegistry, ConfigureAwait(false)).
        CaptureLayer? layer = null;
        Dispatcher? ui = null;
        WpfTestThread.Run(() => { layer = new CaptureLayer(new Shell(), new OneDisplay()); ui = Dispatcher.CurrentDispatcher; });
        // A release off the UI thread is queued there; this waits for the queue to run.
        void Settle() => ui!.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var lease = await Task.Run(() => layer!.Use());
        var second = await Task.Run(() => layer!.Use());

        WpfTestThread.Run(() => Assert.Equal(1, layer!.OverlayCount));
        await Task.Run(lease.Dispose);
        Settle();
        WpfTestThread.Run(() => Assert.Equal(1, layer!.OverlayCount));
        await Task.Run(second.Dispose);
        Settle();
        WpfTestThread.Run(() => Assert.Equal(0, layer!.OverlayCount));
    }

    [Fact]
    public void A_first_use_that_fails_counts_nothing_and_the_next_use_builds() => WpfTestThread.Run(() =>
    {
        var reader = new OneDisplay { FailFirst = true };
        var layer = new CaptureLayer(new Shell(), reader);

        Assert.Throws<InvalidOperationException>(() => layer.Use());
        Assert.Equal(0, layer.OverlayCount);

        var lease = layer.Use();
        Assert.Equal(1, layer.OverlayCount);
        lease.Dispose();
        Assert.Equal(0, layer.OverlayCount);
    });

    /// <summary>One display; its overlay is made with a handle but never shown.</summary>
    private sealed class OneDisplay : IScreenReader
    {
        public bool FailFirst { get; set; }

        public IReadOnlyList<MonitorBounds> Monitors()
        {
            if (FailFirst)
            {
                FailFirst = false;
                throw new InvalidOperationException("No displays yet");
            }
            return [new MonitorBounds(1, new PixelRect(0, 0, 320, 200), 96, true)];
        }

        public byte[]? Read(PixelRect bounds) => null;
    }

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
