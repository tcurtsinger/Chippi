// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.App.Settings;
using Chippi.App.Settings.Pages;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.Sound;
using Chippi.Features.Sound.Mute;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Routing;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;

namespace Chippi.SoundSmoke;

public sealed class SoundShellTests
{
    [Fact]
    public Task The_sound_page_holds_every_sound_feature_and_automation_mutes_only_the_fake_mics() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var panel = OpenPanel(scene, new FakeMedia());
        try
        {
            var host = (ContentControl)panel.FindName("Page");
            panel.ShowPage(SoundTile.Key);
            Assert.Equal(2, All<RoutingQuickView>(host).Count()); // Output and Microphone
            Assert.Single(All<ProcessingQuickView>(host));
            Assert.Single(All<MicMuteView>(host));
            Assert.Empty(All<VolumeView>(host)); // App volume has its own page
            // Two 244 columns inside the 496 x 304 body, nothing to scroll (DESIGN.md §5, revised 2026-10-28).
            panel.UpdateLayout();
            var body = panel.PageBody!;
            Assert.Empty(All<ScrollViewer>(host));
            Assert.All(All<RoutingQuickView>(host), v => Assert.Equal(244, v.ActualWidth));
            var processing = Assert.Single(All<ProcessingQuickView>(host));
            Assert.InRange(processing.TranslatePoint(new Point(0, processing.ActualHeight), body).Y, 0, body.ActualHeight);
            Assert.Equal(44, Assert.IsType<Border>(((StackPanel)processing.Content).Children[0]).ActualHeight);
            Assert.Contains("Mic processing", Texts(processing));
            Invoke(Named<Button>(host, "Mute all microphones"));
            await SmokeDispatcher.Until(() => scene.Mute.StatusText == "Muted");
            Assert.True(scene.Devices.GetLevel("desk").Muted);
            Assert.True(scene.Devices.GetLevel("CableOut").Muted);
            Assert.Contains(scene.Shell.Island.Activities, a => a.TileKey == SoundTile.Key && a.NeedsAttention);
            Assert.Equal("Mic muted", Assert.Single(Tiles(scene), t => t.Key == SoundTile.Key).Status);
            // A registry refresh must not double or lose a section.
            await scene.Shell.Registry.SetInstalledAsync("mic-mute", false);
            panel.SetTiles(Tiles(scene));
            Assert.Empty(All<MicMuteView>(host));
            Assert.Equal(2, All<RoutingQuickView>(host).Count());
            await scene.Shell.Registry.SetInstalledAsync("mic-mute", true);
            panel.SetTiles(Tiles(scene));
            Assert.Single(All<MicMuteView>(host));
        }
        finally { panel.Close(); }
    });

    [Fact]
    public Task Settings_groups_sound_by_task_and_edits_reach_the_shared_fake_engine() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var settings = new SettingsWindow(scene.Shell, "module:audio-routing");
        try
        {
            // Sound is Output, Microphone and App volume, whichever features hold them.
            Assert.Equal(["Now Playing", "Output", "Microphone", "App volume"], settings.NavigationPages.TakeWhile(p => p.Id != "general").Select(p => p.Label));
            Assert.Equal("page:sound-output", settings.CurrentPage);
            Assert.Single(All<RoutingPage>(settings.PageContent));
            var calls = scene.Routing.Settings.Output.Calls;
            Toggle(Named<ToggleButton>(settings.PageContent, "Communications device"));
            await SmokeDispatcher.Until(() => scene.Routing.Settings.Output.Calls != calls);
            Assert.NotNull(Named<ToggleButton>(settings.PageContent, "Priority switching"));

            // The mic is set once, for routing, Mic processing and the mute shortcut.
            settings.Navigate("module:audio-routing/microphone");
            Assert.Equal("page:sound-microphone", settings.CurrentPage);
            Assert.Single(All<RoutingPage>(settings.PageContent));
            Assert.Single(All<ProcessingPage>(settings.PageContent));
            Assert.Equal(2, All<MicMuteView>(settings.PageContent).Count()); // the Microphone row at the top, and the Mute shortcut section
            Assert.Equal(["Mic in use", "Mic processing", "Mute shortcut"], Texts(settings.PageContent).Where(t => t is "Mic in use" or "Mic processing" or "Mute shortcut").Distinct());
            Assert.NotNull(Named<ToggleButton>(settings.PageContent, "Priority switching"));
            Toggle(Named<ToggleButton>(settings.PageContent, "Use mic processing"));
            await SmokeDispatcher.Until(() => scene.Pipelines.Live is not null);
            Assert.Equal("desk", scene.Pipelines.Live!.Source);
            Assert.Single(All<Button>(settings.PageContent), b => AutomationProperties.GetName(b) == "Mute all microphones"); // the island's Mute, once, at the top
            var tone = scene.Mute.Settings.PlaySound;
            Toggle(Named<ToggleButton>(settings.PageContent, "Sound feedback"));
            Assert.NotEqual(tone, scene.Mute.Settings.PlaySound);

            settings.Navigate("module:mic-processing");
            Assert.Equal("page:sound-microphone", settings.CurrentPage);

            // App volume's Settings list every app with the island's controls, then the options.
            settings.Navigate("module:app-volume");
            Assert.Single(All<VolumeView>(settings.PageContent));
            Assert.Equal(scene.Volume.Apps.Count(), All<Slider>(settings.PageContent).Count());
            var quiet = scene.Volume.Settings.ShowQuietApps;
            Toggle(Named<ToggleButton>(settings.PageContent, "Show apps that aren't playing"));
            await SmokeDispatcher.Until(() => scene.Volume.Settings.ShowQuietApps != quiet);
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
            await SmokeDispatcher.Until(() => All<Button>(page).Any(b => AutomationProperties.GetName(b) == "Install driver, Mic processing"));
            Invoke(Named<Button>(page, "Install driver, Mic processing"));
            await SmokeDispatcher.Until(() => scene.Requirement.Installs == 1 && Texts(page).Contains("Restart to finish"));
            Chippi.SmokeTests.SmokeLog.Check();
            Assert.Contains("Install Driver for mic-processing", System.IO.File.ReadAllText(Chippi.Core.Diagnostics.Log.Default.CurrentFile));
            Assert.False(scene.Shell.Registry.Find("mic-processing")!.IsInstalled);

            scene.Requirement.Change(RequirementState.Installing);
            await SmokeDispatcher.Until(() => All<Button>(page).Any(b => Equals(b.Content, "Installing")));
            Assert.False(All<Button>(page).Single(b => Equals(b.Content, "Installing")).IsEnabled);
            scene.Requirement.Change(RequirementState.Disabled);
            await SmokeDispatcher.Until(() => All<Button>(page).Any(b => AutomationProperties.GetName(b) == "Sound settings, Mic processing"));
            Invoke(Named<Button>(page, "Sound settings, Mic processing"));
            await SmokeDispatcher.Until(() => scene.Requirement.SettingsOpened == 1);
            Assert.Contains(All<Chippi.UI.Controls.SettingRow>(page), r => r.Description == "Driver disabled");
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
        var media = new FakeMedia();
        var panel = OpenPanel(scene, media);
        try
        {
            var host = (ContentControl)panel.FindName("Page");
            var grid = Assert.IsType<IslandGrid>(host.Content);
            // The mic mute shortcut is the Sound tile's button (a toggle); the volume slider's arrow opens the page too.
            Assert.Equal(["Microphone", "App volume"], grid.Buttons().Select(b => AutomationProperties.GetName(b).Split(',')[0]));
            Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(grid.Buttons().First());
            Assert.NotNull(Named<Slider>(grid, "System volume"));

            Invoke(Named<Button>(grid, "Sound"));
            await SmokeDispatcher.Until(() => panel.CurrentPage == SoundTile.Key);
            Assert.Contains("Sound", Texts(host)); // every page starts with its name
            Assert.Contains(SoundTile.Microphone, Texts(host));

            panel.ShowPage(AppVolumeModule.TileKey);
            Assert.NotNull(Named<Slider>(host, "Music volume"));
            Assert.DoesNotContain(All<Slider>(host), s => AutomationProperties.GetName(s) == "System volume"); // apps only
            Assert.Empty(All<ComboBox>(host));
            panel.ShowPage("now-playing");
            Invoke(Named<Button>(host, "Play"));
            await SmokeDispatcher.Until(() => media.Plays == 1);
            panel.ShowPage(AppVolumeModule.TileKey);
            await scene.Shell.Registry.SetInstalledAsync("app-volume", false);
            panel.SetTiles(Tiles(scene));
            Assert.Null(panel.CurrentPage);
            Assert.DoesNotContain(Assert.IsType<IslandGrid>(host.Content).Buttons(), b => AutomationProperties.GetName(b).StartsWith("App volume,", StringComparison.Ordinal));
        }
        finally { panel.Close(); media.Dispose(); }
    });

    [Fact]
    public Task Now_playing_is_media_only_and_the_sound_page_sets_the_output_in_use() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var panel = OpenPanel(scene, new FakeMedia());
        try
        {
            var host = (ContentControl)panel.FindName("Page");
            panel.ShowPage("now-playing");
            Assert.Single(All<NowPlayingView>(host));
            Assert.Empty(All<Slider>(host));
            Assert.Empty(All<ComboBox>(host));

            // System volume and mute act on the output in use.
            panel.ShowPage(SoundTile.Key);
            var headphones = scene.Devices.GetLevel("headphones");
            var volume = Named<Slider>(host, "System volume");
            ((IRangeValueProvider)new SliderAutomationPeer(volume).GetPattern(PatternInterface.RangeValue)).SetValue(40);
            await SmokeDispatcher.Until(() => Math.Abs(scene.Devices.GetLevel("speakers").Volume - .4f) < .001);
            Invoke(Named<Button>(host, "Mute system volume"));
            await SmokeDispatcher.Until(() => scene.Devices.GetLevel("speakers").Muted);
            await SmokeDispatcher.Until(() => Texts(host).Contains("Muted") && All<Button>(host).Any(b => b.Content is "Unmute" && AutomationProperties.GetName(b) == "Unmute system volume"));
            Assert.Equal(headphones, scene.Devices.GetLevel("headphones"));

            // Picking another output uses it now and puts it first in routing's list.
            Invoke(Assert.Single(All<Button>(host), b => AutomationProperties.GetName(b)?.EndsWith("Headphones", StringComparison.OrdinalIgnoreCase) == true
                && AutomationProperties.GetName(b)!.StartsWith("Use ", StringComparison.Ordinal)));
            await SmokeDispatcher.Until(() => scene.Routing.Settings.Output.Devices[0].Id == "headphones");
            await SmokeDispatcher.Until(() => scene.Routing.InUse(Chippi.Audio.AudioFlow.Render) == "headphones");

            await scene.Shell.Registry.SetInstalledAsync("audio-routing", false);
            panel.SetTiles(Tiles(scene));
            Assert.DoesNotContain(All<Slider>(host), s => AutomationProperties.GetName(s) == "System volume");
        }
        finally { panel.Close(); }
    });

    private static IReadOnlyList<IslandTileGroup> Tiles(SoundScene scene) =>
        IslandTileContribution.Group(scene.Shell.Registry.Features.SelectMany(e => e.IslandTiles));

    /// <summary>The island, open on its grid off screen, with the scene's tiles.</summary>
    private static IslandPanelWindow OpenPanel(SoundScene scene, FakeMedia media)
    {
        var panel = new IslandPanelWindow();
        panel.SetMedia(media);
        panel.SetTiles(Tiles(scene));
        panel.Initialize();
        var offscreen = new MonitorArea(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);
        panel.Open(IslandEdge.Top, new Rect(-19100, 0, 112, 32), offscreen);
        return panel;
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in All<T>(child)) yield return item;
    }
    private static IEnumerable<string> Texts(DependencyObject root) => All<TextBlock>(root).Select(t => t.Text);
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Assert.Single(All<T>(root), c => AutomationProperties.GetName(c) == name);
    private static void Invoke(ButtonBase button) => ((IInvokeProvider)new ButtonAutomationPeer((Button)button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
}
