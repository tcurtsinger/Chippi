// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Features.Clipboard.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Tests;

public sealed partial class ModuleTests
{
    [Fact]
    public void A_picture_limit_names_the_encoded_size_or_pixel_cap()
    {
        Assert.Equal("Picture not saved · over 16.6 MP", Format.Limit(Chippi.Core.Clipboard.ClipLimit.ImagePixels));
        Assert.Equal("Picture not saved · over 16 MiB", Format.Limit(Chippi.Core.Clipboard.ClipLimit.Image));
    }

    [Fact]
    public async Task A_matched_line_is_visible_closed_and_open_and_copy_keeps_the_whole_clip()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        const string original = "Release notes\nSchedule\nApprovals\nOwner\nQA: ship Tuesday\nSigned";
        _hub.CopyText(original);
        await _history.History!.IdleAsync();
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            try
            {
                Find<SearchBox>(view).Single().Text = "TUESDAY";
                Assert.True(Shows(view, "QA: ship Tuesday"));
                var row = Find<Button>(view).Single(b => AutomationProperties.GetName(b).StartsWith("QA: ship Tuesday,", StringComparison.Ordinal));
                ClickWidgetButton(row);
                Assert.True(Shows(view, "Text · 6 lines"));
                Assert.True(Shows(view, "…QA: ship Tuesday\nSigned"));
                ClickWidgetButton(Assert.Single(Find<Button>(view), b => IsShown(b) && Equals(b.Content, "Copy")));
                Assert.Equal(original, Assert.Single(_hub.Written).Text);
            }
            finally
            {
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.Empty(Find<WholeRows>(view).Single().Children);
            }
        });
    }

    [Fact]
    public void A_long_name_and_a_later_file_show_the_match_within_the_row_excerpt()
    {
        string longName = new string('a', 120) + " matches.txt";
        Assert.StartsWith("…", Format.Excerpt(longName, "MATCH"));
        Assert.InRange(Format.Excerpt(longName, "MATCH").IndexOf("matches", StringComparison.Ordinal), 0, 20);
        var clip = new History.HistoryEntry(5, History.EntryKind.Files, DateTimeOffset.Now, null, null,
            [@"C:\Fixture\first.txt", @"C:\Fixture\second.txt", @"C:\Fixture\" + longName], 0, 0, null, 10);
        Assert.Equal(Format.Excerpt(longName, "MATCH"), Format.Line(clip, "MATCH"));
        Assert.Contains("3 files", Format.AccessibleName(clip, DateTimeOffset.Now, "MATCH"));
        Assert.StartsWith("first.txt, second.txt,", Format.Line(clip));
    }

    [Fact]
    public void Clipped_rows_leave_keyboard_traversal_and_return_when_their_slot_fits() => WpfTestThread.Run(() =>
    {
        var rows = new WholeRows();
        var first = new Button { Height = 30 };
        var last = new Button { Height = 30 };
        rows.Children.Add(first);
        rows.Children.Add(last);
        void Fit(int height)
        {
            rows.Measure(new Size(160, height));
            rows.Arrange(new Rect(0, 0, 160, height));
            rows.UpdateLayout();
        }
        Fit(44);
        Assert.Equal(Visibility.Visible, first.Visibility);
        Assert.Equal(Visibility.Hidden, last.Visibility);
        rows.Keep = last;
        Fit(44);
        Assert.Equal(Visibility.Hidden, first.Visibility);
        Assert.Equal(Visibility.Visible, last.Visibility);
        Fit(60);
        Assert.Equal(Visibility.Visible, first.Visibility);
        Assert.Equal(Visibility.Visible, last.Visibility);
    });
}
