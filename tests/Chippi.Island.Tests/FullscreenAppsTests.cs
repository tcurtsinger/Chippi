// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Settings › General › Fullscreen apps (wave-1.html, settings-general and sheet-settings-states).</summary>
public sealed class FullscreenAppsTests
{
    private sealed class InputFeature : IFeatureModule
    {
        public FeatureDescriptor Descriptor { get; } = new("pause-test", "Click debounce", "", FeatureGroup.Windows) { PausesInFullscreen = true };

        public Task StartAsync(ModuleContext context, CancellationToken ct) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static void With(Action<ShellContext, ConfigStore> test, AppSettings? app = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-fullscreen-apps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigStore(Path.Combine(root, "settings.json"));
        config.SetSection(AppSettings.SectionName, app ?? new AppSettings());
        var shell = new ShellContext(config, NullLoggerFactory.Instance, false, "fullscreen-apps-test");
        try { test(shell, config); }
        finally
        {
            shell.Clipboard.Dispose();
            shell.Input.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static List<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        void Walk(object node)
        {
            if (node is T match) found.Add(match);
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                    Walk(child);
        }
        Walk(root);
        return found;
    }

    [Fact]
    public void It_shows_once_a_Phase_6_input_feature_is_installed() => WpfTestThread.Run(() => With((shell, _) =>
    {
        shell.Registry.Register(new InputFeature());
        var page = new GeneralPage(shell);
        Assert.Equal(Visibility.Collapsed, page.FullscreenApps.Visibility);

        WpfTestThread.Wait(shell.Registry.SetInstalledAsync("pause-test", true));
        page.FullscreenApps.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal(Visibility.Visible, page.FullscreenApps.Visibility);
    }));

    [Fact]
    public void Always_on_is_off_out_of_the_box_saves_and_tells_the_hub_and_the_list_dims_while_it_is_on() => WpfTestThread.Run(() => With((shell, config) =>
    {
        var section = new FullscreenAppsSection(shell);
        var always = Assert.Single(Find<System.Windows.Controls.Primitives.ToggleButton>(section), t => System.Windows.Automation.AutomationProperties.GetName(t) == "Always on");
        Assert.False(always.IsChecked);
        var list = Find<Card>(section).Last();
        Assert.True(list.IsEnabled);

        always.IsChecked = true; // UI Automation's Toggle: Checked, no Click

        Assert.True(config.GetSection<AppSettings>(AppSettings.SectionName).InputAlwaysOn);
        Assert.True(shell.Input.Rules.AlwaysOn);
        Assert.False(list.IsEnabled);
    }));

    [Fact]
    public void The_list_counts_its_apps_and_Remove_takes_one_out() => WpfTestThread.Run(() => With((shell, config) =>
    {
        var section = new FullscreenAppsSection(shell);
        var header = Find<SettingRow>(section).Single(r => r.Header == "Pause in these apps");
        Assert.Equal("2 apps", header.Description);
        Assert.Contains(Find<TextBlock>(section), t => t.Text == "Balatro.exe");

        Find<Button>(section).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Remove Hades2")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([@"C:\Games\Balatro\Balatro.exe"], config.GetSection<AppSettings>(AppSettings.SectionName).PauseApps);
        Assert.Equal("1 app", Find<SettingRow>(section).Single(r => r.Header == "Pause in these apps").Description);
        Assert.Equal([@"C:\Games\Balatro\Balatro.exe"], shell.Input.Rules.Apps);
    }, new AppSettings { PauseApps = [@"C:\Games\Hades II\Hades2.exe", @"C:\Games\Balatro\Balatro.exe"] }));

    [Fact]
    public void Add_app_offers_the_open_apps_not_listed_yet_then_Browse_and_adds_the_one_picked() => WpfTestThread.Run(() => With((shell, config) =>
    {
        var section = new FullscreenAppsSection(shell);
        var menu = section.AddMenu([("Hades II", @"D:\Steam\Hades2.exe"), ("Path of Exile 2", @"C:\Games\PathOfExile.exe"), ("Steam", @"C:\Steam\steam.exe")]);
        var items = Find<Button>(menu.Child).Select(b => System.Windows.Automation.AutomationProperties.GetName(b)).ToList();
        Assert.Equal(["Path of Exile 2", "Steam", "Browse…"], items);

        Find<Button>(menu.Child).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([@"C:\Games\Hades II\Hades2.exe", @"C:\Games\PathOfExile.exe"], config.GetSection<AppSettings>(AppSettings.SectionName).PauseApps);
    }, new AppSettings { PauseApps = [@"C:\Games\Hades II\Hades2.exe"] }));

    [Fact]
    public void Nothing_added_yet_the_header_has_no_count() => WpfTestThread.Run(() => With((shell, _) =>
    {
        var section = new FullscreenAppsSection(shell);
        Assert.Equal("", Find<SettingRow>(section).Single(r => r.Header == "Pause in these apps").Description);
        Assert.DoesNotContain(Find<Button>(section), b => Equals(b.Content, "Remove"));
    }));
}
