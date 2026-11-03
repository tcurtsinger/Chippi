// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Clipboard;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard.History;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// The Clipboard page with Clipboard history (design notes #8 to #16, #23, #59; refresh brief Part D #5):
/// the header's Clean URL, Pause and More; first use; Search; the Pinned and Recent list, as many whole
/// rows as fit, whose selected row opens in place with Paste, Copy, Pin and Delete; the Clean URL view
/// behind the link button. Nothing scrolls.
/// </summary>
internal sealed class ClipboardView : UserControl
{
    /// <summary>More rows than the 268 under Search can show, even at a heading's 22: the list never needs the rest.</summary>
    internal const int BuiltRows = 16;
    private const double ArtWidth = 32, ContentInset = 2 + ArtWidth + 8;

    // A rebuild can follow a clock tick, a newly saved clip, or a click on the row itself.
    private readonly record struct RowTarget(long Id, long Hash, string Part);
    private readonly Dictionary<RowTarget, Button> _buttons = [];

    private readonly ClipboardFeatures _features;
    private readonly TextBlock _note = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
    private readonly Button _cleanButton = Ui.Icon(LucideIcons.Link, "Clean URL");
    private readonly Button _pause = Ui.Icon(LucideIcons.Pause, "Pause");
    private readonly Button _more = Ui.Icon(LucideIcons.Ellipsis, "More");
    private readonly StackPanel _firstUse = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _turnOn = Ui.Button("Turn on", "IslandAccentButtonStyle");
    private readonly SearchBox _search = new() { Placeholder = "Search", Height = 28, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _allHistory = Ui.Button("All history", "SubtleButtonStyle");
    private readonly StackPanel _caution = Ui.Caution();
    private readonly StackPanel _saveError = Ui.Caution();
    private readonly WholeRows _rows = new();
    private readonly TextBlock _empty = Ui.Muted("", 12);
    private readonly DockPanel _history = new();
    private readonly CleanUrlView _clean;
    private readonly HistoryThumbnails _thumbnails = new();
    private readonly Dictionary<long, (string Path, BitmapSource? Image)> _fileThumbnails = []; // the same, checked against the path; null while reading or unreadable
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<HistoryEntry> _shown = [];
    private (long Id, long Hash)? _selected;
    private string? _status; // the selected row's "Clipboard busy" or "Copied"
    private int _actionVersion;
    private bool _cleaning;
    private bool _watching;
    private ClipboardHistoryModule? _module;

    public ClipboardView(ClipboardFeatures features)
    {
        _features = features;
        _clean = new CleanUrlView(features);

        // Header commands (notes #8): Clean URL (link, pressed while open), Pause or Resume, More.
        _cleanButton.Click += (_, _) => SetCleaning(!_cleaning);
        _pause.Click += (_, _) =>
        {
            if (_module is null) return;
            if (_module.Settings.Recording) _module.SetPaused(!_module.Settings.Paused);
            else _module.SetRecording(true);
        };
        _more.Click += (_, _) => OpenMore();
        _note.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Children = { _note, _cleanButton, _pause, _more } };
        DockPanel.SetDock(commands, Dock.Right);
        Header = new DockPanel { Height = 24, Margin = new Thickness(0, 0, 0, 8), Children = { commands, new TextBlock { Text = "Clipboard", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };

        // First use (notes #10): the glyph, "Recording off" and Turn on, in the middle of the body.
        _turnOn.Click += (_, _) => _module?.SetRecording(true);
        _turnOn.HorizontalAlignment = HorizontalAlignment.Center;
        _turnOn.Margin = new Thickness(0, 6, 0, 0);
        var off = Ui.Muted("Recording off");
        off.HorizontalAlignment = HorizontalAlignment.Center;
        var glyph = Ui.Glyph(LucideIcons.ClipboardList, 20, "IslandTextTertiaryBrush");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.Margin = new Thickness(0, 0, 0, 6);
        _firstUse.Children.Add(glyph);
        _firstUse.Children.Add(off);
        _firstUse.Children.Add(_turnOn);

        _search.TextChanged += (_, _) => Refresh();
        _search.PreviewKeyDown += OnSearchKey;
        AutomationProperties.SetName(_search, "Search");
        _caution.Margin = new Thickness(10, 0, 0, 8);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _rows.VerticalAlignment = VerticalAlignment.Top;
        _allHistory.Height = 28;
        _allHistory.Margin = new Thickness(6, 0, 0, 8);
        _allHistory.Padding = new Thickness(8, 0, 8, 0);
        _allHistory.FontSize = 12;
        _allHistory.SetResourceReference(ForegroundProperty, "IslandTextSecondaryBrush");
        _allHistory.Visibility = Visibility.Collapsed;
        _allHistory.Click += (_, _) => _module?.OpenHistory(_search.Text);
        _rows.OverflowChanged = overflow => _allHistory.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        var searchBar = new Grid();
        searchBar.ColumnDefinitions.Add(new ColumnDefinition());
        searchBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_allHistory, 1);
        searchBar.Children.Add(_search);
        searchBar.Children.Add(_allHistory);
        DockPanel.SetDock(searchBar, Dock.Top);
        DockPanel.SetDock(_caution, Dock.Top);
        _history.Children.Add(searchBar);
        _history.Children.Add(_caution);
        _history.Children.Add(new Grid { Children = { _rows, _empty } });

        DockPanel.SetDock(_saveError, Dock.Top);
        _saveError.Margin = new Thickness(10, 0, 0, 8);
        Content = new DockPanel { Children = { _saveError, new Grid { Children = { _firstUse, _history, _clean } } } };
        // The page's whole body, so first use centers in it: the page stacks Clean URL's section under
        // this one, collapsed while History runs, which leaves this one no height of its own.
        Height = IslandPage.BodyHeight;
        AutomationProperties.SetName(this, "Clipboard");

        _clock.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _watching = true;
            _features.Changed += OnFeatures;
            Attach();
            _clock.Start();
            _search.Text = "";
            _selected = null;
            SetCleaning(false, focus: false);
        };
        Unloaded += (_, _) =>
        {
            _watching = false;
            _features.Changed -= OnFeatures;
            Detach();
            _clock.Stop();
            _actionVersion++;
            _selected = null;
            _status = null;
            _search.Text = "";
            _shown.Clear();
            ClearRows();
            _thumbnails.Clear(); // no picture stays in memory while the page is closed
            _fileThumbnails.Clear();
        };
    }

    /// <summary>Where image files' pictures come from (tests use fakes).</summary>
    internal FileThumbnails Files { get; set; } = FileThumbnails.Disk;

    /// <summary>The latest image file read (tests wait on it).</summary>
    internal Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>The page's header: its name, "Paused", then Clean URL, Pause and More (notes #8, #10).</summary>
    public DockPanel Header { get; }

    /// <summary>Where the keyboard lands when the page opens: Search, the link field in Clean URL, or Turn on (notes #58).</summary>
    public UIElement InitialFocus => _cleaning ? _clean.Field : _history.Visibility == Visibility.Visible ? _search : _turnOn;

    /// <summary>The first Esc: clears a search or leaves Clean URL; true when it did (notes #59).</summary>
    public bool HandleEscape()
    {
        if (HandleBack())
            return true;
        if (_search.Text.Length > 0)
        {
            _search.Text = "";
            return true;
        }
        return false;
    }

    public bool HandleBack()
    {
        if (!_cleaning) return false;
        SetCleaning(false);
        return true;
    }

    // ---- Module ----

    private void OnFeatures() => Dispatcher.BeginInvoke(() =>
    {
        if (!_watching) return;
        Detach();
        Attach();
        if (_cleaning && _features.CleanUrl is null) SetCleaning(false);
        else Refresh();
    });

    private void Attach()
    {
        _module = _features.History;
        if (_module is null)
            return;
        _module.Changed += OnModuleChanged;
        if (_module.History is { } history)
            history.Changed += OnModuleChanged;
    }

    private void Detach()
    {
        if (_module is null)
            return;
        _module.Changed -= OnModuleChanged;
        if (_module.History is { } history)
            history.Changed -= OnModuleChanged;
        _module = null;
    }

    private void OnModuleChanged() => Dispatcher.BeginInvoke(() => { if (_module is not null) Refresh(); });

    // ---- Views ----

    private void SetCleaning(bool on, bool focus = true)
    {
        if (_features.CleanUrl is null)
            on = false;
        _cleaning = on;
        // Pressed while Clean URL shows (notes #23).
        _cleanButton.Tag = on ? "Selected" : null;
        _cleanButton.ToolTip = on ? "Back to Clipboard" : "Clean URL";
        AutomationProperties.SetItemStatus(_cleanButton, on ? "Pressed" : "");
        if (on)
            _clean.Open();
        Refresh();
        if (focus)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (IsVisible) InitialFocus.Focus(); });
    }

    private void Refresh()
    {
        var history = _module?.History;
        var settings = _module?.Settings ?? new ClipboardHistorySettings();
        bool recording = settings.Recording;
        var entries = history?.Entries ?? [];
        bool firstUse = !recording && entries.Count == 0;
        Ui.Show(_saveError, _cleaning ? null : _module?.SettingsError);

        _cleanButton.Visibility = _features.CleanUrl is null ? Visibility.Collapsed : Visibility.Visible;
        _pause.Visibility = _more.Visibility = !firstUse && !_cleaning ? Visibility.Visible : Visibility.Collapsed;
        string action = !recording ? "Turn on" : settings.Paused ? "Resume" : "Pause";
        Ui.SetIcon(_pause, action == "Pause" ? LucideIcons.Pause : LucideIcons.Play);
        AutomationProperties.SetName(_pause, action);
        _pause.ToolTip = action;
        _note.Text = firstUse || _cleaning ? "" : !recording ? "Recording off" : settings.Paused ? "Paused" : "";
        // The last icon button's glyph lines up with the body's right edge.
        foreach (var command in new[] { _cleanButton, _pause, _more })
            command.ClearValue(MarginProperty);
        (_more.Visibility == Visibility.Visible ? _more : _cleanButton).SetResourceReference(MarginProperty, "IslandLastIconButtonMargin");

        _clean.Visibility = _cleaning ? Visibility.Visible : Visibility.Collapsed;
        _firstUse.Visibility = !_cleaning && firstUse ? Visibility.Visible : Visibility.Collapsed;
        _thumbnails.Retain(entries);
        foreach (var gone in _fileThumbnails.Keys.Where(id => !entries.Any(e => e.Id == id)).ToList())
            _fileThumbnails.Remove(gone);
        _history.Visibility = !_cleaning && !firstUse ? Visibility.Visible : Visibility.Collapsed;
        if (_history.Visibility != Visibility.Visible)
        {
            // Only identity and the search survive Back. Erasing history must also release hidden previews.
            _shown.Clear();
            ClearRows();
            return;
        }

        Ui.Show(_caution, history?.Full == true ? Format.Full : Format.Limit(history?.Limit ?? ClipLimit.None));
        string search = _search.Text.Trim();
        _shown = [.. entries.Where(e => e.Matches(search))];
        var pinned = _shown.Where(e => e.Pinned).OrderByDescending(e => e.PinnedAt).ToList();
        var recent = _shown.Where(e => !e.Pinned).ToList();
        _shown = [.. pinned, .. recent];
        if (_selected is { } selected && !_shown.Any(e => Identity(e) == selected))
        {
            _selected = null;
            _status = null;
            _actionVersion++;
        }

        // Pinned first, then Recent, as many whole rows as fit; the selected entry stays in view, and a
        // search shows its best matches that fit, newest first (brief Part D #5). Only the rows that can
        // show are built, up to the selected one: building every copy made each key and click slow.
        RowTarget? focused = _rows.IsKeyboardFocusWithin && Keyboard.FocusedElement is Button { Tag: RowTarget target } ? target : null;
        ClearRows();
        _rows.TotalEntries = _shown.Count;
        if (_shown.Count == 0) _allHistory.Visibility = Visibility.Collapsed;
        var now = DateTimeOffset.Now;
        int selectedAt = _selected is { } kept ? _shown.FindIndex(e => Identity(e) == kept) : -1;
        int from = Math.Max(0, selectedAt - BuiltRows + 1), to = Math.Min(_shown.Count, Math.Max(selectedAt + 1, from + BuiltRows));
        for (int i = from; i < to; i++)
        {
            if (i == 0 && pinned.Count > 0)
                _rows.Children.Add(Label("Pinned"));
            if (i == pinned.Count && search.Length == 0 && pinned.Count > 0)
                _rows.Children.Add(Label("Recent"));
            _rows.Children.Add(Row(_shown[i], now, search));
        }
        if (search.Length == 0 && pinned.Count > 0 && recent.Count == 0 && to == _shown.Count)
        {
            _rows.Children.Add(Label("Recent"));
            var none = Ui.Muted("No recent copies");
            none.Height = 30;
            none.Padding = new Thickness(10, 0, 0, 0);
            _rows.Children.Add(none);
        }
        _empty.Text = entries.Count == 0 ? "No items" : _shown.Count == 0 ? "No matches" : "";
        _empty.Visibility = _empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rows.Visibility = _shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (focused is { } prior && IsVisible)
        {
            _rows.UpdateLayout();
            if (!_buttons.TryGetValue(prior, out var next) || !next.IsVisible || !next.Focus())
                InitialFocus.Focus();
        }
    }

    private void ClearRows()
    {
        _buttons.Clear();
        _rows.Children.Clear();
        _rows.Keep = null;
    }

    private static (long Id, long Hash) Identity(HistoryEntry entry) => (entry.Id, entry.ContentHash);

    private void Track(Button button, HistoryEntry entry, string part)
    {
        var key = new RowTarget(entry.Id, entry.ContentHash, part);
        button.Tag = key;
        _buttons[key] = button;
    }

    /// <summary>"Pinned" or "Recent": 22 tall, 11 / 600, 10 in.</summary>
    private static TextBlock Label(string text)
    {
        var label = Ui.Muted(text, 11);
        label.FontWeight = FontWeights.SemiBold;
        label.Height = 22;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Padding = new Thickness(10, 4, 0, 3);
        WholeRows.SetIsHeading(label, true);
        return label;
    }

    /// <summary>A copy on one 30 row: its glyph or picture, the line and when. Selected, it opens in place.</summary>
    private FrameworkElement Row(HistoryEntry entry, DateTimeOffset now, string search)
    {
        bool selected = Identity(entry) == _selected;
        var head = new DockPanel();
        var when = Ui.Muted(Format.When(entry.Recent, now), 11);
        when.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(when, Dock.Right);
        head.Children.Add(when);
        var art = new Grid { Width = ArtWidth, Margin = new Thickness(0, 0, 8, 0), Children = { Kind(entry) } };
        head.Children.Add(art);
        head.Children.Add(Ui.Text(selected ? Format.Details(entry) : Format.Line(entry, search), 12));

        var button = new Button { Content = head }; // an open row's fill is its card's
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        Track(button, entry, "Row");
        AutomationProperties.SetName(button, Format.AccessibleName(entry, now, search));
        AutomationProperties.SetItemStatus(button, selected ? "Expanded" : "");
        button.Click += (_, _) => Select(entry);
        button.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            Paste(entry);
        };
        if (!selected)
            return button;

        // Open: the row, at most 3 lines of preview, then the actions. It counts at its full height.
        button.Padding = new Thickness(2, 0, 2, 0); // The card supplies the other 8 DIPs of a normal row's 10.
        var open = new StackPanel { Children = { button, Preview(entry, search), Actions(entry) } };
        var card = new Border { Child = open, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 8), Margin = new Thickness(0, 2, 0, 2) };
        card.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        _rows.Keep = card;
        return card;
    }

    private FrameworkElement Kind(HistoryEntry entry)
    {
        int missing = HistoryFiles.Missing(entry);
        if ((entry.Kind == EntryKind.Picture ? Thumbnail(entry) : missing == 0 ? FileThumbnail(entry) : null) is { } thumbnail)
            return new Image { Source = thumbnail, Width = 32, Height = 20, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center };
        var glyph = entry.Kind switch
        {
            EntryKind.Link => LucideIcons.Globe,
            EntryKind.Picture => LucideIcons.Image,
            EntryKind.Files when missing > 0 => LucideIcons.FileX,
            EntryKind.Files => entry.Files?.Count == 1 ? LucideIcons.File : LucideIcons.Files,
            _ => LucideIcons.AlignLeft,
        };
        return Ui.Glyph(glyph, 16, "IslandTextTertiaryBrush");
    }

    internal BitmapSource? Thumbnail(HistoryEntry entry) => _thumbnails.Get(entry);

    /// <summary>
    /// The picture of a row that is one image file, read off the UI thread; null until it's read, or when
    /// it's online-only, gone or not a picture. The row stays a file row.
    /// </summary>
    internal BitmapSource? FileThumbnail(HistoryEntry entry)
    {
        if (FileThumbnails.Picture(entry) is not { } path)
            return null;
        if (_fileThumbnails.TryGetValue(entry.Id, out var cached) && cached.Path == path)
            return cached.Image;
        _fileThumbnails[entry.Id] = (path, null);
        var files = Files;
        long id = entry.Id;
        Reading = Task.Run(() => files.Read(path)).ContinueWith(read => Dispatcher.BeginInvoke(() =>
        {
            // The row may have gone, or its id gone to another copy, while the file was read.
            if (read.Result is not { } image || !_fileThumbnails.TryGetValue(id, out var waiting) || waiting.Path != path)
                return;
            _fileThumbnails[id] = (path, image);
            if (_shown.Any(e => e.Id == id))
                Refresh();
        }), TaskScheduler.Default);
        return null;
    }

    /// <summary>
    /// At most 3 lines (brief Part D #5): the text with the last line trimmed, the picture 3 lines tall,
    /// or 3 files with their sizes, the third saying how many more when there are (notes #9, #12).
    /// </summary>
    private FrameworkElement Preview(HistoryEntry entry, string search)
    {
        const double Line = 18, Lines = 3;
        var margin = new Thickness(ContentInset, 0, 0, 8);
        switch (entry.Kind)
        {
            case EntryKind.Picture:
                var image = new Image { MaxWidth = 208, Height = Lines * Line, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = margin, Source = Thumbnail(entry) };
                if (image.Source is null) _ = LoadPictureAsync(entry, image);
                return image;
            case EntryKind.Files:
                var files = new StackPanel { Margin = margin };
                var paths = entry.Files ?? [];
                IEnumerable<string> display = search.Length == 0 ? paths :
                    paths.OrderByDescending(p => Path.GetFileName(p).Contains(search, StringComparison.OrdinalIgnoreCase));
                foreach (var (path, at) in display.Select((p, i) => (p, i)).Take((int)Lines))
                {
                    var line = new DockPanel { Height = Line };
                    if (at == Lines - 1 && paths.Count > Lines)
                    {
                        line.Children.Add(Ui.Muted($"{paths.Count - at} more files", 12));
                        files.Children.Add(line);
                        break;
                    }
                    bool there = HistoryFiles.Exists(path);
                    var size = Ui.Muted(there ? HistoryFiles.Size(path) : "Unavailable", 11);
                    DockPanel.SetDock(size, Dock.Right);
                    line.Children.Add(size);
                    var glyph = Ui.Glyph(there ? LucideIcons.File : LucideIcons.FileX, 14, "IslandTextTertiaryBrush");
                    glyph.Margin = new Thickness(0, 0, 8, 0);
                    line.Children.Add(glyph);
                    string name = Format.Excerpt(Path.GetFileName(path), search);
                    line.Children.Add(there ? Ui.Text(name, 12) : Ui.Muted(name, 12));
                    files.Children.Add(line);
                }
                return files;
            default:
                // Trimming only marks a line cut at its width, so lines past the third end the third with "…";
                // a wrapped line past the 3 lines is trimmed by the TextBlock itself.
                var text = Ui.Text(PreviewText(entry.Text ?? "", (int)Lines, search), 12, "IslandTextSecondaryBrush");
                text.TextWrapping = TextWrapping.Wrap;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.LineHeight = Line;
                text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                text.MaxHeight = Lines * Line;
                text.Margin = margin;
                return text;
        }
    }

    /// <summary>The first <paramref name="lines"/> lines of <paramref name="text"/>, the last ending in "…" when more follow.</summary>
    internal static string PreviewText(string text, int lines, string search = "")
    {
        if (search.Length > 0 && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) is >= 0 and var hit)
        {
            int start = Format.MatchStart(text, hit);
            if (start > 0) text = "…" + text[start..];
        }
        var all = text.TrimEnd('\r', '\n').Split('\n');
        if (all.Length <= lines)
            return string.Join("\n", all.Select(line => line.TrimEnd('\r')));
        return string.Join("\n", all.Take(lines).Select(line => line.TrimEnd('\r'))) + "…";
    }

    private async Task LoadPictureAsync(HistoryEntry entry, Image image)
    {
        if (_module?.History is not { } history || await history.PictureAsync(entry.Id, entry.ContentHash) is not { } png)
            return;
        try
        {
            image.Source = Pictures.Load(png);
        }
        catch (Exception)
        {
            // The thumbnail stays.
        }
    }

    /// <summary>Paste (accent) and Copy, the row's status, then Pin and Delete (notes #9, #12, #14).</summary>
    private FrameworkElement Actions(HistoryEntry entry)
    {
        int missing = HistoryFiles.Missing(entry);
        var paste = Ui.Button("Paste", "IslandAccentButtonStyle");
        var copy = Ui.Button("Copy");
        copy.Margin = new Thickness(8, 0, 0, 0);
        paste.IsEnabled = copy.IsEnabled = missing == 0;
        Track(paste, entry, "Paste");
        Track(copy, entry, "Copy");
        paste.Click += (_, _) => Paste(entry);
        copy.Click += (_, _) => Copy(entry);

        // Pin and Delete: icon buttons 2 apart; Delete's glyph lines up with the content's right edge.
        var pin = Ui.Icon(entry.Pinned ? LucideIcons.PinOff : LucideIcons.Pin, entry.Pinned ? "Unpin" : "Pin");
        pin.Click += (_, _) => Pin(entry);
        var delete = Ui.Icon(LucideIcons.Trash, "Delete");
        Track(pin, entry, "Pin");
        Track(delete, entry, "Delete");
        delete.Margin = new Thickness(2, 0, -7, 0);
        delete.Click += (_, _) => Delete(entry);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { pin, delete } };
        DockPanel.SetDock(icons, Dock.Right);

        string? status = missing > 0 ? Format.Unavailable(missing) : _status;
        var line = new DockPanel { Margin = new Thickness(ContentInset, 0, 0, 0), Children = { icons } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { paste, copy } };
        if (status is not null)
        {
            var caution = status == "Clipboard busy" || status == "Item unavailable" ? Ui.Caution() : null;
            if (caution is not null)
            {
                Ui.Show(caution, status);
                caution.Margin = new Thickness(12, 0, 0, 0);
                buttons.Children.Add(caution);
            }
            else
            {
                var note = Ui.Muted(status, 12);
                note.Margin = new Thickness(12, 0, 0, 0);
                AutomationProperties.SetLiveSetting(note, AutomationLiveSetting.Polite);
                buttons.Children.Add(note);
            }
        }
        line.Children.Add(buttons);
        return line;
    }

    // ---- Actions ----

    private void Select(HistoryEntry entry)
    {
        _status = null;
        _selected = _selected == Identity(entry) ? null : Identity(entry);
        Refresh();
    }

    private void Move(int by)
    {
        if (_shown.Count == 0)
            return;
        int at = _selected is { } selected ? _shown.FindIndex(e => Identity(e) == selected) : -1;
        int next = at < 0 ? (by > 0 ? 0 : _shown.Count - 1) : Math.Clamp(at + by, 0, _shown.Count - 1);
        _status = null;
        _selected = Identity(_shown[next]);
        Refresh();
    }

    private HistoryEntry? Selected => _selected is { } selected ? _shown.FirstOrDefault(e => Identity(e) == selected) : null;

    /// <summary>
    /// Hands the copy to the app that was in front before the island took the keyboard, then closes the
    /// island. With no target, or one that won't come forward, it becomes the copy and the page says
    /// "Copied" (notes #62).
    /// </summary>
    private async void Paste(HistoryEntry entry)
    {
        if (_module?.History is not { } history || Gone(history, entry))
            return;
        _selected = Identity(entry);
        if (_features.PasteTarget?.Invoke() is not { } target)
        {
            Copy(entry);
            return;
        }
        int version = ++_actionVersion;
        var outcome = await history.PasteAsync(entry, target);
        if (version != _actionVersion || _selected != Identity(entry)) return;
        if (outcome == PasteOutcome.Sent)
            _features.CloseIsland?.Invoke();
        else
            ShowStatus(ClipboardFeedback.ForPaste(outcome));
    }

    private async void Copy(HistoryEntry entry)
    {
        if (_module?.History is not { } history || Gone(history, entry))
            return;
        _selected = Identity(entry);
        int version = ++_actionVersion;
        var status = await history.CopyAsync(entry);
        if (version == _actionVersion && _selected == Identity(entry))
            ShowStatus(ClipboardFeedback.ForCopy(status));
    }

    /// <summary>A copy past its 7 days: the list drops it instead of copying it (notes #11).</summary>
    private bool Gone(ClipboardHistory history, HistoryEntry entry)
    {
        if (!history.Expired(entry))
            return false;
        _selected = null;
        _status = null;
        Refresh();
        return true;
    }

    private void ShowStatus(string status)
    {
        _status = status;
        Refresh();
    }

    private void Pin(HistoryEntry entry) => _ = _module?.History?.PinAsync(entry.Id, !entry.Pinned, entry.ContentHash);

    private void Delete(HistoryEntry entry)
    {
        int at = _shown.FindIndex(e => e.Id == entry.Id);
        var next = _shown.ElementAtOrDefault(at + 1) ?? _shown.ElementAtOrDefault(at - 1);
        _selected = next is null ? null : Identity(next);
        _status = null;
        _ = _module?.History?.DeleteAsync(entry.Id, entry.ContentHash);
    }

    private void OpenMore()
    {
        if (_module?.History is not { } history)
            return;
        string query = _search.Text.Trim();
        var entries = history.Entries;
        var unpinned = entries.Where(e => !e.Pinned && e.Matches(query)).ToList();
        string clips = unpinned.Count == 1 ? "clip" : "clips";
        string question = query.Length == 0 ? $"Clear {unpinned.Count} unpinned {clips}?" : $"Clear {unpinned.Count} matching unpinned {clips}?";
        var actions = new List<(string Text, bool Enabled, Action Click)>();
        if (entries.Count > 0 && _allHistory.Visibility != Visibility.Visible)
            actions.Add(("All history", true, () => _module?.OpenHistory(_search.Text)));
        actions.Add((query.Length == 0 ? "Clear history" : "Clear matches", unpinned.Count > 0, () => Confirm(question, "Clear", () => _ = history.ClearAsync(unpinned))));
        actions.Add(("Erase all", entries.Count > 0, () => Confirm("Erase all items and pins?", "Erase", () =>
        {
            _selected = null;
            _ = history.EraseAllAsync();
        })));
        ShowPopup(Ui.Menu(_more, [.. actions]));
    }

    internal Action<Popup> ShowPopup { get; set; } = popup => popup.IsOpen = true;

    private void Confirm(string question, string verb, Action confirmed) => ShowPopup(Ui.Confirm(_more, question, verb, confirmed));

    // ---- Keyboard (notes #59) ----

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.Enter when Selected is { } entry:
                Paste(entry);
                break;
            case Key.C when ctrl && _search.SelectionLength == 0 && Selected is { } entry:
                Copy(entry);
                break;
            case Key.P when ctrl && Selected is { } entry:
                Pin(entry);
                break;
            case Key.Delete when _search.Text.Length == 0 && Selected is { } entry:
                Delete(entry);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}

/// <summary>A file entry's files on disk: attributes only, so an online-only file counts as there and is never downloaded (notes #12).</summary>
internal static class HistoryFiles
{
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static int Missing(HistoryEntry entry) => entry.Kind == EntryKind.Files ? ClipboardHistory.MissingFiles(entry) : 0;

    public static string Size(string path)
    {
        try
        {
            return File.Exists(path) ? Format.Size(new FileInfo(path).Length) : "";
        }
        catch (Exception)
        {
            return "";
        }
    }
}
