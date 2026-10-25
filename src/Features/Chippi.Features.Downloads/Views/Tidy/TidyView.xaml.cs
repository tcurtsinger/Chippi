// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Themes;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Views.Tidy;

/// <summary>The flyout's Tidy panel. Navigation is raised through the module's supplied request callbacks.</summary>
public partial class TidyView : UserControl
{
    private readonly DownloadsModule _module;
    private readonly Action<DownloadsRequest> _request;
    private readonly Action _rules;
    private readonly DispatcherTimer _ages = new() { Interval = TimeSpan.FromMinutes(1) };
    private DownloadsEngine? _subscribed;
    private string? _selected;
    private bool _refreshing;
    private bool _busy;

    public TidyView(DownloadsModule module, Action<DownloadsRequest> request, Action requestRules)
    {
        _module = module;
        _request = request;
        _rules = requestRules;
        InitializeComponent();
        _ages.Tick += OnRefresh;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _subscribed = _module.Engine;
        if (_subscribed is not null)
        {
            _subscribed.PendingChanged += OnRefresh;
            _subscribed.HistoryChanged += OnRefresh;
        }
        ThemeManager.ThemeChanged += OnRefresh;
        _ages.Start();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _ages.Stop();
        ThemeManager.ThemeChanged -= OnRefresh;
        if (_subscribed is not null)
        {
            _subscribed.PendingChanged -= OnRefresh;
            _subscribed.HistoryChanged -= OnRefresh;
            _subscribed = null;
        }
    }

    private void OnRefresh(object? sender, EventArgs e)
    {
        // Engine events can originate inside a file operation. Never synchronously wait on the UI.
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (IsLoaded && !_busy) Refresh(); }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        if (_busy) return;
        _refreshing = true;
        try
        {
            var folders = _module.Settings.Folders;
            if (_selected is null || !folders.Any(f => Same(f.Path, _selected)))
                _selected = folders.FirstOrDefault()?.Path;
            var choices = folders.Select(f => new FolderChoice(TidyProjection.FolderName(f.Path), f.Mode.ToString(), f.Path)).ToList();
            choices.Add(new FolderChoice("Add folder", "", null));
            Folders.ItemsSource = choices;
            Folders.SelectedItem = choices.FirstOrDefault(f => f.Path is not null && Same(f.Path, _selected));
            var current = folders.FirstOrDefault(f => Same(f.Path, _selected));
            FolderCard.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
            Empty.Visibility = current is null ? Visibility.Visible : Visibility.Collapsed;
            Actions.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
            Recent.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
            var engine = _module.Engine;
            var pending = engine?.Pending ?? [];
            if (current is not null)
            {
                ModeLine.Text = TidyProjection.ModeLine(current, pending.Count(p => Same(p.Folder, current.Path) && p.Status == PendingStatus.Ready));
                ModeLine.ToolTip = current.Path;
                Folders.ToolTip = current.Path;
                Modes.SelectedIndex = current.Mode switch { FolderMode.Silent => 0, FolderMode.Manual => 1, _ => 2 };
                FolderChip.IsOn = current.Mode != FolderMode.Paused;
            }
            var history = engine?.History.Query(new HistoryQuery { Take = 24, From = _module.Settings.HistoryClearedUtc }) ?? [];
            var rows = TidyProjection.Rows(pending, history, DateTimeOffset.UtcNow);
            Rows.ItemsSource = rows;
            NoRecent.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FolderCard.IsEnabled = Actions.IsEnabled = engine is not null;
        }
        catch (ObjectDisposedException)
        {
            ShowError("Organizer stopped.");
            FolderCard.IsEnabled = Actions.IsEnabled = false;
        }
        finally { _refreshing = false; }
    }

    private void OnFolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _busy || Folders.SelectedItem is not FolderChoice choice) return;
        if (choice.Path is null)
        {
            ChooseFolder();
            Refresh();
        }
        else { _selected = choice.Path; Refresh(); }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _busy || _selected is null || Modes.SelectedIndex < 0) return;
        var settings = _module.Settings.Clone();
        var folder = settings.Folders.FirstOrDefault(f => Same(f.Path, _selected));
        if (folder is null) return;
        folder.Mode = Modes.SelectedIndex switch { 0 => FolderMode.Silent, 1 => FolderMode.Manual, _ => FolderMode.Paused };
        Save(settings);
    }

    private void OnOrganize(object sender, RoutedEventArgs e) => Navigate(_selected);
    private void Navigate(string? folder)
    {
        if (_busy || _module.Engine is null || folder is null) return;
        Error.Visibility = Visibility.Collapsed;
        try { _request(new DownloadsRequest(DownloadsRequestKind.Organize, Folder: folder)); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OnHistory(object sender, RoutedEventArgs e)
    {
        if (_busy || _module.Engine is null) return;
        try { _request(new DownloadsRequest(DownloadsRequestKind.Run)); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OnRules(object sender, RoutedEventArgs e)
    {
        if (_busy || _module.Engine is null) return;
        try { _rules(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void OnRowAction(object sender, RoutedEventArgs e)
    {
        if (_busy || _module.Engine is not { } engine || ((FrameworkElement)sender).DataContext is not TidyRow row) return;
        if (row.Action is TidyRowAction.Review or TidyRowAction.Retry)
        {
            // Retry starts a fresh preview, never blindly replays a stale move or changes a rule.
            Navigate(row.Folder);
            return;
        }
        if (row.OperationId is not { } operation) return;
        _busy = true;
        IsEnabled = false;
        Error.Visibility = Visibility.Collapsed;
        try
        {
            var result = await engine.UndoAsync(operation);
            if (!result.Succeeded) ShowError(result.Error ?? "Couldn't undo");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            _busy = false;
            IsEnabled = true;
            if (IsLoaded) Refresh();
        }
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e) => ChooseFolder();
    private void ChooseFolder()
    {
        var picker = new OpenFolderDialog { Title = "Choose folder", Multiselect = false };
        var owner = Window.GetWindow(this);
        bool? chosen = owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
        if (chosen == true) AddFolder(picker.FolderName);
    }

    private void OnAddDownloads(object sender, RoutedEventArgs e)
    {
        if (DownloadsModule.DownloadsFolder() is { } folder)
            AddFolder(folder);
        else
            ShowError("Folder not found");
    }

    private void AddFolder(string path)
    {
        if (!Directory.Exists(path)) { ShowError("Folder not found"); return; }
        var settings = _module.Settings.Clone();
        if (!settings.Folders.Any(f => Same(f.Path, path)))
            settings.Folders.Add(new WatchedFolder { Path = path, Mode = FolderMode.Manual });
        _selected = path;
        Save(settings);
    }

    private void Save(DownloadsSettings settings)
    {
        Error.Visibility = Visibility.Collapsed;
        try { _module.SaveSettings(settings); }
        catch (Exception ex) { ShowError(ex.Message); }
        Refresh();
    }

    private void ShowError(string error)
    {
        Error.Text = error;
        Error.Visibility = Visibility.Visible;
    }

    private static bool Same(string? a, string? b) => string.Equals(a is null ? null : Path.TrimEndingDirectorySeparator(a), b is null ? null : Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    private sealed record FolderChoice(string Name, string Mode, string? Path);
}
