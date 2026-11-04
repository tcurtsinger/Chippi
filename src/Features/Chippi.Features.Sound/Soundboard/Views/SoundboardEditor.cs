// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Features.Sound.Soundboard.Library;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Sound.Soundboard.Views;

/// <summary>Inline file/link import and clip editing. Drafts are disposed on cancellation or navigation away.</summary>
internal sealed class SoundboardEditor : UserControl, IDisposable
{
    private readonly ISoundboardController _controller;
    private readonly Action _close;
    private readonly string _collectionId;
    private readonly SoundClip? _clip;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<string> _files = [];
    private readonly Grid _root = new();
    private readonly Grid _body = new();
    private readonly TextBlock _heading = SoundboardUi.Text("Add sound", 14);
    private readonly TextBlock _error = SoundboardUi.Text("", 11, "WarningTextBrush");
    private readonly TextBlock _status = SoundboardUi.Text("", 12, "IslandTextSecondaryBrush");
    private readonly TextBlock _durationLabel = SoundboardUi.Text("", 11, "IslandTextSecondaryBrush");
    private readonly ProgressBar _progress = new() { Height = 3, Minimum = 0, Maximum = 1 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBox _url = SoundboardUi.Input("Audio link");
    private readonly TextBox _name = SoundboardUi.Input("Sound name");
    private readonly TextBox _start = SoundboardUi.Input("Start time"), _end = SoundboardUi.Input("End time");
    private readonly ComboBox _collections = new() { Height = 28, FontSize = 12, DisplayMemberPath = nameof(SoundCollection.Name), SelectedValuePath = nameof(SoundCollection.Id) };
    private readonly CheckBox _favorite = new() { Content = "Favorite", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly SoundWaveform _waveform = new();
    private readonly Button _cover;
    private SoundImportDraft? _draft;
    private CancellationTokenSource? _operation;
    private string? _newImage;
    private bool _removeImage, _urlMode, _busy, _disposed, _updating;
    private double _startSeconds, _endSeconds, _duration;
    private int _totalFiles, _completedFiles;

    internal SoundboardEditor(ISoundboardController controller, string collectionId, SoundClip? clip, Action close)
    {
        _controller = controller; _collectionId = collectionId; _clip = clip; _close = close;
        _heading.FontWeight = FontWeights.SemiBold;
        _progress.SetResourceReference(Control.ForegroundProperty, "IslandAccentBrush");
        _progress.SetResourceReference(Control.BackgroundProperty, "IslandSliderTrackBrush");
        _progress.BorderThickness = new Thickness(0);
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        _root.Children.Add(_heading);
        Grid.SetRow(_body, 1); _root.Children.Add(_body);
        _error.Margin = new Thickness(0, 3, 0, 4); _error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        Grid.SetRow(_error, 2); _root.Children.Add(_error);
        Grid.SetRow(_actions, 3); _root.Children.Add(_actions);
        _url.MaxLength = 4096;
        AutomationProperties.SetName(_collections, "Collection");
        _collections.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        _favorite.SetResourceReference(StyleProperty, "SoundboardFavoriteStyle");
        _collections.ItemsSource = controller.Library.Collections;
        _collections.SelectedValue = collectionId == SoundboardPage.Favorites ? controller.Library.Collections.FirstOrDefault()?.Id : collectionId;
        _cover = SoundboardUi.Icon(LucideIcons.Image, "Choose sound image", ChooseImage);
        _cover.Width = _cover.Height = 42;
        var coverMenu = new ContextMenu();
        var clear = new MenuItem { Header = "Remove image" };
        clear.Click += (_, _) => { _newImage = null; _removeImage = true; UpdateCover(); }; coverMenu.Items.Add(clear); _cover.ContextMenu = coverMenu;
        _start.TextChanged += (_, _) => ReadRange(); _end.TextChanged += (_, _) => ReadRange();
        _waveform.RangeChanged += (start, end) =>
        {
            _startSeconds = start; _endSeconds = end; UpdateRangeFields(); ClearError();
        };
        Content = _root;
        if (clip is null) Source(); else { _heading.Text = "Edit sound"; Review(); }
    }

    internal object? InitialFocus => _clip is not null || _draft is not null ? _name : _urlMode ? _url : _body;

    private void Source()
    {
        SoundboardUi.Detach(_url);
        _body.Children.Clear(); _body.RowDefinitions.Clear(); _actions.Children.Clear(); ClearError();
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        choices.Children.Add(SourceTab("File", false)); choices.Children.Add(SourceTab("Link", true)); stack.Children.Add(choices);
        if (_urlMode)
        {
            var label = SoundboardUi.Text("Link", 11, "IslandTextSecondaryBrush"); label.Margin = new Thickness(0, 0, 0, 4); stack.Children.Add(label);
            stack.Children.Add(_url);
            var hint = SoundboardUi.Text("Audio link, YouTube or SoundCloud", 11, "IslandTextSecondaryBrush"); hint.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(hint);
            _actions.Children.Add(SoundboardUi.Button("Cancel", _close));
            var next = SoundboardUi.Button("Continue", () => _ = Prepare(new(_url.Text.Trim(), true)), true); next.Margin = new Thickness(8, 0, 0, 0); _actions.Children.Add(next);
        }
        else
        {
            var drop = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), AllowDrop = true };
            drop.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var hint = SoundboardUi.Text("Drop audio files here", 12); hint.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(hint);
            var browse = SoundboardUi.Button("Browse", ChooseFiles); browse.Margin = new Thickness(0, 10, 0, 0); center.Children.Add(browse); drop.Child = center;
            drop.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_busy ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            drop.Drop += (_, e) => { if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] files) QueueFiles(files); e.Handled = true; };
            stack.Children.Add(drop);
            _actions.Children.Add(SoundboardUi.Button("Cancel", _close));
        }
        _body.Children.Add(stack);
    }

    private Button SourceTab(string label, bool url)
    {
        var button = SoundboardUi.Button(label, () => { _urlMode = url; Source(); });
        button.Width = 78; button.Margin = new Thickness(0, 0, 6, 0);
        if (_urlMode == url) { button.FontWeight = FontWeights.SemiBold; button.SetResourceReference(Control.BackgroundProperty, "IslandGalleryHoverBrush"); }
        AutomationProperties.SetName(button, label + (_urlMode == url ? ", selected" : ""));
        return button;
    }

    private void ChooseFiles()
    {
        if (_busy) return;
        using var held = _controller.HoldOpen();
        var picker = new OpenFileDialog { Title = "Add sounds", Multiselect = true, CheckFileExists = true,
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.webm;*.mp4|All files|*.*" };
        if (picker.ShowDialog() == true) QueueFiles(picker.FileNames);
    }

    internal void QueueFiles(IEnumerable<string> paths)
    {
        if (_busy || _disposed) return;
        var picked = paths.Take(101).ToArray();
        if (picked.Length > 100) { Error("Add up to 100 sounds at a time"); return; }
        _files.Clear();
        foreach (var path in picked) _files.Enqueue(path);
        _totalFiles = _files.Count; _completedFiles = 0;
        if (_files.TryDequeue(out var first)) _ = Prepare(new(first));
    }

    private async Task Prepare(SoundImportSource source)
    {
        if (_busy || _disposed) return;
        if (string.IsNullOrWhiteSpace(source.Value)) { Error("Enter an audio link"); return; }
        _busy = true; ClearError();
        _draft?.Dispose(); _draft = null;
        _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _body.Children.Clear(); _actions.Children.Clear();
        SoundboardUi.Detach(_status); SoundboardUi.Detach(_progress);
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 0) };
        _status.Text = "Preparing sound"; _status.HorizontalAlignment = HorizontalAlignment.Center; _status.Margin = new Thickness(0, 0, 0, 12);
        status.Children.Add(_status); _progress.IsIndeterminate = true; status.Children.Add(_progress); _body.Children.Add(status);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _actions.Children.Add(SoundboardUi.Button("Cancel", _close));
        try
        {
            var progress = new Progress<SoundImportProgress>(value =>
            {
                if (_disposed) return;
                _status.Text = value.Stage;
                _progress.IsIndeterminate = value.Fraction is null;
                _progress.Value = value.Fraction is { } fraction && double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
            });
            var draft = await _controller.PrepareAsync(source, progress, _operation.Token);
            if (_disposed || _operation.IsCancellationRequested) { draft.Dispose(); return; }
            _draft = draft; _newImage = null; _removeImage = false; Review();
        }
        catch (OperationCanceledException) { if (!_disposed) Source(); }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                Source(); Error(ex.Message);
                _actions.Children.Clear();
                _actions.Children.Add(SoundboardUi.Button("Cancel", _close));
                var retry = SoundboardUi.Button("Retry", () => _ = Prepare(_urlMode ? new(_url.Text.Trim(), true) : source), true);
                retry.Margin = new Thickness(8, 0, 0, 0); _actions.Children.Add(retry);
            }
        }
        finally { _busy = false; }
    }

    private void Review()
    {
        foreach (var control in new FrameworkElement[] { _cover, _name, _collections, _waveform, _start, _end, _favorite, _durationLabel }) SoundboardUi.Detach(control);
        _body.Children.Clear(); _body.RowDefinitions.Clear(); _actions.Children.Clear(); ClearError();
        _heading.Text = _clip is not null ? "Edit sound" : _totalFiles > 1 ? $"Add sound · {_completedFiles + 1} of {_totalFiles}" : "Add sound";
        _duration = _clip?.DurationSeconds ?? _draft?.DurationSeconds ?? 0;
        _startSeconds = _clip?.StartSeconds ?? 0;
        _endSeconds = _clip?.EndSeconds ?? _duration;
        _name.Text = _clip?.Name ?? _draft?.Title ?? "Sound";
        _favorite.IsChecked = _clip?.Favorite ?? _collectionId == SoundboardPage.Favorites;
        _collections.SelectedValue = _clip?.CollectionId ?? (_collectionId == SoundboardPage.Favorites ? _controller.Library.Collections.FirstOrDefault()?.Id : _collectionId);
        var content = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        var identity = SoundboardUi.Columns(new GridLength(50), new GridLength(1, GridUnitType.Star), new GridLength(165));
        _cover.HorizontalAlignment = HorizontalAlignment.Left; identity.Children.Add(_cover); UpdateCover();
        var naming = Field("Name", _name); naming.Margin = new Thickness(0, 0, 8, 0); SoundboardUi.At(identity, naming, 1);
        SoundboardUi.At(identity, Field("Collection", _collections), 2); content.Children.Add(identity);
        _waveform.Margin = new Thickness(0, 9, 0, 5); content.Children.Add(_waveform);
        var range = SoundboardUi.Columns(new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star));
        _start.MaxLength = _end.MaxLength = 20;
        var start = InlineField("Start", _start); start.Margin = new Thickness(0, 0, 12, 0); range.Children.Add(start);
        var end = InlineField("End", _end); end.Margin = new Thickness(0, 0, 12, 0); SoundboardUi.At(range, end, 1);
        SoundboardUi.At(range, _durationLabel, 2); content.Children.Add(range);
        UpdateRangeFields();
        var audition = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var preview = SoundboardUi.Button("Preview", () => _ = Preview()); preview.ToolTip = "Only you hear this";
        var previewLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var headphones = SoundboardUi.Glyph(LucideIcons.Headphones, 14); headphones.Margin = new Thickness(0, 0, 6, 0);
        previewLabel.Children.Add(headphones); previewLabel.Children.Add(SoundboardUi.Text("Preview", 12)); preview.Content = previewLabel;
        DockPanel.SetDock(preview, Dock.Left); audition.Children.Add(preview);
        _favorite.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(_favorite, Dock.Right); audition.Children.Add(_favorite);
        var onlyYou = SoundboardUi.Text("Only you hear this", 11, "IslandTextSecondaryBrush"); onlyYou.Margin = new Thickness(9, 0, 0, 0); audition.Children.Add(onlyYou); content.Children.Add(audition);
        _body.Children.Add(content);
        _actions.Children.Add(SoundboardUi.Button("Cancel", _close));
        var save = SoundboardUi.Button(_clip is null ? "Add" : "Save", () => _ = Save(), true); save.Margin = new Thickness(8, 0, 0, 0); _actions.Children.Add(save);
    }

    private static FrameworkElement Field(string label, FrameworkElement control)
    {
        var stack = new StackPanel(); var text = SoundboardUi.Text(label, 11, "IslandTextSecondaryBrush"); text.Margin = new Thickness(0, 0, 0, 3);
        stack.Children.Add(text); stack.Children.Add(control); return stack;
    }

    private static FrameworkElement InlineField(string label, FrameworkElement control)
    {
        var row = new DockPanel(); var text = SoundboardUi.Text(label, 11, "IslandTextSecondaryBrush"); text.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(text, Dock.Left); row.Children.Add(text); row.Children.Add(control); return row;
    }

    private void UpdateRangeFields()
    {
        _updating = true;
        try
        {
            _start.Text = SecondsText(_startSeconds); _end.Text = SecondsText(_endSeconds);
            _durationLabel.Text = $"{SoundboardUi.Time(Math.Max(0, _endSeconds - _startSeconds))} selected";
            _waveform.Set(_clip?.Waveform ?? _draft?.Waveform ?? [], _duration, _startSeconds, _endSeconds);
        }
        finally { _updating = false; }
    }

    private static string SecondsText(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }

    private void ReadRange()
    {
        if (_updating) return;
        if (SoundboardUi.TryTime(_start.Text, out var start) && SoundboardUi.TryTime(_end.Text, out var end)
            && start >= 0 && end > start && end <= _duration + .001)
        {
            _startSeconds = start; _endSeconds = Math.Min(end, _duration);
            _durationLabel.Text = $"{SoundboardUi.Time(Math.Max(0, _endSeconds - _startSeconds))} selected";
            _waveform.Set(_clip?.Waveform ?? _draft?.Waveform ?? [], _duration, _startSeconds, _endSeconds); ClearError();
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { Error("Enter a sound name"); _name.Focus(); return false; }
        if (_collections.SelectedValue is not string) { Error("Choose a collection"); return false; }
        if (!SoundboardUi.TryTime(_start.Text, out var start) || !SoundboardUi.TryTime(_end.Text, out var end)
            || start < 0 || end <= start || end > _duration + .001)
        { Error($"Choose a range within {SoundboardUi.Time(_duration)}"); return false; }
        _startSeconds = start; _endSeconds = Math.Min(end, _duration); ClearError(); return true;
    }

    private async Task Preview()
    {
        if (_busy || _disposed || !Validate()) return;
        try
        {
            if (_clip is not null) await _controller.PreviewClipAsync(_clip.Id, _startSeconds, _endSeconds, _lifetime.Token);
            else if (_draft is not null) await _controller.PreviewAsync(_draft, _startSeconds, _endSeconds, _lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) Error(ex.Message); }
    }

    private async Task Save()
    {
        if (_busy || _disposed || !Validate()) return;
        _busy = true; _actions.IsEnabled = false; _body.IsEnabled = false;
        string? nextFile = null;
        bool saved = false;
        try
        {
            var details = (_clip ?? new SoundClip()) with
            {
                Name = _name.Text.Trim(), CollectionId = (string)_collections.SelectedValue,
                StartSeconds = _startSeconds, EndSeconds = _endSeconds,
                DurationSeconds = _duration, Favorite = _favorite.IsChecked == true,
                ImageFile = _removeImage ? null : _clip?.ImageFile,
            };
            if (_controller.Playback.IsPreview) await _controller.StopAsync();
            if (_clip is not null) await _controller.SaveClipAsync(details, _newImage);
            else if (_draft is not null) await _controller.AddAsync(_draft, details, _newImage, _lifetime.Token);
            if (_disposed) return;
            _draft?.Dispose(); _draft = null; _completedFiles++;
            _files.TryDequeue(out nextFile); saved = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) Error(ex.Message); }
        finally { _busy = false; _actions.IsEnabled = true; _body.IsEnabled = true; }
        if (saved && !_disposed)
        {
            if (nextFile is not null) await Prepare(new(nextFile));
            else _close();
        }
    }

    private void ChooseImage()
    {
        if (_busy || _disposed) return;
        using var held = _controller.HoldOpen();
        var picker = new OpenFileDialog { Title = "Sound image", Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif", CheckFileExists = true };
        if (picker.ShowDialog() == true)
        {
            if (SoundboardUi.Cover(picker.FileName) is null) { Error("Image could not be opened"); return; }
            _newImage = picker.FileName; _removeImage = false; UpdateCover();
        }
    }

    private void UpdateCover()
    {
        var image = SoundboardUi.Cover(_newImage ?? (_removeImage || _clip is null ? null : _controller.ImagePath(_clip)));
        _cover.Content = image is null ? SoundboardUi.Glyph(LucideIcons.Image, 22) : new Image { Source = image, Stretch = Stretch.UniformToFill, Width = 38, Height = 38 };
    }

    private void Error(string message)
    {
        _error.Text = message; _error.ToolTip = message; _error.Visibility = Visibility.Visible;
    }

    private void ClearError() { _error.Text = ""; _error.Visibility = Visibility.Collapsed; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _operation?.Cancel();
        var draft = _draft; _draft = null;
        _ = ReleaseDraft(draft, _controller.Playback.IsPreview);
        _operation?.Dispose(); _lifetime.Dispose();
    }

    private async Task ReleaseDraft(SoundImportDraft? draft, bool stop)
    {
        try { if (stop) await _controller.StopAsync(); }
        catch { /* the module keeps a playback failure; navigation must still finish */ }
        finally { draft?.Dispose(); }
    }
}
