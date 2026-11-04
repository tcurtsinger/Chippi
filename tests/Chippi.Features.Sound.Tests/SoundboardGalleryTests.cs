// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Features.Sound.Soundboard.Views;
using static Chippi.Features.Sound.Tests.SoundboardUiTests;

namespace Chippi.Features.Sound.Tests;

/// <summary>Gallery behavior on an unhosted WPF tree, with a fake controller and no native input.</summary>
public sealed class SoundboardGalleryTests
{
    [Fact]
    public Task Large_gallery_only_creates_nearby_tiles_and_scrolls_to_every_sound() => SoundUiThread.Sta(() =>
    {
        var page = Open(SoundboardViewFake.WithClips(1000)); Layout(page);
        Assert.InRange(Tiles(page).Length, 12, 28);
        var scroll = Assert.Single(Descendants(page).OfType<ScrollViewer>());
        scroll.ScrollToEnd(); Layout(page);
        Assert.Contains(Tiles(page), button => Equals(button.Tag, "clip999"));
        Assert.DoesNotContain(Tiles(page), button => Equals(button.Tag, "clip0"));
        Assert.InRange(Tiles(page).Length, 12, 28);
        var final = Tiles(page).Single(button => Equals(button.Tag, "clip999"));
        Assert.Equal(1000, AutomationProperties.GetPositionInSet(final));
        Assert.Equal(1000, AutomationProperties.GetSizeOfSet(final));
        scroll.ScrollToHome(); Layout(page);
        Assert.Contains(Tiles(page), button => Equals(button.Tag, "clip0"));
        Assert.Null(PresentationSource.FromVisual(page)); Close(page);
    });

    [Fact]
    public Task Keyboard_navigation_reveals_unrealized_tiles_and_preserves_tab_exit() => SoundUiThread.Sta(() =>
    {
        var page = Open(SoundboardViewFake.WithClips(1000)); Layout(page);
        Assert.True(page.NavigateWithKey("clip0", Key.End, ModifierKeys.None)); Layout(page);
        Assert.Contains(Tiles(page), button => Equals(button.Tag, "clip999"));
        Assert.False(page.NavigateWithKey("clip999", Key.Tab, ModifierKeys.None));
        Assert.True(page.NavigateWithKey("clip999", Key.Home, ModifierKeys.None)); Layout(page);
        Assert.False(page.NavigateWithKey("clip0", Key.Tab, ModifierKeys.Shift));
        Assert.True(page.NavigateWithKey("clip23", Key.Tab, ModifierKeys.None)); Layout(page);
        Assert.Contains(Tiles(page), button => Equals(button.Tag, "clip24"));
        Assert.True(page.NavigateWithKey("clip24", Key.PageDown, ModifierKeys.None)); Layout(page);
        Assert.Contains(Tiles(page), button => Equals(button.Tag, "clip36")); Close(page);
    });

    [Fact]
    public Task Reorder_beyond_first_viewport_keeps_the_moved_sound_visible() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1000); var page = Open(controller); Layout(page);
        Click(Named<Button>(page, "Edit sounds")); Layout(page);
        Assert.True(page.NavigateWithKey("clip0", Key.End, ModifierKeys.None)); Layout(page);
        Assert.True(page.ReorderWithKey("clip999", Key.System, Key.Up, ModifierKeys.Alt)); Layout(page);
        Assert.Contains(Tiles(page), button => Equals(button.Tag, "clip999"));
        Click(Named<Button>(page, "Done")); Layout(page);
        Assert.Equal("clip999", controller.Library.Clips[995].Id);
        Assert.True(Assert.Single(Descendants(page).OfType<ScrollViewer>()).VerticalOffset > 0); Close(page);
    });

    [Fact]
    public Task Editing_and_favoriting_keep_scroll_and_use_the_latest_clip_details() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1000); var page = Open(controller); Layout(page);
        var scroll = Assert.Single(Descendants(page).OfType<ScrollViewer>());
        scroll.ScrollToEnd(); Layout(page);
        double offset = scroll.VerticalOffset;
        var favorite = (MenuItem)Tiles(page).Single(button => Equals(button.Tag, "clip999")).ContextMenu.Items[1];
        controller.Library = controller.Library with { Clips = [.. controller.Library.Clips.Select(clip => clip.Id == "clip999" ? clip with { Name = "Renamed elsewhere", EndSeconds = 4 } : clip)] };
        controller.Notify(); Layout(page);
        favorite.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Layout(page);
        Assert.Equal("Renamed elsewhere", controller.Library.Clips[999].Name);
        Assert.Equal(4, controller.Library.Clips[999].EndSeconds);
        Assert.True(controller.Library.Clips[999].Favorite);
        Assert.Equal(offset, scroll.VerticalOffset);
        Click(Named<Button>(page, "Edit sounds")); Layout(page);
        Assert.Equal(offset, scroll.VerticalOffset);
        Assert.True(page.HandleBack()); Layout(page); Assert.Equal(offset, scroll.VerticalOffset); Close(page);
    });

    [Fact]
    public Task Empty_collection_name_has_visible_validation_and_keeps_the_form() => SoundUiThread.Sta(() =>
    {
        var page = Open(SoundboardViewFake.WithClips(1)); Click(Named<Button>(page, "Add collection"));
        Named<TextBox>(page, "Collection name").Text = "   "; Click(Named<Button>(page, "Add"));
        Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == "Enter a collection name");
        Assert.Equal("   ", Named<TextBox>(page, "Collection name").Text); Close(page);
    });

    [Fact]
    public Task Collection_names_count_user_characters_without_cutting_emoji() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1); var page = Open(controller);
        Click(Named<Button>(page, "Add collection"));
        var input = Named<TextBox>(page, "Collection name"); string name = string.Concat(Enumerable.Repeat("🎵", 100));
        input.Text = name + "x"; Click(Named<Button>(page, "Add"));
        Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == "Use a collection name with up to 100 characters");
        Assert.Single(controller.Library.Collections);
        input.Text = name; Click(Named<Button>(page, "Add"));
        Assert.Equal(name, controller.Library.Collections[1].Name); Close(page);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Finishing_an_old_collection_save_keeps_the_new_form_and_its_error_state(bool fail) => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = SoundboardViewFake.WithClips(1); controller.CreateCollection = _ => pending.Task;
        var page = Open(controller); Click(Named<Button>(page, "Add collection"));
        Named<TextBox>(page, "Collection name").Text = "Old collection"; Click(Named<Button>(page, "Add"));
        Assert.True(page.HandleBack()); Click(Named<Button>(page, "Add collection"));
        var current = Named<TextBox>(page, "Collection name"); current.Text = "Current collection";
        if (fail) pending.SetException(new IOException("Old collection save failed")); else pending.SetResult("old");
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.Same(current, Named<TextBox>(page, "Collection name"));
        Assert.Equal("Current collection", current.Text);
        Assert.DoesNotContain(Descendants(page).OfType<TextBlock>(), text => text.Text == "Old collection save failed"); Close(page);
    });

    [Fact]
    public Task Finishing_an_old_delete_keeps_the_new_sound_editor() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = SoundboardViewFake.WithClips(1); controller.DeleteClip = _ => pending.Task;
        var page = Open(controller);
        ((MenuItem)Tiles(page)[0].ContextMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Click(Named<Button>(page, "Delete")); Assert.True(page.HandleBack()); Click(Named<Button>(page, "+ Add"));
        var editor = Assert.Single(Descendants(page).OfType<SoundboardEditor>());
        pending.SetResult(); await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.Same(editor, Assert.Single(Descendants(page).OfType<SoundboardEditor>())); Close(page);
    });

    [Fact]
    public Task Saving_an_order_keeps_its_draft_stable_until_the_write_finishes() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string[] saved = [];
        var controller = SoundboardViewFake.WithClips(4);
        controller.ReorderClips = (_, ids) => { saved = [.. ids]; return pending.Task; };
        var page = Open(controller); Click(Named<Button>(page, "Edit sounds")); page.Move("clip0", "clip3");
        Click(Named<Button>(page, "Done"));
        Assert.False(Named<Button>(page, "Done").IsEnabled);
        page.Move("clip0", "clip1");
        Assert.True(page.ReorderWithKey("clip0", Key.System, Key.Left, ModifierKeys.Alt));
        Assert.Equal(new[] { "clip1", "clip2", "clip3", "clip0" }, saved);
        Assert.Equal(saved, Tiles(page).Select(button => (string)button.Tag));
        pending.SetResult(); await Dispatcher.Yield(DispatcherPriority.Background); Close(page);
    });

    [Theory]
    [InlineData(false, "clip0")]
    [InlineData(true, "clip0")]
    [InlineData(false, "clip2")]
    [InlineData(true, "clip2")]
    public Task Reorder_reconciles_sounds_deleted_or_moved_by_another_view(bool move, string removed) => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(4); var page = Open(controller);
        Click(Named<Button>(page, "Edit sounds"));
        controller.Library = controller.Library with
        {
            Clips = [.. controller.Library.Clips.Where(clip => move || clip.Id != removed)
                .Select(clip => clip.Id == removed ? clip with { CollectionId = "elsewhere" } : clip)],
        };
        // Invoke before Changed is delivered too: a queued background notification must not
        // leave the draft's indices ahead of the current collection membership.
        Assert.True(page.ReorderWithKey("clip2", Key.System, Key.Right, ModifierKeys.Alt));
        Assert.Equal(removed == "clip0" ? new[] { "clip1", "clip3", "clip2" } : new[] { "clip0", "clip1", "clip3" },
            Tiles(page).Select(button => (string)button.Tag));
        controller.Notify(); Assert.Equal(3, Tiles(page).Length); Close(page);
    });

    [Fact]
    public Task Finishing_an_old_reorder_does_not_close_a_new_edit_draft_or_write_twice() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int saves = 0;
        var controller = SoundboardViewFake.WithClips(4); controller.ReorderClips = (_, _) => { saves++; return pending.Task; };
        var page = Open(controller); Click(Named<Button>(page, "Edit sounds"));
        var done = Named<Button>(page, "Done"); Click(done); Click(done); Assert.Equal(1, saves);
        Assert.True(page.HandleBack()); Click(Named<Button>(page, "Edit sounds")); page.Move("clip0", "clip3");
        pending.SetResult(); await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.NotNull(Named<Button>(page, "Done"));
        Assert.Equal("clip0", Tiles(page)[3].Tag); Assert.Equal(1, saves); Close(page);
    });

    [Fact]
    public Task An_operation_finishing_after_unload_does_not_change_a_reopened_page() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = SoundboardViewFake.WithClips(1); controller.CreateCollection = _ => pending.Task;
        var page = Open(controller); Click(Named<Button>(page, "Add collection"));
        Named<TextBox>(page, "Collection name").Text = "Old collection"; Click(Named<Button>(page, "Add")); Close(page);
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Click(Named<Button>(page, "+ Add"));
        var editor = Assert.Single(Descendants(page).OfType<SoundboardEditor>());
        pending.SetException(new IOException("Old save failed")); await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.Same(editor, Assert.Single(Descendants(page).OfType<SoundboardEditor>())); Close(page);
    });

    private static Button[] Tiles(SoundboardPage page) => Descendants(page).OfType<Button>().Where(button => button.Tag is string).ToArray();
    private static void Layout(SoundboardPage page)
    {
        page.Measure(new Size(496, 296)); page.Arrange(new Rect(0, 0, 496, 296)); page.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded); page.UpdateLayout();
    }
}
