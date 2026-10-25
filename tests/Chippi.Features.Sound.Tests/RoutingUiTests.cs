// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using static Chippi.Audio.Tests.FakeDeviceSystem;

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
            var toggle = Descendants(page).OfType<ToggleButton>().Single(t => AutomationProperties.GetName(t) == "Switch outputs automatically");
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
    public async Task Contributions_create_the_expected_views_and_share_sound_tab()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Mic("M")).WithDefault("A").WithDefault("M");
        await f.Start();
        await Sta(() =>
        {
            Assert.IsType<RoutingPage>(f.Ui.Page!("microphone"));
            Assert.IsType<RoutingQuickView>(Assert.Single(f.Ui.Flyouts).CreateView());
            Assert.All(f.Ui.Tiles, t => Assert.IsType<RoutingQuickView>(t.Spec.CreateSection()));
        });
        Assert.Equal("sound", f.Ui.Flyouts[0].TabKey);
        Assert.Equal("M", f.Ui.Tiles[1].Status);
    }

    [Fact]
    public async Task Off_island_choices_are_disabled_and_unlisted_default_still_shows_in_flyout()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("Windows")).WithDefault("A");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        await f.Module.RemoveAsync(AudioFlow.Render, "Windows");
        f.System.ChangeDefault("Windows");
        await f.Audio.RefreshAsync();
        await Sta(() =>
        {
            var island = new RoutingQuickView(f.Module, AudioFlow.Render, true);
            var prefer = Descendants(island).OfType<System.Windows.Controls.Button>().Single(b => AutomationProperties.GetName(b) == "Prefer A");
            Assert.False(prefer.IsEnabled);
            var flyout = new RoutingQuickView(f.Module);
            var combo = Descendants(flyout).OfType<System.Windows.Controls.ComboBox>().First();
            Assert.Equal("Windows", Assert.IsType<PriorityEntry>(combo.SelectedItem).Name);
        });
    }
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