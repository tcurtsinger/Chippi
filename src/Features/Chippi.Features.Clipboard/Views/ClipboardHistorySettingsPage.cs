// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Features.Clipboard.History;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// Settings › Clipboard history (revised 2026-10-28, brief F3 #6): Recording, apps to skip, then the whole
/// history: Pause / Resume in its header, search with the item count, and every copy on a 48 row, pinned
/// first, each with a ⋯ of Paste, Copy, Pin / Unpin and Delete. The list is as long as the history; the page scrolls.
/// </summary>
public sealed class ClipboardHistorySettingsPage : UserControl
{
    private readonly ClipboardHistoryModule _module;
    private readonly Card _apps = new();
    private readonly Card _list = new();
    private readonly Action _syncRecording;
    private readonly TextBlock _settingsError = SettingsUi.Text("", "CaptionTextStyle");
    private readonly Button _add;
    private readonly Button _pause = SettingsUi.Button("Pause");
    private readonly SearchBox _search = new() { Placeholder = "Search history", Height = 32 };
    private readonly TextBlock _count = SettingsUi.Text("", "CaptionTextStyle");
    private readonly StackPanel _historySection = new();
    private readonly HistoryThumbnails _thumbnails = new();
    private IReadOnlyList<HistoryEntry>? _snapshot;
    private IReadOnlyList<HistoryEntry> _ordered = [];
    private readonly Dictionary<long, HistoryRow> _rows = [];
    private Border? _empty;
    private ClipboardHistory? _history;
    private (long Id, DateTimeOffset Recent)? _status;
    private string _statusText = "";
    private int _action;

    public ClipboardHistorySettingsPage(ClipboardHistoryModule module, string? initialQuery = null)
    {
        _module = module;
        _settingsError.Margin = new Thickness(0, 6, 0, 0);
        _settingsError.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_settingsError, AutomationLiveSetting.Polite);
        var recording = SettingsUi.Switch("Recording", () => _module.Settings.Recording, _module.SetRecording, out _syncRecording);
        _add = SettingsLayout.AddButton("app", () => OpenAdd());
        _pause.Click += (_, _) => _module.SetPaused(!_module.Settings.Paused);
        _search.Text = initialQuery ?? "";
        _search.TextChanged += (_, _) => RefreshHistory();
        AutomationProperties.SetName(_search, "Search history");
        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _count.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_count, Dock.Right);
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Children = { _count, _search } };
        var heading = SettingsLayout.Section("History", _pause);
        _historySection.Children.Add(heading);
        _historySection.Children.Add(bar);
        _historySection.Children.Add(_list);
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Card(SettingsLayout.Row("Recording", recording)),
                _settingsError,
                SettingsLayout.Section("Apps to skip", _add),
                _apps,
                _historySection,
            },
        };
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Watch(_module.History);
            Refresh();
            if (initialQuery is not null)
            {
                initialQuery = null; // Only an explicit All history visit moves focus to the list.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (!IsLoaded || !_search.IsVisible) return;
                    _search.Focus();
                    _search.CaretIndex = _search.Text.Length;
                    heading.BringIntoView();
                });
            }
        };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            Watch(null);
            _action++;
            _thumbnails.Clear();
            ForgetRows();
        };
        Refresh();
    }

    private void Watch(ClipboardHistory? history)
    {
        if (_history is not null)
            _history.Changed -= Refresh;
        _history = history;
        if (_history is not null)
            _history.Changed += Refresh;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (IsLoaded) Refresh();
            });
            return;
        }
        if (IsLoaded && !ReferenceEquals(_history, _module.History))
            Watch(_module.History);
        _syncRecording();
        _settingsError.Text = _module.SettingsError ?? "";
        _settingsError.Visibility = _settingsError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _apps.Children.Clear();
        foreach (var path in _module.Settings.AppsToSkip)
            _apps.Children.Add(SettingsUi.ListRow(Monogram(AppName(path)), AppName(path), path, () => Save(_module.Settings.AppsToSkip.Where(p => p != path))));
        if (_apps.Children.Count == 0)
            _apps.Children.Add(SettingsLayout.Empty("No apps"));
        RefreshHistory();
    }

    /// <summary>Every copy, pinned first: the island's list without its fit, so nothing goes past the page.</summary>
    private void RefreshHistory()
    {
        var settings = _module.Settings;
        var entries = _module.History?.Entries ?? [];
        if (!ReferenceEquals(_snapshot, entries))
        {
            ForgetRows();
            _thumbnails.Retain(entries);
            _snapshot = entries;
            _ordered = [.. entries.Where(e => e.Pinned).OrderByDescending(e => e.PinnedAt), .. entries.Where(e => !e.Pinned)];
        }
        // As on the island: the saved copies stay usable with recording off only when there are some.
        _historySection.Visibility = settings.Recording || entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pause.Content = settings.Paused ? "Resume" : "Pause";
        _pause.Visibility = settings.Recording ? Visibility.Visible : Visibility.Collapsed;
        string search = _search.Text.Trim();
        int shown = 0, position = 0;
        var now = DateTimeOffset.Now;
        foreach (var entry in _ordered)
        {
            bool match = entry.Matches(search);
            if (!_rows.TryGetValue(entry.Id, out var row))
            {
                if (!match) continue; // A deep link builds only the clips its query shows.
                _rows[entry.Id] = row = Row(entry);
                _list.Children.Insert(position, row.Container);
            }
            position++;
            var visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (row.Container.Visibility != visibility)
                row.Container.Visibility = visibility;
            if (!match) continue;
            shown++;
            UpdateRow(entry, row, now, HistoryFiles.Missing(entry));
        }
        _count.Text = search.Length > 0 ? $"{shown:N0} of {entries.Count:N0}"
            : entries.Count == 1 ? "1 item" : $"{entries.Count.ToString(CultureInfo.CurrentCulture)} items";
        if (shown == 0 && _empty is null)
        {
            _empty = SettingsLayout.Empty(entries.Count == 0 ? "No items" : "No matches");
            _list.Children.Add(_empty);
        }
        else if (shown > 0 && _empty is not null)
        {
            _list.Children.Remove(_empty);
            _empty = null;
        }
    }

    private void ForgetRows()
    {
        _snapshot = null;
        _ordered = [];
        _rows.Clear();
        _list.Children.Clear();
        _empty = null;
    }

    private sealed record HistoryRow(Border Container, TextBlock When, IconButton More, LucideIcon? FileIcon)
    {
        public string? Age { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>A copy on one 48 row: its glyph, the line, the pin when pinned, when (or how its Copy went), and ⋯.</summary>
    private HistoryRow Row(HistoryEntry entry)
    {
        var grid = new Grid();
        foreach (var width in new[] { new GridLength(44), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        FrameworkElement kind = entry.Kind == EntryKind.Picture && _thumbnails.Get(entry) is { } thumbnail
            ? new Image { Source = thumbnail, Width = 32, Height = 20, Stretch = Stretch.UniformToFill }
            : Glyph(entry.Kind switch
            {
                EntryKind.Link => LucideIcons.Globe,
                EntryKind.Picture => LucideIcons.Image,
                EntryKind.Files => entry.Files?.Count == 1 ? LucideIcons.File : LucideIcons.Files,
                _ => LucideIcons.AlignLeft,
            });
        kind.HorizontalAlignment = HorizontalAlignment.Center;
        kind.Margin = new Thickness(0, 0, 12, 0);
        Add(grid, kind, 0);
        var line = SettingsUi.Text(Format.Line(entry));
        line.Margin = new Thickness(0, 0, 12, 0);
        Add(grid, line, 1);
        if (entry.Pinned)
        {
            var pin = Glyph(LucideIcons.Pin);
            pin.Margin = new Thickness(0, 0, 12, 0);
            Add(grid, pin, 2);
        }
        var when = SettingsUi.Text("", "CaptionTextStyle");
        when.Margin = new Thickness(0, 0, 8, 0);
        Add(grid, when, 3);
        var more = new IconButton { Icon = LucideIcons.Ellipsis, ToolTip = "More" };
        more.Click += (_, _) => Menu(more, entry).IsOpen = true;
        Add(grid, more, 4);
        var row = new Border { MinHeight = SettingsLayout.RowHeight, Padding = new Thickness(16, 8, 8, 8), Child = grid };
        return new HistoryRow(row, when, more, entry.Kind == EntryKind.Files ? (LucideIcon)kind : null);
    }

    private void UpdateRow(HistoryEntry entry, HistoryRow row, DateTimeOffset now, int missing)
    {
        string age = Format.When(entry.Recent, now);
        string status = missing > 0 ? Format.Unavailable(missing) : _status == (entry.Id, entry.Recent) ? _statusText : "";
        if (row.Age == age && row.Status == status) return;
        row.Age = age;
        row.Status = status;
        row.When.Text = status.Length > 0 ? status : age;
        row.When.SetResourceReference(TextBlock.ForegroundProperty, status.Length > 0 && status != "Copied" ? "WarningTextBrush" : "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(row.When, status.Length > 0 ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
        if (row.FileIcon is { } file)
            file.Data = missing > 0 ? LucideIcons.FileX : entry.Files?.Count == 1 ? LucideIcons.File : LucideIcons.Files;
        string name = Format.AccessibleName(entry, now);
        AutomationProperties.SetName(row.More, $"More for {name}");
        AutomationProperties.SetName(row.Container, status.Length > 0 ? $"{name}, {status}" : name);
    }

    /// <summary>The row's ⋯: Paste, Copy, Pin or Unpin, Delete. Paste and Copy wait while its files are gone.</summary>
    internal Popup Menu(FrameworkElement anchor, HistoryEntry entry)
    {
        int missing = HistoryFiles.Missing(entry);
        if (_rows.TryGetValue(entry.Id, out var row))
            UpdateRow(entry, row, DateTimeOffset.Now, missing);
        bool usable = missing == 0;
        return SettingsUi.Menu(anchor,
            ("Paste", usable, () => Paste(entry)),
            ("Copy", usable, () => Copy(entry)),
            (entry.Pinned ? "Unpin" : "Pin", true, () => _ = _module.History?.PinAsync(entry.Id, !entry.Pinned)),
            ("Delete", true, () => _ = _module.History?.DeleteAsync(entry.Id)));
    }

    /// <summary>
    /// Hands the copy to the app Settings was last switched from; with none, it becomes the copy and
    /// the row says "Copied", as the island's Paste does (notes #62).
    /// </summary>
    private async void Paste(HistoryEntry entry)
    {
        if (_module.History is not { } history || Gone(history, entry))
            return;
        if (_module.Features.SettingsPasteTarget?.Invoke() is not { } target)
        {
            Copy(entry);
            return;
        }
        int action = ++_action;
        Show(action, entry, ClipboardFeedback.ForPaste(await history.PasteAsync(entry, target)));
    }

    private async void Copy(HistoryEntry entry)
    {
        if (_module.History is not { } history || Gone(history, entry))
            return;
        int action = ++_action;
        Show(action, entry, ClipboardFeedback.ForCopy(await history.CopyAsync(entry)));
    }

    /// <summary>A copy past its 7 days: the list drops it instead of copying it (notes #11).</summary>
    private bool Gone(ClipboardHistory history, HistoryEntry entry)
    {
        if (!history.Expired(entry))
            return false;
        Refresh();
        return true;
    }

    private void Show(int action, HistoryEntry entry, string text)
    {
        if (action != _action) return;
        _status = (entry.Id, entry.Recent);
        _statusText = text;
        RefreshHistory();
    }

    private static LucideIcon Glyph(System.Windows.Media.Geometry data)
    {
        var glyph = new LucideIcon { Data = data, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        return glyph;
    }

    private static void Add(Grid grid, UIElement child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private void Save(IEnumerable<string> apps) => _module.Save(_module.Settings with { AppsToSkip = [.. apps] });

    /// <summary>Add app: the running apps with a window, then Browse… for an .exe (notes #52).</summary>
    private void OpenAdd() => CreateAddMenu(RunningApps()).IsOpen = true;

    internal Popup CreateAddMenu(IEnumerable<(string Name, string Path)> apps)
    {
        var items = apps.Where(a => !_module.Settings.AppsToSkip.Contains(a.Path, StringComparer.OrdinalIgnoreCase))
            .Select(app => (app.Name, true, (Action)(() => Save([.. _module.Settings.AppsToSkip, app.Path])))).ToList();
        items.Add(("Browse…", true, () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Save([.. _module.Settings.AppsToSkip, dialog.FileName]);
        }));
        return SettingsUi.Menu(_add, [.. items]);
    }

    /// <summary>Apps with a window, by name. One that can't be read (another user's, elevated) is left out.</summary>
    private static List<(string Name, string Path)> RunningApps()
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0 || process.MainModule?.FileName is not { } path)
                        continue;
                    apps.TryAdd(path, AppName(path));
                }
                catch (Exception)
                {
                    // Access denied or gone.
                }
            }
        }
        return [.. apps.Select(a => (a.Value, a.Key)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The app's own name for itself, else its file name.</summary>
    internal static string AppName(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception)
        {
            // Unreadable version info.
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static Border Monogram(string name)
    {
        var letter = SettingsUi.Text(name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", "CaptionTextStyle");
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 12, 0), Child = letter, VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return box;
    }
}
