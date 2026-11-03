// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.Clipboard.Tests;

public sealed partial class ModuleTests
{
    [Fact]
    public async Task Long_settings_links_show_both_ends_and_copy_the_full_result()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var page = new CleanUrlSettingsPage(_clean);
            var field = Find<TextBox>(page).Single(t => AutomationProperties.GetName(t) == "Link");
            Assert.Equal("Link", field.Tag);
            string clean = "https://example.org/" + string.Concat(Enumerable.Repeat("folder/", 100)) + "?id=123#end";
            field.Text = clean.Replace("?id=123", "?utm_source=synthetic&id=123");
            page.Measure(new Size(440, 720));
            page.Arrange(new Rect(0, 0, 440, 720));
            page.UpdateLayout();
            foreach (var display in Find<MiddleTrimmedText>(page))
            {
                string presented = Find<TextBlock>(display).Single().Text;
                Assert.StartsWith("https://example.org/", presented);
                Assert.Contains("…", presented);
                Assert.EndsWith("#end", presented);
            }
            Find<Button>(page).Single(b => Equals(b.Content, "Copy")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(clean, Assert.Single(_hub.Written).Text);
        });
    }

    [Fact]
    public async Task Site_edits_validate_before_saving_and_rejected_saves_keep_the_live_policy_and_draft()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var page = new CleanUrlSettingsPage(_clean);
            Load(page);
            try
            {
                var automatic = Find<ToggleButton>(page).Single(b => AutomationProperties.GetName(b) == "Automatic cleaning");
                var addSite = Find<Button>(page).Single(b => AutomationProperties.GetName(b) == "Add site");
                void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click(addSite);
                var add = Find<Button>(page).Single(b => Equals(b.Content, "Add"));
                var site = Find<TextBox>(page).Single(t => AutomationProperties.GetName(t) == "Site");
                site.Text = "not a site";
                Assert.False(add.IsEnabled);
                Assert.True(Shows(page, "Enter a host, like example.org"));
                site.Text = "https://Example.org/docs";
                Assert.True(add.IsEnabled);
                _settings.RejectWrites = true;
                Click(add);
                Assert.True(Shows(page, "Save failed"));
                Assert.True(IsShown(site));
                Assert.Equal("https://Example.org/docs", site.Text);
                Assert.Empty(_clean.Settings.SitesToSkip);
                _settings.RejectWrites = false;
                Click(add);
                Assert.False(Shows(page, "Save failed"));
                Assert.Equal(["example.org"], _clean.Settings.SitesToSkip);
                Click(addSite);
                site.Text = "EXAMPLE.org";
                Assert.False(add.IsEnabled);
                Assert.True(Shows(page, "Site already listed"));
                automatic.IsChecked = true;
                Assert.True(_clean.Cleaner!.Listening);
                _settings.RejectWrites = true;
                automatic.IsChecked = false;
                Assert.True(automatic.IsChecked);
                Assert.True(_clean.Settings.Automatic);
                Assert.True(_settings.Get<CleanUrlSettings>().Automatic);
                Assert.True(_clean.Cleaner.Listening);
                Assert.True(Shows(page, "Save failed"));
            }
            finally { page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public async Task A_delayed_prefill_does_not_replace_a_link_edited_back_to_empty()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var view = new CleanUrlView(_features);
            var read = new TaskCompletionSource<ClipRead>();
            _hub.PendingRead = read.Task;
            view.Open();
            view.Field.Text = "draft";
            view.Field.Clear();
            read.SetResult(new ClipRead(ClipStatus.Ok, ClipContent.FromText("https://example.org/old")));
            Flush(view);
            Assert.Empty(view.Field.Text);
            _hub.PendingRead = Task.FromResult(new ClipRead(ClipStatus.Busy));
            view.Open();
            Assert.True(Shows(view, "Clipboard busy"));
            _hub.PendingRead = Task.FromResult(new ClipRead(ClipStatus.Private));
            view.Open();
            Assert.False(Shows(view, "Clipboard busy"));
        });
    }
}
