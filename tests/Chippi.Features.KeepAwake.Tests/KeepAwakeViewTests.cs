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

    private static ToggleButton? Chip(System.Windows.DependencyObject root, string label) =>
        root is ToggleButton { Content: string text } chip && text == label ? chip
        : System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>().Select(c => Chip(c, label)).FirstOrDefault(c => c is not null);

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
}
