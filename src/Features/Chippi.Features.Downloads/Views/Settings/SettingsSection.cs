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
/// Settings › the organizer's first section: watched folders (mode, grace, ignore file, remove),
/// then Keep history, Explorer integration and Import archive. Notifications live on the app-wide page.
/// </summary>
internal sealed class SettingsSection : UserControl
{
    private static readonly (string Label, int Seconds)[] Graces = [("0s", 0), ("30s", 30), ("2 min", 120), ("10 min", 600), ("1 hr", 3600)];
    private static readonly (string Label, int Days)[] Keeps = [("30 days", 30), ("90 days", 90), ("1 year", 365)];

    private readonly DownloadsModule _module;
    private readonly Action<Task<PreviewPlan>> _import;
    private readonly StackPanel _folders = new();
    private readonly TextBlock _error = Ui.Warning("");
    private readonly TextBlock _importError = Ui.Warning("");

    public SettingsSection(DownloadsModule module, Action<Task<PreviewPlan>> import)
    {
        _module = module;
        _import = import;

        var add = Ui.Button("Add folder", icon: LucideIcons.Plus);
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.Click += (_, _) => AddFolder();
        var header = Ui.Columns("*", "Auto");
        header.Children.Add(Ui.Eyebrow("Watched folders").At(0));
        header.Children.Add(add.At(1));

        _error.Visibility = Visibility.Collapsed;
        _importError.Visibility = Visibility.Collapsed;
        var page = Ui.Stack(header.Margin(0, 0, 0, 12), _folders, _error.Margin(0, 6, 0, 0), Ui.Eyebrow("More").Margin(0, 24, 0, 12), KeepHistoryRow(), ExplorerRow(), ImportRow());
        Content = page;
        Render();
    }

    private void Render()
    {
        _folders.Children.Clear();
        var folders = _module.Settings.Folders;
        if (folders.Count == 0)
        {
            _folders.Children.Add(Ui.Surface(Ui.Text("No folders yet", "CaptionTextStyle", "TextTertiaryBrush"), new Thickness(14)));
            return;
        }
        foreach (var folder in folders)
            _folders.Children.Add(FolderRow(folder).Margin(0, 0, 0, 6));
    }

    private Border FolderRow(WatchedFolder folder)
    {
        bool paused = folder.Mode == FolderMode.Paused;
        var chip = new IconChip { Icon = LucideIcons.Folder, IsOn = !paused, VerticalAlignment = VerticalAlignment.Center };

        bool exists = Directory.Exists(folder.Path);
        var name = Ui.Text(Ui.FolderName(folder.Path), "TitleTextStyle");
        var path = Ui.Text(folder.Path + (folder.Path.StartsWith(@"\\", StringComparison.Ordinal) ? " · network" : "") + (exists ? "" : " · not found"),
            "CaptionTextStyle", exists ? "TextTertiaryBrush" : "WarningTextBrush");
        path.ToolTip = folder.Path;

        var mode = Ui.Segment($"{Ui.FolderName(folder.Path)} mode", true, "Silent", "Manual", "Paused");
        mode.Width = 228;
        mode.SelectedIndex = folder.Mode switch { FolderMode.Silent => 0, FolderMode.Manual => 1, _ => 2 };
        mode.SelectionChanged += (_, _) => Edit(folder, f => f.Mode = mode.SelectedIndex switch { 0 => FolderMode.Silent, 1 => FolderMode.Manual, _ => FolderMode.Paused });

        int grace = Array.FindIndex(Graces, g => g.Seconds == folder.GraceSeconds);
        var graceLabels = Graces.Select(g => g.Label).ToList();
        if (grace < 0)
        {
            graceLabels.Add($"{folder.GraceSeconds}s"); // a hand-edited value stays visible
            grace = graceLabels.Count - 1;
        }
        var graceBox = Ui.Choice(graceLabels, grace, $"{Ui.FolderName(folder.Path)} grace period", 86, mono: true);
        graceBox.IsEnabled = folder.Mode == FolderMode.Silent;
        graceBox.SelectionChanged += (_, _) =>
        {
            if (graceBox.SelectedIndex is >= 0 and var i && i < Graces.Length)
                Edit(folder, f => f.GraceSeconds = Graces[i].Seconds);
        };
        var graceLabel = Ui.Text("Grace", "CaptionTextStyle", "TextTertiaryBrush").Margin(14, 0, 8, 0);
        graceLabel.Opacity = graceBox.IsEnabled ? 1 : 0.45;

        var ignore = Ui.Button("Ignore file", name: $"{Ui.FolderName(folder.Path)} ignore file").Margin(12, 0, 0, 0);
        ignore.IsEnabled = exists;
        ignore.Click += (_, _) => OpenIgnoreFile(folder.Path);
        var remove = Ui.Icon(LucideIcons.Close, $"Remove {Ui.FolderName(folder.Path)}").Margin(6, 0, 0, 0);
        remove.Click += (_, _) => Save(s => s.Folders.RemoveAll(f => Same(f.Path, folder.Path)));

        var grid = Ui.Columns("32", "*", "Auto", "Auto", "Auto", "Auto", "Auto");
        grid.Children.Add(chip.At(0));
        grid.Children.Add(Ui.Stack(name, path).Margin(11, 0, 12, 0).At(1));
        grid.Children.Add(mode.At(2));
        grid.Children.Add(graceLabel.At(3));
        grid.Children.Add(graceBox.At(4));
        grid.Children.Add(ignore.At(5));
        grid.Children.Add(remove.At(6));
        return Ui.Surface(grid, new Thickness(12, 9, 10, 9));
    }

    private Border KeepHistoryRow()
    {
        int index = Array.FindIndex(Keeps, k => k.Days == _module.Settings.HistoryDays);
        var labels = Keeps.Select(k => k.Label).ToList();
        if (index < 0)
        {
            labels.Add($"{_module.Settings.HistoryDays} days");
            index = labels.Count - 1;
        }
        var keep = Ui.Choice(labels, index, "Keep history", 120, mono: true);
        keep.SelectionChanged += (_, _) =>
        {
            if (keep.SelectedIndex is >= 0 and var i && i < Keeps.Length)
                Save(s => s.HistoryDays = Keeps[i].Days);
        };
        var grid = Ui.Columns("*", "Auto");
        grid.Children.Add(Ui.Text("Keep history", "TitleTextStyle").At(0));
        grid.Children.Add(keep.At(1));
        return Ui.Surface(grid, new Thickness(14, 10, 14, 10)).Margin(0, 0, 0, 6);
    }

    private Border ExplorerRow()
    {
        var toggle = Ui.Switch("Organize in Explorer", _module.Settings.OrganizeInExplorer);
        toggle.Click += (_, _) =>
        {
            Save(s => s.OrganizeInExplorer = toggle.IsChecked == true);
            toggle.IsChecked = _module.Settings.OrganizeInExplorer;
        };
        var row = Ui.Columns("*", "Auto");
        row.Children.Add(Ui.Stack(Ui.Text("Organize in Explorer", "TitleTextStyle"), Ui.Text("In \"Show more options\"", "CaptionTextStyle", "TextTertiaryBrush")).At(0));
        row.Children.Add(toggle.At(1));
        return Ui.Surface(row, new Thickness(14, 10, 14, 10)).Margin(0, 0, 0, 6);
    }

    private Border ImportRow()
    {
        var choose = Ui.Button("Choose file…", name: "Import archive");
        choose.Click += async (_, _) => await ImportAsync(choose);
        var grid = Ui.Columns("*", "Auto");
        grid.Children.Add(Ui.Stack(Ui.Text("Import archive", "TitleTextStyle"), Ui.Text("zip, tgz, tar.gz", "CaptionTextStyle", "TextTertiaryBrush"), _importError).At(0));
        grid.Children.Add(choose.At(1));
        return Ui.Surface(grid, new Thickness(14, 10, 14, 10));
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
            ShowImportError("The archive could not be read.");
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
