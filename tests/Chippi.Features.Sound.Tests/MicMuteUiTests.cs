// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Audio;
using Chippi.Features.Sound.Mute;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Features.Sound.Tests;

/// <summary>Unshown controls, fake audio, fake shortcut registration and fake tones only.</summary>
public sealed class MicMuteUiTests
{
    [Fact]
    public async Task Automation_mute_button_changes_every_fake_microphone()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), Mic("B"));
        await f.Start();
        await Sta(async () =>
        {
            var page = new MicQuickView(f.Module); // the Sound page's Microphone row
            var mute = Children(page).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute all microphones");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(() => page.IsEnabled && f.Module.Status.State == MicrophoneMuteState.Muted);
            Assert.True(f.System.GetLevel("A").Muted);
            Assert.True(f.System.GetLevel("B").Muted);
            Assert.Contains(Children(page).OfType<Button>(), b => AutomationProperties.GetName(b) == "Unmute all microphones");
        });
    }

    [Fact]
    public async Task Automation_tone_switch_changes_setting_without_playing_audio()
    {
        await using var f = new MuteFixture();
        await f.Start();
        await Sta(() =>
        {
            var page = new MicMuteView(f.Module);
            var toggle = Children(page).OfType<ToggleButton>().Single();
            ((IToggleProvider)new ToggleButtonAutomationPeer(toggle)).Toggle();
            Assert.True(f.Module.Settings.PlaySound);
            Assert.True(f.Settings.Value.PlaySound);
            Assert.Empty(f.Tones);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Missing_mic_disables_mute_on_the_sound_page_and_settings_holds_only_the_shortcut()
    {
        await using var f = new MuteFixture();
        await f.Start();
        await Sta(() =>
        {
            var card = Assert.IsType<MicQuickView>(f.Ui.Tiles.Single().Spec.CreateSection());
            Assert.False(Children(card).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute all microphones").IsEnabled);
            Assert.Contains(Children(card).OfType<TextBlock>(), t => t.Text == "No microphone");
            var settings = Assert.IsType<MicMuteView>(f.Ui.Page!(null));
            Assert.DoesNotContain(Children(settings).OfType<Button>(), b => AutomationProperties.GetName(b) == "Mute all microphones");
            Assert.Contains(Children(settings).OfType<Button>(), b => AutomationProperties.GetName(b) == "Change shortcut");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_microphone_row_sets_the_level_of_the_mic_in_use_only_and_says_muted_in_its_place()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), Mic("B")).WithDefault("A");
        await f.Start();
        float other = f.System.GetLevel("B").Volume;
        await Sta(async () =>
        {
            var row = new MicQuickView(f.Module);
            var level = Children(row).OfType<Slider>().Single();
            Assert.Equal("Microphone volume", AutomationProperties.GetName(level));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)level.Parent).Visibility);
            Assert.Contains(Children(row).OfType<TextBlock>(), t => t.Text == "50%");
            level.Value = 40;
            await Until(() => Math.Abs(f.System.GetLevel("A").Volume - 0.4f) < 0.001f);
            Assert.Equal(other, f.System.GetLevel("B").Volume); // the mic in use, not every mic

            await f.Module.ToggleAsync();
            row = new MicQuickView(f.Module); // unshown controls don't refresh on their own
            Assert.Contains(Children(row).OfType<TextBlock>(), t => t.Text == "Muted" && ((FrameworkElement)t.Parent).Visibility == Visibility.Visible);
            Assert.Contains(Children(row).OfType<Button>(), b => AutomationProperties.GetName(b) == "Unmute all microphones");
        });
    }

    [Fact]
    public async Task With_VB_CABLE_as_the_default_mic_and_no_routing_the_row_says_the_state_and_sets_no_level()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), CableOutput()).WithDefault("CableOut");
        await f.Start();
        await Sta(() =>
        {
            var row = new MicQuickView(f.Module);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)Children(row).OfType<Slider>().Single().Parent).Visibility);
            Assert.Contains(Children(row).OfType<TextBlock>(), t => t.Text == "Live" && t.Visibility == Visibility.Visible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_microphone_row_follows_the_mic_routing_has_in_use()
    {
        await using var f = new MuteFixture(microphone: () => "B");
        f.System.With(Mic("A"), Mic("B")).WithDefault("A");
        await f.Start();
        Assert.Equal("B", f.Module.LevelMicrophone);
        Assert.Equal(("B", 0.5f), f.Module.MicLevel);
        await f.Module.SetMicLevelAsync("B", 0.25f);
        Assert.Equal(0.25f, f.System.GetLevel("B").Volume, 3);
        Assert.Equal(0.5f, f.System.GetLevel("A").Volume, 3);
    }

    [Fact]
    public async Task A_level_change_never_sets_VB_CABLE_or_clears_a_mute_failure()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), Mic("B"), CableOutput()).WithDefault("A");
        f.System.Failing.Add("B"); // B won't mute
        await f.Start();
        await f.Module.ToggleAsync();
        Assert.Equal("Mute failed · some mics", f.Module.Error);
        await f.Module.SetMicLevelAsync("A", 0.3f);
        Assert.Equal("Mute failed · some mics", f.Module.Error); // B still isn't muted
        await f.Module.SetMicLevelAsync("CableOut", 0.1f);
        Assert.Equal(0.5f, f.System.GetLevel("CableOut").Volume, 3);
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Children(child)) yield return item;
    }

    private static async Task Until(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(3);
        while (!done()) { Assert.True(DateTime.UtcNow < end); await Task.Delay(10); }
    }

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
            if (error is null) done.SetResult(); else done.SetException(error);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}