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
using Chippi.Features.Clipboard.History;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// The Clipboard page with Clipboard history (design notes #8 to #16, #23, #59): the header's Clean URL,
/// Pause and More; first use; Search; the Pinned and Recent list, whose selected row opens in place with
/// Paste, Copy, Pin and Delete; the Clean URL view behind the link button.
/// </summary>
internal sealed class ClipboardView : UserControl
{
    private const double ListHeight = 336; // about 8 rows (notes #7)

    private readonly ClipboardFeatures _features;
    private readonly TextBlock _note = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
    private readonly IconButton _cleanButton = Ui.Icon(LucideIcons.Link, "Clean URL");
    private readonly IconButton _pause = Ui.Icon(LucideIcons.Pause, "Pause");
    private readonly IconButton _more = Ui.Icon(LucideIcons.Ellipsis, "More");
    private readonly Border _firstUse = new();
    private readonly Button _turnOn = Ui.Button("Turn on", "PrimaryButtonStyle");
    private readonly SearchBox _search = new() { Placeholder = "Search", Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _caution = Ui.Caution();
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroller = new() { MaxHeight = ListHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly TextBlock _empty = Ui.Muted("", 13);
    private readonly StackPanel _history = new();
    private readonly CleanUrlView _clean;
    private readonly Dictionary<long, (byte[] Png, BitmapSource Image)> _thumbnails = []; // by row, checked against its bytes: ids come back after a delete
    private readonly Dictionary<long, (string Path, BitmapSource? Image)> _fileThumbnails = []; // the same, checked against the path; null while reading or unreadable
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<HistoryEntry> _shown = [];
    private long? _selected;
    private string? _status; // the selected row's "Clipboard busy" or "Copied"
    private bool _cleaning;
    private ClipboardHistoryModule? _module;

    public ClipboardView(ClipboardFeatures features)
    {
        _features = features;
        _clean = new CleanUrlView(features);

        // Header commands (notes #8): Clean URL (link, pressed while open), Pause or Resume, More.
        _cleanButton.Click += (_, _) => SetCleaning(!_cleaning);
        _pause.Click += (_, _) => _module?.SetPaused(!_module.Settings.Paused);
        _more.Click += (_, _) => OpenMore();
        _note.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Children = { _note, _cleanButton, _pause, _more } };
        DockPanel.SetDock(commands, Dock.Right);
        Header = new DockPanel { Height = 32, Margin = new Thickness(0, 0, 0, 8), Children = { commands, new TextBlock { Text = "Clipboard", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };

        // First use (notes #10): "Recording off" and Turn on.
        _turnOn.Click += (_, _) => _module?.SetRecording(true);
        DockPanel.SetDock(_turnOn, Dock.Right);
        _firstUse.SetResourceReference(StyleProperty, "IslandCardStyle");
        _firstUse.Child = new DockPanel { Children = { _turnOn, Ui.Text("Recording off") } };

        _search.TextChanged += (_, _) => Refresh(keepScroll: false);
        _search.PreviewKeyDown += OnSearchKey;
        AutomationProperties.SetName(_search, "Search");
        _caution.Margin = new Thickness(0, 0, 0, 8);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Margin = new Thickness(0, 24, 0, 24);
        _scroller.Content = _rows;
        _history.Children.Add(_search);
        _history.Children.Add(_caution);
        _history.Children.Add(_scroller);
        _history.Children.Add(_empty);

        Content = new StackPanel { Children = { _firstUse, _history, _clean } };
        AutomationProperties.SetName(this, "Clipboard");

        _clock.Tick += (_, _) => Refresh(keepScroll: true);
        Loaded += (_, _) =>
        {
            _features.Changed += OnFeatures;
            Attach();
            _clock.Start();
            _search.Text = "";
            _selected = null;
            SetCleaning(false);
            Refresh(keepScroll: false);
        };
        Unloaded += (_, _) =>
        {
            _features.Changed -= OnFeatures;
            Detach();
            _clock.Stop();
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
        if (_cleaning)
        {
            SetCleaning(false);
            return true;
        }
        if (_search.Text.Length > 0)
        {
            _search.Text = "";
            return true;
        }
        return false;
    }

    // ---- Module ----

    private void OnFeatures() => Dispatcher.BeginInvoke(() =>
    {
        Detach();
        Attach();
        Refresh(keepScroll: true);
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

    private void OnModuleChanged() => Dispatcher.BeginInvoke(() => Refresh(keepScroll: true));

    // ---- Views ----

    private void SetCleaning(bool on)
    {
        if (_features.CleanUrl is null)
            on = false;
        _cleaning = on;
        // Pressed while Clean URL shows (notes #23).
        if (on)
            _cleanButton.SetResourceReference(BackgroundProperty, "ButtonBackgroundBrush");
        else
            _cleanButton.ClearValue(BackgroundProperty);
        AutomationProperties.SetItemStatus(_cleanButton, on ? "Pressed" : "");
        if (on)
        {
            _clean.Open();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _clean.Field.Focus());
        }
        else if (IsKeyboardFocusWithin)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _search.Focus());
        Refresh(keepScroll: true);
    }

    private void Refresh(bool keepScroll)
    {
        var history = _module?.History;
        var settings = _module?.Settings ?? new ClipboardHistorySettings();
        bool recording = settings.Recording;

        _cleanButton.Visibility = _features.CleanUrl is null ? Visibility.Collapsed : Visibility.Visible;
        // With recording off, only Clean URL shows; in Clean URL, Pause and More go (notes #8, #23).
        _pause.Visibility = _more.Visibility = recording && !_cleaning ? Visibility.Visible : Visibility.Collapsed;
        _pause.Icon = settings.Paused ? LucideIcons.Play : LucideIcons.Pause;
        AutomationProperties.SetName(_pause, settings.Paused ? "Resume" : "Pause");
        _pause.ToolTip = settings.Paused ? "Resume" : "Pause";
        _note.Text = recording && settings.Paused ? "Paused" : "";

        _clean.Visibility = _cleaning ? Visibility.Visible : Visibility.Collapsed;
        _firstUse.Visibility = !_cleaning && !recording ? Visibility.Visible : Visibility.Collapsed;
        // Saved copies stay usable with recording off only when there are some.
        var entries = history?.Entries ?? [];
        foreach (var gone in _thumbnails.Keys.Where(id => !entries.Any(e => e.Id == id)).ToList())
            _thumbnails.Remove(gone);
        foreach (var gone in _fileThumbnails.Keys.Where(id => !entries.Any(e => e.Id == id)).ToList())
            _fileThumbnails.Remove(gone);
        _history.Visibility = !_cleaning && (recording || entries.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        if (_history.Visibility != Visibility.Visible)
            return;

        Ui.Show(_caution, history?.Full == true ? Format.Full : Format.Limit(history?.Limit ?? ClipLimit.None));
        string search = _search.Text.Trim();
        _shown = [.. entries.Where(e => e.Matches(search))];
        var pinned = _shown.Where(e => e.Pinned).OrderByDescending(e => e.PinnedAt).ToList();
        var recent = _shown.Where(e => !e.Pinned).ToList();
        _shown = [.. pinned, .. recent];
        if (_selected is { } id && !_shown.Any(e => e.Id == id))
            _selected = null;

        double offset = _scroller.VerticalOffset;
        _rows.Children.Clear();
        var now = DateTimeOffset.Now;
        if (pinned.Count > 0)
        {
            _rows.Children.Add(Label("Pinned"));
            foreach (var entry in pinned)
                _rows.Children.Add(Row(entry, now));
        }
        if (search.Length == 0 && pinned.Count > 0)
            _rows.Children.Add(Label("Recent"));
        foreach (var entry in recent)
            _rows.Children.Add(Row(entry, now));
        if (search.Length == 0 && pinned.Count > 0 && recent.Count == 0)
        {
            var none = Ui.Muted("No recent copies", 13);
            none.Margin = new Thickness(10, 4, 0, 8);
            _rows.Children.Add(none);
        }
        _empty.Text = entries.Count == 0 ? "No items" : _shown.Count == 0 ? "No matches" : "";
        _empty.Visibility = _empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroller.Visibility = _shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (keepScroll)
            _scroller.ScrollToVerticalOffset(offset);
        else
            _scroller.ScrollToTop();
    }

    private static TextBlock Label(string text)
    {
        var label = Ui.Muted(text, 12);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(10, 8, 0, 4);
        return label;
    }

    private FrameworkElement Row(HistoryEntry entry, DateTimeOffset now)
    {
        bool selected = entry.Id == _selected;
        var head = new DockPanel { Height = 24 };
        var when = Ui.Muted(Format.When(entry.Recent, now), 12);
        when.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(when, Dock.Right);
        head.Children.Add(when);
        var kind = Kind(entry);
        kind.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(kind);
        head.Children.Add(selected ? Ui.Muted(Format.Details(entry), 12) : Ui.Text(Format.Line(entry)));

        var button = new Button { Content = head, Tag = selected ? "Selected" : null };
        button.SetResourceReference(StyleProperty, "IslandRowButtonStyle");
        AutomationProperties.SetName(button, $"{Format.Details(entry)}, {Format.When(entry.Recent, now)}{(entry.Pinned ? ", pinned" : "")}");
        button.Click += (_, _) => Select(entry.Id);
        button.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            Paste(entry);
        };
        if (!selected)
            return button;

        var open = new StackPanel { Children = { button, Preview(entry), Actions(entry) } };
        var card = new Border { Child = open, CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 0, 0, 10), Margin = new Thickness(0, 2, 0, 2) };
        card.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        card.Loaded += (_, _) => card.BringIntoView();
        return card;
    }

    private FrameworkElement Kind(HistoryEntry entry)
    {
        if ((entry.Kind == EntryKind.Picture ? Thumbnail(entry) : HistoryFiles.Missing(entry) == 0 ? FileThumbnail(entry) : null) is { } thumbnail)
            return new Image { Source = thumbnail, Width = 32, Height = 20, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center };
        var glyph = entry.Kind switch
        {
            EntryKind.Link => LucideIcons.Globe,
            EntryKind.Picture => LucideIcons.Image,
            EntryKind.Files when HistoryFiles.Missing(entry) == entry.Files?.Count => LucideIcons.FileX,
            EntryKind.Files => entry.Files?.Count == 1 ? LucideIcons.File : LucideIcons.Files,
            _ => LucideIcons.AlignLeft,
        };
        return Ui.Glyph(glyph, 16, "IslandTextTertiaryBrush");
    }

    internal BitmapSource? Thumbnail(HistoryEntry entry)
    {
        if (entry.Thumbnail is not { Length: > 0 } png)
            return null;
        if (_thumbnails.TryGetValue(entry.Id, out var cached) && cached.Png.AsSpan().SequenceEqual(png))
            return cached.Image;
        try
        {
            var image = Pictures.Load(png);
            _thumbnails[entry.Id] = (png, image);
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
                Refresh(keepScroll: true);
        }), TaskScheduler.Default);
        return null;
    }

    /// <summary>Up to 4 lines of text, the picture 208 wide, or the files with their sizes (notes #9, #12).</summary>
    private FrameworkElement Preview(HistoryEntry entry)
    {
        var margin = new Thickness(10, 4, 10, 10);
        switch (entry.Kind)
        {
            case EntryKind.Picture:
                var image = new Image { Width = 208, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = margin, Source = Thumbnail(entry) };
                _ = LoadPictureAsync(entry.Id, image);
                return image;
            case EntryKind.Files:
                var files = new StackPanel { Margin = margin };
                foreach (var path in entry.Files ?? [])
                {
                    bool there = HistoryFiles.Exists(path);
                    var line = new DockPanel { Height = 24 };
                    var size = Ui.Muted(there ? HistoryFiles.Size(path) : "Unavailable", 12);
                    DockPanel.SetDock(size, Dock.Right);
                    line.Children.Add(size);
                    var glyph = Ui.Glyph(there ? LucideIcons.File : LucideIcons.FileX, 14, "IslandTextTertiaryBrush");
                    glyph.Margin = new Thickness(0, 0, 8, 0);
                    line.Children.Add(glyph);
                    line.Children.Add(there ? Ui.Text(Path.GetFileName(path)) : Ui.Muted(Path.GetFileName(path), 13));
                    files.Children.Add(line);
                }
                return files;
            default:
                var text = Ui.Text(entry.Text ?? "");
                text.TextWrapping = TextWrapping.Wrap;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.MaxHeight = 4 * 20;
                text.LineHeight = 20;
                text.Margin = margin;
                return text;
        }
    }

    private async Task LoadPictureAsync(long id, Image image)
    {
        if (_module?.History is not { } history || await history.PictureAsync(id) is not { } png)
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
        var paste = Ui.Button("Paste", "PrimaryButtonStyle");
        var copy = Ui.Button("Copy");
        copy.Margin = new Thickness(8, 0, 0, 0);
        paste.IsEnabled = copy.IsEnabled = missing == 0;
        paste.Click += (_, _) => Paste(entry);
        copy.Click += (_, _) => Copy(entry);

        var pin = Ui.Icon(entry.Pinned ? LucideIcons.PinOff : LucideIcons.Pin, entry.Pinned ? "Unpin" : "Pin");
        pin.Click += (_, _) => Pin(entry);
        var delete = Ui.Icon(LucideIcons.Trash, "Delete");
        delete.Margin = new Thickness(4, 0, 0, 0);
        delete.Click += (_, _) => Delete(entry);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Children = { pin, delete } };
        DockPanel.SetDock(icons, Dock.Right);

        string? status = missing > 0 ? Format.Unavailable(missing) : _status;
        var line = new DockPanel { Margin = new Thickness(10, 0, 6, 0), Children = { icons } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { paste, copy } };
        if (status is not null)
        {
            var caution = status == "Clipboard busy" ? Ui.Caution() : null;
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

    private void Select(long id)
    {
        _status = null;
        _selected = _selected == id ? null : id;
        Refresh(keepScroll: true);
    }

    private void Move(int by)
    {
        if (_shown.Count == 0)
            return;
        int at = _selected is { } id ? _shown.FindIndex(e => e.Id == id) : -1;
        int next = at < 0 ? (by > 0 ? 0 : _shown.Count - 1) : Math.Clamp(at + by, 0, _shown.Count - 1);
        _status = null;
        _selected = _shown[next].Id;
        Refresh(keepScroll: true);
    }

    private HistoryEntry? Selected => _selected is { } id ? _shown.FirstOrDefault(e => e.Id == id) : null;

    /// <summary>
    /// Hands the copy to the app that was in front before the island took the keyboard, then closes the
    /// island. With no target, or one that won't come forward, it becomes the copy and the page says
    /// "Copied" (notes #62).
    /// </summary>
    private async void Paste(HistoryEntry entry)
    {
        if (_module?.History is not { } history || HistoryFiles.Missing(entry) > 0 || Gone(history, entry))
            return;
        _selected = entry.Id;
        if (_features.PasteTarget?.Invoke() is not { } target)
        {
            Copy(entry);
            return;
        }
        var outcome = await history.PasteAsync(entry, target);
        switch (outcome)
        {
            case PasteOutcome.Sent:
                _features.CloseIsland?.Invoke();
                break;
            case PasteOutcome.Busy:
                ShowStatus("Clipboard busy");
                break;
            default:
                ShowStatus("Copied");
                break;
        }
    }

    private async void Copy(HistoryEntry entry)
    {
        if (_module?.History is not { } history || HistoryFiles.Missing(entry) > 0 || Gone(history, entry))
            return;
        _selected = entry.Id;
        ShowStatus(await history.CopyAsync(entry) == ClipStatus.Ok ? "Copied" : "Clipboard busy");
    }

    /// <summary>A copy past its 7 days: the list drops it instead of copying it (notes #11).</summary>
    private bool Gone(ClipboardHistory history, HistoryEntry entry)
    {
        if (!history.Expired(entry))
            return false;
        _selected = null;
        _status = null;
        Refresh(keepScroll: true);
        return true;
    }

    private void ShowStatus(string status)
    {
        _status = status;
        Refresh(keepScroll: true);
    }

    private void Pin(HistoryEntry entry) => _ = _module?.History?.PinAsync(entry.Id, !entry.Pinned);

    private void Delete(HistoryEntry entry)
    {
        int at = _shown.FindIndex(e => e.Id == entry.Id);
        var next = _shown.ElementAtOrDefault(at + 1) ?? _shown.ElementAtOrDefault(at - 1);
        _selected = next?.Id;
        _status = null;
        _ = _module?.History?.DeleteAsync(entry.Id);
    }

    private void OpenMore()
    {
        if (_module?.History is not { } history)
            return;
        var entries = history.Entries;
        Popup? menu = null;
        menu = Ui.Menu(_more,
            ("Clear history", entries.Any(e => !e.Pinned), () => Confirm("Clear unpinned items?", "Clear", () => _ = history.ClearAsync([.. entries.Select(e => e.Id)]))),
            ("Erase all", entries.Count > 0, () => Confirm("Erase all items and pins?", "Erase", () =>
            {
                _selected = null;
                _ = history.EraseAllAsync();
            })));
        menu.IsOpen = true;
    }

    private void Confirm(string question, string verb, Action confirmed) => Ui.Confirm(_more, question, verb, confirmed).IsOpen = true;

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
