// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Settings › Island &amp; pet: Hide tray icon only while the island is on, and the tiles' switches.</summary>
public sealed class IslandPetPageTests
{
    [Fact]
    public void Hide_tray_icon_needs_the_island_and_says_how_to_get_the_icon_back()
    {
        WpfTestThread.Run(() => WithPage((page, config) =>
        {
            var island = Named<ToggleButton>(page, "Island");
            var hide = Named<ToggleButton>(page, "Hide tray icon");
            Assert.True(hide.IsEnabled);
            Assert.DoesNotContain("To get it back, start Chippi from Start", Visible(page));

            Toggle(hide);
            Assert.True(Read(config).HideTrayIcon);
            Assert.Contains("To get it back, start Chippi from Start", Visible(page));

            Toggle(island);
            Assert.False(Read(config).IslandEnabled);
            Assert.False(hide.IsEnabled); // the tray icon is the only way in while the island is off
            Assert.DoesNotContain("To get it back, start Chippi from Start", Visible(page));
        }));
    }

    [Fact]
    public void A_tiles_switch_leaves_it_off_the_grid()
    {
        WpfTestThread.Run(() => WithPage((page, config) =>
        {
            Toggle(Named<ToggleButton>(page, "Show Now Playing"));
            Assert.Equal(["now-playing"], Read(config).IslandHiddenTiles);
            Toggle(Named<ToggleButton>(page, "Show Now Playing"));
            Assert.Empty(Read(config).IslandHiddenTiles!);
        }));
    }

    private static void WithPage(Action<IslandPetPage, ConfigStore> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-islandpet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = new ConfigStore(Path.Combine(root, "settings.json"));
            var shell = new ShellContext(config, NullLoggerFactory.Instance, false, "islandpet-test");
            test(new IslandPetPage(shell), config);
        }
        finally { Directory.Delete(root, true); }
    }

    private static AppSettings Read(ConfigStore config) => config.GetSection<AppSettings>(AppSettings.SectionName);

    private static void Toggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button)).Toggle();

    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject =>
        Assert.Single(All<T>(root), e => AutomationProperties.GetName(e) == name);

    private static IEnumerable<string> Visible(DependencyObject root) =>
        All<TextBlock>(root).Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text);

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in All<T>(child))
                yield return item;
    }
}
