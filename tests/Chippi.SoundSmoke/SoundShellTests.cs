// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Flyout;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.App.Settings;
using Chippi.App.Settings.Pages;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Routing;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;

namespace Chippi.SoundSmoke;

public sealed class SoundShellTests
{
    [Fact]
    public Task Shared_sound_tab_contains_all_modules_and_automation_mutes_only_the_fake_mics() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var flyout = new FlyoutWindow(scene.Shell);
        try
        {
            flyout.Rebuild();
            Assert.Equal(["Sound"], flyout.TabNames);
            flyout.SelectTab("Sound");
            var page = (ContentControl)flyout.FindName("Page");
            Assert.Single(All<RoutingQuickView>(page));
            Assert.Single(All<ProcessingQuickView>(page));
            Assert.Single(All<VolumeView>(page));
            Assert.Single(All<MicMuteView>(page));
            Invoke(Named<Button>(page, "Mute all microphones"));
            await SmokeDispatcher.Until(() => scene.Mute.StatusText == "Muted");
            Assert.True(scene.Devices.GetLevel("desk").Muted);
            Assert.True(scene.Devices.GetLevel("CableOut").Muted);
            await SmokeDispatcher.Until(() => ((TextBlock)flyout.FindName("StatusLine")).Text == "Microphone muted");
            Assert.Contains(scene.Shell.Island.Activities, a => a.TileKey == "sound.microphone" && a.NeedsAttention);
            // Rebuild is the real registry refresh path. It must not double or lose a section.
            await scene.Shell.Registry.SetInstalledAsync("app-volume", false);
            flyout.Rebuild();
            Assert.Empty(All<VolumeView>(page));
            Assert.Single(All<MicMuteView>(page));
            await scene.Shell.Registry.SetInstalledAsync("app-volume", true);
            flyout.Rebuild();
            Assert.Single(All<VolumeView>(page));
            Assert.Equal(["Sound"], flyout.TabNames);
        }
        finally { flyout.Close(); }
    });

    [Fact]
    public Task Settings_routes_expose_real_controls_and_edits_reach_the_shared_fake_engine() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var settings = new SettingsWindow(scene.Shell, "module:audio-routing");
        try
        {
            Assert.Single(All<RoutingPage>(settings.PageContent));
            var calls = scene.Routing.Settings.Output.Calls;
            Toggle(Named<ToggleButton>(settings.PageContent, "Switch call audio too"));
            await SmokeDispatcher.Until(() => scene.Routing.Settings.Output.Calls != calls);
            settings.Navigate("module:audio-routing/microphone");
            Assert.Single(All<RoutingPage>(settings.PageContent));
            Assert.NotNull(Named<ToggleButton>(settings.PageContent, "Pin the call mic too"));

            settings.Navigate("module:mic-processing");
            Assert.Single(All<ProcessingPage>(settings.PageContent));
            Toggle(Named<ToggleButton>(settings.PageContent, "Mic processing"));
            await SmokeDispatcher.Until(() => scene.Pipelines.Live is not null);
            Assert.Equal("desk", scene.Pipelines.Live!.Source);

            settings.Navigate("module:app-volume");
            Assert.Single(All<VolumeView>(settings.PageContent));
            var slider = Named<Slider>(settings.PageContent, "Music volume");
            var peer = new SliderAutomationPeer(slider);
            ((IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)).SetValue(23);
            await SmokeDispatcher.Until(() => Math.Abs(scene.Audio.Snapshot.Apps.Single().Volume - .23f) < .001);

            settings.Navigate("module:mic-mute");
            Assert.Single(All<MicMuteView>(settings.PageContent));
            Invoke(Named<Button>(settings.PageContent, "Mute all microphones"));
            await SmokeDispatcher.Until(() => scene.Devices.GetLevel("desk").Muted);
            settings.Navigate("module:audio-routing");
            Assert.NotNull(Named<ToggleButton>(settings.PageContent, "Switch outputs automatically"));
            Assert.Equal("module:audio-routing", settings.CurrentPage);
        }
        finally { settings.Close(); }
    });

    [Fact]
    public Task Add_features_requirement_states_use_fake_install_and_settings_actions() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        await scene.Shell.Registry.SetInstalledAsync("mic-processing", false);
        var settings = new SettingsWindow(scene.Shell, "features");
        var page = Assert.Single(All<AddFeaturesPage>(settings.PageContent));
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            scene.Requirement.Change(RequirementState.Missing);
            await SmokeDispatcher.Until(() => All<Button>(page).Any(b => AutomationProperties.GetName(b) == "Install Driver, Mic processing"));
            Invoke(Named<Button>(page, "Install Driver, Mic processing"));
            await SmokeDispatcher.Until(() => scene.Requirement.Installs == 1 && Texts(page).Contains("Restart to finish"));
            Assert.False(scene.Shell.Registry.Find("mic-processing")!.IsInstalled);

            scene.Requirement.Change(RequirementState.Installing);
            await SmokeDispatcher.Until(() => All<Button>(page).Any(b => Equals(b.Content, "Installing")));
            Assert.False(All<Button>(page).Single(b => Equals(b.Content, "Installing")).IsEnabled);
            scene.Requirement.Change(RequirementState.Disabled);
            await SmokeDispatcher.Until(() => All<Button>(page).Any(b => AutomationProperties.GetName(b) == "Sound settings, Mic processing"));
            Invoke(Named<Button>(page, "Sound settings, Mic processing"));
            await SmokeDispatcher.Until(() => scene.Requirement.SettingsOpened == 1);
            Assert.Contains("Driver disabled", Texts(page));
            scene.Requirement.Change(RequirementState.Unsupported);
            await SmokeDispatcher.Until(() => Texts(page).Contains("Not supported"));
            scene.Requirement.Change(RequirementState.Ready);
            await SmokeDispatcher.Until(() => All<ToggleButton>(page).Any(b => AutomationProperties.GetName(b) == "Mic processing"));
            Toggle(Named<ToggleButton>(page, "Mic processing"));
            await SmokeDispatcher.Until(() => scene.Shell.Registry.Find("mic-processing")!.IsRunning);
        }
        finally
        {
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            settings.Close();
        }
    });

    [Fact]
    public Task Island_real_sound_sections_share_pages_and_removed_tiles_fall_back_to_grid() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var panel = new IslandPanelWindow();
        var media = new FakeMedia();
        IReadOnlyList<IslandTileGroup> Tiles() => IslandTileContribution.Group(scene.Shell.Registry.Features.SelectMany(e => e.IslandTiles));
        try
        {
            panel.SetMedia(media);
            panel.SetTiles(Tiles());
            panel.Initialize();
            var offscreen = new MonitorArea(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);
            panel.Open(IslandEdge.Top, new Rect(-19100, 0, 112, 32), offscreen);
            var host = (ContentControl)panel.FindName("Page");
            var grid = Assert.IsType<IslandGrid>(host.Content);
            Assert.Equal(["Now Playing", "Volume mixer", "Output", "Microphone"], grid.Buttons().Select(b => AutomationProperties.GetName(b).Split(',')[0]));

            Invoke(grid.Buttons().Single(b => AutomationProperties.GetName(b).StartsWith("Microphone,", StringComparison.Ordinal)));
            await SmokeDispatcher.Until(() => panel.CurrentPage == "sound.microphone");
            Assert.Single(All<RoutingQuickView>(host));
            Assert.Single(All<ProcessingQuickView>(host));
            Assert.Single(All<MicMuteView>(host));
            Invoke(Named<Button>(host, "Mute all microphones"));
            await SmokeDispatcher.Until(() => scene.Mute.StatusText == "Muted");
            panel.SetActivities(scene.Shell.Island.Activities, PetMood.Idle);

            panel.ShowPage("sound.output");
            Assert.Single(All<RoutingQuickView>(host));
            Assert.Single(All<VolumeView>(host));
            panel.ShowPage("sound.mixer");
            Assert.NotNull(Named<ComboBox>(host, "Mixer output"));
            Assert.NotNull(Named<Slider>(host, "Music volume"));
            panel.ShowPage("now-playing");
            Invoke(Named<Button>(host, "Play"));
            await SmokeDispatcher.Until(() => media.Plays == 1);
            panel.ShowPage("sound.mixer");
            await scene.Shell.Registry.SetInstalledAsync("app-volume", false);
            panel.SetTiles(Tiles());
            Assert.Null(panel.CurrentPage);
            Assert.DoesNotContain(Assert.IsType<IslandGrid>(host.Content).Buttons(), b => AutomationProperties.GetName(b).StartsWith("Volume mixer,", StringComparison.Ordinal));
        }
        finally { panel.Close(); media.Dispose(); }
    });

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in All<T>(child)) yield return item;
    }
    private static IEnumerable<string> Texts(DependencyObject root) => All<TextBlock>(root).Select(t => t.Text);
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Assert.Single(All<T>(root), c => AutomationProperties.GetName(c) == name);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
}
