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
/// UI Automation's Toggle (Narrator, Voice access) flips a switch without a Click; the panel and the
/// page must still start and stop Keep awake.
/// </summary>
public sealed class KeepAwakePanelTests
{
    [Theory]
    [InlineData("panel")]
    [InlineData("page")]
    public async Task An_automation_toggle_starts_and_stops_keep_awake(string view)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-panel-{Guid.NewGuid():N}.json");
        var module = new KeepAwakeModule(new NullNative());
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await OnSta(() =>
            {
                object root = view == "panel" ? new KeepAwakePanel(module) : new KeepAwakePage(module);
                var toggle = (IToggleProvider)new ToggleButtonAutomationPeer((ToggleButton)((System.Windows.FrameworkElement)root).FindName("Switch"));
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
