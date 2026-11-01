// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
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

    [Fact]
    public void A_new_status_or_state_shows_on_the_same_buttons_and_only_a_new_set_builds_them_again() => WpfTestThread.Run(() =>
    {
        var grid = Laid(g => g.SetTiles([Tile("keep-awake", split: true), Tile("clipboard")]));
        var before = grid.Buttons().ToList();
        var toggled = new List<string>();
        grid.TileToggled += (_, key) => toggled.Add(key);

        // System's status every 2 seconds, Keep awake turning on: a hover or a press on a button carries on through it.
        grid.SetTiles([Tile("keep-awake", on: true, split: true) with { Status = "1 hr left" }, Tile("clipboard") with { Status = "24 items" }]);
        Assert.Equal(before, grid.Buttons());
        var main = Assert.IsType<ToggleButton>(before[0]);
        Assert.True(main.IsChecked);
        Assert.Equal("On", Find<Button>(grid, b => AutomationProperties.GetName(b) == "Open keep-awake").Tag);
        Assert.Equal("keep-awake, 1 hr left", AutomationProperties.GetName(main));
        Assert.NotNull(Find<TextBlock>(grid, t => t.Text == "24 items"));
        Click(main);
        Assert.Equal(["keep-awake"], toggled); // the kept button toggles the tile it shows now
        Assert.True(main.IsChecked);

        // Another set or order builds them again.
        grid.SetTiles([Tile("clipboard"), Tile("keep-awake", split: true)]);
        Assert.DoesNotContain(grid.Buttons(), before.Contains);
        Assert.Equal(["clipboard", "keep-awake"], grid.Buttons().Select(b => (string)b.Tag));
    });

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

        // On, one accent gradient runs across both halves: the arrow's continues the toggle's, not starting again.
        static System.Windows.Media.Matrix Fill(Control part) =>
            Assert.IsAssignableFrom<System.Windows.Media.GradientBrush>(((Border)part.Template.FindName("Chrome", part)).Background).RelativeTransform.Value;
        double whole = main.ActualWidth + arrow.ActualWidth;
        Assert.Equal(whole, Fill(main).Transform(new Point(1, 1)).X * main.ActualWidth, 1); // the toggle's ends past its edge
        Assert.Equal(-main.ActualWidth, Fill(arrow).Transform(new Point(0, 0)).X * arrow.ActualWidth, 1); // the arrow's starts at the toggle's
        Assert.Equal(new Thickness(0), ((Border)main.Template.FindName("Chrome", main)).BorderThickness); // edge to edge, level with the arrow's

        // Its glyph sits in the middle of the whole button, arrow included, where a plain button's glyph sits.
        double Middle(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth / 2, 0), grid).X;
        Assert.Equal(Middle((FrameworkElement)main.Parent), Middle((FrameworkElement)main.Content), 1);

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

        // The row's two ends mirror each other: the arrow's glyph as far in from the right as the speaker's from the left.
        var row = (FrameworkElement)slider.Parent;
        var speaker = Find<LucideIcon>(row, i => i.Data == LucideIcons.Volume);
        var arrow = Find<LucideIcon>(row, i => i.Data == LucideIcons.ChevronRight);
        double left = speaker.TranslatePoint(default, row).X, right = row.ActualWidth - arrow.TranslatePoint(new Point(arrow.ActualWidth, 0), row).X;
        Assert.Equal(left, right, 1);

        level = null; // no output: it says so in the slider's place
        grid.SetVolume(new IslandTileVolume(() => level, set.Add), "sound");
        Assert.False(slider.IsEnabled);
        Assert.Equal(Visibility.Collapsed, slider.Visibility);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(grid, t => t.Text == "No output").Visibility);

        var opened = new List<string>();
        grid.TileOpened += (_, key) => opened.Add(key);
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Sound"));
        Assert.Equal(["sound"], opened);
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
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Mute"));
        Assert.True(muted);
        var percent = Find<TextBlock>(grid, t => t.Text == "Muted");
        Assert.Same(grid.FindResource("WarningTextBrush"), percent.Foreground);
        var speaker = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Unmute");
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
    public void The_arrows_reach_a_split_buttons_arrow_and_enter_there_opens_its_page() => WpfTestThread.Run(() =>
    {
        var grid = Laid(g => g.SetTiles([Tile("a"), Tile("keep-awake", on: true, split: true), Tile("c"), Tile("d"), Tile("e"), Tile("f")]));
        var buttons = grid.Buttons().ToList();
        var arrow = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Open keep-awake");
        Assert.Same(buttons[1], grid.Next(buttons[0], System.Windows.Input.Key.Right));
        Assert.Same(arrow, grid.Next(buttons[1], System.Windows.Input.Key.Right)); // the toggle, then its arrow
        Assert.Same(buttons[2], grid.Next(arrow, System.Windows.Input.Key.Right));
        Assert.Same(buttons[1], grid.Next(arrow, System.Windows.Input.Key.Left));
        Assert.Same(buttons[5], grid.Next(arrow, System.Windows.Input.Key.Down)); // a row down from its tile
        Assert.Null(grid.Next(arrow, System.Windows.Input.Key.Up));
        Assert.Equal("keep-awake", grid.TileOf(arrow)); // so Enter there opens Keep awake
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
        Assert.Equal(PetMood.Listening, Find<ChippiPet>(grid.Media, _ => true).Mood);
        Assert.True(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Pause").IsEnabled);
        // The 52 art fits the 68 card whole, 8 from its outside at the top and the bottom.
        var art = Find<Border>(grid.Media, b => b.Width == 52);
        double top = art.TranslatePoint(default, grid.Media).Y;
        Assert.Equal(8, top, 1);
        Assert.Equal(8, 68 - top - art.ActualHeight, 1);

        var opened = new List<string>();
        grid.TileOpened += (_, key) => opened.Add(key);
        Click(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Now Playing"));
        Assert.Equal([IslandPanelWindow.NowPlayingKey], opened);
        // The transport sits on the card: its presses don't open Now Playing too.
        var next = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Next track");
        Click(next);
        Assert.Single(opened);
        // A cut-off title shows in full in the card's tooltip, and the next one cut off the same replaces it.
        var card = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Now Playing");
        string longTitle = "A title far too long to fit beside the art and the transport";
        media.Sessions = [media.Sessions[0] with { Title = longTitle }];
        grid.Media.SetMedia(media);
        grid.UpdateLayout();
        Assert.Contains(longTitle, (string)card.ToolTip);
        media.Sessions = [media.Sessions[0] with { Title = "Another title far too long to fit beside the art and the transport" }];
        grid.Media.SetMedia(media);
        grid.UpdateLayout();
        Assert.Contains("Another title", (string)card.ToolTip);
        media.Sessions = [media.Sessions[0] with { Title = "Weekend Mix" }];
        grid.Media.SetMedia(media);
        grid.UpdateLayout();
        Assert.Null(card.ToolTip);

        // A press the player declines says so on the app's line, in Caution.
        media.Refuse = true;
        Click(next);
        Assert.Same(grid.FindResource("WarningTextBrush"), Find<TextBlock>(grid.Media, t => t.Text == "Failed").Foreground);
        Assert.Single(opened);
        media.Refuse = false;

        // Nothing playing: the card says so, on its own line level with the art, and the transport is dimmed.
        media.Sessions = [];
        media.Raise();
        grid.Media.SetMedia(media);
        grid.UpdateLayout();
        var pet = Find<ChippiPet>(grid.Media, _ => true);
        Assert.Equal(PetMood.Sleepy, pet.Mood);
        Assert.False(Find<Button>(grid, b => AutomationProperties.GetName(b) == "Play").IsEnabled);
        var nothing = Find<TextBlock>(grid.Media, t => t.Text == "Nothing playing");
        Assert.Equal(FontWeights.Normal, nothing.FontWeight); // a state, not a song's title
        var note = Find<LucideIcon>(grid.Media, i => i.Data == LucideIcons.Music);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)note.Parent).Visibility); // no app line without an app
        double Middle(FrameworkElement e) => e.TranslatePoint(new Point(0, e.ActualHeight / 2), grid.Media).Y;
        Assert.Equal(Middle(Find<Border>(grid.Media, b => b.Width == 52)), Middle(nothing), 1); // the art's square
        // No buttons, as on the page, with their room kept so nothing moves when a song starts.
        Assert.Equal(Visibility.Hidden, ((FrameworkElement)Find<Button>(grid, b => AutomationProperties.GetName(b) == "Play").Parent).Visibility);

        // A track with no title shows the app as its title, and no app line over it to say it twice.
        media.Sessions = [new MediaTrack("chrome.exe", "Chrome", "", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.Now, null)];
        grid.Media.SetMedia(media);
        grid.UpdateLayout();
        Assert.Equal("Chrome", Find<TextBlock>(grid.Media, t => t.FontSize == 14).Text);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)note.Parent).Visibility);

        // A long app name ends in an ellipsis inside the text's column, not cut mid-letter where the transport starts.
        string longName = string.Concat(Enumerable.Repeat("Windows Media Player Legacy ", 4)).Trim();
        media.Sessions = [new MediaTrack("wmplayer.exe", longName, "Song", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.Now, null)];
        grid.Media.SetMedia(media);
        grid.UpdateLayout();
        var source = Find<TextBlock>(grid.Media, t => t.Text == longName);
        Assert.True(IslandGrid.Cut(source, longName));
        var pause = Find<Button>(grid, b => AutomationProperties.GetName(b) == "Pause");
        Assert.Equal("Pause", pause.ToolTip); // the transport names itself, as the other icon buttons do
        Assert.Equal("Next track", Find<Button>(grid, b => AutomationProperties.GetName(b) == "Next track").ToolTip);
        var transport = (FrameworkElement)pause.Parent;
        Assert.True(source.TranslatePoint(new Point(source.ActualWidth, 0), grid.Media).X <= transport.TranslatePoint(default, grid.Media).X);
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
