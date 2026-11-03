// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using Chippi.Audio;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;
using static Chippi.Features.Sound.Tests.RoutingFixture;
using static Chippi.Features.Sound.Tests.SoundUiThread;

namespace Chippi.Features.Sound.Tests;
public sealed class VolumeUiTests
{
    [Fact]
    public async Task Unshown_volume_and_mute_controls_change_the_same_sessions()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module,VolumeSurface.Island);
            var slider = Descendants(view).OfType<Slider>().Single(s=>AutomationProperties.GetName(s)=="Music volume");
            var range = (IRangeValueProvider)new SliderAutomationPeer(slider);
            Assert.Equal(100,range.Maximum);
            range.SetValue(35);
            await Until(()=>f.System.SessionById("one").Volume==0.35f);
            var mute = Descendants(view).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Mute Music");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(()=>f.System.SessionById("one").Muted);
        });
    }
    [Theory]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(15, 8)]
    public async Task Up_to_eight_app_channels_fit_at_full_height(int count, int visible)
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        for (int i = 0; i < count; i++)
            f.System.WithSession(VolumeFixture.Session($"app{i}", "A", 200 + i, $@"C:\Apps\App{i}.exe"));
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout();
            var bank = (Grid)((Grid)view.Content).Children[0];
            Assert.Equal(visible, bank.Children.Count);
            Assert.Equal(visible, Descendants(bank).OfType<Slider>().Count());
            Assert.True(bank.DesiredSize.Width <= 496.001);
            Assert.True(bank.DesiredSize.Height <= 304.001);
            Assert.Empty(Descendants(view).OfType<ScrollViewer>());
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("Python", "Py")]
    [InlineData(" PowerShell", "Po")]
    [InlineData("R", "R")]
    [InlineData("Microsoft Teams", "MT")]
    [InlineData("Microsoft Teams (work or school)", "MT")]
    [InlineData("VLC media player", "VM")]
    [InlineData("🎧 Studio Pro", "SP")] // letters only: the emoji stays in the name
    [InlineData("🎧Mix", "Mi")]
    [InlineData("7-Zip", "7Z")]
    [InlineData("éclair", "éc")]
    [InlineData("🎧🎵", "🎧")] // no letters: its first character, whole
    public void Fallback_distinguishes_apps_sharing_the_first_letter(string name, string expected) =>
        Assert.Equal(expected, VolumeView.Initials(name));

    [Fact]
    public async Task Reopening_the_page_keeps_a_paused_apps_strip()
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("paused", "B", 124, state: SessionState.Inactive));
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Slider slider;
            try { slider = Descendants(view).OfType<Slider>().Single(); }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try { Assert.Same(slider, Descendants(view).OfType<Slider>().Single()); }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
            return Task.CompletedTask;
        });
    }

    // An error the page showed is gone on the next visit; one that came after it stays.
    [Fact]
    public async Task An_error_shown_is_cleared_when_the_page_closes()
    {
        await using var f = new VolumeFixture(); await f.Start(); f.System.AppPolicyFailsWrite = true;
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => f.Module.SetOutputAsync(VolumeFixture.Key, "B"));
                await Until(() => Descendants(view).OfType<TextBlock>().Any(t => t.Text == "Switch failed · Music" && t.Visibility == Visibility.Visible));
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
            Assert.Null(f.Module.Error);

            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => f.Module.SetOutputAsync(VolumeFixture.Key, "B"));
            Assert.Equal("Switch failed · Music", f.Module.Error); // came while the page was closed: not seen yet
        });
    }

    // Off, the output button still says where the app plays, and why it can't be changed.
    [Fact]
    public async Task An_output_that_cant_be_picked_says_why()
    {
        await using var f = new VolumeFixture(); f.System.AppPolicyAvailable = false; await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            var output = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Music output");
            Assert.False(output.IsEnabled);
            Assert.Equal($"Plays on A · {f.Module.OutputsOff}", output.ToolTip);
            return Task.CompletedTask;
        });
    }

    // A program whose description is blank goes by its file name, here and in its errors.
    [Fact]
    public async Task An_app_without_a_name_goes_by_its_program()
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("one", "A", 123) with { AppName = "  " });
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            Assert.Equal(["Music volume"], Descendants(view).OfType<Slider>().Select(AutomationProperties.GetName));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_silent_app_can_be_adjusted_until_its_last_windows_session_expires()
    {
        await using var f = new VolumeFixture();
        var quiet = VolumeFixture.Session("quiet", "A", 300, @"C:\Apps\Quiet.exe", SessionState.Inactive) with { AppName = "Quiet" };
        f.System.WithSession(quiet);
        f.System.WithSession(quiet with { SessionId = "quiet-2", EndpointId = "B", ProcessId = 301 });
        await f.Start();
        await Sta(async () =>
        {
            var island = new VolumeView(f.Module, VolumeSurface.Island);
            island.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                Slider[] Faders() => Descendants(island).OfType<Slider>().ToArray();
                Assert.Equal(["Music volume", "Quiet volume"], Faders().Select(AutomationProperties.GetName));
                var settings = new VolumeView(f.Module, VolumeSurface.Settings);
                Assert.Equal(["Music volume", "Quiet volume"], Descendants(settings).OfType<Slider>().Select(AutomationProperties.GetName));
                Assert.Contains(Descendants(settings).OfType<TextBlock>(), t => t.Text == "Not playing" && t.Visibility == Visibility.Visible);
                // Captioned or not, the rows are one height, so a caption coming and going moves nothing.
                var card = Descendants(settings).OfType<Card>().First();
                Assert.All(card.Children.OfType<Border>(), row => Assert.Equal(SettingsLayout.CaptionRowHeight, row.MinHeight));

                var fader = Faders()[1];
                Assert.Equal("Quiet · Not playing · Default", AutomationProperties.GetHelpText(fader));
                ((IRangeValueProvider)new SliderAutomationPeer(fader)).SetValue(35);
                await Until(() => f.System.SessionById("quiet").Volume == .35f && f.System.SessionById("quiet-2").Volume == .35f);
                Assert.Equal(.5f, f.System.SessionById("one").Volume);
                f.System.EndSession("quiet");
                await f.Audio.RefreshAsync();
                Assert.Same(fader, Faders()[1]);
                f.System.EndSession("quiet-2");
                await f.Audio.RefreshAsync();
                await Until(() => Faders().Length == 1);
                Assert.Equal("Music volume", AutomationProperties.GetName(Faders()[0]));
            }
            finally { island.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public async Task Paging_keeps_system_sounds_in_its_own_column_and_reaches_every_app()
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("sys", "A", 0) with { IsSystemSounds = true });
        for (int i = 0; i < 14; i++)
            f.System.WithSession(VolumeFixture.Session($"app{i}", "A", 200 + i, $@"C:\Apps\App{i:00}.exe",
                i % 2 == 0 ? SessionState.Active : SessionState.Inactive) with { AppName = $"App {i:00}" });
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            var sliders = () => Descendants(view).OfType<Slider>().ToArray();
            Button Page(string name) => Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            var pinned = sliders()[0];
            Assert.Equal("System sounds volume", AutomationProperties.GetName(pinned));
            Assert.DoesNotContain(Descendants(view).OfType<Button>(), b => AutomationProperties.GetName(b) == "System sounds output");
            Assert.Equal(9, sliders().Length);
            Assert.False(Page("Previous apps").IsEnabled);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "1–8 of 14 apps");
            Page("Next apps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(pinned, sliders()[0]);
            Assert.Equal(7, sliders().Length);
            Assert.Equal("App 13 volume", AutomationProperties.GetName(sliders()[^1]));
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "9–14 of 14 apps");
            Assert.False(Page("Next apps").IsEnabled);
            Page("Previous apps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(pinned, sliders()[0]);
            Assert.Equal("App 00 volume", AutomationProperties.GetName(sliders()[1]));
            return Task.CompletedTask;
        });
    }

    // A strip stays the same element while levels change and other apps come and go, so a drag on it holds.
    [Fact]
    public async Task Strips_update_in_place_as_apps_join_and_levels_change()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // follows the module, as on screen
            try
            {
                var slider = Descendants(view).OfType<Slider>().Single();
                f.System.SetSessionVolume("one", 0.8f);
                f.System.WithSession(VolumeFixture.Session("aaa", "A", 301, @"C:\Apps\Aaa.exe", SessionState.Inactive) with { AppName = "Aaa" });
                await f.Audio.RefreshAsync();
                await Until(() => Descendants(view).OfType<Slider>().Count() == 2);
                Assert.Equal(["Aaa volume", "Music volume"], Descendants(view).OfType<Slider>().Select(AutomationProperties.GetName));
                Assert.Same(slider, Descendants(view).OfType<Slider>().Last());
                Assert.Equal(80, slider.Value, 3);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public async Task The_wheel_moves_a_strip_two_a_notch()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            var strips = (Grid)((Grid)view.Content).Children[0];
            var strip = (Grid)strips.Children[0];
            strip.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.MouseWheelEvent });
            await Until(() => Math.Abs(f.System.SessionById("one").Volume - 0.52f) < 0.001f);
            strip.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -240) { RoutedEvent = UIElement.MouseWheelEvent });
            await Until(() => Math.Abs(f.System.SessionById("one").Volume - 0.48f) < 0.001f);
        });
    }

    // Where the app plays: a pick the app doesn't follow shows in Caution, naming both.
    [Fact]
    public async Task The_output_button_says_where_the_app_plays()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // follows the module, as on screen
            try
            {
                var output = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Music output");
                Assert.Equal("Plays on A · Default", output.ToolTip);
                await f.Module.SetOutputAsync(VolumeFixture.Key, "B");
                await Until(() => Equals(output.ToolTip, "Picked B · still plays on A"));
                var glyph = (Chippi.UI.Controls.LucideIcon)output.Content;
                Assert.Equal(Chippi.UI.Controls.LucideIcons.Ellipsis, glyph.Data);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    // An app with no pick of its own that plays somewhere other than the default isn't "Default".
    [Fact]
    public async Task An_app_following_the_default_but_playing_elsewhere_says_so()
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("one", "B", 123));
        await f.Start();
        Assert.True(f.Module.PlaysElsewhere(VolumeFixture.Key));
        await Sta(() =>
        {
            var island = new VolumeView(f.Module, VolumeSurface.Island);
            var output = Descendants(island).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Music output");
            Assert.Equal("Default · A · still plays on B", output.ToolTip);
            var settings = new VolumeView(f.Module, VolumeSurface.Settings);
            Assert.Contains(Descendants(settings).OfType<TextBlock>(), t => t.Text == "Still plays on B" && t.Visibility == Visibility.Visible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_icon_only_channel_exposes_its_full_name_and_actual_output()
    {
        const string full = "Microsoft Edge WebView2 Runtime";
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("edge", "A", 222) with { AppName = full });
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            var level = Descendants(view).OfType<Slider>().Single();
            Assert.Equal(full + " volume", AutomationProperties.GetName(level));
            Assert.Equal(full + " · Plays on A · Default", AutomationProperties.GetHelpText(level));
            Assert.DoesNotContain(Descendants(view).OfType<TextBlock>(), t => t.Text == full);
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var item in Descendants(child)) yield return item;
    }
}
