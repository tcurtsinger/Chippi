// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
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
            await OnSta(() =>
            {
                var button = view == "island" ? Chip(new KeepAwakeIslandView(module), "1 hr")! : (ToggleButton)new KeepAwakePage(module).FindName("Switch");
                var toggle = (IToggleProvider)new ToggleButtonAutomationPeer(button);
                toggle.Toggle();
                Assert.True(SpinWait.SpinUntil(() => module.IsOn, TimeSpan.FromSeconds(5)));
                toggle.Toggle();
                Assert.True(SpinWait.SpinUntil(() => !module.IsOn, TimeSpan.FromSeconds(5)));
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

    private static Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
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
