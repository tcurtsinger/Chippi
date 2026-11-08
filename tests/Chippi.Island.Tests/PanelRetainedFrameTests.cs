// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Chippi.App.Island;
using Chippi.UI.Themes;

namespace Chippi.Island.Tests;

/// <summary>Uses only the panel's native hit mask, offscreen: no desktop capture, activation or input.</summary>
[Collection("ReducedMotion")]
public sealed class PanelRetainedFrameTests
{
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(System.Drawing.Point point);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Reopening_never_exposes_the_previous_native_frame(bool reduced, bool immediate) => WpfTestThread.Run(() =>
    {
        var motion = Motion.Override;
        var panel = new IslandPanelWindow();
        try
        {
            Motion.Override = reduced;
            panel.Initialize();
            var area = new MonitorArea(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);
            var puck = IslandGeometry.Start(area.Work, 1);
            panel.Open(puck, area);
            IslandWindowTests.Settle(panel);
            var target = panel.Footprint;
            var oldPage = new System.Drawing.Point((int)target.Left + 100, (int)target.Top + 30);
            // WPF may skip a fully offscreen target. Seed its native cache with an opaque fixture frame.
            PresentFixture(panel);
            Assert.Equal((nint)panel.Handle, WindowFromPoint(oldPage));

            long now = 0;
            int frames = 0;
            panel.Clock = () => now;
            panel.Morphing += (_, _) => frames++;
            panel.CloseIsland(immediate);
            if (!immediate)
            {
                IslandWindowTests.PumpUntil(() => frames >= 2);
                // One late close frame can hide the panel before WPF presents the small final outline.
                now += Stopwatch.Frequency;
                IslandWindowTests.PumpUntil(() => !panel.IsVisible);
            }
            Assert.False(panel.IsVisible);

            panel.Open(puck, area);
            Assert.True(panel.IsVisible);
            Assert.NotEqual((nint)panel.Handle, WindowFromPoint(oldPage));
            if (!reduced)
                Assert.Equal(puck.Size, new Size(panel.Shape.Width, panel.Shape.Height));

            // A fresh UpdateLayeredWindow, as WPF uses to present, must replace the zero alpha too.
            IslandWindowTests.Settle(panel);
            PresentFixture(panel);
            Assert.Equal((nint)panel.Handle, WindowFromPoint(oldPage));
        }
        finally
        {
            panel.Close();
            Motion.Override = motion;
        }
    });

    private static void PresentFixture(IslandPanelWindow panel)
    {
        var size = IslandNative.WindowRect(panel.Handle).Size;
        nint dc = CreateCompatibleDC(0);
        nint bitmap = CreateBitmap((int)size.Width, (int)size.Height, 1, 32, 0);
        Assert.NotEqual(0, dc);
        Assert.NotEqual(0, bitmap);
        nint previous = SelectObject(dc, bitmap);
        try
        {
            var source = new System.Drawing.Point();
            var blend = new Blend { Alpha = 255 };
            Assert.True(UpdateLayeredWindow((nint)panel.Handle, 0, 0, 0, dc, ref source, 0, ref blend, 2));
        }
        finally
        {
            SelectObject(dc, previous);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blend { public byte Operation, Flags, Alpha, Format; }

    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, uint planes, uint bits, nint pixels);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(nint hwnd, nint destinationDc, nint destination, nint size,
        nint sourceDc, ref System.Drawing.Point source, uint key, ref Blend blend, uint flags);
}
