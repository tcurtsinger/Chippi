// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
    public void Hide_tray_icon_needs_the_island()
    {
        WpfTestThread.Run(() => WithPage((page, config) =>
        {
            var island = Named<ToggleButton>(page, "Island");
            var hide = Named<ToggleButton>(page, "Hide tray icon");
            Assert.True(hide.IsEnabled);

            Toggle(hide);
            Assert.True(Read(config).HideTrayIcon);

            Toggle(island);
            Assert.False(Read(config).IslandEnabled);
            Assert.False(hide.IsEnabled); // the tray icon is the only way in while the island is off
        }));
    }

    [Fact]
    public void The_fixed_now_playing_card_has_no_show_switch()
    {
        WpfTestThread.Run(() => WithPage((page, _) =>
        {
            Assert.DoesNotContain(All<ToggleButton>(page),
                button => AutomationProperties.GetName(button) == "Show Now Playing");
        }));
    }

    [Fact]
    public void Turning_a_tile_off_frees_its_slot_for_the_switch_turned_on_next_not_the_next_tile_in_order()
    {
        // Ten tiles: "i" is past the home's eight, so its switch shows off; "j" was turned off.
        string[] order = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];
        var hidden = Chippi.App.Island.IslandLayout.Switched(["j"], order, "a", show: false, room: 8);
        Assert.Equal(["j", "i", "a"], hidden); // "i" stays off rather than taking "a"'s slot
        hidden = Chippi.App.Island.IslandLayout.Switched(hidden, order, "j", show: true, room: 8);
        Assert.Equal(["i", "a"], hidden); // so "j" can have it
        hidden = Chippi.App.Island.IslandLayout.Switched(hidden, order, "b", show: false, room: 8);
        Assert.Equal(["i", "a", "b"], hidden); // with room, turning one off hides nothing else
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

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in All<T>(child))
                yield return item;
    }
}
