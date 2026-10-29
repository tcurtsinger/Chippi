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
    public async Task Routing_puts_output_beside_microphone_on_the_sound_page_and_its_tile_names_the_output()
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
    public async Task A_pick_works_with_priority_switching_off_and_only_connected_devices_are_listed()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Speaker("Gone")).WithDefault("A");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        f.System.Disconnect("Gone");
        await f.Audio.RefreshAsync();
        await Sta(async () =>
        {
            var page = new RoutingQuickView(f.Module, AudioFlow.Render);
            var rows = Descendants(page).OfType<System.Windows.Controls.Button>().Select(AutomationProperties.GetName).ToList();
            Assert.Equal(["Mute system volume", "A, in use", "Use B"], rows); // the card's mute, then connected outputs only
            var pick = Descendants(page).OfType<System.Windows.Controls.Button>().Single(b => AutomationProperties.GetName(b) == "Use B");
            ((IInvokeProvider)new ButtonAutomationPeer(pick)).Invoke();
            await Until(() => !Descendants(page).Contains(pick)); // rebuilt once the pick has finished
        });
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal("B", f.Module.Settings.Output.Devices[0].Id);
        Assert.False(f.Module.Settings.Output.Enabled); // the pick is set once; switching stays off
        f.System.ChangeDefault("A");
        await f.Audio.RefreshAsync();
        Assert.Equal("A", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
    }
    [Fact]
    public async Task Sound_rows_are_one_line_and_only_the_rows_that_fit_show()
    {
        await using var f = new RoutingFixture();
        f.System.With([.. Enumerable.Range(1, 9).Select(i => Speaker($"S{i}")), .. Enumerable.Range(1, 7).Select(i => Mic($"M{i}"))]).WithDefault("S1").WithDefault("M1");
        await f.Start();
        await Sta(() =>
        {
            foreach (var (flow, fit) in new[] { (AudioFlow.Render, 7), (AudioFlow.Capture, 5) })
            {
                var view = new RoutingQuickView(f.Module, flow);
                var rows = Descendants(view).OfType<System.Windows.Controls.Button>().Where(b => b.Tag is "Selected" || AutomationProperties.GetName(b).StartsWith("Use ", StringComparison.Ordinal)).ToList();
                Assert.Equal(fit, rows.Count); // the rest are in Settings
                Assert.All(rows, row => Assert.Single(Descendants(row).OfType<System.Windows.Controls.TextBlock>())); // the name only, no state line
            }
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
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)Descendants(view).OfType<System.Windows.Controls.Slider>().Single().Parent).Visibility);
            Assert.False(Descendants(view).OfType<System.Windows.Controls.Button>().Single(b => AutomationProperties.GetName(b) == "Mute system volume").IsEnabled);
            Assert.DoesNotContain(Descendants(view).OfType<System.Windows.Controls.Button>(), b => AutomationProperties.GetName(b).StartsWith("Use ", StringComparison.Ordinal));
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