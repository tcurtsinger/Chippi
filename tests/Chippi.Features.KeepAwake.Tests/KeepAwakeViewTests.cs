// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.KeepAwake.Tests;

/// <summary>
/// UI Automation's Toggle (Narrator, Voice access) flips a switch or chip without a Click; the island
/// page and the Settings page must still start and stop Keep awake.
/// </summary>
public sealed class KeepAwakeViewTests
{
    [Theory]
    [InlineData("island")]
    [InlineData("page")]
    public async Task An_automation_toggle_starts_and_stops_keep_awake(string view)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-view-{Guid.NewGuid():N}.json");
        var module = new KeepAwakeModule(new NullNative());
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await OnSta(async () =>
            {
                var button = view == "island" ? Chip(new KeepAwakeIslandView(module), "1 hr")! : Chip(new KeepAwakePage(module), "1 hr")!;
                var toggle = (IToggleProvider)new ToggleButtonAutomationPeer(button);
                toggle.Toggle();
                await Until(() => module.IsOn && button.IsChecked == true); // the view shows it on, as a user would see
                toggle.Toggle();
                await Until(() => !module.IsOn);
            });
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task The_top_row_reads_the_state_with_chippi_and_stop_and_until_opens_a_time_picker()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-view-{Guid.NewGuid():N}.json");
        var module = new KeepAwakeModule(new NullNative());
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await OnSta(async () =>
            {
                var view = new KeepAwakeIslandView(module);
                var pet = Find<Chippi.UI.Pet.ChippiPet>(view, _ => true)!;
                var stop = Find<System.Windows.Controls.Button>(view, b => b.Content as string == "Stop")!;
                // Off: Chippi asleep beside "Off", and no Stop.
                Assert.Equal(Chippi.UI.Pet.PetMood.Sleepy, pet.Mood);
                Assert.NotNull(Find<System.Windows.Controls.TextBlock>(view, t => t.Text == "Off"));
                Assert.NotEqual(System.Windows.Visibility.Visible, stop.Visibility);

                var hour = Chip(view, "1 hr")!;
                hour.IsChecked = true;
                await Until(() => module.IsOn && hour.IsChecked == true);
                // On: Chippi wide awake beside the time left, when it ends, and Stop; the lit card just keeps its length.
                Assert.Equal(Chippi.UI.Pet.PetMood.WideAwake, pet.Mood);
                Assert.NotNull(Find<System.Windows.Controls.TextBlock>(view, t => t.Text == "1 hr left"));
                Assert.NotNull(Find<System.Windows.Controls.TextBlock>(view, t => t.Text == $"Until {module.Deadline!.Value.LocalDateTime:t}"));
                Assert.Equal("1 hr", hour.Content);
                Assert.Equal(System.Windows.Visibility.Visible, stop.Visibility);
                Click(stop);
                await Until(() => !module.IsOn && hour.IsChecked == false);
                Assert.NotEqual(System.Windows.Visibility.Visible, stop.Visibility);
                hour.IsChecked = true;
                await Until(() => module.IsOn);

                // Until… swaps the cards for the picker, at the last Until time; a step and Start run until then.
                var until = Chip(view, "Until…")!;
                until.IsChecked = true;
                var picker = Find<System.Windows.Controls.Panel>(view, p => System.Windows.Automation.AutomationProperties.GetName(p) == "End time")!;
                Assert.Equal(System.Windows.Visibility.Visible, picker.Visibility);
                Assert.True(module.IsOn); // opening the picker starts nothing
                Click(Find<System.Windows.Controls.Button>(view, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Minute up")!);
                var now = DateTime.Now;
                string day = 18 * 60 + 5 <= now.Hour * 60 + now.Minute ? "Tomorrow" : "Today"; // a time already past is tomorrow's
                Assert.NotNull(Find<System.Windows.Controls.TextBlock>(picker, t => t.Text == day));
                Click(Find<System.Windows.Controls.Button>(view, b => b.Content as string == "Start")!);
                await Until(() => module.Active?.Mode == AwakeMode.UntilTime);
                Assert.Equal(18 * 60 + 5, module.Active!.UntilMinute);
                Assert.NotEqual(System.Windows.Visibility.Visible, picker.Visibility);
                await Until(() => until.IsChecked == true);
                Assert.Equal($"Until {KeepAwakeText.Until(18 * 60 + 5)}", until.Content);
                // An Until session's state is the time picked (the card's text isn't a TextBlock of its own), its detail the time left.
                Assert.NotNull(Find<System.Windows.Controls.TextBlock>(view, t => t.Text == $"Until {KeepAwakeText.Until(18 * 60 + 5)}"));
                Assert.NotNull(Find<System.Windows.Controls.TextBlock>(view, t => t.Text.EndsWith(" left", StringComparison.Ordinal)));

                // Esc (the module's HandleEscape) leaves the picker before it closes the island.
                Assert.False(view.CloseUntil());
                view.OpenUntil();
                Assert.True(view.CloseUntil());
                Assert.NotEqual(System.Windows.Visibility.Visible, picker.Visibility);
            });
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_failed_start_says_keep_awake_failed_never_the_exception()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-view-{Guid.NewGuid():N}.json");
        var module = new KeepAwakeModule(new FailingNative());
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await OnSta(async () =>
            {
                var view = new KeepAwakeIslandView(module);
                Chip(view, "30 min")!.IsChecked = true;
                await Until(() => Find<System.Windows.Controls.TextBlock>(view, t => t.Text == "Keep awake failed") is { Visibility: System.Windows.Visibility.Visible });
                Assert.Null(Find<System.Windows.Controls.TextBlock>(view, t => t.Text.Contains(FailingNative.Message)));
                // It's the line under the state it explains, "Off".
                var failed = Find<System.Windows.Controls.TextBlock>(view, t => t.Text == "Keep awake failed")!;
                var readout = (System.Windows.Controls.Panel)failed.Parent;
                Assert.Equal("Off", ((System.Windows.Controls.TextBlock)readout.Children[0]).Text);
                Assert.Same(failed, readout.Children[1]);
            });
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_failure_overtaken_by_a_later_change_never_shows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-view-{Guid.NewGuid():N}.json");
        var module = new KeepAwakeModule(new FailingOnceNative());
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await OnSta(async () =>
            {
                var view = new KeepAwakeIslandView(module);
                // 30 min fails, but 1 hr was pressed before that came back, and 1 hr runs: the page shows it running, no error.
                Chip(view, "30 min")!.IsChecked = true;
                Chip(view, "1 hr")!.IsChecked = true;
                await Until(() => module.IsOn && Chip(view, "1 hr")!.IsChecked == true);
                await Task.Delay(100);
                Assert.Null(Find<System.Windows.Controls.TextBlock>(view, t => t.Text == "Keep awake failed"));
            });
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task The_picker_steps_once_per_wheel_notch_checks_its_day_as_the_minute_turns_and_starts_on_enter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-view-{Guid.NewGuid():N}.json");
        var module = new KeepAwakeModule(new NullNative());
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await OnSta(async () =>
            {
                // A moment before 6:00 PM, the picker opens at 6:00 PM (the first Until time): today.
                var view = new KeepAwakeIslandView(module) { Now = () => new DateTime(2026, 10, 30, 17, 59, 59, 900) };
                view.OpenUntil();
                var picker = Find<System.Windows.Controls.Panel>(view, p => System.Windows.Automation.AutomationProperties.GetName(p) == "End time")!;
                var day = Find<System.Windows.Controls.TextBlock>(picker, t => t.Text is "Today" or "Tomorrow")!;
                Assert.Equal("Today", day.Text);
                // The minute turns with the picker still open: 6:00 PM is now tomorrow's, and the picker says so.
                view.Now = () => new DateTime(2026, 10, 30, 18, 0, 0, 100);
                await Until(() => day.Text == "Tomorrow");

                // A touchpad reports a notch in parts: they add up to one step; turning back drops a part notch.
                var minute = Find<System.Windows.Controls.Border>(picker, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Minute")!;
                var value = Find<System.Windows.Controls.TextBlock>(minute, t => t.Text == "00")!;
                Wheel(minute, 40);
                Wheel(minute, 40);
                Assert.Equal("00", value.Text);
                Wheel(minute, 40);
                Assert.Equal("05", value.Text);
                Wheel(minute, 240);
                Assert.Equal("15", value.Text);
                Wheel(minute, 60);
                Wheel(minute, -120);
                Assert.Equal("10", value.Text);

                // Enter on a column starts, as Start does.
                minute.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new UnshownSource(), 0, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent });
                Assert.NotEqual(System.Windows.Visibility.Visible, picker.Visibility);
                await Until(() => module.Active?.Mode == AwakeMode.UntilTime);
                Assert.Equal(18 * 60 + 10, module.Active!.UntilMinute);
            });
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    private sealed class UnshownSource : System.Windows.PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static void Wheel(System.Windows.UIElement target, int delta) =>
        target.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) { RoutedEvent = System.Windows.UIElement.MouseWheelEvent });

    // The island's cards carry their fixed label as Tag (a lit Until card reads its time); the Settings page's chips are text.
    private static ToggleButton? Chip(System.Windows.DependencyObject root, string label) =>
        Find<ToggleButton>(root, chip => chip.Tag as string == label || chip.Content as string == label);

    private static T? Find<T>(System.Windows.DependencyObject root, Func<T, bool> where) where T : System.Windows.DependencyObject =>
        root is T hit && where(hit) ? hit
        : System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>().Select(c => Find(c, where)).FirstOrDefault(c => c is not null);


    private static void Click(ButtonBase button) => button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Keep awake did not change after the toggle.");
            await Task.Delay(10);
        }
    }

    /// <summary>Runs on an STA thread whose dispatcher pumps, so the views' queued refreshes run while the test waits.</summary>
    private static Task OnSta(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private sealed class NullNative : IExecutionState
    {
        public void Set(bool active, bool display) { }
    }

    // Fails its first start only, as a one-off native failure would.
    private sealed class FailingOnceNative : IExecutionState
    {
        private int _starts;

        public void Set(bool active, bool display)
        {
            if (active && Interlocked.Increment(ref _starts) == 1)
                throw new System.ComponentModel.Win32Exception(6, "The handle is invalid");
        }
    }

    private sealed class FailingNative : IExecutionState
    {
        public const string Message = "The handle is invalid";

        public void Set(bool active, bool display)
        {
            if (active)
                throw new System.ComponentModel.Win32Exception(6, Message);
        }
    }
}
