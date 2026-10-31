// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Features.Sound.Mute;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Routing;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Features.Sound.Tests;

/// <summary>
/// Settings › Output, Microphone and App volume (brief F2, F3): each island control has a Settings twin
/// that calls the same module method. Unshown controls and fake audio only.
/// </summary>
public sealed class SoundSettingsTests
{
    [Fact]
    public async Task Priority_rows_are_the_one_list_and_their_radios_pick_as_the_island_rows_do()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Speaker("Gone")).WithDefault("A");
        await f.Start();
        f.System.Disconnect("Gone");
        await f.Audio.RefreshAsync();
        await Sta(async () =>
        {
            var page = new RoutingPage(f.Module);
            Assert.Equal(["Priority"], Sections(page)); // no separate "Output in use" list
            var picks = Descendants(page).OfType<Button>().Where(b => AutomationProperties.GetName(b) is { } n && (n.StartsWith("Use ", StringComparison.Ordinal) || n.EndsWith(", in use", StringComparison.Ordinal))).ToList();
            Assert.Equal(["A, in use", "Use B", "Use Gone"], picks.Select(AutomationProperties.GetName));
            Assert.Equal([true, true, false], picks.Select(b => b.IsEnabled)); // an unplugged device can't be used
            Assert.Equal(["Not connected"], Descendants(page).OfType<TextBlock>().Select(t => t.Text).Where(t => t is "Not connected" or "Connected" or "Playing now"));
            ((IInvokeProvider)new ButtonAutomationPeer(picks[1])).Invoke();
            await Until(() => !Descendants(page).Contains(picks[1]));
        });
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal(["A", "B", "Gone"], f.Module.Settings.Output.Devices.Select(d => d.Id)); // UseAsync, the island row's call: the order stays
    }

    [Fact]
    public async Task Output_volume_row_sets_and_mutes_the_output_in_use()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        await Sta(async () =>
        {
            var page = new RoutingPage(f.Module);
            var slider = Descendants(page).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "System volume");
            ((IRangeValueProvider)new SliderAutomationPeer(slider)).SetValue(35);
            await Until(() => Math.Abs(f.System.GetLevel("A").Volume - 0.35f) < 0.001f);
            var mute = Descendants(page).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute system volume");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(() => f.System.GetLevel("A").Muted);
            await Until(() => Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Muted"));
        });
        Assert.False(f.System.GetLevel("B").Muted);
    }

    [Fact]
    public async Task Priority_card_holds_the_switches_with_the_calls_caption_and_48_rows()
    {
        await using var f = new RoutingFixture();
        f.System.With(Mic("M1"), Mic("M2")).WithDefault("M1");
        await f.Start();
        await Sta(() =>
        {
            var page = new RoutingPage(f.Module, AudioFlow.Capture);
            Assert.Empty(Descendants(page).OfType<Slider>()); // the Microphone row is mic mute's
            Assert.Equal(["Priority"], Sections(page));
            Assert.Contains(Descendants(page).OfType<Button>(), b => AutomationProperties.GetName(b) == "Add mic");
            var calls = Descendants(page).OfType<SettingRow>().Single(r => (string)r.Header == "Communications device");
            Assert.Equal("Calls follow this list", calls.Description);
            var devices = Descendants(page).OfType<Border>().Where(b => b.Child is Grid g && g.Children.OfType<DragHandle>().Any()).ToList();
            Assert.Equal(2, devices.Count);
            Assert.All(devices, d => Assert.Equal(SettingsLayout.RowHeight, d.MinHeight));
            // The list is a card of its own, 8 under the switches' card.
            var list = Assert.IsType<Card>(devices[0].Parent);
            Assert.Equal(new Thickness(0, 8, 0, 0), list.Margin);
            Assert.DoesNotContain(list.Children.OfType<SettingRow>(), r => (string)r.Header == "Priority switching");
            Assert.Same(list, devices[1].Parent);
        });
    }

    [Fact]
    public async Task Output_volume_is_captioned_with_the_output_it_sets()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("Speakers (INZONE Buds - Game)"), Speaker("Headphones")).WithDefault("Speakers (INZONE Buds - Game)");
        await f.Start();
        await Sta(() =>
        {
            var volume = Descendants(new RoutingPage(f.Module)).OfType<SettingRow>().Single(r => (string)r.Header == "Volume");
            Assert.Equal("Speakers (INZONE Buds - Game)", volume.Description);
        });
    }

    [Fact]
    public async Task No_devices_is_a_row_inside_the_card()
    {
        await using var f = new RoutingFixture();
        await f.Start();
        await Sta(() =>
        {
            var page = new RoutingPage(f.Module, AudioFlow.Capture);
            Assert.Equal(1, Descendants(page).OfType<TextBlock>().Count(t => t.Text == "No devices"));
            Assert.All(Descendants(page).OfType<TextBlock>().Where(t => t.Text == "No devices"), t => Assert.IsType<Card>(((FrameworkElement)t.Parent).Parent));
        });
    }

    [Fact]
    public async Task Microphone_row_mutes_with_the_island_cards_call_and_says_muted()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), Mic("B"));
        await f.Start();
        await Sta(async () =>
        {
            var view = Assert.IsType<MicMuteView>(f.Ui.Page!("mute"));
            var row = Descendants(view).OfType<SettingRow>().Single();
            Assert.Equal("Microphone", row.Header);
            Assert.Equal("Live", row.Description);
            var mute = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute all microphones");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(() => view.IsEnabled && f.Module.Status.State == MicrophoneMuteState.Muted);
            Assert.True(f.System.GetLevel("A").Muted && f.System.GetLevel("B").Muted);
            Assert.Equal("Muted", Descendants(view).OfType<SettingRow>().Single().Description);
            Assert.DoesNotContain(Descendants(view).OfType<Button>(), b => AutomationProperties.GetName(b) == "Change shortcut");
        });
    }

    [Fact]
    public async Task Microphone_volume_sets_the_mic_in_use_as_the_island_row_does_and_says_muted_in_its_place()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("Microphone (RØDE VideoMic GO II)"), Mic("B")).WithDefault("Microphone (RØDE VideoMic GO II)");
        await f.Start();
        await Sta(async () =>
        {
            var view = Assert.IsType<MicMuteView>(f.Ui.Page!("mute"));
            var row = Descendants(view).OfType<SettingRow>().Single();
            Assert.Equal("Volume", row.Header);
            Assert.Equal("Microphone (RØDE VideoMic GO II)", row.Description);
            var level = Descendants(view).OfType<Slider>().Single();
            Assert.Equal("Microphone volume", AutomationProperties.GetName(level));
            level.Value = 70;
            await Until(() => Math.Abs(f.System.GetLevel("Microphone (RØDE VideoMic GO II)").Volume - 0.7f) < 0.001f);
            Assert.Equal(0.5f, f.System.GetLevel("B").Volume, 3);

            var mute = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute all microphones");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(() => view.IsEnabled && f.Module.Status.State == MicrophoneMuteState.Muted);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "Muted");
            Assert.Same(level, Descendants(view).OfType<Slider>().Single()); // the same row: a level change never rebuilds it
        });
    }

    [Fact]
    public async Task Microphone_volume_keeps_its_own_mute_button_after_showing_the_state_row_and_back()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), CableOutput()).WithDefault("A");
        await f.Start();
        await Sta(async () =>
        {
            var view = Assert.IsType<MicMuteView>(f.Ui.Page!("mute"));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // follow the module, as a shown page does
            string Header() => (string)Descendants(view).OfType<SettingRow>().Single().Header;
            Assert.Equal("Volume", Header());
            f.System.ChangeDefault("CableOut"); // VB-CABLE as the default mic, with routing off: no level to tell
            await Until(() => Header() == "Microphone");
            f.System.ChangeDefault("A");
            await Until(() => Header() == "Volume");

            var mute = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute all microphones");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(() => view.IsEnabled && f.Module.Status.State == MicrophoneMuteState.Muted);
            Assert.Equal("Unmute", Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Unmute all microphones").Content);
        });
    }

    [Fact]
    public async Task The_mute_shortcut_can_be_removed_and_set_again()
    {
        await using var f = new MuteFixture();
        await f.Start();
        await Sta(async () =>
        {
            var view = Assert.IsType<MicMuteView>(f.Ui.Page!(null));
            var remove = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove shortcut");
            ((IInvokeProvider)new ButtonAutomationPeer(remove)).Invoke();
            await Until(() => f.Module.Settings.Shortcut is null);
            Assert.Null(f.Module.Settings.Shortcut);
            Assert.Null(f.Settings.Value.Shortcut);
            Assert.True(f.Hotkey.Disposed);
            view = new MicMuteView(f.Module);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "Not set");
            Assert.Contains(Descendants(view).OfType<Button>(), b => AutomationProperties.GetName(b) == "Set shortcut");
            Assert.DoesNotContain(Descendants(view).OfType<Button>(), b => AutomationProperties.GetName(b) == "Remove shortcut");
        });
        var keys = new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x4B);
        Assert.True(f.Module.ChangeShortcut(keys));
        Assert.Equal(keys, f.Hotkey.Current);
        Assert.Equal(keys, f.Settings.Value.Shortcut);
    }

    [Fact]
    public async Task A_removed_mute_shortcut_stays_removed_after_a_restart()
    {
        await using var f = new MuteFixture();
        f.Settings.Value = new MicMuteSettings { Shortcut = null };
        await f.Start();
        Assert.Null(f.Module.Settings.Shortcut);
        Assert.Null(f.Hotkey.Current);
        Assert.Null(f.Module.ShortcutError);
    }

    [Fact]
    public async Task Mic_processing_folds_its_tuning_and_shows_the_level_only_while_live()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await f.Start();
        await Sta(async () =>
        {
            var off = new ProcessingPage(f.Module);
            var rows = Descendants(off).OfType<SettingRow>().Select(r => (string)r.Header).ToList();
            Assert.Equal(["Mic processing", "Default device", "Noise suppression", "Threshold", "Target level", "Max gain"], rows);
            Assert.Equal("Makes CABLE Output the default mic", Descendants(off).OfType<SettingRow>().Single(r => (string)r.Header == "Default device").Description);
            var folds = Descendants(off).OfType<ExpanderRow>().ToList();
            Assert.Equal(["Noise gate", "Auto gain"], folds.Select(r => (string)r.Header));
            Assert.Contains(Descendants(folds[0]).OfType<SettingRow>(), r => (string)r.Header == "Threshold");
            Assert.All(folds, fold => Assert.False(fold.IsExpanded));

            await f.Module.SetEnabledAsync(true);
            var live = new ProcessingPage(f.Module);
            Assert.Contains(Descendants(live).OfType<SettingRow>(), r => (string)r.Header == "Level");
            // Two cards: whether it runs and its level, then how it processes.
            var cards = Descendants(live).OfType<Card>().Where(c => c.Children.Count > 0 && c.Parent is StackPanel).ToList();
            Assert.Equal(2, cards.Count);
            Assert.Equal(["Mic processing", "Level"], cards[0].Children.OfType<SettingRow>().Select(r => (string)r.Header));
            Assert.Equal("Default device", cards[1].Children.OfType<SettingRow>().Select(r => (string)r.Header).First());
            Assert.Equal(new Thickness(0, 8, 0, 0), cards[1].Margin);
        });
    }

    [Fact]
    public async Task App_volume_settings_lists_every_app_with_the_islands_controls()
    {
        await using var f = new VolumeFixture();
        for (int i = 0; i < 16; i++)
            f.System.WithSession(VolumeFixture.Session($"app{i}", "A", 200 + i, $@"C:\Apps\App{i}.exe"));
        await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Settings);
            int apps = f.Module.Apps.Count();
            Assert.True(apps > 14);
            Assert.Equal(apps, Descendants(view).OfType<Slider>().Count()); // no island limit here
            Assert.All(Descendants(view).OfType<Slider>(), s => Assert.Equal(200, s.Width));
            Assert.Equal(["Show apps that aren't playing", "Remember each app's output"],
                Descendants(view).OfType<SettingRow>().Select(r => (string)r.Header));

            var slider = Descendants(view).OfType<Slider>().First(s => AutomationProperties.GetName(s) == "Music volume");
            ((IRangeValueProvider)new SliderAutomationPeer(slider)).SetValue(35);
            await Until(() => f.System.SessionById("one").Volume == 0.35f);
            var mute = Descendants(view).OfType<Button>().First(b => AutomationProperties.GetName(b) == "Mute Music");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(() => f.System.SessionById("one").Muted);
            var output = Descendants(view).OfType<ComboBox>().First(c => AutomationProperties.GetName(c) == "Music output");
            Assert.Equal("Default output", output.SelectedItem);
            output.SelectedIndex = 2; // Default output, A, B
            await Until(() => f.Settings.Value.Outputs.GetValueOrDefault(VolumeFixture.Key.Value) == "B");
        });
    }

    private static List<string> Sections(DependencyObject root) =>
        Descendants(root).OfType<Grid>().Where(g => g.Children.OfType<TextBlock>().Any() && g.Parent is StackPanel { Parent: RoutingPage })
            .Select(g => g.Children.OfType<TextBlock>().First().Text).ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The page did not finish refreshing after the command.");
            await Task.Delay(10);
        }
    }

    private static Task Sta(Action run) => Sta(() => { run(); return Task.CompletedTask; });

    private static Task Sta(Func<Task> run)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Exception? error = null;
            dispatcher.BeginInvoke(async () =>
            {
                try { await run(); }
                catch (Exception ex) { error = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
            if (error is null) done.SetResult();
            else done.SetException(error);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
