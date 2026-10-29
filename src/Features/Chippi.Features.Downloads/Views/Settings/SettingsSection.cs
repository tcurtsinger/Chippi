// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Import;
using Chippi.Features.Downloads.Model;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// Settings › the organizer's first section (DESIGN.md §5 "File organizer"): watched folders as
/// expander rows (mode, wait, ignore list, remove), then Options: Keep history, Explorer and Import
/// archive. Notifications live on the app-wide page.
/// </summary>
internal sealed class SettingsSection : UserControl
{
    private static readonly (string Label, int Seconds)[] Graces = [("No wait", 0), ("30 seconds", 30), ("2 minutes", 120), ("10 minutes", 600), ("1 hour", 3600)];
    private static readonly (string Label, int Days)[] Keeps = [("30 days", 30), ("90 days", 90), ("1 year", 365)];

    private readonly DownloadsModule _module;
    private readonly Action<Task<PreviewPlan>> _import;
    private readonly ContentControl _folders = new() { Focusable = false };
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _error = Ui.Warning("");
    private readonly TextBlock _importError = Ui.Warning("");

    public SettingsSection(DownloadsModule module, Action<Task<PreviewPlan>> import)
    {
        _module = module;
        _import = import;

        var add = Ui.Button("Add folder", icon: LucideIcons.Plus);
        add.Click += (_, _) => AddFolder();

        _error.Visibility = Visibility.Collapsed;
        _importError.Visibility = Visibility.Collapsed;
        Content = Ui.Stack(
            Ui.Section("Watched folders", add, first: true),
            _folders,
            _error.Margin(0, 6, 0, 0),
            Ui.Section("Options"),
            Ui.Card(KeepHistoryRow(), ExplorerRow(), ImportRow()));
        Render();
    }

    private void Render()
    {
        var folders = _module.Settings.Folders;
        _folders.Content = folders.Count == 0
            ? Ui.Card(Ui.Pad(Ui.Text("No folders yet", "BodyTextStyle", "TextTertiaryBrush")))
            : Ui.Card([.. folders.Select(FolderRow)]);
    }

    private ExpanderRow FolderRow(WatchedFolder folder)
    {
        string name = Ui.FolderName(folder.Path);
        bool paused = folder.Mode == FolderMode.Paused;
        bool exists = Directory.Exists(folder.Path);

        var mode = Ui.Segment($"{name} mode", "Auto", "Manual", "Paused");
        mode.SelectedIndex = folder.Mode switch { FolderMode.Silent => 0, FolderMode.Manual => 1, _ => 2 };
        mode.SelectionChanged += (_, _) => Edit(folder, f => f.Mode = mode.SelectedIndex switch { 0 => FolderMode.Silent, 1 => FolderMode.Manual, _ => FolderMode.Paused });

        int grace = Array.FindIndex(Graces, g => g.Seconds == folder.GraceSeconds);
        var graceLabels = Graces.Select(g => g.Label).ToList();
        if (grace < 0)
        {
            graceLabels.Add($"{folder.GraceSeconds} seconds"); // a hand-edited value stays visible
            grace = graceLabels.Count - 1;
        }
        var graceBox = Ui.Choice(graceLabels, grace, "Wait before moving", 160);
        graceBox.IsEnabled = folder.Mode == FolderMode.Silent;
        graceBox.SelectionChanged += (_, _) =>
        {
            if (graceBox.SelectedIndex is >= 0 and var i && i < Graces.Length)
                Edit(folder, f => f.GraceSeconds = Graces[i].Seconds);
        };

        var ignore = Ui.Button("Edit", name: $"Edit {name} ignore list");
        ignore.IsEnabled = exists;
        ignore.Click += (_, _) => OpenIgnoreFile(folder.Path);
        var remove = Ui.Button("Remove", name: $"Stop watching {name}");
        remove.Click += (_, _) => Save(s => s.Folders.RemoveAll(f => Same(f.Path, folder.Path)));

        var options = new Card();
        options.SetResourceReference(StyleProperty, "CardRowsStyle");
        options.Children.Add(Ui.Setting("Mode", "", mode));
        options.Children.Add(Ui.Setting("Wait before moving", "", graceBox));
        options.Children.Add(Ui.Setting("Ignore list", "", ignore));
        options.Children.Add(Ui.Setting("Stop watching this folder", "", remove));

        string where = folder.Path + (folder.Path.StartsWith(@"\\", StringComparison.Ordinal) ? " · network" : "") + (exists ? "" : " · not found");
        var row = new ExpanderRow
        {
            Header = name,
            Description = where,
            Icon = LucideIcons.Folder,
            Value = folder.Mode.DisplayName(),
            Content = options,
            IsExpanded = _expanded.Contains(folder.Path),
            ToolTip = folder.Path,
        };
        row.SetResourceReference(ForegroundProperty, paused ? "TextMutedBrush" : "AccentBrush");
        row.Expanded += (_, _) => _expanded.Add(folder.Path);
        row.Collapsed += (_, _) => _expanded.Remove(folder.Path);
        return row;
    }

    private SettingRow KeepHistoryRow()
    {
        int index = Array.FindIndex(Keeps, k => k.Days == _module.Settings.HistoryDays);
        var labels = Keeps.Select(k => k.Label).ToList();
        if (index < 0)
        {
            labels.Add($"{_module.Settings.HistoryDays} days");
            index = labels.Count - 1;
        }
        var keep = Ui.Choice(labels, index, "Keep history", 160);
        keep.SelectionChanged += (_, _) =>
        {
            if (keep.SelectedIndex is >= 0 and var i && i < Keeps.Length)
                Save(s => s.HistoryDays = Keeps[i].Days);
        };
        return Ui.Setting("Keep history", "", keep);
    }

    private SettingRow ExplorerRow()
    {
        var toggle = Ui.Switch("Organize in Explorer", _module.Settings.OrganizeInExplorer);
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        void Changed()
        {
            bool on = toggle.IsChecked == true;
            if (on != _module.Settings.OrganizeInExplorer)
                Save(s => s.OrganizeInExplorer = on);
            toggle.IsChecked = _module.Settings.OrganizeInExplorer; // a failed save shows the saved state
        }
        // Checked/Unchecked, so a UI Automation toggle saves too.
        toggle.Checked += (_, _) => Changed();
        toggle.Unchecked += (_, _) => Changed();
        return Ui.Setting("Organize in Explorer", "", toggle);
    }

    private FrameworkElement ImportRow()
    {
        var choose = Ui.Button("Choose file…", name: "Import archive");
        choose.Click += async (_, _) => await ImportAsync(choose);
        var row = Ui.Setting("Import archive", "", choose);
        return Ui.Stack(row, _importError.Margin(16, 0, 16, 10));
    }

    private async Task ImportAsync(Button choose)
    {
        _importError.Visibility = Visibility.Collapsed;
        // Imported files land in the first watched folder that still exists; rules apply as there.
        string? target = _module.Settings.Folders.Select(f => f.Path).FirstOrDefault(Directory.Exists);
        if (target is null || _module.Engine is not { } engine || _module.ImportStaging is not { } staging)
        {
            ShowImportError("Add a folder first");
            return;
        }
        var dialog = new OpenFileDialog { Title = "Import archive", Filter = "Archives (*.zip;*.tgz;*.tar.gz)|*.zip;*.tgz;*.tar.gz" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        choose.IsEnabled = false;
        try
        {
            var extracted = await Task.Run(() => ArchiveImporter.Extract(dialog.FileName, target, staging));
            _import(engine.PreviewImportAsync(target, extracted));
        }
        catch (ArchiveRejectedException ex)
        {
            ShowImportError(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowImportError("Archive unreadable");
        }
        finally
        {
            choose.IsEnabled = true;
        }
    }

    private void ShowImportError(string message)
    {
        _importError.Text = message;
        _importError.Visibility = Visibility.Visible;
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Add folder", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        string path = dialog.FolderName;
        // New folders start in Manual, so their first contact is a preview.
        Save(s =>
        {
            if (!s.Folders.Any(f => Same(f.Path, path)))
                s.Folders.Add(new WatchedFolder { Path = path, Mode = FolderMode.Manual });
        });
    }

    /// <summary>Opens the folder's .chippiignore in the default editor, creating an empty one on first use.</summary>
    private void OpenIgnoreFile(string folder)
    {
        string file = Path.Combine(folder, EngineOptions.IgnoreFileName);
        try
        {
            if (!File.Exists(file))
                File.WriteAllText(file, "");
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowError($"Couldn't open {EngineOptions.IgnoreFileName}");
        }
    }

    private void Edit(WatchedFolder folder, Action<WatchedFolder> change) => Save(s =>
    {
        if (s.Folders.FirstOrDefault(f => Same(f.Path, folder.Path)) is { } copy)
            change(copy);
    });

    /// <summary>Edits a copy (so a folder newly set to Silent gets a fresh baseline), saves, re-renders.</summary>
    private void Save(Action<DownloadsSettings> change)
    {
        _error.Visibility = Visibility.Collapsed;
        var settings = _module.Settings.Clone();
        change(settings);
        try
        {
            _module.SaveSettings(settings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError(ex.Message);
        }
        Dispatcher.BeginInvoke(Render); // not inside the control's own event
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}
