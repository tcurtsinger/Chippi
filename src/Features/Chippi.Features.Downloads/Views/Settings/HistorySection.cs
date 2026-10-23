// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private const int CollapseOver = 10;
    private const int Take = 500;

    private readonly DownloadsModule _module;
    private readonly long? _focusRun;
    private readonly SearchBox _search = new() { Placeholder = "Search" };
    private readonly ComboBox _folder;
    private readonly StackPanel _runs = new();
    private readonly TextBlock _error = Ui.Warning("");
    private readonly HashSet<long> _expanded = [];
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long? _confirming;
    private long? _confirmingRow;

    public HistorySection(DownloadsModule module, long? focusRun, bool confirmUndo = false)
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
        var folders = new List<string> { "All folders" };
        folders.AddRange(module.Settings.Folders.Select(f => Ui.FolderName(f.Path)));
        _folder = Ui.Choice(folders, 0, "Folder", 170);
        _folder.SelectionChanged += (_, _) => Render();

        var clear = Ui.Button("Clear history", icon: LucideIcons.Trash);
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
        string? folder = _folder.SelectedIndex > 0 ? _module.Settings.Folders.ElementAtOrDefault(_folder.SelectedIndex - 1)?.Path : null;
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
            _runs.Children.Add(Ui.Card(Ui.Pad(Ui.Text(_search.Text.Length > 0 ? "No matches" : "Nothing organized yet", "BodyTextStyle", "TextTertiaryBrush"))).Margin(0, 16, 0, 0));
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
        bool collapsed = rows.Count > CollapseOver && !_expanded.Contains(runId);
        // Undo run acts on the whole run, so it counts the whole run: never just the rows a search
        // or the row limit left on screen (Karl's review).
        int undoable = _module.Engine?.History.RunOperations(runId).Count(Undoable) ?? 0;

        string trigger = run?.Trigger switch
        {
            RunTrigger.Silent => "Silent",
            RunTrigger.Explorer => "Explorer",
            RunTrigger.ArchiveImport => "Archive import",
            RunTrigger.Undo => "Undo",
            _ => "Organize now",
        };
        string where = run is null ? "" : $" · {Ui.FolderName(run.Folder)}";
        int moved = rows.Count(o => o.State == OperationState.Completed && o.Kind != HistoryOperationKind.Restore);
        int failed = rows.Count(o => o.State == OperationState.Failed);
        int undone = rows.Count(o => o.State == OperationState.Undone);
        string counts = string.Join(" · ", new[] { (moved, "moved"), (failed, "failed"), (undone, "undone") }.Where(c => c.Item1 > 0).Select(c => $"{c.Item1} {c.Item2}"));
        string time = Ui.Time(run?.StartedAt ?? rows[0].PlannedAt);

        // Header row: "Silent · Downloads" over "10:07 AM · 1 moved", then Undo run.
        var head = Ui.Columns("Auto", "*", "Auto");
        if (rows.Count > CollapseOver)
        {
            var expand = Ui.Icon(collapsed ? LucideIcons.ChevronRight : LucideIcons.ChevronDown, collapsed ? $"Show {rows.Count} files" : "Hide files");
            expand.Click += (_, _) =>
            {
                if (!_expanded.Add(runId))
                    _expanded.Remove(runId);
                Render();
            };
            head.Children.Add(expand.Margin(-8, 0, 8, 0).At(0));
        }
        head.Children.Add(Ui.Stack(Ui.Text(trigger + where), Ui.Text(counts.Length > 0 ? $"{time} · {counts}" : time, "CaptionTextStyle")).At(1));
        if (undoable > 0)
        {
            var undoRun = Ui.Button("Undo run", icon: LucideIcons.Undo);
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
        if (_confirming == runId && undoable > 0)
            card.Children.Add(Confirm(undoable, run?.Folder, () => _ = UndoRunAsync(runId)));
        if (!collapsed)
        {
            foreach (var row in rows)
            {
                card.Children.Add(Row(row, restores.GetValueOrDefault(row.Id)));
                if (_confirmingRow == row.Id)
                    card.Children.Add(Confirm(1, Path.GetDirectoryName(row.SourcePath), () => _ = UndoRowAsync(row.Id)));
            }
        }
        return card;
    }

    /// <summary>A file row, indented under its run: name over "Downloads › Documents", the state, and undo.</summary>
    private Border Row(OperationRecord op, OperationRecord? restore)
    {
        string name = Path.GetFileName(op.State == OperationState.Failed ? op.SourcePath : op.DestinationPath);
        string path = $"{Ui.FolderName(Path.GetDirectoryName(op.SourcePath) ?? "")} › {Destination(op)}";
        var nameBlock = Ui.Text(name);
        nameBlock.ToolTip = op.DestinationPath;

        TextBlock status = op switch
        {
            { State: OperationState.Failed } => Ui.Text($"Failed · {Lower(op.Error)}", "CaptionTextStyle", "WarningTextBrush"),
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
    private Border Confirm(int count, string? folder, Action undo)
    {
        var cancel = Ui.Button("Cancel");
        cancel.Click += (_, _) =>
        {
            _confirming = _confirmingRow = null;
            Render();
        };
        var go = Ui.Button($"Undo {count}", "PrimaryButtonStyle");
        go.Click += (_, _) =>
        {
            go.IsEnabled = cancel.IsEnabled = false;
            undo();
        };
        var grid = Ui.Columns("*", "Auto", "Auto");
        grid.Children.Add(Ui.Stack(
            Ui.Text(count == 1 ? "Undo this move?" : $"Undo {count} moves?"),
            Ui.Text(folder is null ? "Files go back where they were" : $"Files go back to {Ui.FolderName(folder)}", "CaptionTextStyle")).At(0));
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
        ShowError(failed == 0 ? null : $"{failed} not undone · {Lower(results.First(r => !r.Succeeded).Error)}");
        Render();
    }

    private async Task UndoRowAsync(long operationId)
    {
        if (_module.Engine is not { } engine)
            return;
        var result = await engine.UndoAsync(operationId);
        _confirmingRow = null;
        ShowError(result.Succeeded ? null : $"Not undone · {Lower(result.Error)}");
        Render();
    }

    /// <summary>Clear history: a small popover with one consequence line and a Critical Clear.</summary>
    private void ShowClearPopover(Button anchor)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, HorizontalOffset = -150 };
        var cancel = Ui.Button("Cancel");
        cancel.Click += (_, _) => popup.IsOpen = false;
        var clear = Ui.Button("Clear", "CriticalButtonStyle");
        clear.Click += (_, _) =>
        {
            popup.IsOpen = false;
            Clear();
        };
        var content = Ui.Stack(
            Ui.Text("Clear history?", "HeadlineTextStyle"),
            Ui.Text("Files stay where they are. Undo won't be possible.", "CaptionTextStyle").Margin(0, 4, 0, 16),
            Ui.Row(cancel, clear.Margin(8, 0, 0, 0)));
        ((FrameworkElement)content.Children[2]).HorizontalAlignment = HorizontalAlignment.Right;
        var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0), Child = content, Width = 300 };
        card.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "WindowBorderBrush");
        popup.Child = card;
        popup.Opened += (_, _) => cancel.Focus();
        popup.IsOpen = true;
    }

    private void Clear()
    {
        var settings = _module.Settings.Clone();
        var now = DateTimeOffset.UtcNow;
        settings.HistoryClearedUtc = now;
        try
        {
            _module.Engine?.History.ClearBefore(now);
            _module.SaveSettings(settings);
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
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
