// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Displays.Tests;

/// <summary>A display's handle changing, a display the bus can't control, and stopping with a monitor stuck in a call.</summary>
public sealed class DisplayHandleTests
{
    private static void Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(what);
            Thread.Sleep(2);
        }
    }

    [Fact]
    public void A_display_that_keeps_its_id_but_gets_a_new_handle_is_opened_with_the_new_one()
    {
        var bus = new DisplayBrightnessTests.FakeBus();
        bus.Monitors["A"] = new DisplayBrightnessTests.FakeMonitor("A");
        IReadOnlyList<Chippi.UI.Displays.DisplayInfo> list = [DisplayBrightnessTests.Info("A", "OMEN", 1, true)];
        using var engine = new DisplayBrightness(bus, () => list, () => 1);
        engine.Refresh();
        Until(() => engine.Statuses[0].Brightness is not null, "first read");
        Assert.Equal([(nint)1], bus.OpenedHandles.Distinct());

        list = [DisplayBrightnessTests.Info("A", "OMEN", 7, true)];
        engine.Refresh(reopen: true);
        Until(() => bus.OpenedHandles.Contains(7), "reopened with the new handle");
        Assert.Equal((nint)7, bus.OpenedHandles.Last());

        // A plain refresh (no reopen) still moves to a handle that changed.
        list = [DisplayBrightnessTests.Info("A", "OMEN", 9, true)];
        engine.Refresh();
        Until(() => bus.OpenedHandles.Contains(9), "reopened without being asked");
    }

    [Fact]
    public void Try_again_uses_the_handle_from_the_last_refresh()
    {
        var bus = new DisplayBrightnessTests.FakeBus();
        bus.Monitors["A"] = new DisplayBrightnessTests.FakeMonitor("A");
        IReadOnlyList<Chippi.UI.Displays.DisplayInfo> list = [DisplayBrightnessTests.Info("A", "OMEN", 1, true)];
        using var engine = new DisplayBrightness(bus, () => list, () => 1);
        engine.Refresh();
        Until(() => engine.Statuses[0].Brightness is not null, "first read");
        list = [DisplayBrightnessTests.Info("A", "OMEN", 4, true)];
        engine.Refresh();
        Until(() => bus.OpenedHandles.Contains(4), "new handle");
        engine.TryAgain("A");
        Until(() => bus.OpenedHandles.Count(h => h == 4) >= 2, "try again opened with the new handle");
        Assert.DoesNotContain((nint)0, bus.OpenedHandles);
    }

    [Fact]
    public void A_display_the_bus_cant_control_is_not_supported_and_never_opened()
    {
        var bus = new DisplayBrightnessTests.FakeBus();
        bus.Unsupported.Add("P");
        bus.Monitors["A"] = new DisplayBrightnessTests.FakeMonitor("A");
        bus.Monitors["P"] = new DisplayBrightnessTests.FakeMonitor("P");
        var panel = DisplayBrightnessTests.Info("P", "Built-in display", 2) with { IsInternal = true };
        using var engine = new DisplayBrightness(bus, () => [DisplayBrightnessTests.Info("A", "OMEN", 1, true), panel], () => 1);
        engine.Refresh();
        Until(() => engine.Statuses[0].Brightness is not null, "first read");
        var status = engine.Statuses.Single(s => s.Id == "P");
        Assert.True(status.NotSupported);
        Assert.False(status.Failed);
        Assert.Null(status.Brightness);
        Assert.DoesNotContain((nint)2, bus.OpenedHandles);
        Assert.Equal("OMEN 50%", engine.Tooltip);
        engine.Set("P", Vcp.Brightness, 10); // nothing to write to
        engine.Step(1, BrightnessTarget.AllDisplays);
        Assert.Empty(bus.Monitors["P"].Writes);
    }

    [Fact]
    public void Dispose_waits_once_for_monitors_stuck_in_a_call()
    {
        var bus = new DisplayBrightnessTests.FakeBus();
        var hold = new ManualResetEventSlim(false);
        foreach (string id in new[] { "A", "B", "C" })
            bus.Monitors[id] = new DisplayBrightnessTests.FakeMonitor(id) { ReadHold = hold };
        var engine = new DisplayBrightness(bus, () => [DisplayBrightnessTests.Info("A", "A", 1, true), DisplayBrightnessTests.Info("B", "B", 2), DisplayBrightnessTests.Info("C", "C", 3)], () => 1);
        engine.Refresh();
        Until(() => bus.Monitors.Values.All(m => m.ReadStarted.IsSet), "reads started");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        engine.Dispose();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Dispose took {clock.Elapsed.TotalSeconds:F1} s");
        hold.Set();
    }
}
