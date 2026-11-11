// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Xunit;

namespace Chippi.Island.Tests;

/// <summary>The island's home: the tool gallery, quick switches, volume and the media footer.</summary>
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
        // The island's own templates, as the panel gives them.
        grid.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
        set(grid);
        double width = IslandPage.Width - 2 * IslandPage.Inset, height = IslandPage.Height - 2 * IslandPage.Inset;
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    internal static void ToggleThroughAutomation(ToggleButton button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle)).Toggle();
    }

    [Fact]
    public void A_status_refresh_preserves_the_launch_button_and_an_automation_switch_toggles_only_its_feature() => WpfTestThread.Run(() =>
    {
        bool awake = false;
        var grid = Laid(g => g.SetTiles([Tile("keep-awake", split: true), Tile("clipboard")]));
        var original = grid.Buttons().ToList();
        var opened = new List<string>();
        var toggled = new List<string>();
        grid.TileOpened += (_, key) => opened.Add(key);
        grid.TileToggled += (_, key) =>
        {
            toggled.Add(key);
            awake = !awake;
            grid.SetTiles([Tile("keep-awake", awake, split: true), Tile("clipboard") with { Status = "24 items" }]);
        };

        var toggle = Find<ToggleButton>(grid, b => AutomationProperties.GetName(b) == "Keep awake");
        Assert.False(toggle.IsChecked);
        ToggleThroughAutomation(toggle);
        Assert.True(awake);
        Assert.True(toggle.IsChecked);
        Assert.Equal(["keep-awake"], toggled);
        Assert.Empty(opened);
        Assert.Equal(original, grid.Buttons());
        Assert.Contains("24 items", AutomationProperties.GetHelpText(original[1]));

        var button = original[0];
        var label = Find<TextBlock>(button, t => t.Text == "keep-awake");
        var center = label.TranslatePoint(new Point(label.ActualWidth / 2, label.ActualHeight / 2), grid);
        var hit = System.Windows.Media.VisualTreeHelper.HitTest(grid, center).VisualHit;
        Assert.True(ReferenceEquals(hit, button) || button.IsAncestorOf(hit));
        Click(button);
        Assert.Equal(["keep-awake"], opened);
        Assert.Single(toggled);
        Assert.True(awake);

        grid.SetTiles([Tile("clipboard"), Tile("keep-awake", on: true, split: true)]);
        Assert.Equal(["clipboard", "keep-awake"], grid.Buttons().Select(b => (string)b.Tag));
        Assert.Single(toggled); // restating the module's value never issues a second command
    });

    [Fact]
    public void Find_filters_the_gallery_without_resizing_it_or_losing_the_query_on_refresh() => WpfTestThread.Run(() =>
    {
        var tiles = new[] { Tile("clipboard"), Tile("scratchpad"), Tile("search"), Tile("shelf"), Tile("system") };
        var grid = Laid(g => g.SetTiles(tiles));
        double height = grid.HomeHeight;
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Find a tool"));
        var field = Find<TextBox>(grid, _ => true);
        field.Text = "ScR";
        Assert.Equal("scratchpad", Assert.Single(grid.Buttons()).Tag);
        grid.SetTiles([.. tiles.Select(t => t with { Status = "Updated" })]);
        Assert.Equal("ScR", field.Text);
        Assert.Contains("Updated", AutomationProperties.GetHelpText(Assert.Single(grid.Buttons())));
        Assert.Equal(height, grid.HomeHeight);

        field.Text = "unmatched";
        Assert.Empty(grid.Buttons());
        Assert.Equal(Visibility.Visible, Find<TextBlock>(grid, t => t.Text == "No matching tools").Visibility);
        Click(Find<Button>(grid, b => Equals(b.Content, "Clear search")));
        Assert.True(grid.IsSearching);
        Assert.Equal("", field.Text);
        Assert.Equal(tiles.Length, grid.Buttons().Count());
        Assert.True(grid.CloseSearch());
        Assert.False(grid.IsSearching);
        Assert.Equal(tiles.Length, grid.Buttons().Count());

        // The standard Windows Find command routes from a tool to the same field as the header button.
        ApplicationCommands.Find.Execute(null, grid.Buttons().First());
        Assert.True(grid.IsSearching);
        grid.CloseSearch();
        grid.SetTiles([]);
        bool settingsOpened = false;
        grid.SettingsRequested += (_, _) => settingsOpened = true;
        Click(Find<Button>(grid, b => Equals(b.Content, "Open settings")));
        Assert.True(settingsOpened);
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

        level = null; // no output: it says so in the slider's place
        grid.SetVolume(new IslandTileVolume(() => level, set.Add), "sound");
        Assert.False(slider.IsEnabled);
        Assert.Equal(Visibility.Collapsed, slider.Visibility);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(grid, t => t.Text == "No output").Visibility);

    });

    [Fact]
    public void The_speaker_mutes_and_the_wheel_and_the_keys_move_the_volume_in_useful_steps() => WpfTestThread.Run(() =>
    {
        bool muted = false;
        var set = new List<double>();
        var volume = new IslandTileVolume(() => 0.5, set.Add, () => muted, m => { muted = m; return Task.CompletedTask; });
        var grid = Laid(g => g.SetVolume(volume, "sound"));
        var slider = Find<Slider>(grid, s => AutomationProperties.GetName(s) == "System volume");
        Assert.NotNull(Find<TextBlock>(grid, t => t.Text == "50%"));

        // The speaker mutes, as in Quick Settings; muted, the speaker and "Muted" are in Caution.
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Mute system volume"));
        Assert.True(muted);
        var percent = Find<TextBlock>(grid, t => t.Text == "Muted");
        Assert.Same(grid.FindResource("WarningTextBrush"), percent.Foreground);
        var speaker = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Unmute system volume");
        Assert.Same(LucideIcons.VolumeMuted, ((LucideIcon)speaker.Content).Data);
        Click(speaker);
        Assert.False(muted);

        // A wheel notch moves 2, as on the taskbar; an arrow key 5 and Page Up or Down 10, as on Sound's slider.
        slider.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        Assert.Equal(52, slider.Value, 3);
        Assert.Equal(0.52, set[^1], 3);
        Assert.Equal(5, slider.SmallChange);
        Assert.Equal(10, slider.LargeChange);
    });

    [Fact]
    public void The_arrows_follow_four_columns_of_launch_targets() => WpfTestThread.Run(() =>
    {
        var grid = Laid(g => g.SetTiles([Tile("a"), Tile("keep-awake", split: true), Tile("c"), Tile("d"), Tile("e"), Tile("f"), Tile("g")]));
        var buttons = grid.Buttons().ToList();
        Assert.Same(buttons[1], grid.Next(buttons[0], Key.Right));
        Assert.Same(buttons[2], grid.Next(buttons[1], Key.Right));
        Assert.Same(buttons[0], grid.Next(buttons[1], Key.Left));
        Assert.Same(buttons[5], grid.Next(buttons[1], Key.Down));
        Assert.Null(grid.Next(buttons[1], Key.Up));
        Assert.Equal("keep-awake", grid.TileOf(buttons[1]));
    });

    [Fact]
    public void The_footer_names_the_track_or_idle_state_and_opens_now_playing() => WpfTestThread.Run(() =>
    {
        var media = new IslandPanelTests.FakeMedia();
        var grid = Laid(g => g.SetMedia(media));
        var card = Find<Button>(grid.Media, b => AutomationProperties.GetName(b) == "Now Playing");
        Assert.Contains("Nothing playing", AutomationProperties.GetHelpText(card));
        var bounds = card.TranslatePoint(default, grid);

        var track = new MediaTrack("Spotify.exe", "Spotify", "Weekend Mix", "Various Artists", true,
            true, true, true, true, true, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.Now, null);
        media.Sessions = [track];
        grid.SetMedia(media);
        grid.UpdateLayout();
        Assert.Equal(bounds.Y, card.TranslatePoint(default, grid).Y);
        Assert.Contains("Weekend Mix", Find<TextBlock>(grid.Media, t => t.Text.Contains("Weekend Mix")).Text);
        Assert.Contains("Various Artists", AutomationProperties.GetHelpText(card));
        Assert.Null(Find<Button>(grid.Media, b => AutomationProperties.GetName(b) == "Pause"));

        // Artwork can arrive after the title. It replaces the glyph without moving the footer.
        var cover = new System.Windows.Media.DrawingImage();
        media.Sessions = [track with { Artwork = cover }];
        grid.SetMedia(media);
        var coverFace = Find<Border>(grid.Media, b => b.Background is System.Windows.Media.ImageBrush art && art.ImageSource == cover);
        Assert.Equal(Visibility.Visible, coverFace.Visibility);
        string title = string.Concat(Enumerable.Repeat("A long track title ", 10)).Trim();
        media.Sessions = [track with { Playing = false, Title = title }];
        grid.SetMedia(media);
        Assert.Equal(Visibility.Collapsed, coverFace.Visibility);
        Assert.Contains(title, AutomationProperties.GetHelpText(card));
        Assert.Contains(title, card.ToolTip?.ToString());
        media.Sessions = [track with { Title = "", Artist = "" }];
        grid.SetMedia(media);
        Assert.Contains("Spotify", AutomationProperties.GetHelpText(card));

        string? opened = null;
        grid.TileOpened += (_, key) => opened = key;
        Click(card);
        Assert.Equal(IslandPanelWindow.NowPlayingKey, opened);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Tab_visits_the_home_in_reading_order_in_both_directions(bool features, bool petRight) => WpfTestThread.Run(() =>
    {
        var grid = Laid(g =>
        {
            g.SetPetSide(petRight);
            if (features)
            {
                g.SetTiles([Tile("keep-awake", split: true), Tile("shelf")]);
                g.SetVolume(new IslandTileVolume(() => 0.5, _ => { }, () => false, _ => Task.CompletedTask), "sound");
            }
        });
        var host = new Window { Content = grid, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = 0, Width = 520, Height = 360 };
        Control? target = null;
        host.PreviewGotKeyboardFocus += (_, e) =>
        {
            target = e.NewFocus as Control;
            e.Handled = true; // Observe WPF's destination before Windows is asked to take the keyboard.
        };
        try
        {
            host.Show();
            var names = new List<string> { "Find a tool" };
            if (features)
                names.AddRange(["Open keep-awake", "Open shelf", "Keep awake", "Mute system volume", "System volume"]);
            else
                names.Add("Open settings");
            names.AddRange(petRight ? ["Now Playing", "Settings", "Close Chippi"] : ["Close Chippi", "Settings", "Now Playing"]);
            Control Visit(Control from, FocusNavigationDirection direction, string name)
            {
                target = null;
                from.MoveFocus(new TraversalRequest(direction));
                Assert.NotNull(target);
                Assert.Equal(name, AutomationProperties.GetName(target));
                return target;
            }
            var current = Visit(grid, FocusNavigationDirection.First, names[0]);
            foreach (string name in names.Skip(1))
                current = Visit(current, FocusNavigationDirection.Next, name);
            foreach (string name in names.AsEnumerable().Reverse().Skip(1))
                current = Visit(current, FocusNavigationDirection.Previous, name);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void The_footer_chippi_requests_close_and_sits_where_the_pucks_does() => WpfTestThread.Run(() =>
    {
        var grid = Laid(_ => { });
        var close = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Close Chippi");
        Assert.Same(grid.Pet, close.Content); Assert.Equal("Close Chippi", close.ToolTip);
        int requested = 0; grid.CloseRequested += (_, _) => requested++;
        Assert.True(grid.IsCloseTarget(close)); Assert.True(grid.IsCloseTarget(grid.Pet));
        Assert.False(grid.IsCloseTarget(null)); Assert.False(grid.IsCloseTarget(new System.Windows.Documents.Run("Other")));
        Click(close); Assert.Equal(1, requested);

        // Chippi meets the puck at either outer edge. Settings stays beside him.
        var settings = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Settings");
        var mediaLink = Find<Button>(grid.Media, b => AutomationProperties.GetName(b) == "Now Playing");
        foreach (bool right in new[] { false, true })
        {
            grid.SetPetSide(right);
            grid.UpdateLayout();
            var footer = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(close);
            var center = grid.Pet.TranslatePoint(new Point(grid.Pet.ActualWidth / 2, grid.Pet.ActualHeight / 2), footer);
            Assert.Equal(IslandGrid.FooterPet, Math.Round(right ? footer.ActualWidth - center.X : center.X, 3));
            Assert.Equal(IslandGrid.FooterPet, Math.Round(footer.ActualHeight - center.Y, 3));
            var gear = settings.TranslatePoint(new Point(settings.ActualWidth / 2, settings.ActualHeight / 2), footer);
            Assert.InRange(right ? center.X - gear.X : gear.X - center.X, 30, 45);
            Assert.Equal(center.Y, gear.Y, 3);
            Assert.Equal(grid.Media.ActualWidth, mediaLink.ActualWidth, 3);
            Assert.Equal(center.Y, mediaLink.TranslatePoint(new Point(0, mediaLink.ActualHeight / 2), footer).Y, 3);
        }
    });
}
