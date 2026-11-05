// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Shortcuts;
using System.Windows;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.RadialMenu.Wheel;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.RadialMenu.Tests;

/// <summary>The press, hold and flick, the wheel's pointing and submenus, keys and settings: logic alone, no windows or hooks.</summary>
public sealed class WheelTests
{
    // ----- The gesture (docs/phase-5.5/radial-menu.md, Opening it) -----

    private static Gesture Watching(params MouseButton[] buttons) => new(buttons.Contains, slop: 4);

    private static readonly PixelPoint At = new(500, 400);

    [Fact]
    public void A_held_button_opens_the_wheel_where_it_was_pressed()
    {
        var gesture = Watching(MouseButton.Middle);
        var down = gesture.Down(MouseButton.Middle, At);
        Assert.Equal(new GestureStep(true, GestureEffect.StartHold, MouseButton.Middle, At), down);
        Assert.Equal(GestureEffect.Open, gesture.Elapsed(gesture.Press).Effect);
        Assert.True(gesture.IsOpen);
    }

    [Fact]
    public void A_quick_click_reaches_the_app_as_a_click()
    {
        var gesture = Watching(MouseButton.Back);
        gesture.Down(MouseButton.Back, At);
        var up = gesture.Up(MouseButton.Back, At);
        Assert.True(up.Keep);
        Assert.Equal(GestureEffect.ReplayClick, up.Effect);
        Assert.Equal(MouseButton.Back, up.Button);
        // The hold's timer that fires after it does nothing.
        Assert.Equal(GestureEffect.None, gesture.Elapsed(gesture.Press).Effect);
        Assert.True(gesture.IsIdle);
    }

    [Fact]
    public void A_press_that_moves_first_stays_the_apps_drag()
    {
        var gesture = Watching(MouseButton.Middle);
        gesture.Down(MouseButton.Middle, At);
        Assert.Equal(new GestureStep(false), gesture.Move(new PixelPoint(503, 397))); // within the slop
        var moved = gesture.Move(new PixelPoint(510, 400));
        Assert.False(moved.Keep);
        Assert.Equal(GestureEffect.ReplayDown, moved.Effect);
        Assert.Equal(GestureEffect.None, gesture.Elapsed(gesture.Press).Effect);
        Assert.False(gesture.IsIdle);
        // Its up goes to the app too, after the replayed down: kept here, and sent behind it.
        var up = gesture.Up(MouseButton.Middle, new PixelPoint(600, 400));
        Assert.Equal(new GestureStep(true, GestureEffect.ReplayUp, MouseButton.Middle, new PixelPoint(600, 400)), up);
        Assert.True(gesture.IsIdle);
        // The next up of that button is the app's own.
        Assert.False(gesture.Up(MouseButton.Middle, new PixelPoint(600, 400)).Keep);
    }

    [Fact]
    public void Buttons_that_dont_open_it_and_the_left_and_right_buttons_pass_through()
    {
        var gesture = Watching(MouseButton.Middle);
        Assert.False(gesture.Down(MouseButton.Back, At).Keep);
        Assert.False(gesture.Down(MouseButton.Left, At).Keep);
        Assert.False(gesture.Down(MouseButton.Right, At).Keep);
        Assert.False(gesture.Up(MouseButton.Left, At).Keep);
        Assert.True(gesture.IsIdle);
    }

    [Fact]
    public void Held_letting_go_releases_and_the_wheel_waits_for_a_click()
    {
        var gesture = Watching(MouseButton.Middle);
        gesture.Down(MouseButton.Middle, At);
        gesture.Elapsed(gesture.Press);
        Assert.Equal(GestureEffect.Move, gesture.Move(new PixelPoint(600, 400)).Effect);
        var release = gesture.Up(MouseButton.Middle, new PixelPoint(600, 400));
        Assert.Equal(new GestureStep(true, GestureEffect.Release, MouseButton.Middle, new PixelPoint(600, 400)), release);
        // Open, its clicks are Chippi's, down and up.
        var click = gesture.Down(MouseButton.Left, new PixelPoint(620, 400));
        Assert.Equal(GestureEffect.Click, click.Effect);
        Assert.True(click.Keep);
        gesture.Closed();
        Assert.True(gesture.Up(MouseButton.Left, new PixelPoint(620, 400)).Keep);
        Assert.True(gesture.IsIdle);
    }

    [Fact]
    public void Esc_and_a_right_click_close_it_and_stay_Chippis()
    {
        var gesture = Watching(MouseButton.Middle);
        gesture.OpenedByShortcut();
        Assert.Equal(new GestureStep(true, GestureEffect.Dismiss), gesture.Key(0x1B, down: true));
        Assert.True(gesture.Key(0x1B, down: false).Keep);
        Assert.False(gesture.Key(0x41, down: true).Keep);
        gesture.OpenedByShortcut();
        var right = gesture.Down(MouseButton.Right, At);
        Assert.Equal(GestureEffect.Dismiss, right.Effect);
        gesture.Closed();
        Assert.True(gesture.Up(MouseButton.Right, At).Keep);
        // Closed, Esc is the app's.
        Assert.False(gesture.Key(0x1B, down: true).Keep);
    }

    [Fact]
    public void Dismissed_while_held_the_buttons_up_stays_Chippis()
    {
        var gesture = Watching(MouseButton.Forward);
        gesture.Down(MouseButton.Forward, At);
        gesture.Elapsed(gesture.Press);
        gesture.Key(0x1B, down: true);
        gesture.Closed();
        Assert.True(gesture.Up(MouseButton.Forward, At).Keep);
        Assert.False(gesture.IsIdle); // Esc's up is still to come
        gesture.Key(0x1B, down: false);
        Assert.True(gesture.IsIdle);
    }

    [Fact]
    public void A_late_timer_for_an_earlier_press_does_nothing()
    {
        var gesture = Watching(MouseButton.Middle);
        gesture.Down(MouseButton.Middle, At);
        int first = gesture.Press;
        gesture.Up(MouseButton.Middle, At);
        gesture.Down(MouseButton.Middle, At);
        Assert.Equal(GestureEffect.None, gesture.Elapsed(first).Effect);
        Assert.Equal(GestureEffect.Open, gesture.Elapsed(gesture.Press).Effect);
    }

    // ----- The wheel (Picking) -----

    private static readonly Size Screen = new(1920, 1080);
    private static readonly Point Middle = new(960, 540);

    private static Slice Media(MediaAction action) => new() { Kind = SliceKind.Media, Value = action.ToString(), Name = ActionCatalog.NameOf(action) };

    private static Slice Window(WindowAction action) => new() { Kind = SliceKind.Window, Value = action.ToString(), Name = ActionCatalog.NameOf(action) };

    /// <summary>Eight slices: the right one a Window submenu of three, the bottom one an app that's gone.</summary>
    private static List<Slice> Eight() =>
    [
        Media(MediaAction.PlayPause),
        Media(MediaAction.Next),
        new() { Kind = SliceKind.Submenu, Name = "Window", Slices = [Window(WindowAction.SnapLeft), Window(WindowAction.Maximize), Window(WindowAction.SnapRight)] },
        Media(MediaAction.Previous),
        new() { Kind = SliceKind.App, Value = "Steam", Name = "Steam" },
        Media(MediaAction.Mute),
        Media(MediaAction.VolumeUp),
        Media(MediaAction.VolumeDown),
    ];

    private static WheelState Open(bool holding = true, List<Slice>? slices = null) =>
        new(Screen, Middle, slices ?? Eight(), holding, s => s.Value == "Steam");

    private static Point Toward(double degrees, double distance) => WheelGeometry.At(Middle, distance, degrees);

    [Fact]
    public void Held_direction_points_not_distance()
    {
        var wheel = Open();
        Assert.Equal(WheelOutcome.None, wheel.Move(Toward(45, 30))); // in the hub
        Assert.Null(wheel.Pointed);
        Assert.Equal(WheelOutcome.Changed, wheel.Move(Toward(45, 90)));
        Assert.Equal(1, wheel.Pointed);
        wheel.Move(Toward(50, 140)); // past the ring, the same way
        Assert.Equal(1, wheel.Pointed);
        Assert.Equal(("Next", HubTone.Name), wheel.Hub);
        Assert.Equal(WheelGeometry.Direction(45), wheel.Look);
    }

    [Fact]
    public void Letting_go_on_a_slice_runs_it()
    {
        var wheel = Open();
        wheel.Move(Toward(180 + 45, 100));
        Assert.Equal(WheelOutcome.Run, wheel.Release(Toward(180 + 45, 100)));
        Assert.Equal("Mute", wheel.PickedSlice!.Name);
        // Picked, nothing else moves it.
        Assert.Equal(WheelOutcome.None, wheel.Move(Toward(0, 100)));
    }

    [Fact]
    public void Letting_go_in_the_hub_keeps_it_open_for_a_click_on_the_ring_only()
    {
        var wheel = Open();
        Assert.Equal(WheelOutcome.Changed, wheel.Release(Toward(0, 20)));
        Assert.False(wheel.Holding);
        wheel.Move(Toward(0, 140)); // clicking, past the ring points at nothing
        Assert.Null(wheel.Pointed);
        Assert.Equal(WheelOutcome.None, wheel.Click(Toward(0, 20))); // the hub
        Assert.Equal(WheelOutcome.Run, wheel.Click(Toward(0, 100)));
        Assert.Equal("Play/pause", wheel.PickedSlice!.Name);
    }

    [Fact]
    public void A_click_off_the_wheel_closes_it()
    {
        var wheel = Open(holding: false);
        Assert.Equal(WheelOutcome.Close, wheel.Click(Toward(10, 200)));
    }

    [Fact]
    public void A_held_flick_past_a_submenu_opens_it_at_the_pointer_and_back_returns()
    {
        var wheel = Open();
        Assert.Equal(WheelOutcome.Changed, wheel.Move(Toward(90, 150)));
        Assert.Single(wheel.Levels);
        Assert.Equal(WheelOutcome.Changed, wheel.Move(Toward(90, 160)));
        Assert.Equal(2, wheel.Levels.Count);
        var sub = wheel.Top;
        Assert.Equal(Middle.X + WheelGeometry.SubmenuReach, sub.Middle.X, 3);
        Assert.Equal(270, sub.First, 3); // Back points back at the first wheel
        Assert.Equal(4, sub.Items.Count); // Back and three
        Assert.Null(sub.Items[0]);
        Assert.Equal(("Window", HubTone.Quiet), wheel.Hub);
        // Then down: the flick chains on into the submenu's slices.
        var below = WheelGeometry.At(sub.Middle, 90, 90);
        wheel.Move(below);
        Assert.Equal("Maximize", WheelState.NameOf(sub.Items[wheel.Pointed!.Value]!));
        // Past Back by 32: the first wheel again.
        Assert.Equal(WheelOutcome.Changed, wheel.Move(WheelGeometry.At(sub.Middle, 170, 270)));
        Assert.Single(wheel.Levels);
    }

    [Fact]
    public void A_crumb_click_goes_back_to_its_wheel()
    {
        var wheel = Open(holding: false);
        Assert.Equal(WheelOutcome.Changed, wheel.Click(Toward(90, 100)));
        Assert.Equal(2, wheel.Levels.Count);
        Assert.Equal(WheelOutcome.Changed, wheel.Click(Middle + new Vector(10, 0)));
        Assert.Single(wheel.Levels);
    }

    [Fact]
    public void A_gone_slice_says_Not_found_and_runs_nothing()
    {
        var wheel = Open();
        wheel.Move(Toward(180, 100));
        Assert.Equal(("Not found", HubTone.Caution), wheel.Hub);
        Assert.Equal(WheelOutcome.Close, wheel.Release(Toward(180, 100)));
    }

    [Fact]
    public void An_empty_wheel_is_the_hub_with_No_slices()
    {
        var wheel = Open(slices: []);
        Assert.Equal(("No slices", HubTone.Quiet), wheel.Hub);
        Assert.Equal(WheelOutcome.None, wheel.Move(Toward(0, 100)));
        Assert.Equal(WheelOutcome.Changed, wheel.Release(Toward(0, 100)));
        Assert.Equal(WheelOutcome.Close, wheel.Click(Toward(0, 100)));
    }

    [Fact]
    public void Near_an_edge_the_wheel_shifts_in_whole()
    {
        var wheel = new WheelState(Screen, new Point(20, 1075), Eight(), true, _ => false);
        Assert.Equal(new Point(132, 1080 - 132), wheel.Top.Middle);
        // Its directions are read from its middle.
        wheel.Move(new Point(132, 1080 - 132 - 100));
        Assert.Equal(0, wheel.Pointed);
    }

    [Fact]
    public void Four_slices_sit_at_the_compass_points()
    {
        var wheel = Open(slices: [.. Eight().Take(4)]);
        Assert.Equal([0.0, 90, 180, 270], Enumerable.Range(0, 4).Select(wheel.Top.AngleOf));
        wheel.Move(Toward(250, 100));
        Assert.Equal(3, wheel.Pointed);
    }

    // ----- Keys -----

    [Theory]
    [InlineData("Ctrl+Shift+T", KeyModifiers.Ctrl | KeyModifiers.Shift, 0x54u)]
    [InlineData("Win+Shift+S", KeyModifiers.Win | KeyModifiers.Shift, 0x53u)]
    [InlineData("Alt+F4", KeyModifiers.Alt, 0x73u)]
    [InlineData("Ctrl+Page Down", KeyModifiers.Ctrl, 0x22u)]
    [InlineData("Ctrl+/", KeyModifiers.Ctrl, 0xBFu)]
    [InlineData("Print Screen", KeyModifiers.None, 0x2Cu)]
    public void Keys_are_written_and_read_back(string text, KeyModifiers modifiers, uint key)
    {
        var keys = KeyCombo.Parse(text);
        Assert.Equal(new KeyCombo(modifiers, key), keys);
        Assert.Equal(text, keys.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+T")]
    [InlineData("Ctrl+Banana")]
    public void Text_that_isnt_keys_reads_as_none(string text) => Assert.Null(KeyCombo.Parse(text));

    [Fact]
    public void Modifiers_go_down_in_order_and_the_arrows_are_extended()
    {
        Assert.Equal([0x5Bu, 0x11u, 0x10u], new KeyCombo(KeyModifiers.Win | KeyModifiers.Shift | KeyModifiers.Ctrl, 0x53).ModifierKeys);
        Assert.True(KeyCombo.IsExtended(0x25));
        Assert.False(KeyCombo.IsExtended(0x54));
    }

    // ----- Settings -----

    [Fact]
    public void Settings_hold_no_more_than_a_wheel_does()
    {
        var deep = new Slice { Kind = SliceKind.Submenu, Name = "C", Slices = [Media(MediaAction.Next)] };
        var middle = new Slice { Kind = SliceKind.Submenu, Name = "B", Slices = [.. Enumerable.Repeat(Media(MediaAction.Mute), 9), deep] };
        var settings = new RadialMenuSettings
        {
            Shortcut = new KeyShortcut(ShortcutModifiers.Shift, 0x52), // no Ctrl, Alt or Win
            Slices = [.. Enumerable.Repeat(new Slice { Kind = SliceKind.Submenu, Name = "A", Slices = [middle] }, 10)],
        }.Checked();
        Assert.Null(settings.Shortcut);
        Assert.Equal(8, settings.Slices.Count);
        var b = settings.Slices[0].Slices[0];
        Assert.Equal(7, b.Slices.Count); // a submenu holds 7 beside its Back
        Assert.DoesNotContain(b.Slices, s => s.Kind == SliceKind.Submenu); // three wheels deep at most
    }

    [Fact]
    public void No_trigger_is_on_until_one_is_turned_on()
    {
        var settings = new RadialMenuSettings();
        Assert.False(settings.HoldsAButton);
        Assert.Null(settings.Shortcut);
        Assert.Empty(settings.Slices);
    }

    // ----- Names -----

    [Theory]
    [InlineData(SliceKind.Link, "https://www.github.com/tcurtsinger/Chippi", "github.com")]
    [InlineData(SliceKind.Link, "docs.example.org/a", "docs.example.org")]
    [InlineData(SliceKind.File, @"C:\Users\Travis\Downloads\", "Downloads")]
    [InlineData(SliceKind.Keys, "ctrl+shift+t", "Ctrl+Shift+T")]
    [InlineData(SliceKind.Text, "Thanks,\nTravis", "Thanks,")]
    [InlineData(SliceKind.Command, "ipconfig /flushdns", "ipconfig /flushdns")]
    [InlineData(SliceKind.Window, "OtherScreen", "Other screen")]
    [InlineData(SliceKind.Selection, "OpenLink", "Open as link")]
    [InlineData(SliceKind.SettingsPage, "ms-settings:bluetooth", "Bluetooth & devices")]
    [InlineData(SliceKind.App, @"C:\Games\Steam\steam.exe", "steam")]
    public void A_slice_is_named_from_what_it_does(SliceKind kind, string value, string name) =>
        Assert.Equal(name, SliceCatalog.NameFor(new Slice { Kind = kind, Value = value }));

    [Theory]
    [InlineData("github.com/tcurtsinger/Chippi", "https://github.com/tcurtsinger/Chippi")]
    [InlineData("  https://x.dev/a?b=1 ", "https://x.dev/a?b=1")]
    [InlineData("mailto:me@example.com", "mailto:me@example.com")]
    [InlineData("Ashen Gate", null)]
    [InlineData("notes", null)]
    [InlineData("file:///C:/Windows/notepad.exe", null)]
    [InlineData(@"C:\Windows\notepad.exe", null)]
    public void Only_a_web_address_opens_as_a_link(string text, string? link) => Assert.Equal(link, Links.FromSelection(text));
}
