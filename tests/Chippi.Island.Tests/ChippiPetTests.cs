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
    public void Wide_awake_steams_at_every_size_but_the_small_form_unless_a_badge_takes_the_corner()
    {
        // Every mood has its sign in the top corner: the z's, the steam, the sparks, the headphones' band.
        WpfTestThread.Run(() =>
        {
            foreach (var (size, stroke) in new[] { (28.0, 4.4), (40.0, 4.4), (44.0, 4.4), (48.0, 3.4), (72.0, 3.4) })
            {
                var pet = new ChippiPet { Size = size, Mood = PetMood.WideAwake };
                Assert.Equal(Visibility.Visible, pet.Steam.Visibility);
                Assert.Equal(1, pet.Steam.Opacity);
                Assert.Equal(stroke, pet.Steam.StrokeThickness); // heavier in the puck form, as its other strokes
            }
            var page = new ChippiPet { Size = 72, Mood = PetMood.WideAwake };
            var secondary = (SolidColorBrush)Application.Current.FindResource("IslandTextSecondaryBrush");
            Assert.Equal(secondary.Color, ((SolidColorBrush)page.Steam.Stroke).Color);
            page.Mood = PetMood.Idle;
            Assert.Equal(0, page.Steam.Opacity);
            Assert.Equal(Visibility.Collapsed, new ChippiPet { Size = 16, Mood = PetMood.WideAwake }.Steam.Visibility);

            // The lifted puck's Keep awake badge takes that corner: the host hides the steam meanwhile.
            var puck = new ChippiPet { Size = 40, Mood = PetMood.WideAwake, ShowsSteam = false };
            Assert.Equal(Visibility.Collapsed, puck.Steam.Visibility);
            puck.ShowsSteam = true;
            Assert.Equal(Visibility.Visible, puck.Steam.Visibility);
        });
    }

    [Fact]
    public void His_body_sits_in_the_middle_of_his_box_firmly_shaded_and_rimmed()
    {
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72, PetColor = PetColor.Frost };
            var drawing = (Canvas)((Viewbox)pet.Content).Child;
            var lift = Assert.IsType<TranslateTransform>(drawing.RenderTransform);
            var body = ChippiPet.Shapes.Body.Bounds;
            Assert.Equal(60, body.Y + body.Height / 2 + lift.Y, 3); // the body's middle is the box's, at any size
            Assert.Equal(60, body.X + body.Width / 2, 3);
            // Lifted, nothing he draws leaves the box: the sparks' and the small z's tops stay in it.
            Assert.True(ChippiPet.Shapes.Sparks.Bounds.Top + lift.Y >= 0);
            Assert.True(Zs(new ChippiPet { Size = 72, Mood = PetMood.Sleepy }).Min(z => z.Bounds.Top) + lift.Y >= 0);

            // His color to 78% of it, top to bottom, with the eye whites' faint ink rim, so Frost keeps his edge.
            var fill = Assert.IsType<LinearGradientBrush>(pet.Body.Fill);
            var frost = ChippiPet.ColorOf(PetColor.Frost);
            Assert.Equal(frost, fill.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb((byte)(frost.R * 0.78), (byte)(frost.G * 0.78), (byte)(frost.B * 0.78)), fill.GradientStops[1].Color);
            Assert.Equal(Color.FromArgb(0x2E, 0x0B, 0x0F, 0x1F), ((SolidColorBrush)pet.Body.Stroke).Color);
            Assert.Equal(2, pet.Body.StrokeThickness);
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
    public void The_hop_squishes_from_the_bottom_of_the_body_and_the_sparks_follow_the_theme()
    {
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72 };
            Assert.Equal((60.0, 106.0), (pet.Squish.CenterX, pet.Squish.CenterY)); // the body's bottom center, as the breath
            Assert.Equal(0, pet.Hop.Y); // on the ground at rest
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

    [Fact]
    public void He_breathes_at_10_frames_a_second_on_the_puck_and_30_above()
    {
        // The puck sleeps all day; at 30 a second its breath cost about 5% of a core for about a pixel of movement.
        WpfTestThread.Run(() =>
        {
            foreach (var size in new[] { 16.0, 28, 40, 44 })
                Assert.Equal(ChippiPet.BreathFrameRatePuck, new ChippiPet { Size = size }.BreathRate);
            foreach (var size in new[] { 48.0, 72, 96, 120 })
                Assert.Equal(ChippiPet.BreathFrameRate, new ChippiPet { Size = size }.BreathRate);
            Assert.Equal(10, ChippiPet.BreathFrameRatePuck);
        });
    }

    [Fact]
    public void Held_his_eyes_go_round_over_any_mood_and_set_down_his_face_comes_back()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var mood in new[] { PetMood.Sleepy, PetMood.Idle, PetMood.WideAwake, PetMood.Listening })
            {
                var pet = new ChippiPet { Size = 40, Mood = mood };
                pet.IsHeld = true;
                Assert.Equal(1, pet.HeldFace.Opacity);
                Assert.Equal(0, pet.ShownFace.Opacity);
                var held = Paths(pet.HeldFace).Select(p => p.Data).ToList();
                Assert.Equal(new[] { ChippiPet.Shapes.WideEyeWhites, ChippiPet.Shapes.WidePupils, ChippiPet.Shapes.WideHighlights, ChippiPet.Shapes.HeldMouth }, held);

                pet.Mood = mood == PetMood.Idle ? PetMood.Sleepy : PetMood.Idle; // a mood change while held keeps the round eyes
                Assert.Equal(1, pet.HeldFace.Opacity);
                Assert.Equal(0, pet.ShownFace.Opacity);

                pet.IsHeld = false;
                Assert.Equal(0, pet.HeldFace.Opacity);
                Assert.Equal(1, pet.ShownFace.Opacity);
            }
        });
    }

    [Fact]
    public void A_size_that_isnt_an_edge_is_refused_where_its_set()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var size in new[] { -1, double.NaN, double.PositiveInfinity })
                Assert.Throws<ArgumentException>(() => new ChippiPet { Size = size });
            var none = new ChippiPet { Size = 0 };
            Assert.Equal(0, none.Width);
        });
    }

    [Fact]
    public void The_yawn_squeezes_his_eyes_and_opens_his_mouth_tall()
    {
        AssertBounds(new Rect(40, 62, 40, 10), ChippiPet.Shapes.SqueezedEyes);
        AssertBounds(new Rect(53.5, 79, 13, 18), ChippiPet.Shapes.YawnMouth); // 6.5 × 9 at (60, 88)
        AssertBounds(new Rect(56, 91.1, 8, 4.8), ChippiPet.Shapes.YawnTongue); // at the mouth's floor
        AssertBounds(new Rect(56.8, 84.2, 6.4, 7.6), ChippiPet.Shapes.HeldMouth); // 3.2 × 3.8 at (60, 88)
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
    public void Celebrating_he_hops_and_lands_and_under_reduced_motion_stays_put()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72, Mood = PetMood.Idle };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                pet.Celebrate();
                Assert.True(pet.Hop.HasAnimatedProperties);
                Assert.True(pet.Squish.HasAnimatedProperties);

                // The curves, read at their key times rather than by the wall clock: on the ground until take-off, 12 up
                // at the top, down by 360ms; squished, stretched, round, landing squished, settled.
                var leap = ChippiPet.Leap(0, -ChippiPet.HopHeight);
                foreach (var (ms, y) in new[] { (50, 0.0), (100, 0.0), (260, -ChippiPet.HopHeight), (360, 0.0), (450, 0.0) })
                    Assert.Equal(y, At(leap, ms), 3);
                var wide = ChippiPet.HopScale(1.10, 0.94, 1.06);
                foreach (var (ms, x) in new[] { (100, 1.10), (200, 0.94), (260, 1.0), (360, 1.0), (380, 1.06), (499, 1.0) })
                    Assert.Equal(x, At(wide, ms), 2);

                Motion.Override = true;
                IslandWindowTests.Pump(TimeSpan.Zero);
                pet.Celebrate();
                Assert.False(pet.Hop.HasAnimatedProperties);
                Assert.False(pet.Squish.HasAnimatedProperties);
                Assert.Equal(1, pet.Sparks.Opacity); // the sparks just show
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void Keep_awake_perks_him_up_and_when_it_ends_he_yawns_before_he_sleeps()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72, Mood = PetMood.Idle };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);

                // Perking up: a stretch tall, and the steam rising into place.
                pet.Mood = PetMood.WideAwake;
                Assert.True(pet.Squish.HasAnimatedProperties);
                Assert.True(pet.SteamDrift.HasAnimatedProperties);

                // Keep awake ends: a yawn, not the plain falling asleep.
                pet.Mood = PetMood.Sleepy;
                Assert.True(pet.YawnMouth.HasAnimatedProperties);

                // From idle he just falls asleep.
                var idle = new ChippiPet { Size = 72, Mood = PetMood.Idle };
                window.Content = idle;
                IslandWindowTests.Pump(TimeSpan.Zero);
                idle.Mood = PetMood.Sleepy;
                Assert.False(idle.YawnMouth.HasAnimatedProperties);

                // A timed Keep awake runs out: he celebrates, then yawns as he falls asleep after it.
                var timed = new ChippiPet { Size = 72, Mood = PetMood.WideAwake };
                window.Content = timed;
                IslandWindowTests.Pump(TimeSpan.Zero);
                timed.Celebrate();
                timed.Mood = PetMood.Sleepy; // the celebration over
                Assert.True(timed.YawnMouth.HasAnimatedProperties);

                // Under reduced motion, none of it.
                Motion.Override = true;
                IslandWindowTests.Pump(TimeSpan.Zero);
                var still = new ChippiPet { Size = 72, Mood = PetMood.Idle };
                window.Content = still;
                IslandWindowTests.Pump(TimeSpan.Zero);
                still.Mood = PetMood.WideAwake;
                still.Mood = PetMood.Sleepy;
                Assert.False(still.Squish.HasAnimatedProperties);
                Assert.False(still.YawnMouth.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void Reduce_motion_coming_on_mid_glance_mid_beat_or_mid_yawn_snaps_him_to_rest()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var idle = new ChippiPet { Size = 40, Mood = PetMood.Idle };
            var listening = new ChippiPet { Size = 40, Mood = PetMood.Listening };
            var yawning = new ChippiPet { Size = 72, Mood = PetMood.WideAwake };
            var window = Offscreen(new StackPanel { Children = { idle, listening, yawning } });
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                var pupils = idle.Pupils!;
                idle.Glance(-1);
                listening.Beat(1);
                yawning.Mood = PetMood.Sleepy;
                Assert.True(pupils.HasAnimatedProperties);
                Assert.True(listening.BandPulse.HasAnimatedProperties);
                Assert.True(yawning.YawnMouth.HasAnimatedProperties);

                Motion.Override = true;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.False(pupils.HasAnimatedProperties);
                Assert.Equal(0, pupils.X);
                Assert.False(listening.BandPulse.HasAnimatedProperties);
                Assert.Equal((1.0, 1.0), (listening.BandPulse.ScaleX, listening.BandPulse.ScaleY));
                Assert.False(yawning.YawnMouth.HasAnimatedProperties);
                Assert.Equal(0, yawning.YawnFace.Opacity);
                Assert.Equal(1, yawning.ShownFace.Opacity); // asleep, at once
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void He_glances_boops_bobs_to_the_beat_and_lands_and_under_reduced_motion_holds_still()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 40, Mood = PetMood.Idle };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);

                // Open eyes glance; his pupils move, his whites don't.
                pet.Glance(-1);
                Assert.True(pet.Pupils!.HasAnimatedProperties);

                // Not listening, a beat does nothing.
                pet.Beat(1);
                Assert.False(pet.BandPulse.HasAnimatedProperties);

                pet.Boop();
                Assert.True(pet.Squish.HasAnimatedProperties);

                // Listening: a glance is a nod, and a beat dips him and pulses the band.
                var listening = new ChippiPet { Size = 40, Mood = PetMood.Listening };
                window.Content = listening;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.Null(listening.Pupils);
                listening.Glance(1);
                Assert.True(listening.Squish.HasAnimatedProperties);
                listening.Beat(0.8);
                Assert.True(listening.BandPulse.HasAnimatedProperties);

                // Set down, he lands.
                var held = new ChippiPet { Size = 40, Mood = PetMood.Idle };
                window.Content = held;
                IslandWindowTests.Pump(TimeSpan.Zero);
                held.IsHeld = true;
                Assert.False(held.Squish.HasAnimatedProperties);
                held.IsHeld = false;
                Assert.True(held.Squish.HasAnimatedProperties);

                // Under reduced motion: no glance, no beat, no squish.
                Motion.Override = true;
                IslandWindowTests.Pump(TimeSpan.Zero);
                var still = new ChippiPet { Size = 40, Mood = PetMood.Listening };
                window.Content = still;
                IslandWindowTests.Pump(TimeSpan.Zero);
                still.Glance(-1);
                still.Beat(1);
                still.Boop();
                Assert.False(still.Squish.HasAnimatedProperties);
                Assert.False(still.BandPulse.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void Locked_or_with_the_display_off_he_rests_and_seen_again_he_breathes_and_blinks()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var asleep = new ChippiPet { Size = 40, Mood = PetMood.Sleepy };
            var idle = new ChippiPet { Size = 40, Mood = PetMood.Idle };
            var window = Offscreen(new StackPanel { Children = { asleep, idle } });
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.True(asleep.IsBreathing);
                Assert.True(idle.IsBlinking);

                Motion.IsUnseen = true;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.False(asleep.IsBreathing);
                Assert.False(asleep.Breath.HasAnimatedProperties);
                Assert.False(idle.IsBlinking);
                Assert.Equal(1, asleep.ShownFace.Opacity); // still asleep, just still

                Motion.IsUnseen = false;
                IslandWindowTests.Pump(TimeSpan.Zero);
                Assert.True(asleep.IsBreathing);
                Assert.True(idle.IsBlinking);
            }
            finally
            {
                Motion.IsUnseen = false;
                window.Close();
                Motion.Override = motion;
            }
        });
    }

    [Fact]
    public void One_face_shows_at_a_time_through_a_boop_a_yawn_and_being_picked_up()
    {
        var motion = Motion.Override;
        WpfTestThread.Run(() =>
        {
            var pet = new ChippiPet { Size = 72, Mood = PetMood.Idle };
            var window = Offscreen(pet);
            try
            {
                Motion.Override = false;
                window.Show();
                IslandWindowTests.Pump(TimeSpan.Zero);

                // Picked up while he's falling asleep: the closing face goes with the rest, not 600ms later.
                pet.Mood = PetMood.Sleepy;
                pet.IsHeld = true;
                Assert.Equal(1, pet.FacesShown);
                Assert.Equal(1, pet.HeldFace.Opacity);
                pet.IsHeld = false;
                Assert.Equal(1, pet.FacesShown);
                Assert.Equal(1, pet.ShownFace.Opacity);

                // Booped mid-yawn: the yawn goes as the grin comes in, and his face comes back after it.
                pet.Mood = PetMood.Idle;
                IslandWindowTests.Pump(TimeSpan.FromMilliseconds(200));
                pet.Yawn();
                IslandWindowTests.Pump(TimeSpan.FromMilliseconds(50));
                Assert.Equal(1, pet.YawnFace.Opacity);
                pet.Boop();
                IslandWindowTests.Pump(TimeSpan.FromMilliseconds(250));
                Assert.Equal(1, pet.FacesShown);
                Assert.Equal(0, pet.YawnFace.Opacity);
                IslandWindowTests.Pump(TimeSpan.FromMilliseconds(600));
                Assert.Equal(1, pet.FacesShown);
                Assert.Equal(1, pet.ShownFace.Opacity);

                // A yawn while the grin still shows takes its place.
                pet.Boop();
                IslandWindowTests.Pump(TimeSpan.FromMilliseconds(250));
                pet.Yawn();
                IslandWindowTests.Pump(TimeSpan.FromMilliseconds(50));
                Assert.Equal(1, pet.FacesShown);
                Assert.Equal(1, pet.YawnFace.Opacity);

                // Picked up mid-yawn: the round eyes alone.
                pet.IsHeld = true;
                Assert.Equal(1, pet.FacesShown);
                Assert.Equal(0, pet.YawnFace.Opacity);
                pet.IsHeld = false;

                // Listening, a beat waits out a yawn, and a beat that isn't a number does nothing.
                var listening = new ChippiPet { Size = 40, Mood = PetMood.Listening };
                window.Content = listening;
                IslandWindowTests.Pump(TimeSpan.Zero);
                listening.Beat(double.NaN);
                Assert.False(listening.BandPulse.HasAnimatedProperties);
                listening.Yawn();
                listening.Beat(1);
                Assert.False(listening.BandPulse.HasAnimatedProperties);
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

    /// <summary>An animation's value <paramref name="ms"/> into it, from a clock sought there rather than run.</summary>
    private static double At(System.Windows.Media.Animation.AnimationTimeline animation, int ms)
    {
        var clock = animation.CreateClock();
        clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(ms), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
        return (double)animation.GetCurrentValue(0.0, 1.0, clock);
    }
}
