// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using Chippi.UI.Actions;
using Chippi.Island.Tests;

namespace Chippi.Island.Tests;

/// <summary>Opt-in read-only native discovery probe. It never launches, activates, moves or closes a window.</summary>
public sealed class ArrangementDiscoveryTests
{
    [Fact]
    public void Registered_targets_and_shared_window_list_read_without_desktop_input()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ARRANGEMENTS_DISCOVERY") is not { Length: > 0 } report) return;
        WpfTestThread.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            var targets = RegisteredAppTargets.ReadAll();
            long catalog = timer.ElapsedMilliseconds;
            Assert.NotEmpty(targets);
            Assert.All(targets, t => Assert.False(string.IsNullOrWhiteSpace(t.Identity)));
            int resolved = 0;
            foreach (var target in targets)
                if (RegisteredAppTargets.Resolve(target.Identity) is { } actual)
                { Assert.Equal(target.Identity, actual.Identity); resolved++; }
            Assert.Equal(targets.Count, resolved);
            var windows = new WindowsWindowList();
            timer.Restart();
            var cold = windows.Read(includeOtherDesktops: true);
            long firstRead = timer.ElapsedMilliseconds;
            timer.Restart();
            var warm = windows.Read(includeOtherDesktops: true);
            long warmRead = timer.ElapsedMilliseconds;
            Assert.All(warm, w => { Assert.NotEqual((nint)0, w.Handle); Assert.NotEqual((uint)Environment.ProcessId, w.ProcessId); });
            using var pc = new WindowsActionPc();
            var desktop = new Chippi.App.Shell.ArrangementDesktop(pc, () => Chippi.Core.Input.PauseRules.Default, () => false);
            desktop.PrepareAsync().GetAwaiter().GetResult();
            timer.Restart();
            var capture = Chippi.Features.WindowLayouts.ArrangementCaptureBuilder.Capture(desktop.Windows());
            long captureTime = timer.ElapsedMilliseconds;
            Assert.All(capture.Rows, r => Assert.NotNull(r.Placement));
            File.WriteAllText(report, $"Registered apps: {targets.Count}\nResolved: {resolved}\nCatalog: {catalog} ms\n"
                + $"Cold windows: {cold.Count}, {firstRead} ms\nWarm windows: {warm.Count}, {warmRead} ms\n"
                + $"Capture: {capture.Rows.Count} rows, {capture.Skipped.Count} skipped, {captureTime} ms\n");
        });
    }
}
