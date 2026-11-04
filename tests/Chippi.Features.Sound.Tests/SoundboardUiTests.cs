// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Audio.Soundboard;
using Chippi.Features.Sound.Soundboard.Library;
using Chippi.Features.Sound.Soundboard.Views;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Tests;

/// <summary>Unhosted WPF controls only: no Window, input injection, file dialog, or audio endpoints.</summary>
public sealed class SoundboardUiTests
{
    [Fact]
    public Task Playback_updates_keep_gallery_buttons_and_level_controls_in_place() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(17);
        var page = Open(controller);
        var before = Descendants(page).OfType<Button>().Where(b => b.Tag is string).ToArray();
        var slider = Named<Slider>(page, "Sound sent to virtual mic");
        for (int i = 0; i < 4; i++)
        {
            controller.Playback = new("clip0", true, false, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(5)); controller.Notify();
        }
        Assert.Equal(17, before.Length);
        Assert.Equal(before, Descendants(page).OfType<Button>().Where(b => b.Tag is string).ToArray());
        Assert.Same(slider, Named<Slider>(page, "Sound sent to virtual mic"));
        Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "0:03 / 0:05");
        Assert.Null(PresentationSource.FromVisual(page)); Close(page);
    });

    [Fact]
    public Task Empty_collection_and_favorites_offer_the_right_action() => SoundUiThread.Sta(() =>
    {
        var controller = new SoundboardViewFake();
        var page = Open(controller);
        Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "No sounds");
        Assert.Contains(Descendants(page).OfType<Button>(), b => Equals(b.Content, "Add"));
        controller.SelectCollection(SoundboardPage.Favorites);
        Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "No favorites");
        Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "Mark a sound as a favorite");
        Close(page);
    });

    [Fact]
    public Task Reorder_is_a_draft_until_done_and_back_discards_it() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(4); var page = Open(controller);
        Click(Named<Button>(page, "Edit sounds")); page.Move("clip0", "clip3");
        Assert.Equal("clip0", controller.Library.Clips[0].Id);
        Assert.True(page.HandleBack()); Assert.Equal(0, controller.Reorders);
        Click(Named<Button>(page, "Edit sounds")); page.Move("clip0", "clip3"); Click(Named<Button>(page, "Done"));
        Assert.Equal(["clip1", "clip2", "clip3", "clip0"], controller.Library.Clips.Select(c => c.Id));
        Assert.Equal(1, controller.Reorders); Close(page);
    });

    [Theory]
    [InlineData(Key.Left, 4)]
    [InlineData(Key.Right, 6)]
    [InlineData(Key.Up, 1)]
    [InlineData(Key.Down, 9)]
    public Task Wpf_alt_system_keys_reorder_the_edit_draft(Key arrow, int expectedIndex) => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(12); var page = Open(controller);
        Click(Named<Button>(page, "Edit sounds"));
        Assert.False(page.ReorderWithKey("clip5", arrow, Key.None, ModifierKeys.None));
        Assert.True(page.ReorderWithKey("clip5", Key.System, arrow, ModifierKeys.Alt));
        Assert.Equal("clip5", controller.Library.Clips[5].Id); // edit remains a draft
        Click(Named<Button>(page, "Done"));
        Assert.Equal("clip5", controller.Library.Clips[expectedIndex].Id);
        Assert.Equal(1, controller.Reorders);
        Assert.Null(PresentationSource.FromVisual(page)); Close(page);
    });

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task Gradient_labels_and_icons_have_body_text_contrast_across_the_fill(string theme) => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1); var page = Open(controller);
        page.Resources.MergedDictionaries.Insert(0, new ResourceDictionary
        { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") });
        var ink = Assert.IsType<SolidColorBrush>(page.FindResource("SoundboardGradientInkBrush"));
        var gradient = Assert.IsType<LinearGradientBrush>(page.FindResource("IslandAccentFillBrush"));
        var stops = gradient.GradientStops.OrderBy(stop => stop.Offset).ToArray();
        double minimum = double.PositiveInfinity;
        for (int stop = 0; stop < stops.Length - 1; stop++)
        {
            var left = stops[stop].Color; var right = stops[stop + 1].Color;
            for (int step = 0; step <= 100; step++)
            {
                double t = step / 100d;
                byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * t);
                var color = Color.FromRgb(Mix(left.R, right.R), Mix(left.G, right.G), Mix(left.B, right.B));
                minimum = Math.Min(minimum, Contrast(ink.Color, color));
            }
        }
        Assert.True(minimum >= 4.5, $"{theme} gradient minimum contrast was {minimum:F3}:1");
        controller.Playback = new("clip0", true, false, TimeSpan.Zero, TimeSpan.FromSeconds(5)); controller.Notify();
        var active = Descendants(page).OfType<Button>().Single(button => Equals(button.Tag, "clip0"));
        Assert.All(Descendants(active).OfType<TextBlock>(), text => Assert.Same(ink, text.Foreground));
        Assert.All(Descendants(active).OfType<LucideIcon>(), icon => Assert.Same(ink, icon.Brush));
        Click(Named<Button>(page, "Edit sounds")); Assert.Same(ink, Named<Button>(page, "Done").Foreground);
        page.HandleBack(); Click(Named<Button>(page, "+ Add")); Click(Named<Button>(page, "Link"));
        Assert.Same(ink, Named<Button>(page, "Continue").Foreground);
        Assert.Equal(theme == "Light" ? Colors.White : (Color)ColorConverter.ConvertFromString("#170F2E"),
            Assert.IsType<SolidColorBrush>(page.FindResource("IslandOnAccentBrush")).Color);
        Close(page);

        static double Contrast(Color a, Color b)
        {
            static double Luminance(Color color)
            {
                static double Linear(byte channel) { double c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
                return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
            }
            double first = Luminance(a), second = Luminance(b);
            return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        }
    });

    [Fact]
    public Task A_selected_collection_stays_visible_with_many_long_names() => SoundUiThread.Sta(() =>
    {
        var controller = new SoundboardViewFake { Library = new()
        {
            Collections = [.. Enumerable.Range(0, 12).Select(i => new SoundCollection($"c{i}", $"Long collection name {i}"))],
            SelectedCollectionId = "c11",
        } };
        var page = Open(controller);
        Assert.NotNull(Named<Button>(page, "Long collection name 11, selected"));
        Assert.NotNull(Named<Button>(page, "More collections"));
        page.Measure(new Size(496, 296)); page.Arrange(new Rect(0, 0, 496, 296));
        Assert.Equal(496, page.ActualWidth); Close(page);
    });

    [Fact]
    public Task Multiple_files_are_reviewed_saved_and_disposed_in_order() => SoundUiThread.Sta(async () =>
    {
        var controller = new SoundboardViewFake(); int closed = 0;
        using var editor = new SoundboardEditor(controller, "default", null, () => closed++);
        editor.QueueFiles(["first.wav", "second.wav"]);
        Assert.Equal("first", Named<TextBox>(editor, "Sound name").Text);
        Click(Named<Button>(editor, "Add")); await Task.Yield();
        Assert.Equal("second", Named<TextBox>(editor, "Sound name").Text);
        Click(Named<Button>(editor, "Add")); await Task.Yield();
        Assert.Equal(["first", "second"], controller.Library.Clips.Select(c => c.Name));
        Assert.Equal(2, controller.DisposedDrafts); Assert.Equal(1, closed);
        Assert.Null(PresentationSource.FromVisual(editor));
    });

    [Fact]
    public Task A_late_import_result_is_cleaned_up_after_navigation_away() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource<SoundImportDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new SoundboardViewFake { Prepare = (_, _, _) => pending.Task };
        var editor = new SoundboardEditor(controller, "default", null, () => { });
        editor.QueueFiles(["pending.wav"]); editor.Dispose();
        var draft = new SoundImportDraft("fake.wav", "Sound", "File", 5, [], () => controller.DisposedDrafts++);
        pending.SetResult(draft);
        for (int i = 0; i < 5 && !draft.IsDisposed; i++) await Task.Yield();
        Assert.True(draft.IsDisposed); Assert.Empty(controller.Library.Clips);
    });

    [Fact]
    public Task Editing_validates_trim_and_previews_locally_before_saving() => SoundUiThread.Sta(async () =>
    {
        var controller = SoundboardViewFake.WithClips(1); int closed = 0;
        using var editor = new SoundboardEditor(controller, "default", controller.Library.Clips[0], () => closed++);
        Named<TextBox>(editor, "End time").Text = "9:00"; Click(Named<Button>(editor, "Save"));
        Assert.Equal(0, controller.Saves);
        Assert.Contains(Descendants(editor).OfType<TextBlock>(), t => t.Text.StartsWith("Choose a range"));
        Named<TextBox>(editor, "Start time").Text = "0:01"; Named<TextBox>(editor, "End time").Text = "0:03";
        Click(Named<Button>(editor, "Preview")); await Task.Yield();
        Assert.True(controller.Playback.IsPreview); Assert.Equal(0, controller.Plays);
        Click(Named<Button>(editor, "Save")); await Task.Yield();
        Assert.Equal(1, controller.Saves); Assert.Equal(1, controller.Stops); Assert.Equal(1, closed);
        Assert.Equal(1, controller.Library.Clips[0].StartSeconds); Assert.Equal(3, controller.Library.Clips[0].EndSeconds);
    });

    [Fact]
    public Task Collection_form_survives_playback_updates_and_back_returns_to_tiles() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(2); var page = Open(controller);
        Click(Named<Button>(page, "Add collection"));
        var input = Named<TextBox>(page, "Collection name"); input.Text = "Games";
        controller.Notify(); Assert.Same(input, Named<TextBox>(page, "Collection name"));
        Assert.True(page.HandleBack());
        Assert.Equal(2, Descendants(page).OfType<Button>().Count(b => b.Tag is string)); Close(page);
    });

    [Fact]
    public Task Missing_virtual_mic_keeps_library_and_add_available() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1); controller.SendUnavailableReason = "Virtual mic is not installed";
        var page = Open(controller);
        Assert.NotNull(Named<Button>(page, "Set up")); Assert.NotNull(Named<Button>(page, "+ Add"));
        controller.SendUnavailableReason = null; controller.Notify();
        Assert.DoesNotContain(Descendants(page).OfType<Button>(), b => AutomationProperties.GetName(b) == "Set up"); Close(page);
    });

    [Theory]
    [InlineData("0:02.50", 2.5)]
    [InlineData("1:02:03", 3723)]
    [InlineData("0:00", 0)]
    public void Trim_time_fields_accept_minutes_hours_and_subseconds(string value, double seconds)
    {
        Assert.True(SoundboardUi.TryTime(value, out var actual)); Assert.Equal(seconds, actual);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0:NaN")]
    [InlineData("1:2:3:4")]
    public void Invalid_trim_time_fields_are_rejected(string value) => Assert.False(SoundboardUi.TryTime(value, out _));

    internal static SoundboardPage Open(SoundboardViewFake controller)
    {
        var page = new SoundboardPage(controller);
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); return page;
    }

    internal static void Close(SoundboardPage page) => page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    internal static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    internal static T Named<T>(DependencyObject root, string name) where T : DependencyObject =>
        Assert.Single(Descendants(root).OfType<T>(), element => AutomationProperties.GetName(element) == name);
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

internal sealed class SoundboardViewFake : ISoundboardController
{
    public SoundboardLibrary Library { get; set; } = new();
    public SoundboardPlaybackState Playback { get; set; } = SoundboardPlaybackState.Idle;
    public string? Error { get; set; }
    public string? SendUnavailableReason { get; set; }
    public event Action? Changed;
    public int Plays, Stops, Saves, Reorders, DisposedDrafts;
    public Func<SoundImportSource, IProgress<SoundImportProgress>?, CancellationToken, Task<SoundImportDraft>>? Prepare;
    public Func<string, Task<string>>? CreateCollection;
    public Func<string, Task>? DeleteClip;
    public Func<string, IReadOnlyList<string>, Task>? ReorderClips;
    public void Notify() => Changed?.Invoke();
    public string? CoverPath { get; set; }
    public string? ImagePath(SoundClip clip) => clip.ImageFile is null ? null : CoverPath;
    public IDisposable HoldOpen() => new EmptyLease();
    public void SelectCollection(string id) { Library = Library with { SelectedCollectionId = id }; Notify(); }
    public Task PlayAsync(string clipId) { Plays++; Playback = new(clipId, true, false, TimeSpan.Zero, TimeSpan.FromSeconds(5)); Notify(); return Task.CompletedTask; }
    public Task StopAsync() { Stops++; Playback = SoundboardPlaybackState.Idle; Notify(); return Task.CompletedTask; }
    public Task SetLevelsAsync(float send, float hear) { Library = Library with { SendVolume = send, HearVolume = hear }; Notify(); return Task.CompletedTask; }
    public Task<string> CreateCollectionAsync(string name)
    {
        if (CreateCollection is not null) return CreateCollection(name);
        var id = Guid.NewGuid().ToString("N"); Library = Library with { Collections = [.. Library.Collections, new(id, name)] }; Notify(); return Task.FromResult(id);
    }
    public Task RenameCollectionAsync(string id, string name) { Library = Library with { Collections = [.. Library.Collections.Select(c => c.Id == id ? c with { Name = name } : c)] }; Notify(); return Task.CompletedTask; }
    public Task DeleteCollectionAsync(string id) { Library = Library with { Collections = [.. Library.Collections.Where(c => c.Id != id)] }; Notify(); return Task.CompletedTask; }
    public Task SaveClipAsync(SoundClip clip, string? newImagePath = null, SoundClip? expected = null) { Saves++; Library = Library with { Clips = [.. Library.Clips.Select(c => c.Id == clip.Id ? clip : c)] }; Notify(); return Task.CompletedTask; }
    public Task DeleteClipAsync(string id) { if (DeleteClip is not null) return DeleteClip(id); Library = Library with { Clips = [.. Library.Clips.Where(c => c.Id != id)] }; Notify(); return Task.CompletedTask; }
    public Task ReorderClipsAsync(string collectionId, IReadOnlyList<string> ids)
    {
        if (ReorderClips is not null) return ReorderClips(collectionId, ids);
        Reorders++; Library = Library with { Clips = [.. ids.Select(id => Library.Clips.Single(c => c.Id == id))] }; Notify(); return Task.CompletedTask;
    }
    public Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress, CancellationToken ct) =>
        Prepare?.Invoke(source, progress, ct) ?? Task.FromResult(new SoundImportDraft(source.Value, System.IO.Path.GetFileNameWithoutExtension(source.Value), "File", 12,
            [.. Enumerable.Range(0, 120).Select(i => (float)(.15 + Math.Abs(Math.Sin(i * .3)) * .6))], () => DisposedDrafts++));
    public Task<string> PreviewAsync(SoundImportDraft draft, double startSeconds, double endSeconds, CancellationToken ct)
    { Playback = new("preview", true, true, TimeSpan.Zero, TimeSpan.FromSeconds(endSeconds - startSeconds)); Notify(); return Task.FromResult("preview"); }
    public Task<string> PreviewClipAsync(string clipId, double startSeconds, double endSeconds, CancellationToken ct)
    { Playback = new(clipId, true, true, TimeSpan.Zero, TimeSpan.FromSeconds(endSeconds - startSeconds)); Notify(); return Task.FromResult(clipId); }
    public Task AddAsync(SoundImportDraft draft, SoundClip details, string? imagePath, CancellationToken ct)
    { Library = Library with { Clips = [.. Library.Clips, details] }; Notify(); return Task.CompletedTask; }
    public void OpenSetup() { }
    private sealed class EmptyLease : IDisposable { public void Dispose() { } }
    public static SoundboardViewFake WithClips(int count) => new()
    {
        Library = new()
        {
            Clips = [.. Enumerable.Range(0, count).Select(i => new SoundClip { Id = $"clip{i}", Name = $"Sound {i + 1}", DurationSeconds = 5, EndSeconds = 5 })],
        },
    };
}
