// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
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
    public void Now_playing_uses_full_body_art_and_an_empty_track_disables_transport() => WpfTestThread.Run(() =>
    {
        var view = new NowPlayingView(new IslandPanelTests.FakeMedia());
        Fit(view, 496, 336);
        var art = Find<Border>(view).Single(b => b.Width == 304);
        Assert.Equal(304, art.ActualHeight);
        Assert.All(Find<Button>(view).Where(b => b.Content is Chippi.UI.Controls.LucideIcon), b => Assert.False(b.IsEnabled));
        Assert.Empty(Find<ScrollViewer>(view));
        Assert.True(view.DesiredSize.Width <= 496);
        Assert.True(view.DesiredSize.Height <= 336);
    });

    [Fact]
    public void Keep_awake_has_six_equal_cards_centered_inside_the_body() => WpfTestThread.Run(() =>
    {
        var view = new KeepAwakeIslandView(new KeepAwakeModule());
        Fit(view, 496, 304);
        var cards = Find<ToggleButton>(view).ToArray();
        Assert.Equal(6, cards.Length);
        Assert.All(cards, c => Assert.Equal(96, c.ActualHeight));
        var grid = Find<UniformGrid>(view).Single();
        Assert.Equal(3, grid.Columns);
        Assert.True(grid.TranslatePoint(new Point(), view).Y > 40);
        Assert.True(grid.TranslatePoint(new Point(0, grid.ActualHeight), view).Y <= 304);
        Assert.Empty(Find<ScrollViewer>(view));
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
    public void Hover_label_tracks_running_state_on_every_refresh(bool until) => WpfTestThread.Run(() =>
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
        Assert.Equal("Stop", card.Content);
        active.SetValue(module, null);
        refresh.Invoke(view, null); // session stops while the pointer remains over it
        Assert.False(card.IsChecked);
        Assert.NotEqual("Stop", card.Content);
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
