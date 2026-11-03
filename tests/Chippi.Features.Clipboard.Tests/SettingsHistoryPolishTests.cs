// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Tests;

public sealed partial class ModuleTests
{
    [Fact]
    public async Task History_filters_on_arrival_and_identifies_an_entry_before_opening_its_menu()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        _hub.CopyText("Invoice summary\nTerms due next month");
        _hub.CopyText("Meeting moved");
        await _history.History!.IdleAsync();
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history, "terms");
            var search = Assert.Single(Find<SearchBox>(page));
            Assert.Equal("terms", search.Text);
            Assert.True(Shows(page, "1 of 2"));
            var menu = Assert.Single(Find<IconButton>(page));
            Assert.StartsWith("More for Invoice summary, Text · 2 lines", AutomationProperties.GetName(menu));
            search.Text = "no such clip";
            Assert.True(Shows(page, "0 of 2"));
            Assert.True(Shows(page, "No matches"));
            search.Clear();
            Assert.True(Shows(page, "2 items"));
            Assert.Same(menu, Find<IconButton>(page).Single(b => AutomationProperties.GetName(b).Contains("Invoice summary")));
            search.Text = "invoice";
            Assert.Same(menu, Assert.Single(Find<IconButton>(page), IsShown));
        });
    }

    [Fact]
    public async Task Same_size_pictures_have_distinct_previews_reused_across_searches()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        _hub.Copy(ClipContent.FromImage(new ClipImage(1, 1, [255, 0, 0, 255])));
        _hub.Copy(ClipContent.FromImage(new ClipImage(1, 1, [0, 0, 255, 255])));
        await _history.History!.IdleAsync();
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history);
            var previews = Find<Image>(page).Select(i => Assert.IsAssignableFrom<BitmapSource>(i.Source)).ToArray();
            Assert.Equal(2, previews.Length);
            static byte[] Pixel(BitmapSource image)
            {
                var pixel = new byte[4];
                new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(pixel, 4, 0);
                return pixel;
            }
            Assert.NotEqual(Pixel(previews[0]), Pixel(previews[1]));
            var search = Assert.Single(Find<SearchBox>(page));
            search.Text = "nothing";
            Assert.DoesNotContain(Find<Image>(page), IsShown);
            search.Clear();
            Assert.Collection(Find<Image>(page),
                i => Assert.Same(previews[0], i.Source), i => Assert.Same(previews[1], i.Source));

            var entry = _history.History.Entries[0];
            var thumbs = new HistoryThumbnails();
            Assert.NotNull(thumbs.Get(entry));
            Assert.Null(thumbs.Get(entry with { Thumbnail = [1, 2, 3] }));
            Assert.NotNull(thumbs.Get(entry));
        });
    }

    [Theory]
    [InlineData("Paste")]
    [InlineData("Copy")]
    public async Task A_file_that_disappears_while_its_menu_is_open_is_reported_as_unavailable(string command)
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        var files = new[] { _folder.File("draft.txt"), _folder.File("agenda.txt") };
        Directory.CreateDirectory(_folder.Path);
        foreach (var file in files)
            File.WriteAllText(file, "fixture");
        _hub.Copy(ClipContent.FromFiles(files));
        await _history.History!.IdleAsync();
        var entry = Assert.Single(_history.History.Entries);
        WpfTestThread.Run(() =>
        {
            _features.SettingsPasteTarget = () => new PasteTarget(7, 7);
            var page = new ClipboardHistorySettingsPage(_history);
            var anchor = Assert.Single(Find<IconButton>(page));
            var menu = page.Menu(anchor, entry);
            var go = Find<Button>(menu).Single(b => Equals(b.Content, command));
            Assert.True(go.IsEnabled);
            File.Delete(files[0]);
            go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Flush(page);
            Assert.True(Shows(page, "1 file unavailable"));
            Assert.False(Shows(page, "Copied"));
            Assert.Empty(_hub.Written);
            Assert.Empty(_hub.Pasted);
            var reopened = page.Menu(Assert.Single(Find<IconButton>(page)), entry);
            Assert.All(Find<Button>(reopened).Where(b => b.Content as string is "Copy" or "Paste"), b => Assert.False(b.IsEnabled));
        });
    }
}
