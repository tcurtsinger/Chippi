// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
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
            Assert.Single(All<ProcessingQuickView>(host));
            Assert.Single(All<MicQuickView>(host));
            Assert.Empty(All<VolumeView>(host));
            panel.UpdateLayout();
            var body = panel.PageBody!;
            var output = Named<ComboBox>(host, "Output device");
            var input = Named<ComboBox>(host, "Microphone device");
            var processing = Assert.Single(All<ProcessingQuickView>(host));
            Assert.True(output.TranslatePoint(default, body).Y < input.TranslatePoint(default, body).Y);
            Assert.True(input.TranslatePoint(default, body).Y < processing.TranslatePoint(default, body).Y);
            Assert.InRange(processing.TranslatePoint(new Point(0, processing.ActualHeight), body).Y, 0, body.ActualHeight);
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
            Assert.Empty(All<MicQuickView>(host));
            Assert.Single(All<ComboBox>(host), b => AutomationProperties.GetName(b) == "Microphone device");
            await scene.Shell.Registry.SetInstalledAsync("mic-mute", true);
            panel.SetTiles(Tiles(scene));
            Assert.Single(All<MicQuickView>(host));
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
            Assert.Equal(["Priority", "Mic processing", "Mute shortcut"], Texts(settings.PageContent).Where(t => t is "Priority" or "Mic processing" or "Mute shortcut").Distinct());
            Assert.NotNull(Named<ToggleButton>(settings.PageContent, "Priority switching"));
            Toggle(Named<ToggleButton>(settings.PageContent, "Mic processing"));
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
            var remember = scene.Volume.Settings.RememberOutputs;
            Toggle(Named<ToggleButton>(settings.PageContent, "Remember each app's output"));
            await SmokeDispatcher.Until(() => scene.Volume.Settings.RememberOutputs != remember);
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
    public Task Audio_opens_both_views_from_one_tile_remembers_the_tab_and_adapts_to_installed_features() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        var media = new FakeMedia();
        var panel = OpenPanel(scene, media);
        try
        {
            var host = (ContentControl)panel.FindName("Page");
            var grid = Assert.IsType<IslandGrid>(host.Content);
            Assert.Equal(["Open Audio"], grid.Buttons().Select(AutomationProperties.GetName));
            var sound = Named<Button>(grid, "Open Audio");
            Assert.Same(LucideIcons.Speaker, Assert.Single(All<LucideIcon>(sound)).Data);
            Assert.Contains("Microphone live", AutomationProperties.GetHelpText(sound));
            Assert.NotNull(Named<ToggleButton>(grid, "Mute mic"));
            Assert.NotNull(Named<Slider>(grid, "System volume"));

            Invoke(sound);
            await SmokeDispatcher.Until(() => panel.CurrentPage == SoundTile.Key);
            Assert.Contains("Audio", Texts(host));
            Assert.Contains(SoundTile.Microphone, Texts(host));

            panel.UpdateLayout();
            SelectTab(host, "App volume");
            await SmokeDispatcher.Until(() => panel.CurrentPage == AppVolumeModule.TileKey);
            Assert.NotNull(Named<Slider>(host, "Music volume"));
            Assert.DoesNotContain(All<Slider>(host), s => AutomationProperties.GetName(s) == "System volume"); // apps only
            Assert.Empty(All<ComboBox>(host));
            Invoke(Named<Button>(host, "Back"));
            await SmokeDispatcher.Until(() => panel.CurrentPage is null);
            Invoke(Named<Button>(host, "Open Audio"));
            await SmokeDispatcher.Until(() => panel.CurrentPage == AppVolumeModule.TileKey);
            panel.ShowPage("now-playing");
            Invoke(Named<Button>(host, "Play"));
            await SmokeDispatcher.Until(() => media.Plays == 1);
            panel.ShowPage(AppVolumeModule.TileKey);
            await scene.Shell.Registry.SetInstalledAsync("app-volume", false);
            panel.SetTiles(Tiles(scene));
            Assert.Equal(SoundTile.Key, panel.CurrentPage);
            Assert.Empty(All<SegmentedControl>(host));
            Assert.NotNull(Named<Slider>(host, "System volume"));
            foreach (var id in new[] { "mic-processing", "mic-mute", "audio-routing" })
                await scene.Shell.Registry.SetInstalledAsync(id, false);
            panel.SetTiles(Tiles(scene));
            Assert.Null(panel.CurrentPage);
            Assert.Empty(Assert.IsType<IslandGrid>(host.Content).Buttons());

            // Essentials may consist of App volume alone. Its only gallery destination still says Audio.
            await scene.Shell.Registry.SetInstalledAsync("app-volume", true);
            panel.SetTiles(Tiles(scene));
            Invoke(Named<Button>(host, "Open Audio"));
            await SmokeDispatcher.Until(() => panel.CurrentPage == AppVolumeModule.TileKey);
            Assert.Empty(All<SegmentedControl>(host));
            Assert.Single(All<VolumeView>(host));
        }
        finally { panel.Close(); media.Dispose(); }
    });

    [Fact]
    public Task System_sounds_stays_on_the_left_and_overflow_pages_only_the_apps() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        await scene.GrowMixerTo(9);
        var panel = OpenPanel(scene, new FakeMedia());
        try
        {
            var host = (ContentControl)panel.FindName("Page");
            panel.ShowPage(AppVolumeModule.TileKey);
            List<string> Order()
            {
                panel.UpdateLayout();
                return All<Slider>(host).Where(s => s.Orientation == Orientation.Vertical && s.IsVisible)
                    .OrderBy(s => s.TranslatePoint(default, host).X).Select(AutomationProperties.GetName).ToList();
            }

            var firstNine = Order();
            Assert.Equal(9, firstNine.Count);
            Assert.Equal("System sounds volume", firstNine[0]);
            // In the tightest bank the app's mute and output actions still have independent hit areas.
            var mute = Named<Button>(host, "Mute Discord");
            var output = Named<Button>(host, "Discord output");
            var muteBounds = new Rect(mute.TranslatePoint(default, host), mute.RenderSize);
            var outputBounds = new Rect(output.TranslatePoint(default, host), output.RenderSize);
            Assert.True(outputBounds.Width >= 24 && outputBounds.Height >= 24);
            Assert.False(muteBounds.IntersectsWith(outputBounds));
            Assert.DoesNotContain(All<Button>(host), b => b.IsVisible && AutomationProperties.GetName(b) == "Next apps");
            var system = Named<Slider>(host, "System sounds volume");
            var systemX = system.TranslatePoint(default, host).X;
            ((IRangeValueProvider)new SliderAutomationPeer(system).GetPattern(PatternInterface.RangeValue)).SetValue(27);
            await SmokeDispatcher.Until(() => Math.Abs(scene.Volume.Apps.Single(a => a.Key == Chippi.Audio.AppKey.SystemSounds).Volume - .27f) < .001);
            Assert.Equal(.68f, scene.Volume.Apps.Single(a => a.Name == "Discord").Volume);

            await scene.GrowMixerTo(15);
            await SmokeDispatcher.Until(() => All<Button>(host).Any(b => b.IsVisible && AutomationProperties.GetName(b) == "Next apps"));
            var first = Order();
            Assert.Equal(9, first.Count);
            Invoke(Named<Button>(host, "Next apps"));
            await SmokeDispatcher.Until(() => All<Button>(host).Any(b => b.IsEnabled && AutomationProperties.GetName(b) == "Previous apps"));
            var second = Order();
            Assert.Equal(7, second.Count);
            Assert.Equal("System sounds volume", second[0]);
            Assert.Equal(15, first.Union(second).Count());
            Assert.Same(system, Named<Slider>(host, "System sounds volume"));
            Assert.Equal(systemX, system.TranslatePoint(default, host).X, 1);
        }
        finally { panel.Close(); }
    });

    [Fact]
    public Task An_app_output_menu_identifies_the_channel_and_reaches_the_last_device() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        scene.Devices.With([.. Enumerable.Range(1, 12).Select(i =>
            Chippi.Audio.Tests.FakeDeviceSystem.Speaker($"usb-{i}", $"USB speaker {i:00}"))]);
        await scene.Start();
        var panel = OpenPanel(scene, new FakeMedia());
        try
        {
            panel.ShowPage(AppVolumeModule.TileKey);
            var route = Named<Button>(panel.PageBody!, "Music output");
            await SmokeDispatcher.Until(() => route.IsEnabled);
            Invoke(route);

            // The popup has its own presentation source, separate from the panel.
            Border? Menu() => PresentationSource.CurrentSources.OfType<PresentationSource>()
                .Select(s => s.RootVisual).OfType<Visual>().SelectMany(VisualChildren)
                .OfType<Border>().FirstOrDefault(b => AutomationProperties.GetName(b) == "Music output");
            await SmokeDispatcher.Until(() => Menu() is not null);
            var menu = Menu()!;
            menu.UpdateLayout();
            var scroller = Assert.Single(All<ScrollViewer>(menu));
            Assert.True(scroller.ScrollableHeight > 0);
            var current = Assert.Single(All<Button>(menu), b => AutomationProperties.GetItemStatus(b) == "Selected");
            Assert.Contains("Default", AutomationProperties.GetName(current));
            var last = Named<Button>(menu, "USB speaker 12");
            last.BringIntoView();
            Invoke(last);
            await SmokeDispatcher.Until(() => scene.Volume.ChosenOutput(scene.Volume.Apps.Single().Key) == "usb-12");
            Assert.True(panel.IsOpen);
            Assert.Equal(AppVolumeModule.TileKey, panel.CurrentPage);
        }
        finally { panel.Close(); }
    });

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in VisualChildren(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

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

            // Picking another output uses it now and leaves routing's list as it was.
            var order = scene.Routing.Settings.Output.Devices.Select(d => d.Id).ToList();
            var picker = Named<ComboBox>(host, "Output device");
            picker.IsDropDownOpen = true;
            await SmokeDispatcher.Until(() => picker.IsDropDownOpen);
            Assert.True(panel.PageHandlesEscape());
            Assert.False(picker.IsDropDownOpen);
            Assert.True(panel.IsOpen);
            Assert.False(panel.PageHandlesEscape()); // a second Esc is available to the shell
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "headphones"));
            await SmokeDispatcher.Until(() => scene.Routing.InUse(Chippi.Audio.AudioFlow.Render) == "headphones");
            Assert.Equal(order, scene.Routing.Settings.Output.Devices.Select(d => d.Id));

            await scene.Shell.Registry.SetInstalledAsync("audio-routing", false);
            panel.SetTiles(Tiles(scene));
            Assert.DoesNotContain(All<Slider>(host), s => AutomationProperties.GetName(s) == "System volume");
        }
        finally { panel.Close(); }
    });

    [Fact]
    public Task Without_a_microphone_the_input_has_no_live_slider_or_actionable_picker() => SmokeDispatcher.Run(async () =>
    {
        await using var scene = new SoundScene();
        await scene.Start();
        scene.Devices.Remove("desk", "CableOut");
        await scene.Audio.RefreshAsync();
        var panel = OpenPanel(scene, new FakeMedia());
        try
        {
            var host = (ContentControl)panel.FindName("Page");
            panel.ShowPage(SoundTile.Key);
            panel.UpdateLayout();
            Assert.Contains("No microphone", Texts(All<MicQuickView>(host).Single()));
            Assert.DoesNotContain(All<ComboBox>(host), b => AutomationProperties.GetName(b) == "Microphone device" && b.IsVisible && b.IsEnabled);
            Assert.DoesNotContain(All<Slider>(host), b => AutomationProperties.GetName(b) == "Microphone volume" && b.IsVisible && b.IsEnabled);
        }
        finally { panel.Close(); }
    });

    private static IReadOnlyList<IslandTileGroup> Tiles(SoundScene scene) =>
        IslandTileContribution.Group(scene.Shell.Registry.Features.SelectMany(e => e.IslandTiles));

    /// <summary>The island, open on its grid off screen, with the scene's tiles.</summary>
    private static IslandPanelWindow OpenPanel(SoundScene scene, FakeMedia media)
    {
        var panel = new IslandPanelWindow { ActivateWindow = _ => { } };
        panel.SetMedia(media);
        panel.SetTiles(Tiles(scene));
        panel.Initialize();
        var offscreen = new MonitorArea(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);
        panel.Open(IslandGeometry.Start(offscreen.Work, 1), offscreen);
        return panel;
    }

    internal static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in All<T>(child)) yield return item;
    }
    private static IEnumerable<string> Texts(DependencyObject root) => All<TextBlock>(root).Select(t => t.Text);
    internal static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Assert.Single(All<T>(root), c => AutomationProperties.GetName(c) == name);
    private static void SelectTab(DependencyObject root, string name)
    {
        var tabs = Assert.Single(All<SegmentedControl>(root));
        var peer = new ListBoxAutomationPeer(tabs);
        var item = Assert.Single(peer.GetChildren(), p => p.GetName() == name);
        ((ISelectionItemProvider)item.GetPattern(PatternInterface.SelectionItem)).Select();
    }

    private static void Invoke(ButtonBase button) => ((IInvokeProvider)new ButtonAutomationPeer((Button)button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
}
