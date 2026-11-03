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
using Chippi.UI.Pet;
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
    public void The_fixed_now_playing_link_has_no_show_switch()
    {
        WpfTestThread.Run(() => WithPage((page, _) =>
        {
            Assert.DoesNotContain(All<ToggleButton>(page),
                button => AutomationProperties.GetName(button) == "Show Now Playing");
        }));
    }

    [Fact]
    public void Hiding_one_tool_preserves_other_tools_and_saved_choices_for_uninstalled_features()
    {
        string[] order = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k"];
        var hidden = Chippi.App.Island.IslandLayout.Switched(["uninstalled"], "a", show: false);
        Assert.Equal(["uninstalled", "a"], hidden);
        var layout = Chippi.App.Island.IslandLayout.Default with { Hidden = hidden.ToHashSet() };
        Assert.Equal(order.Skip(1), layout.Shown(order));

        hidden = Chippi.App.Island.IslandLayout.Switched(hidden, "a", show: true);
        Assert.Equal(["uninstalled"], hidden);
        Assert.Equal(order, (layout with { Hidden = hidden.ToHashSet() }).Shown(order));
    }

    [Fact]
    public void Picking_a_color_makes_the_preview_celebrate_in_it_and_a_click_shows_the_next_mood()
    {
        WpfTestThread.Run(() => WithPage((page, config) =>
        {
            var preview = Named<Button>(page, "Next mood");
            var pet = Assert.IsType<ChippiPet>(preview.Content);
            Assert.Equal(PetMood.Idle, pet.Mood);
            Assert.True(pet.IsDecorative); // the button carries the name
            Assert.Equal(PetSize.Preview, pet.Size); // his puck size, in a 44 button: with the row's padding, a captioned row's 60
            Assert.Equal(44, preview.Height);

            // The tiles' little puck draws the real puck's face, at his share of it (40 of 50), not the small form's.
            var puck = All<ChippiPet>(page).Single(p => p != pet);
            Assert.Equal(24, puck.Size);
            Assert.True(puck.Size > ChippiPet.SmallFormMaxSize);

            Named<RadioButton>(page, "Ember").IsChecked = true;
            Assert.Equal(PetColor.Ember, pet.PetColor);
            Assert.Equal(PetMood.Celebrating, pet.Mood);
            Assert.Equal(PetColor.Ember, Read(config).PetColor);

            foreach (var mood in IslandPetPage.PreviewMoods.Skip(1))
            {
                preview.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(mood, pet.Mood);
            }

            // Reduce motion is General's: it covers the whole app, not only Chippi.
            Assert.DoesNotContain(All<ComboBox>(page), combo => AutomationProperties.GetName(combo) == "Reduce motion");
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

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in All<T>(child))
                yield return item;
    }
}
