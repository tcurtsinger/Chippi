// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Audio.Soundboard;
using Chippi.Features.Sound.Soundboard.Library;
using Chippi.Features.Sound.Soundboard.Views;
using static Chippi.Features.Sound.Tests.SoundboardUiTests;

namespace Chippi.Features.Sound.Tests;

public sealed class SoundboardEditorHardeningTests
{
    [Theory]
    [InlineData("de-DE", "0:01,125", 1.125)]
    [InlineData("de-DE", "1,125", 1.125)]
    [InlineData("de-DE", "0:01.125", 1.125)]
    [InlineData("en-US", "1:02:03.125", 3723.125)]
    public void Trim_accepts_local_decimal_seconds(string culture, string input, double expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.True(SoundboardUi.TryTime(input, out double actual)); Assert.Equal(expected, actual);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("0:60")]
    [InlineData("1:60:00")]
    [InlineData("1.5:00")]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData(":01")]
    public void Trim_rejects_ambiguous_or_invalid_time_segments(string input) => Assert.False(SoundboardUi.TryTime(input, out _));

    [Fact]
    public Task Editing_only_a_name_preserves_the_exact_saved_trim() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1);
        var clip = controller.Library.Clips[0] with { StartSeconds = .1234567, EndSeconds = 4.9876543 };
        controller.Library = controller.Library with { Clips = [clip] };
        using var editor = new SoundboardEditor(controller, "default", clip, () => { });
        Named<TextBox>(editor, "Sound name").Text = "Renamed";
        Click(Named<Button>(editor, "Save"));
        Assert.Equal(clip.StartSeconds, controller.Library.Clips[0].StartSeconds);
        Assert.Equal(clip.EndSeconds, controller.Library.Clips[0].EndSeconds);
    });

    [Fact]
    public Task A_time_field_can_return_to_its_original_displayed_value() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1);
        using var editor = new SoundboardEditor(controller, "default", controller.Library.Clips[0], () => { });
        var start = Named<TextBox>(editor, "Start time"); string original = start.Text;
        start.Text = "1"; start.Text = original;
        Click(Named<Button>(editor, "Save"));
        Assert.Equal(0, controller.Library.Clips[0].StartSeconds);
    });

    [Fact]
    public Task Ranges_shorter_than_the_storage_minimum_are_rejected_before_preview_or_save() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1);
        using var editor = new SoundboardEditor(controller, "default", controller.Library.Clips[0], () => { });
        Named<TextBox>(editor, "End time").Text = "0.01";
        Click(Named<Button>(editor, "Preview")); Click(Named<Button>(editor, "Save"));
        Assert.False(controller.Playback.IsPlaying); Assert.Equal(0, controller.Saves);
        Assert.Contains(Descendants(editor).OfType<TextBlock>(), text => text.Text.Contains("at least 0.02"));
    });

    [Fact]
    public Task Names_keep_complete_Unicode_text_elements_and_explain_the_length_limit() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1);
        using var editor = new SoundboardEditor(controller, "default", controller.Library.Clips[0], () => { });
        var name = Named<TextBox>(editor, "Sound name");
        string valid = new string('界', 99) + "👨‍👩‍👧‍👦";
        name.Text = valid + "x"; Click(Named<Button>(editor, "Save"));
        Assert.Equal(0, controller.Saves); Assert.Equal(valid + "x", name.Text);
        name.Text = valid; Click(Named<Button>(editor, "Save"));
        Assert.Equal(valid, controller.Library.Clips[0].Name);
    });

    [Fact]
    public Task Save_is_single_flight_and_failure_keeps_the_draft_for_retry() => SoundUiThread.Sta(async () =>
    {
        var first = new TaskCompletionSource();
        var controller = new EditorController(); int adds = 0, closed = 0;
        controller.Add = (draft, details, image, token) => ++adds == 1 ? first.Task : controller.Inner.AddAsync(draft, details, image, token);
        using var editor = new SoundboardEditor(controller, "default", null, () => closed++);
        editor.QueueFiles(["prepared.wav"]);
        Named<TextBox>(editor, "Sound name").Text = "Keep my title";
        Named<TextBox>(editor, "Start time").Text = "1.234";
        var add = Named<Button>(editor, "Add");
        Click(add); Click(add);
        Assert.Equal(1, adds); Assert.False(add.IsEnabled); Assert.Equal(0, controller.Inner.DisposedDrafts);
        first.SetException(new IOException("Disk is full")); await Task.Yield();
        Assert.True(add.IsEnabled); Assert.Equal("Keep my title", Named<TextBox>(editor, "Sound name").Text);
        Assert.Contains(Descendants(editor).OfType<TextBlock>(), text => text.Text == "Save failed. Disk is full");
        Click(add); await Task.Yield();
        Assert.Single(controller.Library.Clips); Assert.Equal("Keep my title", controller.Library.Clips[0].Name);
        Assert.Equal(1.234, controller.Library.Clips[0].StartSeconds);
        Assert.Equal(1, closed); Assert.Equal(1, controller.Inner.DisposedDrafts);
    });

    [Fact]
    public Task Cancel_disposes_a_late_preparation_result_without_closing_twice() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource<SoundImportDraft>();
        var controller = new SoundboardViewFake { Prepare = (_, _, _) => pending.Task }; int closed = 0;
        using var editor = new SoundboardEditor(controller, "default", null, () => closed++);
        editor.QueueFiles(["pending.wav"]);
        var cancel = Named<Button>(editor, "Cancel"); Click(cancel); Click(cancel);
        var draft = new SoundImportDraft("fake.wav", "Name", "File", 5, []);
        pending.SetResult(draft); await Task.Yield();
        Assert.True(draft.IsDisposed); Assert.Equal(1, closed); Assert.Empty(controller.Library.Clips);
    });

    [Fact]
    public Task Closing_an_unused_editor_does_not_stop_another_views_preview() => SoundUiThread.Sta(() =>
    {
        var controller = SoundboardViewFake.WithClips(1);
        controller.Playback = new("clip0", true, true, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        using (var editor = new SoundboardEditor(controller, "default", controller.Library.Clips[0], () => { })) { }
        Assert.Equal(0, controller.Stops); Assert.True(controller.Playback.IsPlaying);
    });

    [Fact]
    public Task Cancel_waits_for_pending_preview_before_stopping_and_releasing_its_draft() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource(); var controller = new EditorController(); int previews = 0;
        controller.Preview = async (draft, start, end, token) =>
        {
            previews++; await pending.Task;
            Assert.False(draft.IsDisposed);
            controller.Inner.Playback = new("preview", true, true, TimeSpan.Zero, TimeSpan.FromSeconds(end - start));
            return "preview";
        };
        using var editor = new SoundboardEditor(controller, "default", null, () => { });
        editor.QueueFiles(["pending.wav"]);
        var preview = Named<Button>(editor, "Preview"); Click(preview); Click(preview);
        Click(Named<Button>(editor, "Add"));
        Assert.Equal(1, previews); Assert.Empty(controller.Library.Clips);
        Click(Named<Button>(editor, "Cancel"));
        Assert.Equal(0, controller.Inner.DisposedDrafts);
        pending.SetResult();
        for (int i = 0; i < 10 && controller.Inner.DisposedDrafts == 0; i++) await Task.Yield();
        Assert.Equal(1, controller.Inner.Stops); Assert.Equal(1, controller.Inner.DisposedDrafts);
    });

    [Fact]
    public Task A_superseded_preview_completion_does_not_claim_another_views_playback() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource<string>(); var controller = new EditorController();
        controller.Preview = (_, _, _, _) => pending.Task;
        using var editor = new SoundboardEditor(controller, "default", null, () => { });
        editor.QueueFiles(["first.wav"]); Click(Named<Button>(editor, "Preview"));
        controller.Inner.Playback = new("other-preview", true, true, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        pending.SetResult("our-preview"); await Task.Yield();
        Click(Named<Button>(editor, "Cancel"));
        Assert.Equal(0, controller.Inner.Stops); Assert.Equal("other-preview", controller.Playback.ClipId);
        Assert.Equal(1, controller.Inner.DisposedDrafts);
    });

    [Fact]
    public Task Navigation_while_stopping_preview_does_not_start_a_save() => SoundUiThread.Sta(async () =>
    {
        var stop = new TaskCompletionSource(); var controller = new EditorController(SoundboardViewFake.WithClips(1));
        controller.Stop = () => stop.Task;
        using var editor = new SoundboardEditor(controller, "default", controller.Library.Clips[0], () => { });
        Click(Named<Button>(editor, "Preview")); Click(Named<Button>(editor, "Save"));
        editor.Dispose(); controller.Inner.Playback = SoundboardPlaybackState.Idle;
        stop.SetResult(); await Task.Yield();
        Assert.Equal(0, controller.Inner.Saves);
    });

    [Fact]
    public Task Navigation_during_add_keeps_the_source_until_add_finishes() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource(); var controller = new EditorController(); SoundImportDraft? usedDraft = null;
        controller.Add = async (draft, _, _, token) =>
        {
            usedDraft = draft; await pending.Task;
            Assert.False(draft.IsDisposed); token.ThrowIfCancellationRequested();
        };
        using var editor = new SoundboardEditor(controller, "default", null, () => { });
        editor.QueueFiles(["pending.wav"]); Click(Named<Button>(editor, "Add")); editor.Dispose();
        Assert.NotNull(usedDraft); Assert.False(usedDraft.IsDisposed);
        pending.SetResult();
        for (int i = 0; i < 10 && !usedDraft.IsDisposed; i++) await Task.Yield();
        Assert.True(usedDraft.IsDisposed); Assert.Empty(controller.Library.Clips);
    });

    [Fact]
    public Task Source_focus_targets_are_real_controls_and_long_errors_keep_actions_in_bounds() => SoundUiThread.Sta(() =>
    {
        var controller = new SoundboardViewFake { Prepare = (_, _, _) => throw new IOException(new string('界', 500)) };
        using var editor = new SoundboardEditor(controller, "default", null, () => { });
        Assert.Same(Named<Button>(editor, "Browse"), editor.InitialFocus);
        Click(Named<Button>(editor, "Link"));
        Assert.Same(Named<TextBox>(editor, "Audio link"), editor.InitialFocus);
        Named<TextBox>(editor, "Audio link").Text = "https://example.test/audio.wav";
        Click(Named<Button>(editor, "Continue"));
        editor.Measure(new Size(496, 240)); editor.Arrange(new Rect(0, 0, 496, 240)); editor.UpdateLayout();
        var retry = Named<Button>(editor, "Retry");
        var position = retry.TranslatePoint(new Point(), editor);
        Assert.InRange(position.Y + retry.ActualHeight, 0, 240);
        Assert.Null(PresentationSource.FromVisual(editor));
    });

    [Theory]
    [InlineData("release")]
    [InlineData("capture")]
    [InlineData("unload")]
    [InlineData("disable")]
    public Task Waveform_interruptions_end_the_gesture_and_the_next_drag_works(string interruption) => SoundUiThread.Sta(() =>
    {
        var waveform = new SoundWaveform(); waveform.Set([.5f], 10, 0, 10);
        waveform.Measure(new Size(108, 48)); waveform.Arrange(new Rect(0, 0, 108, 48));
        var changes = new List<(double Start, double End)>(); waveform.RangeChanged += (start, end) => changes.Add((start, end));
        waveform.BeginDrag(14);
        switch (interruption)
        {
            case "release": waveform.MoveDrag(24, pressed: false); break;
            case "capture": waveform.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "unload": waveform.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
            case "disable": waveform.IsEnabled = false; waveform.IsEnabled = true; break;
        }
        int count = changes.Count; waveform.MoveDrag(44, pressed: true); Assert.Equal(count, changes.Count);
        waveform.BeginDrag(94); waveform.MoveDrag(84, pressed: true); waveform.EndDrag();
        Assert.Equal(1, changes[^1].Start); Assert.Equal(8, changes[^1].End);
        Assert.Null(PresentationSource.FromVisual(waveform)); Assert.False(waveform.IsMouseCaptured);
    });

    private sealed class EditorController(SoundboardViewFake? inner = null) : ISoundboardController
    {
        internal SoundboardViewFake Inner { get; } = inner ?? new();
        internal Func<SoundImportDraft, SoundClip, string?, CancellationToken, Task>? Add { get; set; }
        internal Func<SoundImportDraft, double, double, CancellationToken, Task<string>>? Preview { get; set; }
        internal Func<Task>? Stop { get; set; }
        public SoundboardLibrary Library => Inner.Library;
        public SoundboardPlaybackState Playback => Inner.Playback;
        public string? Error => Inner.Error;
        public string? SendUnavailableReason => Inner.SendUnavailableReason;
        public event Action? Changed { add => Inner.Changed += value; remove => Inner.Changed -= value; }
        public string? ImagePath(SoundClip clip) => Inner.ImagePath(clip);
        public IDisposable HoldOpen() => Inner.HoldOpen();
        public void SelectCollection(string id) => Inner.SelectCollection(id);
        public Task PlayAsync(string clipId) => Inner.PlayAsync(clipId);
        public Task StopAsync() => Stop?.Invoke() ?? Inner.StopAsync();
        public Task SetLevelsAsync(float send, float hear) => Inner.SetLevelsAsync(send, hear);
        public Task<string> CreateCollectionAsync(string name) => Inner.CreateCollectionAsync(name);
        public Task RenameCollectionAsync(string id, string name) => Inner.RenameCollectionAsync(id, name);
        public Task DeleteCollectionAsync(string id) => Inner.DeleteCollectionAsync(id);
        public Task SaveClipAsync(SoundClip clip, string? newImagePath = null, SoundClip? expected = null) => Inner.SaveClipAsync(clip, newImagePath, expected);
        public Task DeleteClipAsync(string id) => Inner.DeleteClipAsync(id);
        public Task ReorderClipsAsync(string collectionId, IReadOnlyList<string> ids) => Inner.ReorderClipsAsync(collectionId, ids);
        public Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress, CancellationToken ct) => Inner.PrepareAsync(source, progress, ct);
        public Task<string> PreviewAsync(SoundImportDraft draft, double start, double end, CancellationToken ct) => Preview?.Invoke(draft, start, end, ct) ?? Inner.PreviewAsync(draft, start, end, ct);
        public Task<string> PreviewClipAsync(string clipId, double start, double end, CancellationToken ct) => Inner.PreviewClipAsync(clipId, start, end, ct);
        public Task AddAsync(SoundImportDraft draft, SoundClip details, string? imagePath, CancellationToken ct) => Add?.Invoke(draft, details, imagePath, ct) ?? Inner.AddAsync(draft, details, imagePath, ct);
        public void OpenSetup() => Inner.OpenSetup();
    }
}
