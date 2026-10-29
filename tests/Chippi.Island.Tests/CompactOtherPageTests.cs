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
