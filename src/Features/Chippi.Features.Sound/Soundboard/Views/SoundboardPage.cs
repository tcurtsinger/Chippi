// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Features.Sound.Soundboard.Library;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Soundboard.Views;

/// <summary>Collection tabs and a four-column sound gallery, within Audio's compact island body.</summary>
public sealed class SoundboardPage : UserControl
{
    public const string Favorites = "@favorites";
    private const string DragFormat = "Chippi.Soundboard.Clip";
    private readonly ISoundboardController _controller;
    private readonly Grid _root = new();
    private readonly Grid _stage = new();
    private readonly Grid _gallery = new();
    private readonly Grid _toolbar = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _error = SoundboardUi.Text("", 11, "WarningTextBrush");
    private readonly TextBlock _playingName = SoundboardUi.Text("Nothing playing", 12);
    private readonly TextBlock _time = SoundboardUi.Text("", 11, "IslandTextTertiaryBrush");
    private readonly Slider _send = new(), _hear = new();
    private readonly TextBlock _sendValue = SoundboardUi.Text("", 11), _hearValue = SoundboardUi.Text("", 11);
    private readonly Button _stop;
    private readonly LatestWrite _levelWrites = new();
    private readonly Dictionary<string, Cell> _cells = [];
    private SoundboardEditor? _editor;
    private string? _localError;
    private string _shape = "";
    private bool _loaded, _updating, _editing;
    private List<string>? _order;
    private Point? _press;
    private string? _pressedId;
    private string? _selectedOverride;
    private SoundboardLibrary? _renderedLibrary;
    private string? _renderedSelection, _renderedAvailability, _activeTile;
    private bool _formActive;
    private TextBlock? _formError;
    private sealed record Cell(Button Button, TextBlock Name, TextBlock Duration, LucideIcon? Glyph, Grid Track, Border Progress);

    public SoundboardPage(ISoundboardController controller)
    {
        _controller = controller;
        Width = 496; Height = 296;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi.Features.Sound;component/Soundboard/Views/SoundboardStyles.xaml") });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(37) });
        _root.Children.Add(_stage);
        _stop = SoundboardUi.Icon(LucideIcons.Square, "Stop sound", () => _ = Run(_controller.StopAsync));
        ((LucideIcon)_stop.Content).Filled = true;
        _stop.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _stop.MinWidth = 28; _stop.Padding = new Thickness(5);
        var footer = Playback();
        Grid.SetRow(footer, 1); _root.Children.Add(footer);
        _error.ToolTip = "";
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        _error.Margin = new Thickness(0, 0, 0, 5);
        Content = _root;
        Loaded += (_, _) => { _loaded = true; _controller.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) =>
        {
            _loaded = false; _controller.Changed -= OnChanged;
            _editor?.Dispose(); _editor = null; _editing = false; _order = null; _shape = "";
            _formActive = false; _formError = null;
            _press = null; _pressedId = null;
        };
        Refresh();
    }

    public object? InitialFocus => _editor?.InitialFocus ?? _cells.Values.FirstOrDefault()?.Button ?? (object)_tabs;

    public bool HandleBack()
    {
        if (_editor is not null) { CloseEditor(); return true; }
        if (_formActive) { CloseForm(); return true; }
        if (_editing) { _editing = false; _order = null; _shape = ""; Refresh(); return true; }
        return false;
    }

    public bool HandleEscape() => HandleBack();

    private string Selected => _selectedOverride ?? _controller.Library.SelectedCollectionId ?? _controller.Library.Collections.FirstOrDefault()?.Id ?? Favorites;

    private void OnChanged()
    {
        if (!_loaded) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_selectedOverride is { } selection && selection == _controller.Library.SelectedCollectionId) _selectedOverride = null;
        _updating = true;
        try
        {
            if (_editor is null && !_formActive)
            {
                if (!ReferenceEquals(_renderedLibrary, _controller.Library) || _shape.Length == 0
                    || _renderedSelection != Selected || _renderedAvailability != _controller.SendUnavailableReason)
                {
                    var clips = VisibleClips();
                    var shape = string.Join('|', _controller.Library.Collections.Select(c => $"{c.Id}:{c.Name}")) + ";" + Selected + ";" + _editing + ";" + _controller.SendUnavailableReason + ";"
                        + string.Join('|', clips.Select(c => $"{c.Id}:{c.Name}:{c.ImageFile}:{c.StartSeconds}:{c.EndSeconds}:{c.Favorite}"));
                    if (shape != _shape) { _shape = shape; BuildGallery(clips); }
                    _renderedLibrary = _controller.Library; _renderedSelection = Selected; _renderedAvailability = _controller.SendUnavailableReason;
                }
                var message = _localError ?? _controller.Error ?? _controller.SendUnavailableReason;
                _error.Text = message ?? ""; _error.ToolTip = message;
                _error.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
                UpdateTiles();
            }
            UpdatePlayback();
        }
        finally { _updating = false; }
    }

    private List<SoundClip> VisibleClips()
    {
        var clips = _controller.Library.Clips.Where(c => Selected == Favorites ? c.Favorite : c.CollectionId == Selected).ToList();
        if (_editing && _order is not null)
        {
            var byId = clips.ToDictionary(c => c.Id);
            return [.. _order.Where(byId.ContainsKey).Select(id => byId[id]), .. clips.Where(c => !_order.Contains(c.Id))];
        }
        return clips;
    }

    private void BuildGallery(IReadOnlyList<SoundClip> clips)
    {
        SoundboardUi.Detach(_gallery); SoundboardUi.Detach(_error);
        _stage.Children.Clear(); _stage.RowDefinitions.Clear();
        _stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        _stage.RowDefinitions.Add(new RowDefinition());
        BuildTabs();
        _stage.Children.Add(_toolbar);
        var body = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        Grid.SetRow(body, 1); _stage.Children.Add(body);
        var messageRow = new DockPanel();
        if (_controller.SendUnavailableReason is not null)
        {
            var setup = SoundboardUi.Button("Set up", _controller.OpenSetup);
            setup.Height = 23; setup.MinWidth = 50; setup.Margin = new Thickness(8, 0, 0, 4);
            DockPanel.SetDock(setup, Dock.Right); messageRow.Children.Add(setup);
        }
        messageRow.Children.Add(_error); DockPanel.SetDock(messageRow, Dock.Top); body.Children.Add(messageRow);
        _gallery.Children.Clear(); _gallery.RowDefinitions.Clear(); _gallery.ColumnDefinitions.Clear(); _cells.Clear(); _activeTile = null;
        for (var i = 0; i < 4; i++) _gallery.ColumnDefinitions.Add(new ColumnDefinition());
        if (clips.Count == 0)
        {
            var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var glyph = SoundboardUi.Glyph(Selected == Favorites ? LucideIcons.Star : LucideIcons.AudioLines, 24);
            glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.Margin = new Thickness(0, 0, 0, 8);
            var title = SoundboardUi.Text(Selected == Favorites ? "No favorites" : "No sounds", 13);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(glyph); empty.Children.Add(title);
            if (Selected == Favorites)
            {
                var hint = SoundboardUi.Text("Mark a sound as a favorite", 11, "IslandTextSecondaryBrush"); hint.Margin = new Thickness(0, 4, 0, 0); empty.Children.Add(hint);
            }
            else
            {
                var add = SoundboardUi.Button("Add", BeginAdd, true); add.Margin = new Thickness(0, 10, 0, 0); empty.Children.Add(add);
            }
            body.Children.Add(empty); return;
        }
        int rows = (clips.Count + 3) / 4;
        for (int row = 0; row < rows; row++) _gallery.RowDefinitions.Add(new RowDefinition { Height = new GridLength(row == rows - 1 ? 66 : 73) });
        foreach (var (clip, index) in clips.Select((clip, index) => (clip, index)))
        {
            var tile = Tile(clip);
            tile.Margin = new Thickness(0, 0, index % 4 == 3 ? 0 : 7, index / 4 == rows - 1 ? 0 : 7);
            SoundboardUi.At(_gallery, tile, index % 4, index / 4);
        }
        body.Children.Add(new ScrollViewer { Content = _gallery, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Focusable = false });
    }

    private void BuildTabs()
    {
        _toolbar.Children.Clear(); _toolbar.ColumnDefinitions.Clear(); _tabs.Children.Clear();
        _toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        _toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (_editing)
        {
            var done = SoundboardUi.Button("Done", () => _ = SaveOrder(), true); actions.Children.Add(done);
        }
        else
        {
            var edit = SoundboardUi.Icon(LucideIcons.Pencil, "Edit sounds", BeginEdit);
            edit.IsEnabled = _controller.Library.Clips.Any(c => Selected == Favorites ? c.Favorite : c.CollectionId == Selected);
            actions.Children.Add(edit);
            var add = SoundboardUi.Button("+ Add", BeginAdd); add.MinWidth = 58; add.Margin = new Thickness(3, 0, 0, 0); actions.Children.Add(add);
        }
        SoundboardUi.At(_toolbar, actions, 1);
        var collections = new List<SoundCollection> { new(Favorites, "Favorites") };
        collections.AddRange(_controller.Library.Collections);
        // Use actual label widths: ordinary collections stay one click away, long names yield to
        // overflow, and the selected collection always keeps its own visible tab.
        double available = 496 - (_editing ? 64 : 89) - 28;
        double WidthOf(SoundCollection collection)
        {
            var label = SoundboardUi.Text(collection.Name, 12);
            label.FontWeight = FontWeights.SemiBold;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Clamp(label.DesiredSize.Width + 14, 35, 92);
        }
        double total = collections.Sum(WidthOf), budget = total <= available ? available : available - 30;
        double used = 0;
        var visible = new List<SoundCollection>();
        foreach (var collection in collections)
        {
            double width = WidthOf(collection);
            if (used + width > budget) break;
            visible.Add(collection); used += width;
        }
        var selected = collections.FirstOrDefault(c => c.Id == Selected);
        if (selected is not null && !visible.Contains(selected))
        {
            while (visible.Count > 0 && used + WidthOf(selected) > budget)
            { used -= WidthOf(visible[^1]); visible.RemoveAt(visible.Count - 1); }
            visible.Add(selected);
        }
        foreach (var collection in visible) _tabs.Children.Add(CollectionTab(collection));
        if (collections.Count > visible.Count)
        {
            Button? overflow = null;
            overflow = SoundboardUi.Button($"+{collections.Count - visible.Count}", () => CollectionMenu(overflow!, collections.Where(c => !visible.Contains(c))));
            overflow.MinWidth = 25; overflow.Width = 30; overflow.Padding = new Thickness(2); overflow.SetResourceReference(StyleProperty, "SoundboardQuietButtonStyle");
            AutomationProperties.SetName(overflow, "More collections"); overflow.IsEnabled = !_editing; _tabs.Children.Add(overflow);
        }
        var addCollection = SoundboardUi.Icon(LucideIcons.Plus, "Add collection", () => CollectionForm(null));
        addCollection.IsEnabled = !_editing; _tabs.Children.Add(addCollection);
        _toolbar.Children.Add(_tabs);
    }

    private Button CollectionTab(SoundCollection collection)
    {
        var active = collection.Id == Selected;
        var text = SoundboardUi.Text(collection.Name, 12, active ? "IslandTextBrush" : "IslandTextSecondaryBrush");
        text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        var panel = new Grid(); panel.Children.Add(text);
        if (active)
        {
            var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1) };
            line.SetResourceReference(Border.BackgroundProperty, "IslandAccentBrush"); panel.Children.Add(line);
        }
        var tab = new Button { Content = panel, MaxWidth = 92, MinWidth = 35, Padding = new Thickness(7, 0, 7, 3), Height = 30,
            ToolTip = collection.Name, IsEnabled = !_editing };
        tab.SetResourceReference(StyleProperty, "SoundboardQuietButtonStyle");
        AutomationProperties.SetName(tab, collection.Name + (active ? ", selected" : ""));
        tab.Click += (_, _) => Select(collection.Id);
        if (collection.Id != Favorites)
        {
            var menu = new ContextMenu();
            MenuItem(menu, "Rename", () => CollectionForm(collection));
            var delete = MenuItem(menu, "Delete", () => Confirm($"Delete {collection.Name}?", () => _controller.DeleteCollectionAsync(collection.Id)));
            delete.IsEnabled = _controller.Library.Collections.Count > 1 && !_controller.Library.Clips.Any(c => c.CollectionId == collection.Id);
            if (!delete.IsEnabled) delete.ToolTip = "Move sounds out before deleting this collection";
            tab.ContextMenu = menu;
        }
        return tab;
    }

    private void CollectionMenu(Button anchor, IEnumerable<SoundCollection> collections)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var collection in collections)
        {
            var item = MenuItem(menu, collection.Name, () => Select(collection.Id)); item.IsCheckable = true; item.IsChecked = collection.Id == Selected;
        }
        menu.IsOpen = true;
    }

    private void Select(string id)
    {
        if (_editing) return;
        _selectedOverride = id; _controller.SelectCollection(id); _localError = null; _shape = ""; Refresh();
    }

    private Button Tile(SoundClip clip)
    {
        var button = new Button { Height = 66, Tag = clip.Id, AllowDrop = _editing };
        button.SetResourceReference(StyleProperty, "SoundboardTileStyle");
        var content = new Grid { Margin = new Thickness(5, 4, 5, 4) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        content.RowDefinitions.Add(new RowDefinition());
        var image = SoundboardUi.Cover(_controller.ImagePath(clip));
        LucideIcon? glyph = null;
        FrameworkElement art = image is null ? glyph = SoundboardUi.Glyph(LucideIcons.AudioLines, 18) : new Image { Source = image, Width = 22, Height = 22, Stretch = Stretch.UniformToFill };
        art.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(art);
        var name = SoundboardUi.Text(clip.Name, 12); name.FontWeight = FontWeights.SemiBold; name.TextAlignment = TextAlignment.Center;
        SoundboardUi.At(content, name, 0, 1);
        var duration = SoundboardUi.Text(SoundboardUi.Time(clip.EndSeconds - clip.StartSeconds), 11, "IslandTextSecondaryBrush");
        duration.TextAlignment = TextAlignment.Center; Typography.SetNumeralAlignment(duration, FontNumeralAlignment.Tabular);
        SoundboardUi.At(content, duration, 0, 2);
        var track = new Grid { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), Visibility = Visibility.Collapsed };
        var remainder = new Border { CornerRadius = new CornerRadius(1), Opacity = .2 };
        remainder.SetResourceReference(Border.BackgroundProperty, "SoundboardGradientInkBrush"); track.Children.Add(remainder);
        var progress = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1), Opacity = .75 };
        progress.SetResourceReference(Border.BackgroundProperty, "SoundboardGradientInkBrush"); track.Children.Add(progress);
        Grid.SetRowSpan(track, 3); content.Children.Add(track);
        button.Content = content;
        AutomationProperties.SetName(button, $"{clip.Name}, {SoundboardUi.Time(clip.EndSeconds - clip.StartSeconds)}");
        button.ToolTip = _editing ? $"{clip.Name} · Drag to reorder, or Alt+arrow keys" : clip.Name;
        button.Click += (_, _) => { if (_editing) BeginClipEdit(clip); else _ = Run(() => _controller.PlayAsync(clip.Id)); };
        button.PreviewMouseLeftButtonDown += (_, e) => { if (_editing) { _press = e.GetPosition(this); _pressedId = clip.Id; } };
        button.PreviewMouseLeftButtonUp += (_, _) => { _press = null; _pressedId = null; };
        button.MouseMove += (_, e) =>
        {
            if (!_editing || _press is not { } start || _pressedId != clip.Id || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(this);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _press = null; _pressedId = null;
            DragDrop.DoDragDrop(button, new DataObject(DragFormat, clip.Id), DragDropEffects.Move);
            e.Handled = true;
        };
        button.DragOver += (_, e) => { e.Effects = ValidDrag(e) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        button.Drop += (_, e) =>
        {
            if (ValidDrag(e) && e.Data.GetData(DragFormat) is string id) Move(id, clip.Id);
            e.Handled = true;
        };
        button.PreviewKeyDown += (_, e) =>
        {
            e.Handled = ReorderWithKey(clip.Id, e.Key, e.SystemKey, Keyboard.Modifiers);
        };
        var menu = new ContextMenu();
        MenuItem(menu, "Edit", () => BeginClipEdit(clip));
        MenuItem(menu, clip.Favorite ? "Remove favorite" : "Favorite", () => _ = Run(() => _controller.SaveClipAsync(clip with { Favorite = !clip.Favorite })));
        MenuItem(menu, "Delete", () => Confirm($"Delete {clip.Name}?", () => _controller.DeleteClipAsync(clip.Id)));
        button.ContextMenu = menu;
        _cells[clip.Id] = new(button, name, duration, glyph, track, progress);
        track.SizeChanged += (_, _) => UpdateTiles();
        return button;
    }

    private bool ValidDrag(DragEventArgs e) => _editing && _order is not null && e.Data.GetDataPresent(DragFormat)
        && e.Data.GetData(DragFormat) is string id && _order.Contains(id);

    internal bool ReorderWithKey(string clipId, Key key, Key systemKey, ModifierKeys modifiers)
    {
        if (!_editing || (modifiers & ModifierKeys.Alt) == 0 || _order is null) return false;
        // WPF reports Alt combinations as Key.System; the arrow lives in SystemKey.
        var actualKey = key == Key.System ? systemKey : key;
        int delta = actualKey switch { Key.Left => -1, Key.Right => 1, Key.Up => -4, Key.Down => 4, _ => 0 };
        if (delta == 0) return false;
        int index = _order.IndexOf(clipId), target = index + delta;
        if (index >= 0 && target >= 0 && target < _order.Count)
        { Move(clipId, _order[target]); _cells.GetValueOrDefault(clipId)?.Button.Focus(); }
        return true;
    }

    internal void Move(string id, string before)
    {
        if (_order is null || id == before) return;
        int from = _order.IndexOf(id), target = _order.IndexOf(before);
        if (from < 0 || target < 0) return;
        _order.RemoveAt(from); _order.Insert(target, id); _shape = ""; Refresh();
    }

    private void BeginEdit()
    {
        _editing = true; _order = VisibleClips().Select(c => c.Id).ToList(); _shape = ""; Refresh();
    }

    private async Task SaveOrder()
    {
        var order = _order?.ToArray();
        if (order is null) return;
        if (await Run(() => _controller.ReorderClipsAsync(Selected, order)))
        { _editing = false; _order = null; _shape = ""; Refresh(); }
    }

    private void UpdateTiles()
    {
        var playing = _controller.Playback;
        string? activeId = !_editing && playing.IsPlaying && !playing.IsPreview ? playing.ClipId : null;
        if (_activeTile != activeId)
        {
            if (_activeTile is not null && _cells.TryGetValue(_activeTile, out var previous)) Paint(previous, false);
            if (activeId is not null && _cells.TryGetValue(activeId, out var current)) Paint(current, true);
            _activeTile = activeId;
        }
        if (activeId is not null && _cells.TryGetValue(activeId, out var cell))
        {
            cell.Progress.HorizontalAlignment = HorizontalAlignment.Left;
            double fraction = playing.Duration.TotalSeconds <= 0 ? 0 : Math.Clamp(playing.Position.TotalSeconds / playing.Duration.TotalSeconds, 0, 1);
            cell.Progress.Width = Math.Max(0, cell.Track.ActualWidth * fraction);
        }
        static void Paint(Cell cell, bool active)
        {
            if (active) cell.Button.SetResourceReference(Control.BackgroundProperty, "IslandAccentFillBrush");
            else cell.Button.ClearValue(Control.BackgroundProperty);
            cell.Name.SetResourceReference(TextBlock.ForegroundProperty, active ? "SoundboardGradientInkBrush" : "IslandTextBrush");
            cell.Duration.SetResourceReference(TextBlock.ForegroundProperty, active ? "SoundboardGradientInkBrush" : "IslandTextSecondaryBrush");
            cell.Glyph?.SetResourceReference(LucideIcon.BrushProperty, active ? "SoundboardGradientInkBrush" : "IslandTextBrush");
            cell.Track.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private FrameworkElement Playback()
    {
        var row = SoundboardUi.Columns(new GridLength(1, GridUnitType.Star), new GridLength(125), new GridLength(125), new GridLength(36));
        row.Margin = new Thickness(0, 5, 0, 0);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        title.Children.Add(_playingName); title.Children.Add(_time); row.Children.Add(title);
        SoundboardUi.At(row, Level(_send, _sendValue, "Send", "Sound sent to virtual mic", LucideIcons.Mic), 1);
        SoundboardUi.At(row, Level(_hear, _hearValue, "Hear", "Sound in your speakers or headphones", LucideIcons.Headphones), 2);
        _stop.HorizontalAlignment = HorizontalAlignment.Right; SoundboardUi.At(row, _stop, 3);
        var border = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
        border.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        return border;
    }

    private FrameworkElement Level(Slider slider, TextBlock value, string label, string accessible, Geometry icon)
    {
        var panel = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) }); panel.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel();
        var glyph = SoundboardUi.Glyph(icon, 12); glyph.Margin = new Thickness(0, 0, 4, 0);
        DockPanel.SetDock(glyph, Dock.Left); header.Children.Add(glyph);
        value.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(value, Dock.Right); header.Children.Add(value);
        header.Children.Add(SoundboardUi.Text(label, 11, "IslandTextSecondaryBrush")); panel.Children.Add(header);
        slider.Minimum = 0; slider.Maximum = 100; slider.SmallChange = 1; slider.LargeChange = 10; slider.Height = 16;
        slider.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(slider, accessible); Grid.SetRow(slider, 1); panel.Children.Add(slider);
        slider.ValueChanged += (_, _) =>
        {
            value.Text = $"{slider.Value:0}%";
            if (!_updating)
            {
                float send = (float)(_send.Value / 100), hear = (float)(_hear.Value / 100);
                _levelWrites.Send(async () => { await Run(() => _controller.SetLevelsAsync(send, hear)); });
            }
        };
        return panel;
    }

    private void UpdatePlayback()
    {
        var state = _controller.Playback;
        var clip = _controller.Library.Clips.FirstOrDefault(c => c.Id == state.ClipId);
        _playingName.Text = state.IsPlaying ? state.IsPreview ? "Preview" : clip?.Name ?? "Sound" : "Nothing playing";
        _playingName.ToolTip = _playingName.Text;
        _time.Text = state.IsPlaying ? $"{SoundboardUi.Time(state.Position.TotalSeconds)} / {SoundboardUi.Time(state.Duration.TotalSeconds)}" : "";
        _time.Visibility = state.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        _stop.IsEnabled = state.IsPlaying;
        if (!_levelWrites.Busy && !_send.IsMouseCaptureWithin) _send.Value = _controller.Library.SendVolume * 100;
        if (!_levelWrites.Busy && !_hear.IsMouseCaptureWithin) _hear.Value = _controller.Library.HearVolume * 100;
        _sendValue.Text = $"{_send.Value:0}%"; _hearValue.Text = $"{_hear.Value:0}%";
    }

    private void BeginAdd() => ShowEditor(new SoundboardEditor(_controller, Selected, null, CloseEditor));
    private void BeginClipEdit(SoundClip clip) => ShowEditor(new SoundboardEditor(_controller, clip.CollectionId, clip, CloseEditor));

    private void ShowEditor(SoundboardEditor editor)
    {
        _editor?.Dispose(); _editor = editor; _shape = "";
        _stage.Children.Clear(); _stage.RowDefinitions.Clear(); _stage.Children.Add(editor);
    }

    private void CloseEditor()
    {
        _editor?.Dispose(); _editor = null; _shape = ""; Refresh();
    }

    private void CollectionForm(SoundCollection? collection)
    {
        _formActive = true;
        var form = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(56, 0, 56, 0) };
        var title = SoundboardUi.Text(collection is null ? "Add collection" : "Rename collection", 14); title.FontWeight = FontWeights.SemiBold;
        form.Children.Add(title);
        var input = SoundboardUi.Input("Collection name", collection?.Name ?? ""); input.Margin = new Thickness(0, 10, 0, 12); form.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = SoundboardUi.Button("Cancel", CloseForm); cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel);
        actions.Children.Add(SoundboardUi.Button(collection is null ? "Add" : "Save", () => _ = Save(), true)); form.Children.Add(actions);
        _formError = SoundboardUi.Text("", 11, "WarningTextBrush"); _formError.Margin = new Thickness(0, 8, 0, 0); form.Children.Add(_formError);
        _stage.Children.Clear(); _stage.RowDefinitions.Clear(); _stage.Children.Add(form);
        async Task Save()
        {
            if (!actions.IsEnabled) return;
            string name = input.Text.Trim();
            if (name.Length == 0) { AutomationProperties.SetHelpText(input, "Enter a collection name"); input.Focus(); return; }
            actions.IsEnabled = false; input.IsEnabled = false;
            bool ok = await Run(async () =>
            {
                if (collection is null) Select(await _controller.CreateCollectionAsync(name));
                else await _controller.RenameCollectionAsync(collection.Id, name);
            });
            actions.IsEnabled = true; input.IsEnabled = true;
            if (ok) CloseForm();
        }
    }

    private void Confirm(string question, Func<Task> action)
    {
        _formActive = true;
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 0) };
        var title = SoundboardUi.Text(question, 14); title.TextWrapping = TextWrapping.Wrap; title.TextTrimming = TextTrimming.None; panel.Children.Add(title);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = SoundboardUi.Button("Cancel", CloseForm); cancel.Margin = new Thickness(0, 0, 8, 0); controls.Children.Add(cancel);
        controls.Children.Add(SoundboardUi.Button("Delete", () => _ = Delete(), true)); panel.Children.Add(controls);
        _formError = SoundboardUi.Text("", 11, "WarningTextBrush"); _formError.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(_formError);
        _stage.Children.Clear(); _stage.RowDefinitions.Clear(); _stage.Children.Add(panel);
        async Task Delete()
        {
            if (!controls.IsEnabled) return;
            controls.IsEnabled = false;
            bool ok = await Run(action);
            controls.IsEnabled = true;
            if (ok) CloseForm();
        }
    }

    private void CloseForm() { _formActive = false; _formError = null; _shape = ""; Refresh(); }

    private static MenuItem MenuItem(ContextMenu menu, string text, Action action)
    {
        var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); return item;
    }

    private async Task<bool> Run(Func<Task> command)
    {
        try { _localError = null; await command(); Refresh(); return true; }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _localError = ex.Message;
            if (_formError is not null) { _formError.Text = ex.Message; _formError.ToolTip = ex.Message; }
            Refresh(); return false;
        }
    }
}
