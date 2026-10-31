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
    public async Task The_lit_card_reads_the_time_left_and_until_opens_a_time_picker_in_the_page()
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
                var hour = Chip(view, "1 hr")!;
                hour.IsChecked = true;
                await Until(() => module.IsOn && hour.IsChecked == true);
                // Lit: the length over the time left, the one live fact on the page.
                Assert.Equal(["1 hr", "1 hr left"], Lines(hour));
                Assert.Equal("1 hr, 1 hr left", System.Windows.Automation.AutomationProperties.GetName(hour));

                // Until… swaps the cards for the picker, at the last Until time; a step and Start run until then.
                var until = Chip(view, "Until…")!;
                until.IsChecked = true;
                var picker = Find<System.Windows.Controls.StackPanel>(view, p => System.Windows.Automation.AutomationProperties.GetName(p) == "End time")!;
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
                Assert.Equal($"Until {KeepAwakeText.Until(18 * 60 + 5)}", Lines(until)[0]);

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
                var failed = Find<System.Windows.Controls.TextBlock>(view, t => t.Text == "Keep awake failed")!;
                await Until(() => failed.Visibility == System.Windows.Visibility.Visible);
                Assert.Null(Find<System.Windows.Controls.TextBlock>(view, t => t.Text.Contains(FailingNative.Message)));
            });
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    // The island's cards carry their fixed label as Tag (their content is two lines); the Settings page's chips are text.
    private static ToggleButton? Chip(System.Windows.DependencyObject root, string label) =>
        Find<ToggleButton>(root, chip => chip.Tag as string == label || chip.Content as string == label);

    private static T? Find<T>(System.Windows.DependencyObject root, Func<T, bool> where) where T : System.Windows.DependencyObject =>
        root is T hit && where(hit) ? hit
        : System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>().Select(c => Find(c, where)).FirstOrDefault(c => c is not null);

    // A card's visible lines: its length (or Stop) and, lit, the time left.
    private static string[] Lines(ToggleButton card) =>
        [.. ((System.Windows.Controls.Panel)card.Content).Children.OfType<System.Windows.Controls.TextBlock>()
            .Where(t => t.Visibility == System.Windows.Visibility.Visible).Select(t => t.Text)];

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
