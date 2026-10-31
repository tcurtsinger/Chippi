// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.Rules;
using Chippi.UI.Themes;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Views.Tidy;

/// <summary>File organizer's island page. Navigation is raised through the module's supplied request callback.</summary>
public partial class TidyView : UserControl
{
    private readonly DownloadsModule _module;
    private readonly Action<DownloadsRequest> _request;
    private readonly DispatcherTimer _ages = new() { Interval = TimeSpan.FromMinutes(1) };
    private DownloadsEngine? _subscribed;
    private string? _selected;
    private bool _refreshing;
    private bool _busy;

    /// <summary>What Organize now just did here, shown as the first row until the page closes or it's undone.</summary>
    private (string Folder, long RunId, int Moved, int Failed, int NeedsLook)? _last;

    public TidyView(DownloadsModule module, Action<DownloadsRequest> request)
    {
        _module = module;
        _request = request;
        InitializeComponent();
        _ages.Tick += OnRefresh;
        Recent.SizeChanged += (_, _) => { if (!_refreshing) Refresh(); };
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
        _last = null; // Undo of the last run lives in History once the page closes
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
            var choices = folders.Select(f => new FolderChoice(TidyProjection.FolderName(f.Path), f.Mode.DisplayName(), f.Path)).ToList();
            choices.Add(new FolderChoice("Add folder", "", null));
            Folders.ItemsSource = choices;
            Folders.SelectedItem = choices.FirstOrDefault(f => f.Path is not null && Same(f.Path, _selected));
            var current = folders.FirstOrDefault(f => Same(f.Path, _selected));
            bool missing = current is not null && !Directory.Exists(current.Path);
            FolderCard.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
            Empty.Visibility = current is null ? Visibility.Visible : Visibility.Collapsed;
            Actions.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
            Recent.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
            Modes.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
            NotFound.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
            Missing.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
            Organize.IsEnabled = !missing;
            var engine = _module.Engine;
            if (current is null)
            {
                Rows.ItemsSource = null;
                return;
            }
            Folders.ToolTip = current.Path;
            Modes.SelectedIndex = current.Mode switch { FolderMode.Silent => 0, FolderMode.Manual => 1, _ => 2 };
            FolderChip.SetResourceReference(Chippi.UI.Controls.LucideIcon.BrushProperty,
                missing ? "WarningBrush" : current.Mode == FolderMode.Paused ? "IslandTextTertiaryBrush" : "IslandAccentBrush");
            if (missing)
            {
                // Nothing here can be organized or undone: the folder is the news.
                MissingText.Text = $"{TidyProjection.FolderName(current.Path)} not found";
                Rows.ItemsSource = null;
                NoRecent.Visibility = Visibility.Collapsed;
                return;
            }
            var pending = engine?.Pending ?? [];
            var history = engine?.History.Query(new HistoryQuery { Folder = current.Path, Take = 24, From = _module.Settings.HistoryClearedUtc }) ?? [];
            var rows = TidyProjection.Rows(current.Path, pending, history, DateTimeOffset.UtcNow).ToList();
            if (_last is { } last && Same(last.Folder, current.Path))
                rows.Insert(0, LastRun(last));
            Rows.ItemsSource = rows.Take(Math.Max(0, (int)(Recent.ActualHeight / 30))).ToList();
            NoRecent.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FolderCard.IsEnabled = Actions.IsEnabled = engine is not null;
        }
        catch (ObjectDisposedException)
        {
            ShowError("File organizer is off");
            FolderCard.IsEnabled = Actions.IsEnabled = false;
        }
        finally { _refreshing = false; }
    }

    /// <summary>"116 moved" with "2 need a look" (or "3 failed") in Caution, and Undo for the whole run.</summary>
    private static TidyRow LastRun((string Folder, long RunId, int Moved, int Failed, int NeedsLook) last)
    {
        string detail = string.Join(" · ", new[]
        {
            last.Failed > 0 ? $"{last.Failed} failed" : null,
            last.NeedsLook > 0 ? $"{last.NeedsLook} {(last.NeedsLook == 1 ? "needs" : "need")} a look" : null,
        }.Where(s => s is not null));
        return new TidyRow($"{last.Moved} moved", detail, last.Folder, last.Folder, false, false, false,
            last.Moved > 0 ? TidyRowAction.UndoRun : TidyRowAction.None, last.RunId, needsLook: detail.Length > 0);
    }

    private void OnFolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _busy || Folders.SelectedItem is not FolderChoice choice) return;
        if (choice.Path is null)
        {
            ChooseFolder();
            Refresh();
        }
        else { _selected = choice.Path; Error.Visibility = Visibility.Collapsed; Refresh(); }
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

    /// <summary>
    /// Organize now, here: the files a rule moves or renames with nothing to look at go as one run, with Undo
    /// on its row. Held files, misleading names and any warning stay for Preview; with only those, it opens.
    /// </summary>
    private async void OnOrganize(object sender, RoutedEventArgs e)
    {
        if (_busy || _module.Engine is not { } engine || _selected is not { } folder) return;
        _busy = true;
        Organize.IsEnabled = false;
        Error.Visibility = Visibility.Collapsed;
        try
        {
            var plan = await engine.CreatePreviewAsync(folder);
            var clean = plan.Items.Where(Clean).Select(i => i.Id).ToList();
            int needsLook = plan.Items.Count(i => i.Kind != OperationKind.NoMatch && !Clean(i));
            if (clean.Count == 0)
            {
                engine.Discard(plan.Id);
                _last = null;
                if (needsLook > 0)
                    Navigate(folder);
                else
                    ShowError("Nothing to organize", "IslandTextTertiaryBrush");
                return;
            }
            var result = await engine.ApproveAsync(plan.Id, clean);
            _last = (folder, result.RunId, result.Succeeded, result.Failed, needsLook);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            ShowError("Organize failed");
        }
        finally
        {
            _busy = false;
            Organize.IsEnabled = true;
            if (IsLoaded) Refresh();
        }
    }

    /// <summary>Moves or renames by a rule with no warning (a taken name, another drive is fine; a misleading name isn't).</summary>
    private static bool Clean(PreviewItem item) => item.Selected && item.Destination is not null && item.Warnings.Count == 0;

    /// <summary>Opens Preview in Settings (with <paramref name="file"/> shown first). Callers check <see cref="_busy"/>: Organize now opens it mid-run.</summary>
    private void Navigate(string? folder, string? file = null)
    {
        if (_module.Engine is null || folder is null) return;
        Error.Visibility = Visibility.Collapsed;
        try { _request(new DownloadsRequest(DownloadsRequestKind.Organize, Folder: folder, File: file)); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { ShowError("Preview failed"); }
    }

    private void OnHistory(object sender, RoutedEventArgs e)
    {
        if (_busy || _module.Engine is null) return;
        try { _request(new DownloadsRequest(DownloadsRequestKind.Run, Folder: _selected)); } // that folder's runs
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { ShowError("History failed"); }
    }

    private async void OnRowAction(object sender, RoutedEventArgs e)
    {
        if (_busy || _module.Engine is not { } engine || ((FrameworkElement)sender).DataContext is not TidyRow row) return;
        if (row.Action is TidyRowAction.Review or TidyRowAction.Retry)
        {
            // Retry starts a fresh preview, never blindly replays a stale move or changes a rule.
            Navigate(row.Folder, row.IsHeld ? row.Name : null);
            return;
        }
        if (row.OperationId is not { } operation) return;
        _busy = true;
        IsEnabled = false;
        Error.Visibility = Visibility.Collapsed;
        try
        {
            if (row.Action == TidyRowAction.UndoRun)
            {
                var results = await engine.UndoRunAsync(operation);
                int failed = results.Count(r => !r.Succeeded);
                _last = null;
                if (failed > 0) ShowError(failed == 1 ? "Undo failed for 1 file" : $"Undo failed for {failed} files");
            }
            else
            {
                var result = await engine.UndoAsync(operation);
                if (!result.Succeeded) ShowError("Undo failed");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            ShowError("Undo failed");
        }
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
        if (PickFolder("Choose folder") is { } path)
            AddFolder(path);
    }

    private string? PickFolder(string title)
    {
        var picker = new OpenFolderDialog { Title = title, Multiselect = false };
        var owner = Window.GetWindow(this);
        bool? chosen = owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
        return chosen == true ? picker.FolderName : null;
    }

    /// <summary>The picked folder is gone: watch another in its place, with its mode (a new path starts from now).</summary>
    private void OnRelocate(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } gone || PickFolder("Choose folder") is not { } path) return;
        var settings = _module.Settings.Clone();
        if (settings.Folders.Any(f => Same(f.Path, path)))
            settings.Folders.RemoveAll(f => Same(f.Path, gone));
        else if (settings.Folders.FirstOrDefault(f => Same(f.Path, gone)) is { } folder)
            folder.Path = path;
        _selected = path;
        Save(settings);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } gone) return;
        var settings = _module.Settings.Clone();
        settings.Folders.RemoveAll(f => Same(f.Path, gone));
        _selected = null;
        Save(settings);
    }

    private void OnAddDownloads(object sender, RoutedEventArgs e)
    {
        if (DownloadsModule.DownloadsFolder() is { } folder && Directory.Exists(folder))
            AddFolder(folder);
        else
            ShowError("Add Downloads failed · not found");
    }

    private void AddFolder(string path)
    {
        if (!Directory.Exists(path)) { ShowError("Add folder failed · not found"); return; }
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
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError("Save failed");
        }
        Refresh();
    }

    /// <summary>The line over the buttons: an error in Caution, or a plain note ("Nothing to organize").</summary>
    private void ShowError(string error, string brush = "WarningTextBrush")
    {
        Error.Text = error;
        Error.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Error.Visibility = Visibility.Visible;
    }

    private static bool Same(string? a, string? b) => TidyProjection.SameFolder(a, b);
    private sealed record FolderChoice(string Name, string Mode, string? Path);
}
