// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Modules;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Storage;
using Chippi.Rules;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// Preview and approve, inside Settings (DESIGN.md §5), with its own back button. One card of
/// checkbox rows with size, rule and destination; held rows start unchecked with a caution line;
/// files no rule matches show a disabled box. After applying: a check and undo per moved row, a
/// caution line per failed one, then Preview again and Done. Approval never re-plans: a file that
/// changed fails its row.
/// </summary>
internal sealed class PreviewView : UserControl
{
    private readonly DownloadsModule _module;
    private readonly Func<Task<PreviewPlan>> _plan;
    private readonly bool _canPreviewAgain;
    private readonly Action _close;
    private readonly Action? _addRule;
    private readonly TextBlock _subtitle = Ui.Text("", "CaptionTextStyle", "TextTertiaryBrush");
    private readonly ContentControl _rows = new() { Focusable = false };
    private readonly Grid _columns = Columns();
    private readonly Grid _footer = Ui.Columns("*", "Auto", "Auto");
    private readonly TextBlock _summary = Ui.Text("", "CaptionTextStyle", "TextSecondaryBrush");
    private readonly TextBlock _error = Ui.Warning("");
    private readonly CheckBox _all = Ui.Check("Select all");
    private readonly Dictionary<int, CheckBox> _checks = [];
    private PreviewPlan? _preview;
    private bool _syncing;
    private bool _applied;
    private bool _started;
    private bool _released;

    public PreviewView(DownloadsModule module, Func<Task<PreviewPlan>> plan, bool canPreviewAgain, Action close, Action? addRule = null)
    {
        _module = module;
        _plan = plan;
        _canPreviewAgain = canPreviewAgain;
        _close = close;
        _addRule = addRule;

        // The shell has no back button, so Preview has its own; Cancel and Done also return.
        var back = Ui.Icon(LucideIcons.ChevronLeft, "Back");
        back.Click += (_, _) => Leave();
        var header = Ui.Columns("Auto", "*");
        header.Children.Add(back.Margin(-8, 0, 8, 0).At(0));
        header.Children.Add(Ui.Stack(Ui.Text("Preview", "HeadlineTextStyle"), _subtitle).At(1));
        header.Margin = new Thickness(0, 16, 0, 16);

        // Checked, Unchecked and Indeterminate, so a UI Automation toggle selects too.
        _all.Checked += (_, _) => AllFlipped();
        _all.Unchecked += (_, _) => AllFlipped();
        _all.Indeterminate += (_, _) => AllFlipped();
        _columns.Children.Add(_all.At(0));
        _columns.Children.Add(Ui.Text("File", "CaptionTextStyle").At(1));
        _columns.Children.Add(Right(Ui.Text("Size", "CaptionTextStyle")).At(2));
        _columns.Children.Add(Ui.Text("Rule", "CaptionTextStyle").Margin(14, 0, 0, 0).At(3));
        _columns.Children.Add(Ui.Text("Destination", "CaptionTextStyle").At(4));

        _error.Visibility = Visibility.Collapsed;
        Content = Ui.Stack(header, _rows, _footer.Margin(0, 16, 0, 0), _error.Margin(0, 8, 0, 0));
        Loaded += async (_, _) =>
        {
            if (!_started)
            {
                _started = true;
                await LoadAsync();
            }
        };
        // Every way off this view (the shell's Back, the sidebar, closing Settings) releases the
        // preview, and an import's extracted files with it (Karl's review).
        Unloaded += (_, _) => Release();
    }

    private async Task LoadAsync()
    {
        _rows.Content = Ui.Card(Ui.Pad(Ui.Text("Planning…", "BodyTextStyle", "TextTertiaryBrush")));
        try
        {
            _preview = await _plan();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _rows.Content = null;
            ShowError(ex.Message);
            return;
        }
        if (_released)
        {
            // Planning finished after the view was left: nobody will approve it.
            _module.Engine?.Discard(_preview.Id);
            return;
        }
        _applied = false;
        _subtitle.Text = $"{Ui.FolderName(_preview.Folder)}{(_preview.Archive is { } a ? $" · {a}" : "")} · planned {Ui.Time(_preview.CreatedAt)}";
        RenderPlan();
    }

    private void RenderPlan()
    {
        var preview = _preview!;
        var card = NewCard();
        _checks.Clear();
        _all.Visibility = Visibility.Visible;
        if (preview.Items.Count == 0)
            card.Children.Add(Ui.Pad(Ui.Text("Nothing to organize", "BodyTextStyle", "TextTertiaryBrush")));
        foreach (var item in preview.Items.Where(i => i.Kind != OperationKind.NoMatch))
        {
            bool actionable = item.Kind is OperationKind.Move or OperationKind.Rename && item.Destination is not null;
            var check = Ui.Check($"Include {item.Name}", actionable && item.Selected);
            check.IsEnabled = actionable;
            // Checked/Unchecked, so a UI Automation toggle counts too.
            check.Checked += (_, _) => Sync();
            check.Unchecked += (_, _) => Sync();
            if (actionable)
                _checks[item.Id] = check;
            card.Children.Add(Row(item, check, null));
        }
        var unmatched = preview.Items.Where(i => i.Kind == OperationKind.NoMatch).ToList();
        if (unmatched.Count > 0)
        {
            var names = SettingsLayout.Card([.. unmatched.Select(i => SettingsLayout.Empty(i.Name))]);
            names.SetResourceReference(StyleProperty, "CardRowsStyle");
            card.Children.Add(new ExpanderRow { Header = $"{unmatched.Count} files \u00b7 no rule matches", Description = $"They stay in {Ui.FolderName(preview.Folder)}", Content = names });
        }
        RenderPlanFooter();
        Sync();
    }

    private void RenderPlanFooter()
    {
        _footer.Children.Clear();
        var cancel = Ui.Button("Cancel");
        cancel.Click += (_, _) => Leave();
        var approve = Ui.Button("Approve", "PrimaryButtonStyle");
        approve.Click += async (_, _) => await ApproveAsync(approve);
        bool nothing = _checks.Count == 0;
        _summary.Text = nothing ? "Nothing to organize" : _summary.Text;
        if (nothing && _addRule is not null)
        {
            approve = SettingsLayout.AddButton("rule", () => { Release(); _addRule(); });
            approve.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        }
        _footer.Children.Add(_summary.At(0));
        _footer.Children.Add(cancel.Margin(0, 0, 8, 0).At(1));
        _footer.Children.Add(approve.At(2));
    }

    private void Sync()
    {
        if (_syncing || _preview is null)
            return;
        _syncing = true;
        var chosen = _preview.Items.Where(i => _checks.TryGetValue(i.Id, out var c) && c.IsChecked == true).ToList();
        _all.IsThreeState = true;
        _all.IsChecked = chosen.Count == 0 ? false : chosen.Count == _checks.Count ? true : null;
        _all.IsEnabled = _checks.Count > 0;
        _summary.Text = _checks.Count == 0 ? "Nothing to organize" : $"{chosen.Count} of {_checks.Count} selected · {Ui.Size(chosen.Sum(i => i.Bytes))}";
        if (_footer.Children.OfType<Button>().LastOrDefault() is { } approve)
            approve.IsEnabled = chosen.Count > 0 || _checks.Count == 0 && _addRule is not null;
        _syncing = false;
    }

    /// <summary>A fresh card for the rows, headed by the column row (which holds Select all).</summary>
    private Card NewCard()
    {
        if (_columns.Parent is Border old)
            old.Child = null;
        var card = Ui.Card(Ui.Pad(_columns, 40));
        _rows.Content = card;
        return card;
    }

    private void AllFlipped()
    {
        if (!_syncing)
            SelectAll(_all.IsChecked == true);
    }

    private void SelectAll(bool on)
    {
        _syncing = true;
        foreach (var check in _checks.Values)
            check.IsChecked = on;
        _syncing = false;
        Sync();
    }

    private async Task ApproveAsync(Button approve)
    {
        if (_preview is null || _module.Engine is not { } engine)
            return;
        var selected = _checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
        approve.IsEnabled = false;
        _error.Visibility = Visibility.Collapsed;
        try
        {
            var result = await engine.ApproveAsync(_preview.Id, selected);
            _applied = true;
            RenderResult(result);
        }
        catch (ObjectDisposedException)
        {
            ShowError("File organizer is off");
        }
        catch (InvalidOperationException ex)
        {
            // Used or expired: say so; Preview again plans afresh.
            ShowError(ex.Message);
            RenderDone(_preview.Items.Count(i => i.Kind == OperationKind.Hold), 0, 0);
        }
    }

    private void RenderResult(ApplyResult result)
    {
        var preview = _preview!;
        _subtitle.Text = $"{Ui.FolderName(preview.Folder)} · applied {Ui.Time(DateTimeOffset.Now)}";
        var card = NewCard();
        _all.Visibility = Visibility.Hidden;
        foreach (var outcome in result.Items)
        {
            var item = preview.Items.First(i => i.Id == outcome.ItemId);
            card.Children.Add(Row(item with { Destination = outcome.Destination ?? item.Destination }, null, outcome));
        }
        int held = preview.Items.Count(i => i.Kind == OperationKind.Hold || i.Warnings.Any(w => w.Kind == WarningKind.MisleadingName) && !result.Items.Any(r => r.ItemId == i.Id));
        RenderDone(held, result.Succeeded, result.Failed);
    }

    private void RenderDone(int held, int moved, int failed)
    {
        _footer.Children.Clear();
        var summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        summary.SetResourceReference(StyleProperty, "CaptionTextStyle");
        summary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        summary.Inlines.Add($"{moved} moved");
        if (failed > 0)
        {
            var count = new System.Windows.Documents.Run($" · {failed} failed");
            count.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "WarningTextBrush");
            summary.Inlines.Add(count);
        }
        if (held > 0)
            summary.Inlines.Add($" · {held} held");
        _footer.Children.Add(summary.At(0));
        if (_canPreviewAgain)
        {
            var again = Ui.Button("Refresh");
            again.Click += async (_, _) => await LoadAsync();
            _footer.Children.Add(again.Margin(0, 0, 8, 0).At(1));
        }
        var done = Ui.Button("Done", "PrimaryButtonStyle");
        done.Click += (_, _) => _close();
        _footer.Children.Add(done.At(2));
    }

    /// <summary>One preview row: before applying it has a checkbox; after, the outcome glyph and undo.</summary>
    private Border Row(PreviewItem item, CheckBox? check, ItemResult? outcome)
    {
        var preview = _preview!;
        bool noMatch = item.Kind == OperationKind.NoMatch;

        UIElement lead;
        if (outcome is null)
            lead = check!;
        else if (outcome.Succeeded)
            lead = Ui.Glyph(LucideIcons.Check, "SuccessBrush", 16);
        else
            lead = Ui.Glyph(LucideIcons.Info, "WarningBrush", 16);

        var name = Ui.Text(outcome?.Destination is { } moved && item.Kind == OperationKind.Move ? Path.GetFileName(moved) : item.Name, "TitleTextStyle", noMatch ? "TextTertiaryBrush" : null);
        name.ToolTip = item.Source;
        var nameStack = Ui.Stack(name);
        if (outcome is { Succeeded: false })
            nameStack.Children.Add(Ui.Warning($"Failed · {Lower(outcome.Error)}"));
        else if (outcome is null && Note(item) is { } note)
            nameStack.Children.Add(WarningLine(note));

        var size = Right(Ui.Number(Ui.Size(item.Bytes), "TextTertiaryBrush"));
        var rule = Ui.Text(noMatch ? "No match" : item.RuleName ?? (preview.Archive is not null && item.RuleId is null ? "Import" : "Held"), "BodyTextStyle", noMatch ? "TextTertiaryBrush" : null).Margin(14, 0, 8, 0);

        var destination = Ui.Row(Ui.Text(Destination(preview, item), "CaptionTextStyle", "TextSecondaryBrush"));
        if (item.Destination is { } dest && !SameDrive(item.Source, dest))
            destination.Children.Add(Ui.Badge("Other drive", BadgeKind.Needs).Margin(8, 0, 0, 0));

        var grid = Columns();
        grid.Children.Add(lead.At(0));
        grid.Children.Add(nameStack.At(1));
        grid.Children.Add(size.At(2));
        grid.Children.Add(rule.At(3));
        grid.Children.Add(destination.At(4));
        if (outcome is { Succeeded: true, OperationId: { } operation })
        {
            var undo = Ui.Icon(LucideIcons.Undo, $"Undo {item.Name}");
            undo.Click += async (_, _) => await UndoAsync(undo, operation);
            grid.Children.Add(undo.At(5));
        }

        var row = Ui.Pad(grid, 52);
        if (noMatch)
            row.Opacity = 0.75;
        return row;
    }

    private async Task UndoAsync(IconButton undo, long operation)
    {
        if (_module.Engine is not { } engine)
            return;
        undo.IsEnabled = false;
        var result = await engine.UndoAsync(operation);
        if (result.Succeeded)
        {
            undo.Icon = LucideIcons.Check;
            undo.ToolTip = "Undone";
        }
        else
        {
            undo.IsEnabled = true;
            ShowError(result.Error ?? "Couldn't undo");
        }
    }

    /// <summary>The amber line under a name: why it is held, or that its name was taken.</summary>
    private static string? Note(PreviewItem item)
    {
        if (item.Warnings.FirstOrDefault(w => w.Kind == WarningKind.MisleadingName) is not null)
            return "Held · misleading name";
        if (item.Kind == OperationKind.Hold)
            return item.Warnings.Any(w => w.Kind == WarningKind.IgnoreMatch) ? "Held · in ignore file" : $"Held · {Lower(item.HoldReason ?? item.Warnings.LastOrDefault()?.Detail)}";
        if (item.Kind == OperationKind.Move && item.Destination is { } dest && !string.Equals(Path.GetFileName(dest), item.Name, StringComparison.OrdinalIgnoreCase))
            return $"Name taken · saves as {Path.GetFileName(dest)}";
        return null;
    }

    private static string Destination(PreviewPlan preview, PreviewItem item) => item switch
    {
        { Kind: OperationKind.NoMatch } => $"Stays in {Ui.FolderName(preview.Folder)}",
        { Kind: OperationKind.Hold } => $"Stays in {Ui.FolderName(preview.Folder)}",
        { Kind: OperationKind.Rename, Destination: { } d } => Path.GetFileName(d),
        { Destination: { } d } when Path.GetDirectoryName(d) is { } parent && string.Equals(parent, Path.TrimEndingDirectorySeparator(preview.Folder), StringComparison.OrdinalIgnoreCase)
            => Ui.FolderName(preview.Folder),
        { Destination: { } d } => Ui.Relative(preview.Folder, Path.GetDirectoryName(d) ?? d),
        _ => "",
    };

    private static StackPanel WarningLine(string text)
    {
        var line = Ui.Row(Ui.Glyph(LucideIcons.Info, "WarningBrush", 12), Ui.Text(text, "CaptionTextStyle", "WarningTextBrush").Margin(5, 0, 0, 0));
        line.Margin = new Thickness(0, 1, 0, 0);
        return line;
    }

    private static string Lower(string? text) => string.IsNullOrEmpty(text) ? "not moved" : char.ToLowerInvariant(text[0]) + text[1..].TrimEnd('.');

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static Grid Columns() => Ui.Columns("34", "3*", "84", "1.4*", "2.2*", "34");

    private static FrameworkElement Right(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Right;
        return element;
    }

    private void Leave()
    {
        Release();
        _close();
    }

    /// <summary>
    /// Drops an unapproved preview (an import's extracted files with it). Safe during an approval:
    /// approving takes the preview out of the engine first, so Discard then finds nothing to delete.
    /// </summary>
    private void Release()
    {
        _released = true;
        if (!_applied && _preview is not null)
            _module.Engine?.Discard(_preview.Id);
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
