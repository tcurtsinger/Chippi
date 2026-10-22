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
/// History (DESIGN.md §5): search and a folder filter, then day eyebrows over one Panel per run with
/// Surface rows. Undo per row and per run goes through an inline confirm bar; Clear history uses a
/// popover. Import rows have no undo (nothing to go back to).
/// </summary>
internal sealed class HistorySection : UserControl
{
    private const int CollapseOver = 10;
    private const int Take = 500;

    private readonly DownloadsModule _module;
    private readonly long? _focusRun;
    private readonly SearchBox _search = new() { Placeholder = "Search", Height = 36 };
    private readonly ComboBox _folder;
    private readonly StackPanel _runs = new();
    private readonly TextBlock _error = Ui.Warning("");
    private readonly HashSet<long> _expanded = [];
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long? _confirming;
    private long? _confirmingRow;

    public HistorySection(DownloadsModule module, long? focusRun)
    {
        _module = module;
        _focusRun = focusRun;
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
        _folder.Height = 36;
        _folder.SelectionChanged += (_, _) => Render();

        var clear = Ui.Button("Clear history", icon: LucideIcons.Trash);
        clear.HorizontalAlignment = HorizontalAlignment.Right;
        clear.Click += (_, _) => ShowClearPopover(clear);

        var bar = Ui.Columns("*", "Auto", "Auto");
        bar.Children.Add(_search.At(0));
        bar.Children.Add(_folder.Margin(10, 0, 0, 0).At(1));
        bar.Children.Add(clear.Margin(10, 0, 0, 0).At(2));

        _error.Visibility = Visibility.Collapsed;
        Content = Ui.Stack(bar, _error.Margin(0, 8, 0, 0), _runs.Margin(0, 6, 0, 0));
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
            _runs.Children.Add(Ui.Text(_search.Text.Length > 0 ? "No matches" : "Nothing organized yet", "CaptionTextStyle", "TextTertiaryBrush").Margin(0, 14, 0, 0));
            return;
        }

        foreach (var day in shown.GroupBy(o => o.PlannedAt.ToLocalTime().Date).OrderByDescending(g => g.Key))
        {
            _runs.Children.Add(Ui.Eyebrow(DayLabel(day.Key)).Margin(0, 14, 0, 8));
            foreach (var run in day.GroupBy(o => o.RunId).OrderByDescending(g => g.Max(o => o.PlannedAt)))
                _runs.Children.Add(Run(engine.History.GetRun(run.Key), [.. run.OrderBy(o => o.Id)], restores).Margin(0, 0, 0, 8));
        }
        if (_focusRun is { } focus && _runs.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag is long id && id == focus) is { } target)
            Dispatcher.BeginInvoke(() => target.BringIntoView(), DispatcherPriority.Loaded);
    }

    private Border Run(RunRecord? run, IReadOnlyList<OperationRecord> rows, IReadOnlyDictionary<long, OperationRecord> restores)
    {
        long runId = rows[0].RunId;
        bool collapsed = rows.Count > CollapseOver && !_expanded.Contains(runId);
        var undoable = rows.Where(Undoable).ToList();

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

        var head = Ui.Columns("Auto", "*", "Auto", "Auto");
        if (rows.Count > CollapseOver)
        {
            var expand = Ui.Icon(collapsed ? LucideIcons.ChevronRight : LucideIcons.ChevronDown, collapsed ? $"Show {rows.Count} files" : "Hide files");
            expand.Click += (_, _) =>
            {
                if (!_expanded.Add(runId))
                    _expanded.Remove(runId);
                Render();
            };
            head.Children.Add(expand.Margin(0, 0, 4, 0).At(0));
        }
        head.Children.Add(Ui.Row(Ui.Text(trigger + where, "TitleTextStyle"), Ui.Mono(Ui.Time(run?.StartedAt ?? rows[0].PlannedAt), "TextTertiaryBrush").Margin(10, 0, 0, 0)).At(1));
        head.Children.Add(Ui.Text(counts, "CaptionTextStyle", "TextTertiaryBrush").Margin(0, 0, 10, 0).At(2));
        if (undoable.Count > 0)
        {
            var undoRun = Ui.Button("Undo run", icon: LucideIcons.Undo);
            undoRun.Click += (_, _) =>
            {
                _confirming = runId;
                _confirmingRow = null;
                Render();
            };
            head.Children.Add(undoRun.At(3));
        }
        head.Margin = new Thickness(8, 4, 4, 6);

        var body = Ui.Stack(head);
        if (_confirming == runId && undoable.Count > 0)
            body.Children.Add(Confirm(undoable.Count, run?.Folder, () => _ = UndoRunAsync(runId)));
        if (!collapsed)
        {
            foreach (var row in rows)
            {
                body.Children.Add(Row(row, restores.GetValueOrDefault(row.Id)).Margin(0, 0, 0, 4));
                if (_confirmingRow == row.Id)
                    body.Children.Add(Confirm(1, Path.GetDirectoryName(row.SourcePath), () => _ = UndoRowAsync(row.Id)).Margin(0, 0, 0, 4));
            }
        }
        var panel = Ui.Panel(body, new Thickness(6, 6, 6, 2));
        panel.Tag = runId;
        if (_focusRun == runId)
            panel.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return panel;
    }

    private Border Row(OperationRecord op, OperationRecord? restore)
    {
        string name = Path.GetFileName(op.State == OperationState.Failed ? op.SourcePath : op.DestinationPath);
        var from = Ui.Text(Ui.FolderName(Path.GetDirectoryName(op.SourcePath) ?? ""), "CaptionTextStyle", "TextTertiaryBrush");
        var to = Ui.Text(Destination(op), "CaptionTextStyle", "TextTertiaryBrush");
        var path = Ui.Row(from, Ui.Glyph(LucideIcons.ChevronRight, "TextMutedBrush", 11).Margin(5, 0, 5, 0), to);
        var nameBlock = Ui.Text(name, "TitleTextStyle");
        nameBlock.ToolTip = op.DestinationPath;

        FrameworkElement status = op switch
        {
            { State: OperationState.Failed } => Ui.Row(Ui.Glyph(LucideIcons.Info, "WarningBrush", 13), Ui.Text($"Failed · {Lower(op.Error)}", "CaptionTextStyle", "WarningBrush").Margin(5, 0, 0, 0)),
            { State: OperationState.Undone } => Ui.Row(Ui.Glyph(LucideIcons.Undo, "TextTertiaryBrush", 13),
                Ui.Text("Undone", "CaptionTextStyle", "TextTertiaryBrush").Margin(5, 0, 0, 0),
                Ui.Mono(restore?.CompletedAt is { } at ? Ui.Time(at) : "", "TextTertiaryBrush").Margin(6, 0, 0, 0)),
            { Kind: HistoryOperationKind.Import } => Ui.Text("Imported", "CaptionTextStyle", "TextSecondaryBrush"),
            { Kind: HistoryOperationKind.Rename } => Ui.Text("Renamed", "CaptionTextStyle", "TextSecondaryBrush"),
            { Kind: HistoryOperationKind.Restore } => Ui.Text($"Not restored · {Lower(op.Error)}", "CaptionTextStyle", "WarningBrush"),
            _ => Ui.Text("Moved", "CaptionTextStyle", "TextSecondaryBrush"),
        };

        var grid = Ui.Columns("2*", "1.4*", "Auto", "34");
        grid.Children.Add(Ui.Stack(nameBlock, path).At(0));
        grid.Children.Add(status.Margin(10, 0, 10, 0).At(1));
        grid.Children.Add(Ui.Mono(Ui.Time(op.CompletedAt ?? op.PlannedAt), "TextTertiaryBrush").Margin(0, 0, 8, 0).At(2));
        if (Undoable(op))
        {
            var undo = Ui.Icon(LucideIcons.Undo, $"Undo {name}");
            undo.Click += (_, _) =>
            {
                _confirmingRow = op.Id;
                _confirming = null;
                Render();
            };
            grid.Children.Add(undo.At(3));
        }
        var row = Ui.Surface(grid, new Thickness(12, 7, 6, 7));
        if (op.State == OperationState.Undone)
            nameBlock.Opacity = 0.7;
        return row;
    }

    /// <summary>The inline confirm bar: "Undo 4 moves?" with Cancel and the only primary on screen.</summary>
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
            Ui.Text(count == 1 ? "Undo this move?" : $"Undo {count} moves?", "TitleTextStyle"),
            Ui.Text(folder is null ? "Files go back where they were" : $"Files go back to {Ui.FolderName(folder)}", "CaptionTextStyle", "TextTertiaryBrush")).At(0));
        grid.Children.Add(cancel.Margin(0, 0, 8, 0).At(1));
        grid.Children.Add(go.At(2));
        var bar = Ui.Surface(grid, new Thickness(14, 9, 9, 9));
        bar.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush");
        bar.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        bar.Margin = new Thickness(0, 0, 0, 4);
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

    /// <summary>Clear history: a small popover with one consequence line and a Signal Red Deep Clear.</summary>
    private void ShowClearPopover(Button anchor)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, HorizontalOffset = -150 };
        var cancel = Ui.Button("Cancel");
        cancel.Click += (_, _) => popup.IsOpen = false;
        var clear = Ui.Button("Clear", "PrimaryButtonStyle");
        clear.SetResourceReference(BackgroundProperty, "DangerDeepBrush");
        clear.Click += (_, _) =>
        {
            popup.IsOpen = false;
            Clear();
        };
        var content = Ui.Stack(
            Ui.Text("Clear history?", "TitleTextStyle"),
            Ui.Text("Files stay where they are. Undo won't be possible.", "CaptionTextStyle", "TextTertiaryBrush").Margin(0, 4, 0, 14),
            Ui.Row(cancel, clear.Margin(8, 0, 0, 0)));
        ((FrameworkElement)content.Children[2]).HorizontalAlignment = HorizontalAlignment.Right;
        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0), Child = content, Width = 300 };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
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
