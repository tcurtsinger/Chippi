// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Audio;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;
using static Chippi.Features.Sound.Tests.RoutingFixture;
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
    [InlineData(7, 56)]
    [InlineData(12, 34)]
    [InlineData(14, 28)]
    [InlineData(18, 28)]
    public async Task Strips_fit_the_body_at_each_density_and_leave_overflow_out(int count, double width)
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
            var strips = (StackPanel)((Grid)view.Content).Children[0];
            Assert.Equal(Math.Min(14, count), strips.Children.Count);
            foreach (FrameworkElement strip in strips.Children) Assert.Equal(width, strip.Width);
            Assert.True(strips.DesiredSize.Width <= 496.001);
            Assert.True(strips.DesiredSize.Height <= 304.001);
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

    // The island keeps its page: opened again, the strips that still fit are the same ones, not rebuilt.
    [Fact]
    public async Task Reopening_the_page_keeps_its_strips()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var slider = Descendants(view).OfType<Slider>().Single();
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Same(slider, Descendants(view).OfType<Slider>().Single());
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
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => f.Module.SetOutputAsync(VolumeFixture.Key, "B"));
            await Until(() => Descendants(view).OfType<TextBlock>().Any(t => t.Text == "Switch failed · Music" && t.Visibility == Visibility.Visible));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
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

    // The island shows what's playing; Settings lists every app, saying which aren't.
    [Fact]
    public async Task The_island_shows_playing_apps_and_settings_every_app()
    {
        await using var f = new VolumeFixture();
        f.System.WithSession(VolumeFixture.Session("quiet", "A", 300, @"C:\Apps\Quiet.exe", SessionState.Inactive) with { AppName = "Quiet" });
        await f.Start();
        await Sta(() =>
        {
            var island = new VolumeView(f.Module, VolumeSurface.Island);
            Assert.Equal(["Music volume"], Descendants(island).OfType<Slider>().Select(AutomationProperties.GetName));
            var settings = new VolumeView(f.Module, VolumeSurface.Settings);
            Assert.Equal(["Music volume", "Quiet volume"], Descendants(settings).OfType<Slider>().Select(AutomationProperties.GetName));
            Assert.Contains(Descendants(settings).OfType<TextBlock>(), t => t.Text == "Not playing" && t.Visibility == Visibility.Visible);
            // Captioned or not, the rows are one height, so a caption coming and going moves nothing.
            var card = Descendants(settings).OfType<Card>().First();
            Assert.All(card.Children.OfType<Border>(), row => Assert.Equal(SettingsLayout.CaptionRowHeight, row.MinHeight));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Past_fourteen_the_last_strip_counts_the_rest()
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        for (int i = 0; i < 18; i++)
            f.System.WithSession(VolumeFixture.Session($"app{i}", "A", 200 + i, $@"C:\Apps\App{i:00}.exe") with { AppName = $"App {i:00}" });
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            var strips = (StackPanel)((Grid)view.Content).Children[0];
            Assert.Equal(14, strips.Children.Count);
            var more = Assert.Single(Descendants(strips.Children[^1]).OfType<Button>());
            Assert.Equal("+5", more.Content);
            Assert.Equal("5 more apps, open Settings", AutomationProperties.GetName(more));
            Assert.DoesNotContain(Descendants(strips).OfType<Slider>(), s => AutomationProperties.GetName(s) is "App 17 volume");
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
            var slider = Descendants(view).OfType<Slider>().Single();
            f.System.SetSessionVolume("one", 0.8f);
            f.System.WithSession(VolumeFixture.Session("aaa", "A", 301, @"C:\Apps\Aaa.exe") with { AppName = "Aaa" });
            await f.Audio.RefreshAsync();
            await Until(() => Descendants(view).OfType<Slider>().Count() == 2);
            Assert.Equal(["Aaa volume", "Music volume"], Descendants(view).OfType<Slider>().Select(AutomationProperties.GetName));
            Assert.Same(slider, Descendants(view).OfType<Slider>().Last());
            Assert.Equal(80, slider.Value, 3);
        });
    }

    [Fact]
    public async Task The_wheel_moves_a_strip_two_a_notch()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            var strips = (StackPanel)((Grid)view.Content).Children[0];
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
            var output = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Music output");
            Assert.Equal("Plays on A · Default", output.ToolTip);
            await f.Module.SetOutputAsync(VolumeFixture.Key, "B");
            await Until(() => Equals(output.ToolTip, "Picked B · still plays on A"));
            var glyph = (Chippi.UI.Controls.LucideIcon)output.Content;
            Assert.Equal(Chippi.UI.Controls.LucideIcons.Speaker, glyph.Data);
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
    public async Task A_name_past_two_lines_marks_the_second_and_keeps_its_tooltip()
    {
        const string full = "Microsoft Edge WebView2 Runtime";
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("edge", "A", 222) with { AppName = full });
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout();
            var name = Descendants(view).OfType<TextBlock>().Single(t => Equals(t.ToolTip, full));
            Assert.EndsWith("\u2026", name.Text);
            Assert.NotEqual(full, name.Text);
            Assert.Equal(TextWrapping.WrapWithOverflow, name.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            var measured = new TextBlock { Text = name.Text, FontSize = name.FontSize, FontFamily = name.FontFamily, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = name.TextWrapping };
            measured.Measure(new Size(56, double.PositiveInfinity));
            Assert.Equal(28, measured.DesiredSize.Height);
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var item in Descendants(child)) yield return item;
    }
    private static Task Sta(Func<Task> run)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Exception? error=null;
            dispatcher.BeginInvoke(async()=>
            {
                try { await run(); } catch(Exception ex) { error=ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
            if(error is null) done.SetResult(); else done.SetException(error);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }
}