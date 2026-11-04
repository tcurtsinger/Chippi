// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

public sealed class SettingsSurfaceTests
{
    [Fact]
    public void A_settings_switch_changes_gallery_visibility_without_running_the_action()
    {
        WpfTestThread.Run(() =>
        {
            var pc = new FakePc();
            var store = new Store();
            var module = new QuickActionsModule(() => pc, () => new NoHotkey());
            module.StartAsync(new ModuleContext(QuickActionsModule.Id, store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new NoUi()), default).GetAwaiter().GetResult();
            var view = new QuickActionsSettingsPage(module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var toggle = Find<ToggleButton>(view).Single(t => AutomationProperties.GetName(t) == "Show Lock in Actions");
                toggle.ApplyTemplate();
                Assert.Equal("Hidden", toggle.Content);
                var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
                peer.Toggle();
                Assert.True(module.Settings.Shows(QuickActionId.Lock));
                Assert.Equal("Shown", toggle.Content);
                Assert.Equal("Shown", AutomationProperties.GetItemStatus(toggle));
                peer.Toggle();
                Assert.False(module.Settings.Shows(QuickActionId.Lock));
                Assert.Equal("Hidden", toggle.Content);
                Assert.Empty(pc.Calls);
            }
            finally
            {
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                module.StopAsync(default).GetAwaiter().GetResult();
            }
        });
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class Store : IModuleSettings
    {
        private QuickActionsSettings _value = new();
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class => _value = (QuickActionsSettings)(object)value;
    }

    private sealed class NoUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }

    private sealed class NoHotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
