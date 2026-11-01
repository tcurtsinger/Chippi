// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Island.Media;
using Chippi.Features.KeepAwake;
using Chippi.Features.Downloads;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Views.Tidy;

namespace Chippi.Island.Tests;

public sealed class CompactOtherPageTests
{
    [Fact]
    public void Now_playing_is_a_156_art_card_and_nothing_playing_is_chippi_asleep() => WpfTestThread.Run(() =>
    {
        var playing = new NowPlayingView(new IslandPanelTests.FakeMedia { Sessions = [new("spotify", "Spotify", "Weekend Mix", "Various Artists", true, false, true, true, true, true,
            TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null)] });
        Fit(playing, 496, 336);
        var art = Find<Border>(playing).Single(b => b.Width == 156);
        Assert.Equal(156, art.ActualHeight);
        // No artwork: Chippi's own cover, him filling the square on the accent tint, with a cover's hairline.
        var chippi = Assert.IsType<Chippi.UI.Pet.ChippiPet>(art.Child);
        Assert.Equal(Chippi.UI.Pet.PetSize.Cover, chippi.ActualWidth);
        Assert.Equal(Chippi.UI.Pet.PetMood.Listening, chippi.Mood);
        Assert.Same(Application.Current.FindResource("IslandAccentTintBrush"), art.Background);
        Assert.Equal(new Thickness(1), art.BorderThickness);
        var cover = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
        var covered = new NowPlayingView(new IslandPanelTests.FakeMedia { Sessions = [new("spotify", "Spotify", "Weekend Mix", "Various Artists", true, true, true, true, true, true,
            TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, cover)] });
        Fit(covered, 496, 336);
        var coverArt = Find<Border>(covered).Single(b => b.Width == 156);
        Assert.Null(coverArt.Child);
        Assert.Equal(new Thickness(1), coverArt.BorderThickness); // a hairline, so a dark cover keeps its edge
        var transport = Find<Button>(playing).Where(b => b.Content is Chippi.UI.Controls.LucideIcon).ToArray();
        Assert.Equal([false, true, true], transport.Select(b => b.IsEnabled)); // the app offers no previous
        Assert.Equal(52, transport[1].ActualHeight);
        Assert.Empty(Find<ScrollViewer>(playing));
        Assert.True(playing.DesiredSize.Width <= 496);
        Assert.True(playing.DesiredSize.Height <= 336);

        var nothing = new NowPlayingView(new IslandPanelTests.FakeMedia());
        Fit(nothing, 496, 336);
        // Off screen IsVisible is always false: shown means nothing up the tree is hidden or collapsed.
        static bool Shown(DependencyObject? at)
        {
            for (; at is not null; at = LogicalTreeHelper.GetParent(at))
                if (at is UIElement { Visibility: not Visibility.Visible })
                    return false;
            return true;
        }
        var asleep = Find<Chippi.UI.Pet.ChippiPet>(nothing).Single(Shown);
        Assert.Equal(Chippi.UI.Pet.PetMood.Sleepy, asleep.Mood);
        Assert.Contains(Find<TextBlock>(nothing), t => t.Text == "Nothing playing" && Shown(t));
        Assert.DoesNotContain(Find<Button>(nothing), Shown); // no dead controls
    });

    [Fact]
    public void Now_playing_sits_in_the_middle_with_the_transport_grouped_and_no_glow() => WpfTestThread.Run(() =>
    {
        var view = new NowPlayingView(new IslandPanelTests.FakeMedia { Sessions = [new("spotify", "Spotify", "Weekend Mix", "Various Artists", false, true, true, true, true, true,
            TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null)] });
        Fit(view, 496, 336);
        double Left(FrameworkElement e) => e.TranslatePoint(new Point(), view).X;
        double Bottom(FrameworkElement e) => e.TranslatePoint(new Point(0, e.ActualHeight), view).Y;
        var art = Find<Border>(view).Single(b => b.Width == 156);
        var timeline = Find<Grid>(view).Single(g => g.Margin.Top == 22);
        double top = Chippi.Core.Modules.IslandPage.HeaderHeight + Chippi.Core.Modules.IslandPage.Gap;
        double above = art.TranslatePoint(new Point(), view).Y - top, below = top + Chippi.Core.Modules.IslandPage.BodyHeight - Bottom(timeline);
        Assert.Equal(above, below, 1); // the art and the progress in the body's middle
        Assert.True(above > 40, $"{above}");
        var transport = Find<Button>(view).Where(b => b.Content is Chippi.UI.Controls.LucideIcon).ToArray();
        Assert.Equal(48 + 8, Left(transport[1]) - Left(transport[0]), 1); // grouped 8 apart, not spread
        Assert.Equal(52 + 8, Left(transport[2]) - Left(transport[1]), 1);
        Assert.Equal(156 + 16 - 12, Left(transport[0]), 1); // the previous glyph on the title's left edge
        Assert.Equal(Bottom(art), Bottom(transport[1]), 1); // the play disc's bottom on the art's
        Assert.All(transport, b => Assert.Null(b.Effect)); // no glow
        Assert.Empty(Find<System.Windows.Shapes.Ellipse>(view)); // a bar, not a slider: no thumb
    });

    [Fact]
    public void The_source_opens_a_choice_only_when_two_apps_have_sessions() => WpfTestThread.Run(() =>
    {
        static MediaTrack Track(string id) => new(id, id, "Title", "Artist", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null);
        foreach (var (sessions, choice) in new[] { (new[] { Track("spotify") }, false), ([Track("spotify"), Track("firefox")], true) })
        {
            var view = new NowPlayingView(new IslandPanelTests.FakeMedia { Sessions = sessions });
            Fit(view, 496, 336);
            var source = Find<Button>(view).Single(b => AutomationProperties.GetName(b) == "Media source");
            Assert.Equal(Visibility.Visible, source.Visibility);
            Assert.Equal(choice, source.IsHitTestVisible);
            Assert.Equal(choice, source.Focusable);
        }
    });

    [Fact]
    public void Keep_awake_has_chippi_and_the_state_on_top_then_four_lengths_then_two_centered_in_the_body() => WpfTestThread.Run(() =>
    {
        var view = new KeepAwakeIslandView(new KeepAwakeModule());
        Fit(view, 496, 304);
        var cards = Find<ToggleButton>(view).ToArray();
        Assert.Equal(6, cards.Length);
        Assert.All(cards, c => Assert.Equal(52, c.ActualHeight));
        // Grouped by meaning: the durations in a row, then No limit and Until….
        var rows = Find<UniformGrid>(view).ToArray();
        Assert.Equal([4, 2], rows.Select(r => r.Columns));
        Assert.Equal(["30 min", "1 hr", "2 hr", "4 hr"], rows[0].Children.OfType<ToggleButton>().Select(c => (string)c.Tag));
        Assert.Equal(["No limit", "Until…"], rows[1].Children.OfType<ToggleButton>().Select(c => (string)c.Tag));
        // The block (Chippi's row to the last cards) sits in the body's vertical middle, Chippi's body on the cards' edge.
        var pet = Find<Chippi.UI.Pet.ChippiPet>(view).Single();
        double top = pet.TranslatePoint(new Point(), view).Y, bottom = rows[1].TranslatePoint(new Point(0, rows[1].ActualHeight), view).Y;
        Assert.Equal(304 - bottom, top, 1);
        Assert.Equal(-10, pet.TranslatePoint(new Point(), view).X, 1);
        Assert.Equal(24, rows[0].TranslatePoint(new Point(), view).Y - (top + pet.ActualHeight), 1);
        Assert.Empty(Find<ScrollViewer>(view));
    });

    [Fact]
    public void Keep_awake_picker_holds_the_page_edges_with_the_hour_under_chippi_and_start_on_the_right() => WpfTestThread.Run(() =>
    {
        var view = new KeepAwakeIslandView(new KeepAwakeModule());
        Fit(view, 496, 304);
        Find<ToggleButton>(view).Single(c => c.Tag as string == "Until…").IsChecked = true; // opens the picker; starts nothing
        view.UpdateLayout();
        // Chippi's body is centered in his box (60 of its 120 units); the hour column's center sits under it.
        var pet = Find<Chippi.UI.Pet.ChippiPet>(view).Single();
        var hour = Find<Border>(view).Single(b => AutomationProperties.GetName(b) == "Hour");
        Assert.Equal(pet.TranslatePoint(new Point(pet.ActualWidth / 2, 0), view).X, hour.TranslatePoint(new Point(hour.ActualWidth / 2, 0), view).X, 1);
        // Start ends on the right edge, where Stop does; Cancel 8 before it.
        var start = Find<Button>(view).Single(b => b.Content as string == "Start");
        var cancel = Find<Button>(view).Single(b => b.Content as string == "Cancel");
        Assert.Equal(496, start.TranslatePoint(new Point(start.ActualWidth, 0), view).X, 1);
        Assert.Equal(8, start.TranslatePoint(new Point(), view).X - cancel.TranslatePoint(new Point(cancel.ActualWidth, 0), view).X, 1);
        cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    });

    [Fact]
    public void Organizer_folder_control_is_one_row_and_recent_rows_fit_whole() => WpfTestThread.Run(() =>
    {
        var module = new DownloadsModule();
        module.Settings.Folders.Add(new WatchedFolder { Path = @"X:\\test-fixture", Mode = FolderMode.Silent });
        var view = new TidyView(module, _ => { });
        Fit(view, 496, 304);
        Assert.Equal(44, ((Border)view.FindName("FolderCard")).ActualHeight);
        var recent = (Border)view.FindName("Recent");
        Assert.True(recent.ActualHeight >= 210);
        Assert.True(recent.ActualHeight < 240);
        Assert.Equal("Auto", FolderMode.Silent.DisplayName());
        Assert.Equal(FolderMode.Silent, Enum.Parse<FolderMode>("Silent"));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_lit_card_keeps_its_length_under_the_pointer_and_unlights_when_stopped(bool until) => WpfTestThread.Run(() =>
    {
        var module = new KeepAwakeModule();
        var active = typeof(KeepAwakeModule).GetProperty("Active")!;
        active.SetValue(module, new KeepAwakeSettings { Mode = until ? AwakeMode.UntilTime : AwakeMode.Duration, DurationMinutes = 60, UntilMinute = 1080 });
        var view = new KeepAwakeIslandView(module);
        Fit(view, 496, 304);
        var cards = Find<ToggleButton>(view).ToArray();
        var card = until ? cards.Last() : cards[1];
        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        // Set the WPF hover state directly: no real pointer or native input.
        var writeFlag = typeof(UIElement).GetMethod("WriteFlag", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var hoverFlag = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
        card.SetValue(hoverKey, true);
        writeFlag.Invoke(card, [hoverFlag, true]);
        card.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        var refresh = typeof(KeepAwakeIslandView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        refresh.Invoke(view, null); // the timer refresh while still pointing at the active card
        // Stop is its own button in the top row now: the lit card keeps its length (an Until card its time) under the pointer.
        Assert.True(card.IsChecked);
        Assert.Equal(until ? $"Until {KeepAwakeText.Until(1080)}" : "1 hr", card.Content);
        active.SetValue(module, null);
        refresh.Invoke(view, null); // session stops while the pointer remains over it
        Assert.False(card.IsChecked);
        Assert.Equal(until ? "Until\u2026" : "1 hr", card.Content);
        card.SetValue(hoverKey, false);
        writeFlag.Invoke(card, [hoverFlag, false]);
    });

    [Fact]
    public void No_folders_state_is_centered_across_the_entire_body() => WpfTestThread.Run(() =>
    {
        var view = new TidyView(new DownloadsModule(), _ => { });
        Fit(view, 496, 304);
        var empty = (FrameworkElement)view.FindName("Empty");
        var center = empty.TranslatePoint(new Point(empty.ActualWidth / 2, empty.ActualHeight / 2), view);
        Assert.Equal(248, center.X, 1);
        Assert.Equal(152, center.Y, 1);
    });

    private static void Fit(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var value2 in Find<T>(child)) yield return value2;
    }
}
