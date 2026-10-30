// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.UI.Themes;
namespace Chippi.Island.Tests;

/// <summary>The island follows the app theme and keeps the pet's violet (shell redesign, PR 1).</summary>
[Collection("ReducedMotion")] // switches the app theme, which every other test reads
public sealed class IslandThemeTests
{
    [Fact]
    public void An_open_panel_follows_a_theme_change_and_points_shared_keys_at_the_island_ones() => WpfTestThread.Run(() =>
    {
        var start = WpfTestThread.ShotsTheme == "Light" ? ThemePreference.Light : ThemePreference.Dark;
        var panel = new IslandPanelWindow();
        try
        {
            foreach (var (theme, surface, accent, onAccent) in new[]
            {
                (ThemePreference.Light, "#F5F5F5F6", "#FF7444F0", "#FFFFFFFF"),
                (ThemePreference.Dark, "#F528282C", "#FFB79CFF", "#FF170F2E"),
            })
            {
                ThemeManager.Apply(theme);
                Assert.Equal(surface, Color(panel.FindResource("IslandBrush")));
                // Module views and Chippi.UI templates inside the island ask for these.
                Assert.Equal(accent, Color(panel.FindResource("AccentBrush")));
                Assert.Equal(onAccent, Color(panel.FindResource("OnAccentBrush")));
                Assert.Equal(onAccent, Color(panel.FindResource("SegmentActiveBrush")));
                Assert.IsType<LinearGradientBrush>(panel.FindResource("SegmentActiveBgBrush"));
                // The light theme lifts a selected segment with a shadow; on the island's accent fill it doesn't.
                Assert.Null(panel.TryFindResource("SegmentSelectedShadow"));
            }
        }
        finally
        {
            panel.Close();
            ThemeManager.Apply(start);
        }
    });

    private static string Color(object brush) => ((SolidColorBrush)brush).Color.ToString();
}
