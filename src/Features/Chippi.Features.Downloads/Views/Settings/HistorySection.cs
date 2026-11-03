// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// History (DESIGN.md §5): search, a folder filter and Clear history, then day headers over one card
/// per run (a header row, then indented file rows). Undo per row and per run goes through an inline
/// confirm bar; Clear history uses a popover. Import rows have no undo (nothing to go back to).
/// </summary>
internal sealed class HistorySection : UserControl
{
    private const int Take = 500;

    private readonly DownloadsModule _module;
    private readonly long? _focusRun;
    private readonly SearchBox _search = new() { Placeholder = "Search history" };
    private readonly ComboBox _folder;
    private readonly List<string> _folderPaths;
    private readonly StackPanel _runs = new();
    private readonly TextBlock _error = Ui.Warning("");
    private readonly HashSet<long> _expanded = [];
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long? _confirming;
    private long? _confirmingRow;

    /// <param name="folder">A watched folder to show the runs of (the island's History ›), else all folders.</param>
    public HistorySection(DownloadsModule module, long? focusRun, bool confirmUndo = false, string? folder = null)
    {
        _module = module;
        _focusRun = focusRun;
        if (confirmUndo)
            _confirming = focusRun; // the notification's Undo: ask here, never undo on the click itself
        if (focusRun is { } run)
            _expanded.Add(run);

        System.Windows.Automation.AutomationProperties.SetName(_search, "Search history");
        _search.TextChanged += (_, _) =>
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        };
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            Render();
        };
        _folderPaths = module.Settings.Folders.Select(f => f.Path).ToList();
        if (folder is not null && !_folderPaths.Any(path => Views.Tidy.TidyProjection.SameFolder(path, folder)))
            _folderPaths.Add(folder); // A deep link can still show the history of a removed folder.
        var duplicateNames = OrganizerLabels.RepeatedFolderNames(_folderPaths.Select(Ui.FolderName));
        var labels = _folderPaths.Select(p => duplicateNames.Contains(Ui.FolderName(p)) ? p : Ui.FolderName(p));
        int picked = folder is null ? -1 : _folderPaths.FindIndex(p => Views.Tidy.TidyProjection.SameFolder(p, folder));
        _folder = Ui.Choice(labels.Prepend("All folders"), picked + 1, "Folder", 170);
        _folder.SelectionChanged += (_, _) => Render();

        var clear = Ui.Button("Clear all history");
        const string clearHelp = "Files stay where they are. Cleared runs can no longer be undone here.";
        clear.ToolTip = clearHelp;
        AutomationProperties.SetHelpText(clear, clearHelp);
        clear.HorizontalAlignment = HorizontalAlignment.Right;
        clear.Click += (_, _) => ShowClearPopover(clear);

        var bar = Ui.Columns("*", "Auto", "Auto");
        bar.Children.Add(_search.At(0));
        bar.Children.Add(_folder.Margin(10, 0, 0, 0).At(1));
        bar.Children.Add(clear.Margin(10, 0, 0, 0).At(2));

        _error.Visibility = Visibility.Collapsed;
        Content = Ui.Stack(bar.Margin(0, 16, 0, 0), _error.Margin(0, 8, 0, 0), _runs);
        Loaded += (_, _) =>
        {
            if (_module.Engine is { } engine)
                engine.HistoryChanged += OnHistoryChanged;
        };
        Unloaded += (_, _) =>
        {
            _searchDelay.Stop();
            if (_module.Engine is { } engine)
                engine.HistoryChanged -= OnHistoryChanged;
        };
        Render();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && _confirming is null && _confirmingRow is null)
                Render();
        }, DispatcherPriority.Background);

    private void Render()
    {
        _runs.Children.Clear();
        if (_module.Engine is not { } engine)
            return;
        string? folder = _folder.SelectedIndex > 0 ? _folderPaths[_folder.SelectedIndex - 1] : null;
        _folder.ToolTip = folder ?? "All folders";
        var operations = engine.History.Query(new HistoryQuery
        {
            Folder = folder,
            Text = _search.Text.Trim() is { Length: > 0 } text ? text : null,
            From = _module.Settings.HistoryClearedUtc,
            Take = Take,
        });
        // Restores show on the row they undid, not as rows of their own.
        var restores = operations.Where(o => o.Kind == HistoryOperationKind.Restore && o.UndoOf is not null && o.State == OperationState.Completed)
            .GroupBy(o => o.UndoOf!.Value).ToDictionary(g => g.Key, g => g.First());
        var shown = operations.Where(o => o.Kind != HistoryOperationKind.Restore || o.State != OperationState.Completed).ToList();
        if (shown.Count == 0)
        {
            string empty = _search.Text.Trim().Length > 0 ? "No matching runs" : folder is not null ? "No history for this folder" : "Nothing organized yet";
            _runs.Children.Add(Ui.Card(Ui.Pad(Ui.Text(empty, "BodyTextStyle", "TextTertiaryBrush"))).Margin(0, 16, 0, 0));
            return;
        }

        foreach (var day in shown.GroupBy(o => o.PlannedAt.ToLocalTime().Date).OrderByDescending(g => g.Key))
        {
            _runs.Children.Add(Ui.Section(DayLabel(day.Key)));
            foreach (var run in day.GroupBy(o => o.RunId).OrderByDescending(g => g.Max(o => o.PlannedAt)))
                _runs.Children.Add(Run(engine.History.GetRun(run.Key), [.. run.OrderBy(o => o.Id)], restores).Margin(0, 0, 0, 8));
        }
        if (_focusRun is { } focus && _runs.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag is long id && id == focus) is { } target)
            Dispatcher.BeginInvoke(() => target.BringIntoView(), DispatcherPriority.Loaded);
    }

    private Card Run(RunRecord? run, IReadOnlyList<OperationRecord> rows, IReadOnlyDictionary<long, OperationRecord> restores)
    {
        long runId = rows[0].RunId;
        bool collapsed = !_expanded.Contains(runId); // every run starts collapsed; the focused run starts open
        // Undo acts on the whole run, so it counts the whole run: never just the rows a search
        // or the row limit left on screen (Karl's review).
        var wholeRun = _module.Engine?.History.RunOperations(runId) ?? rows;
        var undoable = wholeRun.Where(Undoable).ToList();

        string trigger = run?.Trigger switch
        {
            RunTrigger.Silent => "Auto",
            RunTrigger.Explorer => "Explorer",
            RunTrigger.ArchiveImport => "Archive import",
            RunTrigger.Undo => "Undo",
            _ => "Organize now",
        };
        string where = run is null ? "" : $" · {Ui.FolderName(run.Folder)}";
        int Completed(HistoryOperationKind kind) => wholeRun.Count(o => o.State == OperationState.Completed && o.Kind == kind);
        int failed = wholeRun.Count(o => o.State == OperationState.Failed);
        int undone = wholeRun.Count(o => o.State == OperationState.Undone);
        string time = Ui.Time(run?.StartedAt ?? rows[0].PlannedAt);

        void Toggle()
        {
            if (!_expanded.Add(runId))
                _expanded.Remove(runId);
            Render();
        }

        // Header row: a chevron, "Silent · Downloads" over "10:07 AM · 1 moved", then Undo.
        // Clicking anywhere on it (but a button) opens or closes the run, like a rule's header.
        var head = Ui.Columns("Auto", "*", "Auto");
        head.Background = Brushes.Transparent;
        var expand = Ui.Icon(collapsed ? LucideIcons.ChevronRight : LucideIcons.ChevronDown, collapsed ? $"Show {rows.Count} files" : "Hide files");
        expand.Click += (_, _) => Toggle();
        head.Children.Add(expand.Margin(-8, 0, 8, 0).At(0));
        // "4:00 PM · 3 moved · 1 failed", the failures in Caution.
        var caption = Ui.Text(time, "CaptionTextStyle");
        foreach (var (count, what) in new[] { (Completed(HistoryOperationKind.Move), "moved"), (Completed(HistoryOperationKind.Rename), "renamed"),
            (Completed(HistoryOperationKind.Import), "imported"), (failed, "failed"), (undone, "undone") }.Where(c => c.Item1 > 0))
        {
            var part = new System.Windows.Documents.Run($" · {count} {what}");
            if (what == "failed")
                part.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "WarningTextBrush");
            caption.Inlines.Add(part);
        }
        head.Children.Add(Ui.Stack(Ui.Text(trigger + where), caption).At(1));
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && !Ui.IsInside<ButtonBase>(source))
                Toggle();
        };
        if (undoable.Count > 0)
        {
            var undoRun = Ui.Button("Undo", icon: LucideIcons.Undo, name: "Undo run");
            undoRun.Click += (_, _) =>
            {
                _confirming = runId;
                _confirmingRow = null;
                Render();
            };
            head.Children.Add(undoRun.Margin(12, 0, 0, 0).At(2));
        }

        var card = Ui.Card(Ui.Pad(head, 60));
        card.Tag = runId;
        if (_confirming == runId && undoable.Count > 0)
            card.Children.Add(Confirm(UndoQuestion(undoable), () => _ = UndoRunAsync(runId)));
        if (!collapsed)
        {
            foreach (var row in rows)
            {
                card.Children.Add(Row(row, restores.GetValueOrDefault(row.Id)));
                if (_confirmingRow == row.Id)
                    card.Children.Add(Confirm(UndoQuestion([row]), () => _ = UndoRowAsync(row.Id)));
            }
        }
        return card;
    }

    /// <summary>A file row, indented under its run: name over "Downloads › Documents", the state, and undo.</summary>
    private Border Row(OperationRecord op, OperationRecord? restore)
    {
        string name = Path.GetFileName(op.State is OperationState.Failed or OperationState.Undone ? op.SourcePath : op.DestinationPath);
        string path = $"{Ui.FolderName(Path.GetDirectoryName(op.SourcePath) ?? "")} › {Destination(op)}";
        if (op.Kind == HistoryOperationKind.Rename)
            path = op.State switch
            {
                OperationState.Undone => $"Restored from {Path.GetFileName(op.DestinationPath)}",
                OperationState.Failed => $"Rename to {Path.GetFileName(op.DestinationPath)}",
                _ => $"From {Path.GetFileName(op.SourcePath)}",
            };
        var nameBlock = Ui.Text(name);
        nameBlock.ToolTip = op.State is OperationState.Failed or OperationState.Undone ? op.SourcePath : op.DestinationPath;

        TextBlock status = op switch
        {
            { State: OperationState.Failed } => Ui.Text($"{OrganizerLabels.FailedAction(op.Kind)} · {Lower(op.Error)}", "CaptionTextStyle", "WarningTextBrush"),
            { State: OperationState.Undone } => Ui.Text(restore?.CompletedAt is { } at ? $"Undone · {Ui.Time(at)}" : "Undone", "CaptionTextStyle"),
            { Kind: HistoryOperationKind.Import } => Ui.Text("Imported", "CaptionTextStyle"),
            { Kind: HistoryOperationKind.Rename } => Ui.Text("Renamed", "CaptionTextStyle"),
            { Kind: HistoryOperationKind.Restore } => Ui.Text($"Not restored · {Lower(op.Error)}", "CaptionTextStyle", "WarningTextBrush"),
            _ => Ui.Text("", "CaptionTextStyle"),
        };

        var grid = Ui.Columns("*", "Auto", "32");
        grid.Children.Add(Ui.Stack(nameBlock, Ui.Text(path, "CaptionTextStyle")).At(0));
        grid.Children.Add(status.Margin(12, 0, 8, 0).At(1));
        if (Undoable(op))
        {
            var undo = Ui.Icon(LucideIcons.Undo, $"Undo {name}");
            undo.Click += (_, _) =>
            {
                _confirmingRow = op.Id;
                _confirming = null;
                Render();
            };
            grid.Children.Add(undo.At(2));
        }
        if (op.State == OperationState.Undone)
            nameBlock.Opacity = 0.7;
        var row = Ui.Pad(grid, 52);
        row.Padding = new Thickness(50, 8, 16, 8);
        return row;
    }

    /// <summary>The inline confirm bar: "Undo 4 moves?" with Cancel and the only accent button on screen.</summary>
    private static string UndoQuestion(IReadOnlyList<OperationRecord> operations)
    {
        string action = operations.All(o => o.Kind == HistoryOperationKind.Move) ? "move"
            : operations.All(o => o.Kind == HistoryOperationKind.Rename) ? "rename" : "change";
        return operations.Count == 1 ? $"Undo this {action}?" : $"Undo {operations.Count} {action}s?";
    }

    private Border Confirm(string prompt, Action undo)
    {
        var cancel = Ui.Button("Cancel");
        cancel.Click += (_, _) =>
        {
            _confirming = _confirmingRow = null;
            Render();
        };
        var go = Ui.Button("Undo", "PrimaryButtonStyle", name: "Confirm undo");
        go.Click += (_, _) =>
        {
            go.IsEnabled = cancel.IsEnabled = false;
            undo();
        };
        var grid = Ui.Columns("*", "Auto", "Auto");
        var question = Ui.Text(prompt);
        question.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(question.At(0));
        grid.Children.Add(cancel.Margin(0, 0, 8, 0).At(1));
        grid.Children.Add(go.At(2));
        var bar = Ui.Pad(grid, 60);
        bar.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush");
        return bar;
    }

    private async Task UndoRunAsync(long runId)
    {
        if (_module.Engine is not { } engine)
            return;
        var results = await engine.UndoRunAsync(runId);
        _confirming = null;
        int failed = results.Count(r => !r.Succeeded);
        ShowError(failed == 0 ? null : $"Undo failed for {(failed == 1 ? "1 file" : $"{failed} files")} · {Lower(results.First(r => !r.Succeeded).Error)}");
        Render();
    }

    private async Task UndoRowAsync(long operationId)
    {
        if (_module.Engine is not { } engine)
            return;
        var result = await engine.UndoAsync(operationId);
        _confirmingRow = null;
        ShowError(result.Succeeded ? null : $"Undo failed · {Lower(result.Error)}");
        Render();
    }

    /// <summary>Clear history: a small popover, the question with Cancel and a Critical Clear.</summary>
    private void ShowClearPopover(Button anchor)
    {
        const double width = 288;
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, HorizontalOffset = anchor.ActualWidth - width };
        var cancel = Ui.Button("Cancel");
        void Close()
        {
            popup.IsOpen = false;
            anchor.Focus();
        }
        cancel.Click += (_, _) => Close();
        var clear = Ui.Button("Clear", "CriticalButtonStyle");
        clear.Click += (_, _) =>
        {
            Close();
            Clear();
        };
        var title = Ui.Text("Clear history for all folders?", "HeadlineTextStyle");
        title.TextWrapping = TextWrapping.Wrap;
        var actions = Ui.Row(cancel, clear.Margin(8, 0, 0, 0));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var content = Ui.Stack(title.Margin(0, 0, 0, 16), actions);
        var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0), Child = content, Width = width };
        card.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "WindowBorderBrush");
        popup.Child = card;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        void CloseOnLeave(object sender, RoutedEventArgs e) => popup.IsOpen = false;
        anchor.Unloaded += CloseOnLeave;
        popup.Closed += (_, _) => anchor.Unloaded -= CloseOnLeave;
        popup.Opened += (_, _) => cancel.Focus();
        popup.IsOpen = true;
    }

    private void Clear()
    {
        var settings = _module.Settings.Clone();
        // History includes the cutoff millisecond. Start with the next one so a row from
        // this millisecond disappears too, even if the self-trigger guard retains it.
        var cutoff = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1);
        settings.HistoryClearedUtc = cutoff;
        bool pruning = false;
        try
        {
            _module.SaveSettings(settings);
            pruning = true;
            _module.Engine?.History.ClearBefore(cutoff);
            ShowError(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            string status = _module.Settings.HistoryClearedUtc != cutoff ? "Clear history failed" :
                pruning ? "History cleared · storage cleanup failed" : "History cleared · settings refresh failed";
            ShowError($"{status} · {Lower(ex.Message)}");
        }
        Render();
    }

    private static bool Undoable(OperationRecord op) =>
        op.State == OperationState.Completed && op.Kind is HistoryOperationKind.Move or HistoryOperationKind.Rename;

    private static string Destination(OperationRecord op)
    {
        string? folder = Path.GetDirectoryName(op.DestinationPath);
        string? source = Path.GetDirectoryName(op.SourcePath);
        if (folder is null || source is null)
            return op.DestinationPath;
        if (string.Equals(folder, source, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(op.DestinationPath); // a rename in place
        return Ui.Relative(source, folder);
    }

    private static string DayLabel(DateTime day) =>
        day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("dddd, MMM d", CultureInfo.CurrentCulture);

    private static string Lower(string? text) => string.IsNullOrEmpty(text) ? "unknown" : char.ToLowerInvariant(text[0]) + text[1..].TrimEnd('.');

    private void ShowError(string? message)
    {
        _error.Text = message ?? "";
        _error.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
