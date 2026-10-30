// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Xunit;

namespace Chippi.Island.Tests;

/// <summary>The island's home: split toggle buttons, the volume slider, the Now Playing card and the footer.</summary>
public sealed class IslandHomeTests
{
    private static IslandTileView Tile(string key, bool on = false, bool split = false) =>
        new(key, key, LucideIcons.Music, "Status", on, false, split);

    private static T Find<T>(DependencyObject root, Func<T, bool> where) where T : DependencyObject
    {
        if (root is T hit && where(hit))
            return hit;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(System.Windows.Media.VisualTreeHelper.GetChild(root, i), where) is { } found)
                return found;
        return null!;
    }

    private static IslandGrid Laid(Action<IslandGrid> set)
    {
        var grid = new IslandGrid();
        set(grid);
        double width = IslandPage.Width - 2 * IslandPage.Inset, height = IslandPage.Height - 2 * IslandPage.Inset;
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public void A_split_button_toggles_without_opening_and_its_arrow_opens_without_toggling() => WpfTestThread.Run(() =>
    {
        var grid = Laid(g => g.SetTiles([Tile("keep-awake", on: true, split: true), Tile("clipboard")]));
        var opened = new List<string>();
        var toggled = new List<string>();
        grid.TileOpened += (_, key) => opened.Add(key);
        grid.TileToggled += (_, key) => toggled.Add(key);

        var main = Assert.IsType<ToggleButton>(grid.Buttons().First());
        Assert.True(main.IsChecked);
        Click(main);
        Assert.Equal(["keep-awake"], toggled);
        Assert.Empty(opened);
        Assert.True(main.IsChecked); // it follows the module's state, not the click

        var arrow = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Open keep-awake");
        Click(arrow);
        Assert.Equal(["keep-awake"], opened);
        Assert.Single(toggled);

        // A plain button only opens.
        Click(grid.Buttons().Last());
        Assert.Equal(["keep-awake", "clipboard"], opened);
        Assert.Single(toggled);
    });

    [Fact]
    public void The_slider_shows_only_with_a_volume_follows_it_and_sets_it() => WpfTestThread.Run(() =>
    {
        double? level = 0.62;
        var set = new List<double>();
        var grid = Laid(g => g.SetVolume(null, null));
        var slider = Find<Slider>(grid, s => AutomationProperties.GetName(s) == "System volume");
        Assert.NotEqual(Visibility.Visible, ((FrameworkElement)slider.Parent).Visibility);

        grid.SetVolume(new IslandTileVolume(() => level, set.Add), "sound");
        grid.UpdateLayout();
        Assert.Equal(Visibility.Visible, ((FrameworkElement)slider.Parent).Visibility);
        Assert.Equal(62, slider.Value, 3);
        Assert.Empty(set); // reading it back isn't a write

        slider.Value = 30;
        Assert.Equal([0.3], set);

        level = null; // no output
        grid.SetVolume(new IslandTileVolume(() => level, set.Add), "sound");
        Assert.False(slider.IsEnabled);

        var opened = new List<string>();
        grid.TileOpened += (_, key) => opened.Add(key);
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Sound"));
        Assert.Equal(["sound"], opened);
    });

    [Fact]
    public void The_card_shows_the_track_its_transport_works_and_pressing_it_opens_now_playing() => WpfTestThread.Run(() =>
    {
        var media = new IslandPanelTests.FakeMedia
        {
            Sessions = [new MediaTrack("Spotify.exe", "Spotify", "Weekend Mix", "Various Artists", true, true, true, true, true, true,
                TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null)],
        };
        var grid = Laid(g => g.SetMedia(media));
        var texts = new List<string>();
        void Collect(DependencyObject o)
        {
            if (o is TextBlock { Text.Length: > 0 } t)
                texts.Add(t.Text);
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
                Collect(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
        }
        Collect(grid.Media);
        Assert.Contains("Weekend Mix", texts);
        Assert.Contains("Various Artists", texts);
        Assert.Contains("Spotify", texts);
        Assert.True(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Pause").IsEnabled);

        var opened = new List<string>();
        grid.TileOpened += (_, key) => opened.Add(key);
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Now Playing"));
        Assert.Equal([IslandPanelWindow.NowPlayingKey], opened);

        // Nothing playing: the card says so and the transport is dimmed.
        media.Sessions = [];
        media.Raise();
        grid.Media.SetMedia(media);
        Assert.False(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Play").IsEnabled);
    });

    [Fact]
    public void The_footer_button_asks_for_settings() => WpfTestThread.Run(() =>
    {
        var grid = Laid(_ => { });
        int asked = 0;
        grid.SettingsRequested += (_, _) => asked++;
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Settings"));
        Assert.Equal(1, asked);
    });
}
