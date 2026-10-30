// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.Core.Modules;

namespace Chippi.Island.Tests;

/// <summary>
/// The island on a screen without a window: places the panel's tab on an edge of a <paramref name="width"/> ×
/// <paramref name="height"/> DIP screen with a 48 DIP taskbar, then checks the one 520 × 360 size, its 496 × 304
/// body, and that the island stays in the work area (DESIGN.md §5 Island, revised 2026-10-28).
/// </summary>
internal static class IslandBounds
{
    public const double Taskbar = 48;

    /// <summary>Puts the tab mid-edge on <paramref name="edge"/>, as IslandWindow would before opening.</summary>
    public static void Place(IslandPanelWindow panel, IslandEdge edge, double width = 1280, double height = 720)
    {
        var work = new Rect(0, 0, width, height - Taskbar);
        Set(panel, "_edge", edge);
        Set(panel, "_area", new MonitorArea(default, new Rect(0, 0, width, height), work, 1));
        Set(panel, "_window", work);
        Set(panel, "_tab", edge switch
        {
            IslandEdge.Left => new Rect(0, work.Height / 2 - 56, 32, 112),
            IslandEdge.Right => new Rect(width - 32, work.Height / 2 - 56, 32, 112),
            _ => new Rect(width / 2 - 56, 0, 112, 32),
        });
    }

    /// <summary>The island is 520 × 360 with a 496 × 304 body, and it sits inside the work area.</summary>
    public static Rect Check(IslandPanelWindow panel, IslandEdge edge, double width = 1280, double height = 720, bool body = true)
    {
        panel.GetType().GetMethod("Reshape", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [true]);
        var shape = Get<Rect>(panel, "_target");
        Assert.Equal(IslandPage.Width, shape.Width);
        Assert.Equal(IslandPage.Height, shape.Height);
        var footprint = shape;
        Assert.InRange(footprint.Left, 0, width);
        Assert.InRange(footprint.Right, 0, width);
        Assert.InRange(footprint.Top, 0, height - Taskbar);
        Assert.InRange(footprint.Bottom, 0, height - Taskbar);
        if (body)
        {
            panel.Card.Measure(new Size(IslandPage.Width, IslandPage.Height));
            panel.Card.Arrange(new Rect(0, 0, IslandPage.Width, IslandPage.Height));
            var page = Assert.IsAssignableFrom<FrameworkElement>(panel.PageBody);
            Assert.Equal(IslandPage.BodyWidth, page.ActualWidth, 3);
            Assert.Equal(IslandPage.BodyHeight, page.ActualHeight, 3);
        }
        return footprint;
    }

    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);

    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
