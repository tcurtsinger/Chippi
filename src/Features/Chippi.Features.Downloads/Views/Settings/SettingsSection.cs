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
    private readonly Action<string> _preview;
    private readonly Action<Task<PreviewPlan>> _import;
    private readonly ContentControl _folders = new() { Focusable = false };
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _error = Ui.Warning("");
    private readonly TextBlock _importError = Ui.Warning("");

    /// <summary>Import archive's Choose file…, off while no watched folder exists to import into.</summary>
    private readonly Button _importChoose = Ui.Button("Choose file…", name: "Import archive");
    private readonly SettingRow _importRow;
    private string? _importFolder;
    private bool _importing;

    public SettingsSection(DownloadsModule module, Action<Task<PreviewPlan>> import, Action<string>? preview = null)
    {
        _module = module;
        _preview = preview ?? (_ => { });
        _import = import;

        var add = SettingsLayout.AddButton("folder", AddFolder);
        _importRow = Ui.Setting("Import archive", "", _importChoose);
        _importChoose.Click += async (_, _) => await ImportAsync();

        _error.Visibility = Visibility.Collapsed;
        _importError.Visibility = Visibility.Collapsed;
        Content = Ui.Stack(
            Ui.Section("Watched folders", add, first: true).Margin(0, 16, 0, 8), // 16 under the tabs, as Rules and History start
            _folders,
            _error.Margin(0, 6, 0, 0),
            Ui.Section("Options"),
            Ui.Card(KeepHistoryRow(), ExplorerRow(), Ui.Stack(_importRow, _importError.Margin(16, 0, 16, 10))));
        Render();
    }

    private void Render()
    {
        var folders = _module.Settings.Folders;
        RefreshImportFolder();
        _folders.Content = folders.Count == 0
            ? SettingsLayout.Card(NoFolders())
            : Ui.Card([.. folders.Select(FolderRow)]);
    }

    private void RefreshImportFolder()
    {
        _importFolder = _module.Settings.Folders.Select(f => f.Path).FirstOrDefault(Directory.Exists);
        _importRow.Description = _importFolder is null ? "Add a watched folder to preview an archive." : $"Preview in {Ui.FolderName(_importFolder)}";
        _importRow.ToolTip = _importFolder;
        System.Windows.Automation.AutomationProperties.SetHelpText(_importChoose, _importFolder ?? _importRow.Description);
        _importChoose.IsEnabled = !_importing && _importFolder is not null;
    }

    /// <summary>"No folders yet", with the island's one-click Add Downloads when the Downloads folder exists.</summary>
    private UIElement NoFolders()
    {
        if (DownloadsModule.DownloadsFolder() is not { } downloads || !Directory.Exists(downloads))
            return SettingsLayout.Empty("No folders yet");
        var add = Ui.Button("Add Downloads", name: "Add Downloads");
        add.Click += (_, _) => Save(s => s.Folders.Add(new WatchedFolder { Path = downloads, Mode = FolderMode.Manual })); // Manual: its first contact is a preview
        return Ui.Setting("No folders yet", "", add);
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
        var graceBox = Ui.Choice(graceLabels, grace, "Wait before moving", 180);
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
        string modeDescription = folder.Mode switch
        {
            FolderMode.Silent => "Organize matching files automatically.",
            FolderMode.Manual => "Queue matching files for review.",
            _ => "Watching is paused.",
        };
        options.Children.Add(Ui.Setting("Mode", modeDescription, mode));
        options.Children.Add(Ui.Setting("Wait before moving", "", graceBox));
        options.Children.Add(Ui.Setting("Ignore list", "", ignore));
        options.Children.Add(Ui.Setting("Stop watching", "", remove));

        string where = folder.Path + (folder.Path.StartsWith(@"\\", StringComparison.Ordinal) ? " · network" : "");
        var row = new ExpanderRow
        {
            Header = name,
            Description = where,
            Icon = LucideIcons.Folder,
            Value = FolderActions(folder, exists, out var state),
            Content = options,
            IsExpanded = _expanded.Contains(folder.Path),
            ToolTip = folder.Path,
        };
        row.SetResourceReference(ForegroundProperty, !exists ? "WarningBrush" : paused ? "TextMutedBrush" : "AccentBrush");
        // Open, the Mode segment says the mode; "Not found" stays.
        void ShowState() => state.Visibility = row.IsExpanded && exists ? Visibility.Collapsed : Visibility.Visible;
        row.Expanded += (_, _) => { _expanded.Add(folder.Path); ShowState(); };
        row.Collapsed += (_, _) => { _expanded.Remove(folder.Path); ShowState(); };
        ShowState();
        return row;
    }

    /// <summary>The folder's mode and preview action, or a way to locate a missing folder.</summary>
    private StackPanel FolderActions(WatchedFolder folder, bool exists, out TextBlock state)
    {
        state = Ui.Text(exists ? folder.Mode.DisplayName() : "Not found", brush: exists ? "TextSecondaryBrush" : "WarningTextBrush");
        state.Margin = new Thickness(0, 0, 12, 0);
        if (!exists)
        {
            var relocate = Ui.Button("Choose folder…", name: $"Choose a folder in place of {Ui.FolderName(folder.Path)}");
            relocate.Click += (_, _) => Relocate(folder);
            return Ui.Row(state, relocate);
        }
        var preview = Ui.Button("Preview", name: $"Preview {Ui.FolderName(folder.Path)}");
        preview.ToolTip = $"Preview changes in {folder.Path}";
        preview.Click += (_, _) => _preview(folder.Path);
        return Ui.Row(state, preview);
    }

    /// <summary>A watched folder that's gone: watch another in its place, keeping its mode and wait (a new path starts from now).</summary>
    private void Relocate(WatchedFolder folder)
    {
        var dialog = new OpenFolderDialog { Title = "Choose folder", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        string path = dialog.FolderName;
        Save(s =>
        {
            if (s.Folders.Any(f => Same(f.Path, path)))
                s.Folders.RemoveAll(f => Same(f.Path, folder.Path));
            else if (s.Folders.FirstOrDefault(f => Same(f.Path, folder.Path)) is { } gone)
                gone.Path = path;
        });
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
        var keep = Ui.Choice(labels, index, "Keep history", 180);
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

    private async Task ImportAsync()
    {
        if (_importing) return;
        _importError.Visibility = Visibility.Collapsed;
        // Use the folder shown in the row, even if another watched folder has since appeared.
        if (_importFolder is not { } target || _module.Engine is not { } engine || _module.ImportStaging is not { } staging)
        {
            Render();
            ShowImportError("Import failed · no folder to import into");
            return;
        }
        if (!CheckImportFolder(target)) return;
        var dialog = new OpenFileDialog { Title = "Import archive", Filter = "Archives (*.zip;*.tgz;*.tar.gz)|*.zip;*.tgz;*.tar.gz" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        _importing = true;
        _importChoose.IsEnabled = false;
        try
        {
            var extracted = await Task.Run(() => ArchiveImporter.Extract(dialog.FileName, target, staging));
            if (!IsLoaded || !CheckImportFolder(target))
            {
                ArchiveImporter.DeleteStaging(extracted.Staging, null);
                return;
            }
            _import(engine.PreviewImportAsync(target, extracted));
        }
        catch (ArchiveRejectedException ex)
        {
            ShowImportError($"Import failed · {char.ToLowerInvariant(ex.Message[0])}{ex.Message[1..].TrimEnd('.')}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowImportError("Import failed · archive unreadable");
        }
        finally
        {
            _importing = false;
            if (IsLoaded) RefreshImportFolder();
        }
    }

    private bool CheckImportFolder(string path)
    {
        if (_module.Settings.Folders.Any(f => Same(path, f.Path)) && Directory.Exists(path)) return true;
        RefreshImportFolder();
        ShowImportError("Import failed · watched folder unavailable");
        return false;
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
            ShowError("Ignore list failed");
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
            ShowError(ex is UnauthorizedAccessException ? "Save failed · access denied" : "Save failed");
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
