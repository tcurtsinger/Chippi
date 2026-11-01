// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.UI.Pet;
using Chippi.Core.Config;
using Chippi.UI.Themes;

namespace Chippi.Island.Tests;

public sealed class ChippiPetTests
{
    [Fact]
    public void A_pet_inside_a_host_takes_its_color_unless_given_one()
    {
        // The panel sets the color once; a feature page's Chippi (Keep awake's) picks it up without hearing of the setting.
        WpfTestThread.Run(() =>
        {
            var page = new ChippiPet();
            var own = new ChippiPet { PetColor = PetColor.Synth };
            var host = new StackPanel { Children = { new Border { Child = page }, own } };
            ChippiPet.SetPetColor(host, PetColor.Iris);
            Assert.Equal(PetColor.Iris, page.PetColor);
            Assert.Equal(PetColor.Synth, own.PetColor); // a color set on the pet itself wins
        });
    }

    [Fact]
    public void Every_mood_color_and_size_has_the_named_pet()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var color in Enum.GetValues<PetColor>())
                foreach (var size in new[] { 20.0, 24, 32, 40, 48, 72 })
                    foreach (var mood in Enum.GetValues<PetMood>())
                    {
                        var pet = new ChippiPet { Size = size, PetColor = color, Mood = mood };
                        var peer = UIElementAutomationPeer.CreatePeerForElement(pet)!;
                        Assert.Equal("ChippiPet", peer.GetClassName());
                        Assert.Equal($"Chippi, {ChippiPet.Describe(mood)}", peer.GetName());
                        Assert.Equal(size, pet.Width);
                    }
        });
    }

    [Fact]
    public void The_puck_form_draws_one_big_z_and_above_it_two()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var size in new[] { 23.0, 28, 36, 40, 44 })
                Assert.Single(Zs(new ChippiPet { Size = size, Mood = PetMood.Sleepy }));
            foreach (var size in new[] { 44.5, 48, 72 })
                Assert.Equal(2, Zs(new ChippiPet { Size = size, Mood = PetMood.Sleepy }).Count);

            // Resizing across the line redraws them.
            var pet = new ChippiPet { Size = 48, Mood = PetMood.Sleepy };
            pet.Size = 40;
            var one = Assert.Single(Zs(pet));
            pet.Size = 72;
            var two = Zs(pet);
            Assert.Equal(2, two.Count);

            // The one z (30px) stands taller than the bigger of the two (22px), which stands taller than the other (14px).
            Assert.True(one.Bounds.Height > two[0].Bounds.Height);
            Assert.True(two[0].Bounds.Height > two[1].Bounds.Height);

            // In the theme's secondary island text, not a fixed gray.
            var secondary = (SolidColorBrush)Application.Current.FindResource("IslandTextSecondaryBrush");
            Assert.Equal(secondary.Color, ((SolidColorBrush)pet.ZGlyphs.Fill).Color);
        });
    }

    [Fact]
    public void The_puck_form_strokes_heavier()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var (size, asleep, happy) in new[] { (40.0, 4.6, 4.8), (28.0, 4.6, 4.8), (48.0, 3.8, 4.2), (72.0, 3.8, 4.2) })
            {
                Assert.Equal(asleep, Stroke(new ChippiPet { Size = size, Mood = PetMood.Sleepy }, ChippiPet.Shapes.SleepyEyes));
                Assert.Equal(happy, Stroke(new ChippiPet { Size = size, Mood = PetMood.Listening }, ChippiPet.Shapes.HappyEyes));
                Assert.Equal(happy, Stroke(new ChippiPet { Size = size, Mood = PetMood.Celebrating }, ChippiPet.Shapes.HappyEyes));
            }
        });
    }

    [Fact]
    public void Wide_awake_has_its_own_open_eyed_face()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var size in new[] { 40.0, 72 })
            {
                var parts = Paths(new ChippiPet { Size = size, Mood = PetMood.WideAwake }.ShownFace);
                var data = parts.Select(p => p.Data).ToList();
                Assert.Equal(new[] { ChippiPet.Shapes.WideEyeWhites, ChippiPet.Shapes.WidePupils, ChippiPet.Shapes.WideHighlights, ChippiPet.Shapes.WideMouth, ChippiPet.Shapes.WideLids }, data);
                Assert.DoesNotContain(ChippiPet.Shapes.HappyEyes, data); // no longer Listening's face without the headphones

                // White eyes with a faint ink rim, ink pupils and mouth, whatever the pet's color; the lids only show in a blink.
                Assert.Equal(Colors.White, Fill(parts[0]));
                Assert.Equal(Color.FromArgb(0x2E, 0x0B, 0x0F, 0x1F), ((SolidColorBrush)parts[0].Stroke).Color);
                Assert.Equal(Color.FromRgb(0x0B, 0x0F, 0x1F), Fill(parts[1]));
                Assert.Equal(Colors.White, Fill(parts[2]));
                Assert.Equal(Color.FromRgb(0x0B, 0x0F, 0x1F), Fill(parts[3]));
                Assert.Equal(0, parts[4].Opacity);
            }

            AssertBounds(new Rect(34, 51, 52, 28), ChippiPet.Shapes.WideEyeWhites);      // 12 × 14 at (46, 65) and (74, 65)
            AssertBounds(new Rect(41.6, 61.6, 36.8, 8.8), ChippiPet.Shapes.WidePupils); // r 4.4 at (46, 66) and (74, 66)
            AssertBounds(new Rect(46.1, 62.2, 31.2, 3.2), ChippiPet.Shapes.WideHighlights); // r 1.6 at (47.7, 63.8) and (75.7, 63.8)
            AssertBounds(new Rect(55.6, 84, 8.8, 10), ChippiPet.Shapes.WideMouth);       // 4.4 × 5 at (60, 89)
        });
    }

    [Fact]
    public void Wide_awake_steams_above_the_puck_form_only()
    {
        // The puck's corner is the Keep awake badge's, so the steam is for the bigger Chippi on Keep awake's page.
        WpfTestThread.Run(() =>
        {
            var page = new ChippiPet { Size = 72, Mood = PetMood.WideAwake };
            Assert.Equal(Visibility.Visible, page.Steam.Visibility);
            Assert.Equal(1, page.Steam.Opacity);
            var secondary = (SolidColorBrush)Application.Current.FindResource("IslandTextSecondaryBrush");
            Assert.Equal(secondary.Color, ((SolidColorBrush)page.Steam.Stroke).Color);

            page.Mood = PetMood.Idle;
            Assert.Equal(0, page.Steam.Opacity);
            foreach (var size in new[] { 16.0, 28, 40, 44 })
                Assert.Equal(Visibility.Collapsed, new ChippiPet { Size = size, Mood = PetMood.WideAwake }.Steam.Visibility);
        });
    }

    [Fact]
    public void The_small_form_has_the_tray_icons_eyes_and_never_the_headphones()
    {
        WpfTestThread.Run(() =>
        {
            var small = new ChippiPet { Size = 16, Mood = PetMood.Listening };
            var data = Paths(small.ShownFace).Select(p => p.Data).ToList();
            Assert.Equal(new[] { ChippiPet.Shapes.SmallEyeWhites, ChippiPet.Shapes.SmallEyes }, data);
            Assert.Equal(Visibility.Collapsed, small.Headphones.Visibility);

            small.Size = 40; // grown into the puck form, the headphones go on
            Assert.Equal(Visibility.Visible, small.Headphones.Visibility);
            small.Size = 22;
            Assert.Equal(Visibility.Collapsed, small.Headphones.Visibility);
        });
    }

    [Fact]
    public void A_celebration_during_a_song_keeps_the_headphones_on()
    {
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 40, Mood = PetMood.Listening };
            pet.Celebrate();
            Assert.Equal(PetMood.Celebrating, pet.Mood);
            Assert.Equal(Visibility.Visible, pet.Headphones.Visibility);

            var quiet = new ChippiPet { Size = 40, Mood = PetMood.Idle };
            quiet.Celebrate();
            Assert.Equal(Visibility.Collapsed, quiet.Headphones.Visibility);
        });
    }

    [Fact]
    public void The_squish_springs_from_the_bottom_of_the_body_and_the_sparks_follow_the_theme()
    {
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72 };
            Assert.Equal((60.0, 106.0), (pet.Squish.CenterX, pet.Squish.CenterY)); // the body's bottom center, as the breath
            Assert.Equal((60.0, 106.0), (pet.Breath.CenterX, pet.Breath.CenterY));
            Assert.IsType<RadialGradientBrush>(pet.Shadow.Fill); // a soft contact shadow, not a hard plinth

            // Gold on the dark island, a deeper amber on the light one.
            foreach (var (theme, color) in new[] { ("Dark", Color.FromRgb(0xF5, 0xC4, 0x51)), ("Light", Color.FromRgb(0xB0, 0x78, 0x00)) })
            {
                var themed = new ChippiPet { Size = 72, Mood = PetMood.Celebrating };
                var host = new Border { Child = themed };
                host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") });
                Assert.Equal(color, ((SolidColorBrush)themed.Sparks.Stroke).Color);
                Assert.Equal(1, themed.Sparks.Opacity);
            }
        });
    }

#if DEBUG
    [Fact]
    public void Render_approved_pet_sizes()
    {
        var output = Environment.GetEnvironmentVariable("CHIPPI_PET_SHOTS");
        if (output is not null)
            WpfTestThread.Run(() => Chippi.App.Pet.PetSheet.Save(output));
    }
#endif

    private static List<Geometry> Zs(ChippiPet pet) => [.. ((GeometryGroup)pet.ZGlyphs.Data).Children];

    private static double Stroke(ChippiPet pet, Geometry shape) => Paths(pet.ShownFace).Single(p => p.Data == shape).StrokeThickness;

    private static Color Fill(Shape shape) => ((SolidColorBrush)shape.Fill).Color;

    /// <summary>The face's shapes in drawing order: its eye layer's, then the rest.</summary>
    private static List<Path> Paths(Panel panel)
    {
        var paths = new List<Path>();
        foreach (var child in panel.Children)
        {
            if (child is Path path)
                paths.Add(path);
            else if (child is Panel inner)
                paths.AddRange(Paths(inner));
        }
        return paths;
    }

    private static void AssertBounds(Rect expected, Geometry geometry)
    {
        var actual = geometry.Bounds; // in single precision
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Width, actual.Width, 4);
        Assert.Equal(expected.Height, actual.Height, 4);
    }
}

/// <summary>The asleep pet's breathing loop. It reads and sets reduced motion, a process-wide setting, so it runs alone.</summary>
[Collection("ReducedMotion")]
public sealed class ChippiPetMotionTests
{
    [Fact]
    public void Breathes_only_while_asleep_shown_and_with_motion()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 40, Mood = PetMood.Sleepy };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.True(pet.IsLoaded);
                Assert.True(pet.IsBreathing);
                Assert.True(pet.Breath.HasAnimatedProperties);
                Assert.True(pet.ZGlyphs.HasAnimatedProperties); // the z drifts with it
                Assert.False(pet.IsBlinking);

                // Any other mood stops it; wide awake blinks instead.
                foreach (var mood in new[] { PetMood.Idle, PetMood.Listening, PetMood.WideAwake, PetMood.Celebrating })
                {
                    pet.Mood = PetMood.Sleepy;
                    Assert.True(pet.IsBreathing);
                    pet.Mood = mood;
                    Assert.False(pet.IsBreathing);
                    Assert.Equal(mood is PetMood.Idle or PetMood.WideAwake, pet.IsBlinking);
                }
                pet.Mood = PetMood.Sleepy;
                Assert.True(pet.IsBreathing);

                // Reduced motion turned on mid-breath stops it at rest; turned off, it starts again.
                Motion.Override = true;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.False(pet.IsBreathing);
                Assert.False(pet.Breath.HasAnimatedProperties);
                Assert.False(pet.ZGlyphs.HasAnimatedProperties);
                Assert.Equal((1.0, 1.0, 1.0), (pet.Breath.ScaleX, pet.Breath.ScaleY, pet.ZGlyphs.Opacity));
                pet.Mood = PetMood.Idle;
                pet.Mood = PetMood.Sleepy;
                Assert.False(pet.IsBreathing); // and asleep again under it, still not
                Motion.Override = false;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.True(pet.IsBreathing);

                // Hidden (a fullscreen app in front), it stops; shown again, it starts.
                window.Hide();
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.False(pet.IsBreathing);
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.True(pet.IsBreathing);

                // Unloaded, it stops.
                window.Content = null;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.False(pet.IsLoaded);
                Assert.False(pet.IsBreathing);
                Assert.False(pet.Breath.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void Under_reduced_motion_the_asleep_pet_never_breathes()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72, Mood = PetMood.Idle };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = true;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                pet.Mood = PetMood.Sleepy;
                Assert.False(pet.IsBreathing);
                Assert.False(pet.Breath.HasAnimatedProperties);
                Assert.Equal(1.0, pet.ZGlyphs.Opacity);
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void The_small_form_blinks_only_while_its_eyes_are_open()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 16, Mood = PetMood.Idle };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                foreach (var mood in Enum.GetValues<PetMood>())
                {
                    pet.Mood = mood;
                    Assert.Equal(mood is PetMood.Idle or PetMood.WideAwake, pet.IsBlinking);
                }
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    /// <summary>Off every screen and never activated: nothing shows and nothing takes focus.</summary>
    private static Window Offscreen(UIElement content) => new()
    {
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = -10000,
        Top = -10000,
        SizeToContent = SizeToContent.WidthAndHeight,
        Content = content,
    };
}
