// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
            timer.Restart();
            foreach (var target in targets.Where(t => t.PackageFullName.Length == 0).Take(6))
                RegisteredAppTargets.NeedsAdminShortcut(target);
            long adminChecks = timer.ElapsedMilliseconds;
            File.WriteAllText(report + ".admin.txt", $"Six Start menu safety checks: {adminChecks} ms\n");
            var windows = new WindowsWindowList();
            timer.Restart();
            var cold = windows.Read(includeOtherDesktops: true);
            long firstRead = timer.ElapsedMilliseconds;
            timer.Restart();
            var warm = windows.Read(includeOtherDesktops: true);
            long warmRead = timer.ElapsedMilliseconds;
            Assert.All(warm, w => { Assert.NotEqual((nint)0, w.Handle); Assert.NotEqual((uint)Environment.ProcessId, w.ProcessId); });
            int shellGhosts = 0;
            var manager = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!)!;
            try
            {
                var native = new List<nint>();
                Assert.True(EnumWindows((window, _) => { native.Add(window); return true; }, 0));
                foreach (nint window in native)
                {
                    if (DwmGetWindowAttribute(window, 14, out uint cloak, sizeof(uint)) == 0 && cloak == 2
                        && manager.IsWindowOnCurrentVirtualDesktop(window, out bool current) == 0 && current)
                    {
                        shellGhosts++;
                        Assert.DoesNotContain(warm, w => w.Handle == window);
                    }
                }
            }
            finally { Marshal.ReleaseComObject(manager); }
            using var pc = new WindowsActionPc();
            var desktop = new Chippi.App.Shell.ArrangementDesktop(pc, () => Chippi.Core.Input.PauseRules.Default, () => false);
            desktop.PrepareAsync().GetAwaiter().GetResult();
            timer.Restart();
            var capture = Chippi.Features.WindowLayouts.ArrangementCaptureBuilder.Capture(desktop.Windows());
            long captureTime = timer.ElapsedMilliseconds;
            Assert.All(capture.Rows, r => Assert.NotNull(r.Placement));
            File.WriteAllText(report, $"Registered apps: {targets.Count}\nResolved: {resolved}\nCatalog: {catalog} ms\n"
                + $"Cold windows: {cold.Count}, {firstRead} ms\nWarm windows: {warm.Count}, {warmRead} ms\n"
                + $"Capture: {capture.Rows.Count} rows, {capture.Skipped.Count} skipped, {captureTime} ms\n"
                + $"Shell-cloaked current-desktop windows independently checked and excluded: {shellGhosts}\n");
        });
    }
    private delegate bool EnumWindow(nint window, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindow callback, nint data);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, uint size);
    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }
}
