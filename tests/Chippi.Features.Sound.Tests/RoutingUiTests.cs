// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Features.Sound.Routing;
using static Chippi.Audio.Tests.FakeDeviceSystem;
using static Chippi.Features.Sound.Tests.SoundUiThread;

namespace Chippi.Features.Sound.Tests;

/// <summary>Unshown controls only: no windows, keyboard injection or real device system.</summary>
public sealed class RoutingUiTests
{
    [Fact]
    public async Task Settings_switch_automation_changes_routing_and_not_only_the_check()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        await Sta(async () =>
        {
            var page = new RoutingPage(f.Module);
            var toggle = Descendants(page).OfType<ToggleButton>().Single(t => AutomationProperties.GetName(t) == "Priority switching");
            ((IToggleProvider)new ToggleButtonAutomationPeer(toggle)).Toggle();
            await Until(() => !Descendants(page).Contains(toggle));
            Assert.False(f.Module.Settings.Output.Enabled);
        });
        f.System.ChangeDefault("B");
        await f.Audio.RefreshAsync();
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
    }

    [Fact]
    public async Task Grip_keyboard_moves_saved_priority_and_default()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        await Sta(async () =>
        {
            var page = new RoutingPage(f.Module);
            var handle = Descendants(page).OfType<Chippi.UI.Controls.DragHandle>().First();
            handle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new UnshownSource { RootVisual = page }, 0, Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });
            await Until(() => !Descendants(page).Contains(handle));
            Assert.Equal("B", f.Module.Settings.Output.Devices[0].Id);
        });
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
    }

    [Fact]
    public async Task Routing_contributes_output_and_microphone_to_the_Audio_destination()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Mic("M")).WithDefault("A").WithDefault("M");
        await f.Start();
        await Sta(() =>
        {
            Assert.IsType<RoutingPage>(f.Ui.Page!("microphone"));
            Assert.All(f.Ui.Tiles, t => Assert.IsType<RoutingQuickView>(t.Spec.CreateSection()));
        });
        Assert.All(f.Ui.Tiles, t => Assert.Equal(SoundTile.Key, t.Spec.Key));
        Assert.Equal([SoundTile.Output, SoundTile.Microphone], f.Ui.Tiles.Select(t => t.Spec.Column));
        Assert.Equal("A", f.Ui.Tiles[0].Status);
    }

    [Fact]
    public Task A_pick_works_with_priority_switching_off_and_only_connected_devices_are_listed() => Sta(async () =>
    {
        // The picker command resumes on this dispatcher. Dispose the fixture before the dispatcher shuts down.
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Speaker("Gone")).WithDefault("A");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        f.System.Disconnect("Gone");
        await f.Audio.RefreshAsync();
        var page = new RoutingQuickView(f.Module, AudioFlow.Render);
        var picker = Picker(page);
        Assert.Equal("Output device", AutomationProperties.GetName(picker));
        Assert.Equal(["A", "B"], picker.Items.OfType<ComboBoxItem>().Select(i => (string)i.Content));
        picker.SelectedIndex = 1;
        await Until(() => f.Module.InUse(AudioFlow.Render) == "B"
            && f.Audio.Snapshot.Owner(AudioFlow.Render, AudioRole.Multimedia) == RoleOwner.None);
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal(["A", "B", "Gone"], f.Module.Settings.Output.Devices.Select(d => d.Id)); // a pick leaves the order alone
        Assert.False(f.Module.Settings.Output.Enabled); // the pick is set once; switching stays off
        f.System.ChangeDefault("A");
        await f.Audio.RefreshAsync();
        Assert.Equal("A", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
    });
    [Fact]
    public async Task Both_device_pickers_offer_the_complete_connected_list()
    {
        await using var f = new RoutingFixture();
        f.System.With([.. Enumerable.Range(1, 12).Select(i => Speaker($"S{i}")), .. Enumerable.Range(1, 9).Select(i => Mic($"M{i}"))]).WithDefault("S1").WithDefault("M1");
        await f.Start();
        await Sta(() =>
        {
            foreach (var (flow, count, name) in new[] { (AudioFlow.Render, 12, "Output device"), (AudioFlow.Capture, 9, "Microphone device") })
            {
                var picker = Picker(new RoutingQuickView(f.Module, flow));
                Assert.Equal(count, picker.Items.Count);
                Assert.Equal(name, AutomationProperties.GetName(picker));
                Assert.Equal(0, picker.SelectedIndex);
            }
        });
    }

    [Fact]
    public async Task The_volume_slider_sets_the_output_it_shows_never_one_that_took_its_place()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("Headset"), Speaker("Speakers")).WithDefault("Headset");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        await Sta(async () =>
        {
            var view = new RoutingQuickView(f.Module, AudioFlow.Render); // shows the headset's level
            f.System.ChangeDefault("Speakers"); // the speakers take over before the view hears of it
            await f.Audio.RefreshAsync();
            Descendants(view).OfType<System.Windows.Controls.Slider>().Single().Value = 100;
            await Until(() => Math.Abs(f.System.GetLevel("Headset").Volume - 1f) < 0.001f);
            Assert.Equal(0.5f, f.System.GetLevel("Speakers").Volume, 3);
        });
    }

    [Fact]
    public async Task A_microphone_card_without_a_mic_mute_row_says_no_microphone()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A")).WithDefault("A");
        await f.Start();
        await Sta(() =>
        {
            var view = new RoutingQuickView(f.Module, AudioFlow.Capture);
            Assert.Equal(Visibility.Collapsed, Picker(view).Visibility);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "No microphone" && t.Visibility == Visibility.Visible);
        });
    }

    [Fact]
    public async Task A_pick_keeps_the_existing_choices()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("S1"), Speaker("S2"), Speaker("S3")).WithDefault("S1");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        await Sta(async () =>
        {
            var picker = Picker(new RoutingQuickView(f.Module, AudioFlow.Render));
            var third = picker.Items[2];
            picker.SelectedIndex = 1;
            await Until(() => f.Module.InUse(AudioFlow.Render) == "S2");
            Assert.Same(third, picker.Items[2]);
        });
    }

    [Fact]
    public async Task Unplugging_another_output_keeps_the_surviving_picker_items_and_selection()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("S1"), Speaker("S2"), Speaker("S3")).WithDefault("S2");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        f.System.ChangeDefault("S2");
        await f.Audio.RefreshAsync();
        await Sta(async () =>
        {
            var view = new RoutingQuickView(f.Module, AudioFlow.Render);
            var card = Assert.IsType<AudioDeviceCard>(view.Content);
            var picker = Picker(view);
            card.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var before = picker.Items.OfType<ComboBoxItem>().ToDictionary(i => (string)i.Tag);
                f.System.Disconnect("S1");
                await f.Audio.RefreshAsync();
                await Until(() => picker.Items.Count == 2);
                Assert.Same(before["S2"], picker.SelectedItem);
                Assert.Contains(before["S3"], picker.Items.OfType<ComboBoxItem>());
            }
            finally { card.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public async Task No_output_hides_the_slider_and_disables_mute()
    {
        await using var f = new RoutingFixture();
        f.System.With(Mic("M")).WithDefault("M");
        await f.Start();
        await Sta(() =>
        {
            var view = new RoutingQuickView(f.Module, AudioFlow.Render);
            Assert.Equal(Visibility.Visible, Descendants(view).OfType<System.Windows.Controls.TextBlock>().Single(t => t.Text == "No output").Visibility);
            Assert.Equal(Visibility.Collapsed, Descendants(view).OfType<Slider>().Single().Visibility);
            Assert.False(Descendants(view).OfType<System.Windows.Controls.Button>().Single(b => AutomationProperties.GetName(b) == "Mute system volume").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Picker(view).Visibility);
        });
    }

    [Fact]
    public async Task The_current_device_is_selected_even_at_the_end_of_a_long_list()
    {
        await using var f = new RoutingFixture();
        f.System.With([.. Enumerable.Range(1, 9).Select(i => Speaker($"S{i}"))]).WithDefault("S1");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        f.System.ChangeDefault("S9"); // Windows picked one far down the list
        await f.Audio.RefreshAsync();
        await Sta(() =>
        {
            var view = new RoutingQuickView(f.Module, AudioFlow.Render);
            Assert.Equal("S9", ((ComboBoxItem)Picker(view).SelectedItem).Content);
        });
    }

    [Fact]
    public async Task Picker_items_keep_full_names_and_disambiguate_identical_short_names()
    {
        await using var f = new RoutingFixture();
        string[] names = ["Speakers (INZONE Buds - Game)", "Headset Earphone (INZONE Buds - Chat)", "Speakers (Realtek(R) Audio)", "Digital Audio (S/PDIF) (Realtek(R) Audio)"];
        f.System.With([.. names.Select(n => Speaker(n))]).WithDefault(names[0]);
        await f.Start();
        await Sta(() =>
        {
            var view = new RoutingQuickView(f.Module, AudioFlow.Render);
            var items = Picker(view).Items.OfType<ComboBoxItem>().ToList();
            Assert.Equal(["INZONE Buds - Game", "INZONE Buds - Chat", "Speakers (Realtek(R) Audio)", "Digital Audio (S/PDIF) (Realtek(R) Audio)"],
                items.Select(item => (string)item.Content));
            Assert.Equal(names, items.Select(item => (string)item.ToolTip));
        });
    }

    [Fact]
    public async Task Stopped_mic_processing_says_why_on_its_card_and_offers_the_fix()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real")).WithDefault("Real"); // no VB-CABLE yet
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        await Sta(async () =>
        {
            var card = new Processing.ProcessingQuickView(f.Module);
            var texts = Descendants(card).OfType<System.Windows.Controls.TextBlock>().ToList();
            Assert.Contains(texts, t => t.Text == "Stopped · No VB-CABLE");
            Assert.Equal(["Install"], Descendants(card).OfType<System.Windows.Controls.Button>().Select(b => (string)b.Content));
            Assert.Equal(Visibility.Collapsed, Descendants(card).OfType<Processing.SegmentMeter>().Single().Visibility);

            f.System.Connect(CableInput());
            f.System.Connect(CableOutput());
            await Until(() => f.Module.Status.State == ProcessingState.Live);
            card = new Processing.ProcessingQuickView(f.Module); // unshown controls don't refresh on their own
            Assert.Empty(Descendants(card).OfType<System.Windows.Controls.Button>());
            Assert.Equal(Visibility.Visible, Descendants(card).OfType<Processing.SegmentMeter>().Single().Visibility);
        });
    }

    private static ComboBox Picker(DependencyObject view) => Descendants(view).OfType<ComboBox>().Single();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private sealed class UnshownSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
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

}
