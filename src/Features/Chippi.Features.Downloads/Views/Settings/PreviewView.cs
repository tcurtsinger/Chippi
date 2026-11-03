// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Storage;
using Chippi.Rules;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// Preview and approve, inside Settings (DESIGN.md §5), with its own back button. One card: the column
/// row with Select all, then "Needs a look" (held files, misleading names, warnings) open at the top,
/// then a group per destination with its own checkbox, closed when there are more than
/// <see cref="OpenUpTo"/> files to approve; files no rule matches fold into one row at the end. The
/// count, Cancel and Approve stay pinned to the bottom of the window while the list scrolls. After
/// applying: the same groups with a check and undo per moved row and a caution line per failed one,
/// then Refresh and Done. Approval never re-plans: a file that changed fails its row.
/// </summary>
internal sealed class PreviewView : UserControl
{
    /// <summary>With more files to approve than this, destination groups start closed.</summary>
    private const int OpenUpTo = 12;

    private readonly DownloadsModule _module;
    private readonly Func<Task<PreviewPlan>> _plan;
    private readonly bool _canPreviewAgain;
    private readonly Action _close;
    private readonly Action? _addRule;
    private readonly TextBlock _subtitle = Ui.Text("", "CaptionTextStyle", "TextTertiaryBrush");
    private readonly ContentControl _rows = new() { Focusable = false };
    private readonly Grid _columns = Columns();
    private readonly Grid _footer = Ui.Columns("*", "Auto", "Auto");
    private readonly Border _bar;
    private readonly TranslateTransform _pin = new();
    private readonly TextBlock _summary = Ui.Text("", "CaptionTextStyle", "TextSecondaryBrush");
    private readonly Button _approve = Ui.Button("Approve", "PrimaryButtonStyle");
    private readonly TextBlock _error = Ui.Warning("");
    private readonly CheckBox _all = Ui.Check("Select all");
    private readonly Dictionary<int, CheckBox> _checks = [];
    private readonly List<(CheckBox Box, IReadOnlyList<CheckBox> Checks)> _groups = [];
    private ScrollViewer? _scroller;
    private Border? _first;
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
        _approve.Click += async (_, _) => await ApproveAsync();

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

        // The bar under the list rides the window's bottom edge while the list runs past it (Pin),
        // over the rows, with a hairline on top; at the end of the list it sits in its place.
        _error.Visibility = Visibility.Collapsed;
        _bar = new Border { Child = Ui.Stack(_footer, _error.Margin(0, 8, 0, 0)), Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, 1, 0, 0), RenderTransform = _pin };
        _bar.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        _bar.BorderBrush = Brushes.Transparent;
        _bar.SizeChanged += (_, _) => Pin();
        Panel.SetZIndex(_bar, 1);
        Content = Ui.Stack(header, _rows, _bar.Margin(0, 4, 0, 0));
        Loaded += async (_, _) =>
        {
            _scroller = Ancestor<ScrollViewer>(this);
            if (_scroller is not null)
            {
                _scroller.ScrollChanged -= OnScrolled;
                _scroller.ScrollChanged += OnScrolled;
            }
            if (!_started)
            {
                _started = true;
                await LoadAsync();
            }
        };
        // Every way off this view (the shell's Back, the sidebar, closing Settings) releases the
        // preview, and an import's extracted files with it (Karl's review).
        Unloaded += (_, _) =>
        {
            if (_scroller is not null)
                _scroller.ScrollChanged -= OnScrolled;
            Release();
        };
    }

    /// <summary>A file to show first, open and marked, from a file-specific Settings deep link.</summary>
    public string? ShowFirst { get; init; }

    private async Task LoadAsync()
    {
        _footer.Children.Clear(); // A refresh gets exactly one new plan.
        _error.Visibility = Visibility.Collapsed;
        _rows.IsEnabled = true;
        _rows.Content = Ui.Card(Ui.Pad(Ui.Text("Planning…", "BodyTextStyle", "TextTertiaryBrush")));
        try
        {
            _preview = await _plan();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _rows.Content = null;
            RenderFinishActions();
            ShowError("Preview failed");
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
        var planned = preview.Items.Where(i => i.Kind != OperationKind.NoMatch).ToList();
        bool open = planned.Count(Actionable) <= OpenUpTo;
        var look = planned.Where(i => !Clean(i)).ToList();
        if (look.Count > 0)
            AddGroup(card, "Needs a look", null, [.. look.Select(i => (i, (ItemResult?)null))], open: true, look: true);
        foreach (var group in ByDestination(planned.Where(Clean), i => i.Destination!, i => i.Kind))
            AddGroup(card, Title(preview, group.Key, group.First().Kind), group.Key, [.. group.Select(i => (i, (ItemResult?)null))], open, look: false);
        var unmatched = preview.Items.Where(i => i.Kind == OperationKind.NoMatch).ToList();
        if (unmatched.Count > 0)
        {
            var names = SettingsLayout.Card([.. unmatched.Select(i => SettingsLayout.Empty(i.Name))]);
            names.SetResourceReference(StyleProperty, "CardRowsStyle");
            card.Children.Add(new ExpanderRow { Header = $"{Files(unmatched.Count)} · no rule matches",
                Description = $"{(unmatched.Count == 1 ? "It stays" : "They stay")} in {Ui.FolderName(preview.Folder)}", Content = names });
        }
        RenderPlanFooter();
        Sync();
        Reveal();
    }

    /// <summary>Moves into one folder, or renames in place (the empty key), in the folders' order with renames last.</summary>
    private static IEnumerable<IGrouping<string, PreviewItem>> ByDestination(IEnumerable<PreviewItem> items, Func<PreviewItem, string> destination, Func<PreviewItem, OperationKind> kind) =>
        items.GroupBy(i => kind(i) == OperationKind.Rename ? "" : Path.GetDirectoryName(destination(i)) ?? "", StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Length == 0 ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

    private string Title(PreviewPlan preview, string folder, OperationKind kind) =>
        kind == OperationKind.Rename || folder.Length == 0 ? (_applied ? "Renamed" : "Rename in place")
        : string.Equals(folder, Path.TrimEndingDirectorySeparator(preview.Folder), StringComparison.OrdinalIgnoreCase) ? Ui.FolderName(preview.Folder)
        : Ui.Relative(preview.Folder, folder);

    private void RenderPlanFooter()
    {
        _footer.Children.Clear();
        _footer.Children.Add(_summary.At(0));
        int held = _preview!.Items.Count(i => i.Kind == OperationKind.Hold);
        if (_checks.Count == 0)
        {
            _summary.Text = held > 0 ? $"{Files(held)} held" : "Nothing to organize";
            if (_addRule is null || !_preview.Items.Any(i => i.Kind == OperationKind.NoMatch) && held > 0)
            {
                var done = Ui.Button("Done", "PrimaryButtonStyle");
                done.Click += (_, _) => Leave();
                _footer.Children.Add(done.At(2));
                return;
            }
        }
        var cancel = Ui.Button("Cancel");
        cancel.Click += (_, _) => Leave();
        _footer.Children.Add(cancel.Margin(0, 0, 8, 0).At(1));
        if (_checks.Count == 0 && _addRule is not null)
        {
            var add = SettingsLayout.AddButton("rule", () => { Release(); _addRule(); });
            add.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
            _footer.Children.Add(add.At(2));
        }
        else _footer.Children.Add(_approve.At(2));
    }

    /// <summary>
    /// A group's header (its checkbox, a chevron, the title over its count and size, an Other drive badge)
    /// and its rows, straight in the card so dividers run between them. Clicking the header opens or closes it.
    /// </summary>
    private void AddGroup(Card card, string title, string? folder, IReadOnlyList<(PreviewItem Item, ItemResult? Outcome)> entries, bool open, bool look)
    {
        var preview = _preview!;
        var rows = new List<Border>();
        var checks = new List<CheckBox>();
        foreach (var (item, outcome) in entries)
        {
            CheckBox? check = null;
            if (outcome is null)
            {
                bool actionable = Actionable(item);
                check = Ui.Check($"Include {item.Name}", actionable && item.Selected);
                check.IsEnabled = actionable;
                // Checked/Unchecked, so a UI Automation toggle counts too.
                check.Checked += (_, _) => Sync();
                check.Unchecked += (_, _) => Sync();
                if (actionable)
                {
                    _checks[item.Id] = check;
                    checks.Add(check);
                }
            }
            var row = Row(item, check, outcome, look);
            if (ShowFirst is { } first && string.Equals(item.Name, first, StringComparison.OrdinalIgnoreCase))
            {
                row.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush");
                _first = row;
                open = true;
            }
            rows.Add(row);
        }

        var head = Columns();
        if (entries.Any(e => e.Outcome is null))
        {
            var box = Ui.Check($"Include {title}");
            box.IsThreeState = true;
            box.IsEnabled = checks.Count > 0;
            box.Checked += (_, _) => GroupFlipped(box, checks);
            box.Unchecked += (_, _) => GroupFlipped(box, checks);
            box.Indeterminate += (_, _) => GroupFlipped(box, checks);
            _groups.Add((box, checks));
            head.Children.Add(box.At(0));
        }
        long bytes = entries.Sum(e => e.Item.Bytes);
        int failed = entries.Count(e => e.Outcome is { Succeeded: false });
        string count = entries.All(e => e.Outcome is null) ? $"{Files(entries.Count)} · {Ui.Size(bytes)}"
            : failed == entries.Count ? Files(failed) : string.Join(" · ", OrganizerLabels.CompletedCounts(entries.Select(e => e.Item)));
        var caption = Ui.Row(Ui.Text(count, "CaptionTextStyle", "TextSecondaryBrush"));
        if (folder is { Length: > 0 } && !SameDrive(preview.Folder, folder))
            caption.Children.Add(Ui.Badge("Other drive", BadgeKind.Needs).Margin(8, 0, 0, 0));
        var name = Ui.Text(title, "HeadlineTextStyle");
        if (folder is { Length: > 0 })
            name.ToolTip = folder;
        var expand = Ui.Icon(LucideIcons.ChevronRight, "");
        void Show(bool show)
        {
            foreach (var row in rows)
                row.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            expand.Icon = show ? LucideIcons.ChevronDown : LucideIcons.ChevronRight;
            System.Windows.Automation.AutomationProperties.SetName(expand, show ? "Hide files" : $"Show {Files(rows.Count)}");
        }
        expand.Click += (_, _) => Show(rows[0].Visibility != Visibility.Visible);
        head.Children.Add(Ui.Row(expand.Margin(-6, 0, 6, 0), Ui.Stack(name, caption)).At(1));
        var header = Ui.Pad(head, 52);
        header.Background = Brushes.Transparent; // the whole header takes the click
        header.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && !Ui.IsInside<ButtonBase>(source))
                Show(rows[0].Visibility != Visibility.Visible);
        };
        card.Children.Add(header);
        foreach (var row in rows)
            card.Children.Add(row);
        Show(open);
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private void Sync()
    {
        if (_syncing || _preview is null)
            return;
        _syncing = true;
        var chosen = _preview.Items.Where(i => _checks.TryGetValue(i.Id, out var c) && c.IsChecked == true).ToList();
        _all.IsThreeState = true;
        _all.IsChecked = chosen.Count == 0 ? false : chosen.Count == _checks.Count ? true : null;
        _all.IsEnabled = _checks.Count > 0;
        foreach (var (box, checks) in _groups)
        {
            int on = checks.Count(c => c.IsChecked == true);
            box.IsChecked = on == 0 ? false : on == checks.Count ? true : null;
        }
        if (_checks.Count > 0)
        {
            _summary.Text = $"{chosen.Count} of {_checks.Count} selected · {Ui.Size(chosen.Sum(i => i.Bytes))}";
            _approve.IsEnabled = chosen.Count > 0;
        }
        _syncing = false;
    }

    /// <summary>A fresh card for the rows, headed by the column row (which holds Select all).</summary>
    private Card NewCard()
    {
        if (_columns.Parent is Border old)
            old.Child = null;
        _groups.Clear();
        _first = null;
        var card = Ui.Card(Ui.Pad(_columns, 40));
        _rows.Content = card;
        return card;
    }

    private void AllFlipped()
    {
        if (!_syncing)
            Select(_checks.Values, _all.IsChecked == true);
    }

    /// <summary>A group's box: checked selects its files; a click past checked (mixed) clears them, like Select all.</summary>
    private void GroupFlipped(CheckBox box, IReadOnlyList<CheckBox> checks)
    {
        if (!_syncing)
            Select(checks, box.IsChecked == true);
    }

    private void Select(IEnumerable<CheckBox> checks, bool on)
    {
        _syncing = true;
        foreach (var check in checks)
            check.IsChecked = on;
        _syncing = false;
        Sync();
    }

    /// <summary>The file Review asked for: brought into view once laid out.</summary>
    private void Reveal()
    {
        if (_first is { } row)
            Dispatcher.BeginInvoke(() => row.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnScrolled(object sender, ScrollChangedEventArgs e) => Pin();

    /// <summary>Keeps the bar's bottom inside the window: lifted over the rows while the list runs past the edge.</summary>
    private void Pin()
    {
        if (_scroller is null || !_bar.IsVisible || !_scroller.IsAncestorOf(_bar))
            return;
        double top = _bar.TranslatePoint(default, _scroller).Y - _pin.Y;
        double over = top + _bar.ActualHeight - _scroller.ViewportHeight;
        _pin.Y = over > 0 ? -over : 0;
        if (over > 0)
            _bar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        else
            _bar.BorderBrush = Brushes.Transparent;
    }

    private async Task ApproveAsync()
    {
        if (_preview is null || _module.Engine is not { } engine)
            return;
        var selected = _checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
        _footer.Children.Clear();
        _summary.Text = "Applying…";
        _footer.Children.Add(_summary.At(0));
        _rows.IsEnabled = false;
        _error.Visibility = Visibility.Collapsed;
        try
        {
            var result = await engine.ApproveAsync(_preview.Id, selected);
            if (_released) return;
            _applied = true;
            _rows.IsEnabled = true;
            RenderResult(result);
        }
        catch (ObjectDisposedException)
        {
            RenderFinishActions();
            ShowError("File organizer is off");
        }
        catch (InvalidOperationException ex)
        {
            // Used or expired: say so; Refresh plans afresh.
            ShowError($"Approve failed · {Lower(ex.Message)}");
            RenderFinishActions();
        }
    }

    private void RenderResult(ApplyResult result)
    {
        var preview = _preview!;
        _subtitle.Text = $"{Ui.FolderName(preview.Folder)} · applied {Ui.Time(DateTimeOffset.Now)}";
        var card = NewCard();
        _all.Visibility = Visibility.Hidden;
        var outcomes = result.Items.ToDictionary(r => r.ItemId);
        var entries = preview.Items.Where(i => outcomes.ContainsKey(i.Id)).Select(item =>
        {
            var outcome = outcomes[item.Id];
            return (Item: item with { Destination = outcome.Destination ?? item.Destination }, Outcome: (ItemResult?)outcome);
        }).ToList();
        var failed = entries.Where(e => e.Outcome is { Succeeded: false }).ToList();
        if (failed.Count > 0)
            AddGroup(card, "Failed", null, failed, open: true, look: true);
        bool open = entries.Count <= OpenUpTo;
        foreach (var group in ByDestination(entries.Where(e => e.Outcome is { Succeeded: true }).Select(e => e.Item), i => i.Destination ?? i.Source, i => i.Kind))
        {
            AddGroup(card, Title(preview, group.Key, group.First().Kind), group.Key, [.. group.Select(i => (i, (ItemResult?)outcomes[i.Id]))], open, look: false);
        }
        int held = preview.Items.Count(i => i.Kind == OperationKind.Hold || i.Warnings.Any(w => w.Kind == WarningKind.MisleadingName) && !outcomes.ContainsKey(i.Id));
        RenderFinishActions();
        var summary = Ui.Text(string.Join(" · ", OrganizerLabels.CompletedCounts(entries.Where(e => e.Outcome is { Succeeded: true }).Select(e => e.Item))), "CaptionTextStyle", "TextSecondaryBrush");
        if (result.Failed > 0)
        {
            var count = new System.Windows.Documents.Run($"{(summary.Text.Length == 0 ? "" : " · ")}{result.Failed} failed");
            count.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "WarningTextBrush");
            summary.Inlines.Add(count);
        }
        if (held > 0)
            summary.Inlines.Add($"{(summary.Text.Length == 0 ? "" : " · ")}{held} held");
        _footer.Children.Add(summary.At(0));
    }

    private void RenderFinishActions()
    {
        _footer.Children.Clear();
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

    /// <summary>
    /// One row: before applying a checkbox, after it the outcome glyph and undo. The name centers when it's
    /// alone (36 high) and sits over its line when it has one (52): why it's held, a taken name, a rename's
    /// new name, a failure. In Needs a look, Show in folder opens Explorer at the file.
    /// </summary>
    private Border Row(PreviewItem item, CheckBox? check, ItemResult? outcome, bool look)
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
        string displayName = outcome is { Succeeded: true, Destination: { } destination } ? Path.GetFileName(destination) : item.Name;
        var name = Ui.Text(displayName, "TitleTextStyle", noMatch ? "TextTertiaryBrush" : null);
        name.ToolTip = outcome is { Succeeded: true } ? item.Destination : item.Source;
        var nameStack = Ui.Stack(name);
        nameStack.VerticalAlignment = VerticalAlignment.Center;
        if (outcome is { Succeeded: false })
        {
            string verb = item.IsDirectImport ? "Import" : item.Kind == OperationKind.Rename ? "Rename" : "Move";
            nameStack.Children.Add(Ui.Warning($"{verb} failed · {Lower(outcome.Error)}"));
        }
        else if (outcome is null && Note(item) is { } note)
            nameStack.Children.Add(WarningLine(note));
        else if (item.Kind == OperationKind.Rename && item.Destination is { } renamed && outcome is null)
            nameStack.Children.Add(Ui.Text($"→ {Path.GetFileName(renamed)}", "CaptionTextStyle", "TextSecondaryBrush"));
        else if (outcome is { Succeeded: true } && displayName != item.Name)
            nameStack.Children.Add(Ui.Text($"From {item.Name}", "CaptionTextStyle", "TextSecondaryBrush"));
        var size = Right(Ui.Number(Ui.Size(item.Bytes), "TextTertiaryBrush"));
        size.VerticalAlignment = VerticalAlignment.Center;
        string ruleName = noMatch ? "No match" : item.RuleName ?? (preview.Archive is not null && item.RuleId is null ? "Import" : "Safety check");
        // Needs a look has no destination header, so a file there that would move says where: "Rule → Installers".
        string? to = look && item.Destination is { } target
            ? item.Kind == OperationKind.Rename ? Path.GetFileName(target) : Title(preview, Path.GetDirectoryName(target) ?? "", item.Kind)
            : null;
        var rule = Ui.Text(to is null ? ruleName : $"{ruleName} → {to}", "CaptionTextStyle", "TextSecondaryBrush").Margin(14, 0, 8, 0);
        rule.VerticalAlignment = VerticalAlignment.Center;
        rule.TextTrimming = TextTrimming.CharacterEllipsis;
        rule.ToolTip = to is null ? item.RuleName : item.Destination;
        var grid = Columns();
        grid.Children.Add(lead.At(0));
        grid.Children.Add(nameStack.At(1));
        grid.Children.Add(size.At(2));
        grid.Children.Add(rule.At(3));
        if (outcome is { Succeeded: true, OperationId: { } operation } && !item.IsDirectImport)
        {
            var undo = Ui.Icon(LucideIcons.Undo, $"Undo {displayName}");
            undo.Click += async (_, _) => await UndoAsync(undo, operation);
            grid.Children.Add(undo.At(4));
        }
        else if (outcome is null && look)
        {
            var show = Ui.Icon(LucideIcons.Folder, $"Show {item.Name} in folder");
            show.ToolTip = "Show in folder";
            show.Click += (_, _) => ShowInFolder(item.Source);
            grid.Children.Add(show.At(4));
        }
        var row = Ui.Pad(grid, nameStack.Children.Count > 1 ? 52 : 36);
        if (noMatch)
            row.Opacity = 0.75;
        return row;
    }

    private void ShowInFolder(string path)
    {
        if (!FileLocation.ShowInFolder(path))
            ShowError("Show in folder failed");
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
            ShowError($"Undo failed · {Lower(result.Error)}");
        }
    }

    /// <summary>Moves or renames by a rule with nothing to look at: a taken name is fine, a warning or a misleading name isn't.</summary>
    private static bool Clean(PreviewItem item) => item.Selected && item.Destination is not null && item.Warnings.Count == 0;

    private static bool Actionable(PreviewItem item) => item.Kind is OperationKind.Move or OperationKind.Rename && item.Destination is not null;

    /// <summary>The amber line under a name: why it is held, or that its name was taken.</summary>
    private static string? Note(PreviewItem item)
    {
        if (item.Warnings.FirstOrDefault(w => w.Kind == WarningKind.MisleadingName) is { } disguise)
            return $"Held · {Lower(disguise.Detail)}";
        if (item.Kind == OperationKind.Hold)
        {
            if (item.Warnings.Any(w => w.Kind == WarningKind.IgnoreMatch)) return "Held · in ignore file";
            string reason = OrganizerLabels.HoldReason(item.HoldReason ?? item.Warnings.LastOrDefault()?.Detail);
            return reason.Length == 0 ? "Held" : $"Held · {Lower(reason)}";
        }
        if (item.Warnings.LastOrDefault() is { } warning)
            return warning.Detail;
        if (item.Kind == OperationKind.Move && item.Destination is { } dest && !string.Equals(Path.GetFileName(dest), item.Name, StringComparison.OrdinalIgnoreCase))
            return $"Name taken · saves as {Path.GetFileName(dest)}";
        return null;
    }

    private static StackPanel WarningLine(string text)
    {
        var line = Ui.Row(Ui.Glyph(LucideIcons.Info, "WarningBrush", 12), Ui.Text(text, "CaptionTextStyle", "WarningTextBrush").Margin(5, 0, 0, 0));
        line.Margin = new Thickness(0, 1, 0, 0);
        return line;
    }

    private static string Lower(string? text) => string.IsNullOrEmpty(text) ? "not moved" : char.ToLowerInvariant(text[0]) + text[1..].TrimEnd('.');

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static Grid Columns() => Ui.Columns("34", "3*", "84", "1.6*", "34");

    private static FrameworkElement Right(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Right;
        return element;
    }

    private static T? Ancestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(from); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found)
                return found;
        return null;
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
