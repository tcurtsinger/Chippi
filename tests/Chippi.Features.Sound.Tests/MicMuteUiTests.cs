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
            var page = new MicMuteView(f.Module, compact: true); // the Sound page's mic card
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
            var card = Assert.IsType<MicMuteView>(f.Ui.Tiles.Single().Spec.CreateSection());
            Assert.False(Children(card).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute all microphones").IsEnabled);
            var settings = Assert.IsType<MicMuteView>(f.Ui.Page!(null));
            Assert.DoesNotContain(Children(settings).OfType<Button>(), b => AutomationProperties.GetName(b) == "Mute all microphones");
            Assert.Contains(Children(settings).OfType<Button>(), b => AutomationProperties.GetName(b) == "Change shortcut");
            return Task.CompletedTask;
        });
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