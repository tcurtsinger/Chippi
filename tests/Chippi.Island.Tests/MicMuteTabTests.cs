// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.Core.Island;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>Unshown windows only, no HWND initialization or desktop input.</summary>
public sealed class MicMuteTabTests
{
    [Fact]
    public void Muted_glyph_stays_after_the_top_activity_and_disappears_when_unmuted()
    {
        WpfTestThread.Run(() =>
        {
            var window = new IslandWindow();
            try
            {
                var music = new IslandActivity("music", IslandActivityKind.Media, "Track", "Player");
                var muted = new IslandActivity("muted", IslandActivityKind.Status, "Muted", "Microphone")
                    { ModuleId = "mic-mute", NeedsAttention = true, TileKey = "sound.microphone" };
                window.SetActivities([music, muted], PetMood.Idle);
                var glyph = Assert.IsType<LucideIcon>(window.FindName("MutedMicGlyph"));
                Assert.Equal(Visibility.Visible, glyph.Visibility);
                Assert.Same(LucideIcons.MicOff, glyph.Data);
                Assert.Equal(Visibility.Visible, Assert.IsAssignableFrom<FrameworkElement>(window.FindName("Equalizer")).Visibility);
                Assert.Equal(IslandGeometry.TabSize(Chippi.Core.Config.IslandEdge.Top, live: true), window.TabTarget);
                window.SetActivities([music, muted with { Title = "Some microphones muted" }], PetMood.Idle);
                Assert.Equal("Some microphones muted", AutomationProperties.GetHelpText((DependencyObject)window.FindName("Tab")));
                window.SetActivities([music], PetMood.Idle);
                Assert.Equal(Visibility.Collapsed, glyph.Visibility);
            }
            finally { window.Close(); }
        });
    }
}