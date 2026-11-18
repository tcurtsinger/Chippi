// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chippi.UI.Controls;
using static Chippi.Features.Uninstaller.Views.UninstallerUi;

namespace Chippi.Features.Uninstaller.Views;

public enum LeftoverStep { Scanning, Review, Empty, Removing, Results }

/// <summary>
/// The leftover review (docs/phase-7/design/uninstaller.html, settings-scan to settings-results): the read-only scan, the grouped list with ticks, the
/// question, the removal and its results. Nothing is removed until the verb of the question is pressed, and only what is ticked.
/// </summary>
internal sealed class LeftoverPage : Grid, IDisposable
{
    private static readonly GridLength[] Widths = [new(20), new(20), new(1, GridUnitType.Star), new(76), new(24)];
    private readonly UninstallerEngine _engine;
    private readonly AppRecord _record;
    private readonly Action<bool> _back;
    private readonly Action<string> _openFolder;
    private readonly Border _title = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Border _body = new();
    private readonly Border _barHost = new() { Height = 60, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(-40, 0, -40, -28), Padding = new Thickness(40, 0, 40, 0) };
    private readonly Canvas _overlay = new();
    private readonly HashSet<string> _ticked = [];
    private readonly HashSet<string> _folded = [];
    private readonly Dictionary<string, LeftoverResult> _done = [];
    private readonly CancellationTokenSource _cts = new();
    private LeftoverScan? _scan;
    private LeftoverRun? _run;
    private FrameworkElement? _popover;
    private string? _focusKey;
    private bool _disposed, _changed;

    public LeftoverPage(UninstallerEngine engine, AppRecord record, Action<bool> back, Action<string> openFolder, LeftoverScan? scanned = null)
    {
        _engine = engine;
        _record = record;
        _back = back;
        _openFolder = openFolder;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        Add(this, _title, 0, 0);
        Add(this, _body, 0, 1);
        _barHost.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        _barHost.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        Add(this, _barHost, 0, 2);
        Grid.SetRowSpan(_overlay, 3);
        Children.Add(_overlay);
        PreviewMouseDown += (_, e) => { if (_popover is not null && e.OriginalSource is DependencyObject s && !Inside(s, _popover)) ClosePopover(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _popover is not null) { ClosePopover(); e.Handled = true; } };
        Step = LeftoverStep.Scanning;
        if (scanned is not null) Adopt(scanned); else Render();
        Loaded += (_, _) => { if (_scan is null && Step == LeftoverStep.Scanning && !_started) _ = ScanAsync(); };
    }

    private bool _started;

    internal LeftoverStep Step { get; private set; }
    internal LeftoverScan? Scan => _scan;
    internal LeftoverRun? Run => _run;
    internal IReadOnlyCollection<string> Ticked => _ticked;
    internal bool AsksQuestion => _popover is not null;
    internal FrameworkElement? PopoverElement => _popover;
    internal string? FocusedKeyOverride { get; set; }
    internal Button? FocusTarget => _focusKey is not null ? Walk<Button>(this).FirstOrDefault(b => b.Tag as string == _focusKey) : null;
    internal FrameworkElement Bar => _barHost;
    internal bool ChangedAnything => _changed;

    /// <summary>The scan, off the UI thread. Null (the entry is still there, or a restart is pending) goes back to the list.</summary>
    internal async Task ScanAsync()
    {
        if (_started) return;
        _started = true;
        LeftoverScan? scan;
        try { scan = await _engine.ScanAsync(_record, _cts.Token); }
        catch (OperationCanceledException) { return; }
        if (_disposed) return;
        if (scan is null) { _back(false); return; }
        Adopt(scan);
        if (Step == LeftoverStep.Review) FocusLater(Walk<CheckBox>(_body).FirstOrDefault() as UIElement ?? this);
    }

    /// <summary>Shows a finished scan: Safe items ticked, nothing else. (The scan itself is <see cref="ScanAsync"/>; the shots hand one in.)</summary>
    internal void Adopt(LeftoverScan scan)
    {
        _started = true;
        _scan = scan;
        _ticked.Clear();
        foreach (var item in scan.Items.Where(i => i.TickedByDefault)) _ticked.Add(item.Id);
        Step = scan.Items.Count == 0 ? LeftoverStep.Empty : LeftoverStep.Review;
        Render();
    }

    /// <summary>Folds a group shut or open (the shots and tests).</summary>
    internal void Fold(string group)
    {
        if (!_folded.Remove(group)) _folded.Add(group);
        Render();
    }

    /// <summary>The shots' stand-in for a removal under way.</summary>
    internal void PreviewRemoving(IReadOnlyList<LeftoverItem> removing, IReadOnlyList<LeftoverResult> done)
    {
        _removing = removing;
        _done.Clear();
        foreach (var r in done) _done[r.Item.Id] = r;
        Step = LeftoverStep.Removing;
        Render();
    }

    /// <summary>The shots' stand-in for a finished removal.</summary>
    internal void PreviewResults(LeftoverRun run)
    {
        _run = run;
        Step = LeftoverStep.Results;
        Render();
    }

    // ---- drawing ----

    internal void Redraw() => Render();

    private void Render()
    {
        if (_disposed) return;
        string? key = FocusedKeyOverride ?? ((IsKeyboardFocusWithin && Keyboard.FocusedElement is FrameworkElement { Tag: string tag } ? tag : null));
        BuildTitle();
        switch (Step)
        {
            case LeftoverStep.Scanning: _body.Child = ScanningCard(); _barHost.Child = BarButtons(null, Verb("Cancel", Cancel)); break;
            case LeftoverStep.Empty: _body.Child = Wrap(EmptyRow()); _barHost.Child = BarButtons(null, Verb("Done", Done, VerbKind.Accent)); break;
            case LeftoverStep.Review: _body.Child = Wrap(ReviewRows()); _barHost.Child = ReviewBar(); break;
            case LeftoverStep.Removing: _body.Child = Wrap(RemovingRows()); _barHost.Child = RemovingBar(); break;
            default: _body.Child = Wrap(ResultRows()); _barHost.Child = ResultsBar(); break;
        }
        _focusKey = key;
        if (key is not null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (!_disposed && IsLoaded && FocusTarget is { IsVisible: true, IsEnabled: true } t && !t.IsKeyboardFocusWithin) t.Focus();
        });
    }

    private void BuildTitle()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var back = Icon(LucideIcons.ArrowLeft, "Back", () => { if (Step != LeftoverStep.Removing) Done(); }, 18, 32);
        back.Margin = new Thickness(-8, 0, 4, 0);
        back.Tag = "back";
        back.IsEnabled = Step != LeftoverStep.Removing;
        row.Children.Add(back);
        var text = new TextBlock { Text = _record.Name + " leftovers", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        row.Children.Add(text);
        _title.Child = row;
    }

    private static FrameworkElement Wrap(UIElement rows)
    {
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = rows, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var scroll = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        return scroll;
    }

    private UIElement ScanningCard()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 28) };
        var mark = Glyph(UninstallerIcons.ScanSearch, 24);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(mark);
        var label = Text("Scanning", 14);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 8, 0, 8);
        stack.Children.Add(label);
        var track = new Border { Width = 200, Height = 3, CornerRadius = new CornerRadius(2), ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
        var fill = new Border { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), RenderTransform = new TranslateTransform() };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        fill.Loaded += (_, _) => ((TranslateTransform)fill.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-80, 200, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
        fill.Unloaded += (_, _) => ((TranslateTransform)fill.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
        track.Child = fill;
        stack.Children.Add(track);
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack, VerticalAlignment = VerticalAlignment.Top };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    private static UIElement EmptyRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 22, 16, 22) };
        var mark = Glyph(LucideIcons.CircleCheck, 20, "SuccessBrush");
        mark.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(mark);
        row.Children.Add(Text("No leftovers", 14, "TextSecondaryBrush"));
        return row;
    }

    // ---- the review ----

    private static readonly (LeftoverClass Class, string Title)[] Groups = [(LeftoverClass.Safe, "Safe"), (LeftoverClass.CheckFirst, "Check first"), (LeftoverClass.Shared, "Shared")];

    private UIElement ReviewRows()
    {
        var stack = new StackPanel();
        var scan = _scan!;
        if (!scan.HasRecord)
        {
            var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 14) };
            var mark = Glyph(LucideIcons.CircleAlert, 20, "WarningTextBrush");
            mark.Margin = new Thickness(0, 0, 12, 0);
            note.Children.Add(mark);
            note.Children.Add(Text("No uninstall record", 14));
            stack.Children.Add(note);
        }
        stack.Children.Add(ColumnHeader());
        foreach (var (cls, title) in Groups)
        {
            var items = scan.Items.Where(i => i.Class == cls).ToList();
            if (items.Count == 0) continue;
            stack.Children.Add(GroupHeader(title, items.Count.ToString(), null, cls, items, tick: cls != LeftoverClass.Shared));
            if (!_folded.Contains(title)) foreach (var item in items) stack.Children.Add(ItemRow(item));
        }
        return stack;
    }

    private UIElement ColumnHeader()
    {
        var grid = Columns(Widths, 10);
        grid.Height = 36;
        grid.Margin = new Thickness(16, 0, 16, 0);
        var removable = _scan!.Removable;
        bool? all = removable.Count == 0 || _ticked.Count == 0 ? false : removable.All(i => _ticked.Contains(i.Id)) ? true : null;
        var check = Check(all, "Select all", removable.Count > 0);
        check.IsThreeState = true;
        check.Tag = "check:all";
        check.Click += (_, _) =>
        {
            bool on = !removable.All(i => _ticked.Contains(i.Id));
            foreach (var item in removable) { if (on) _ticked.Add(item.Id); else _ticked.Remove(item.Id); }
            Render();
        };
        Add(grid, check, At(0));
        Add(grid, Text("Item", 12, "TextTertiaryBrush"), At(2));
        var size = Text("Size", 12, "TextTertiaryBrush");
        size.HorizontalAlignment = HorizontalAlignment.Right;
        Add(grid, size, At(3));
        return grid;
    }

    private UIElement GroupHeader(string title, string count, TagKind? tag, LeftoverClass? cls, IReadOnlyList<LeftoverItem>? items, bool tick)
    {
        var grid = new Grid { Height = 44, Margin = new Thickness(16, 0, 16, 0), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (tick && items is not null)
        {
            var ticked = items.Count(i => _ticked.Contains(i.Id));
            bool? state = ticked == 0 ? false : ticked == items.Count ? true : null;
            var check = Check(state, "Select " + title);
            check.IsThreeState = true;
            check.Tag = "group:" + title;
            check.Click += (_, _) =>
            {
                bool on = items.Any(i => !_ticked.Contains(i.Id));
                foreach (var item in items) { if (on) _ticked.Add(item.Id); else _ticked.Remove(item.Id); }
                Render();
            };
            Add(grid, check, 0);
        }
        bool open = !_folded.Contains(title);
        Add(grid, Glyph(open ? LucideIcons.ChevronDown : LucideIcons.ChevronRight, 14, "TextTertiaryBrush"), 2);
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Text(title, 14);
        name.FontWeight = FontWeights.SemiBold;
        line.Children.Add(name);
        if (tag is { } kind) { var badge = UninstallerUi.Tag(count, kind); badge.Margin = new Thickness(8, 0, 0, 0); line.Children.Add(badge); }
        else { var small = Text(count, 12, "TextTertiaryBrush"); small.Margin = new Thickness(8, 0, 0, 0); line.Children.Add(small); }
        Add(grid, line, 4);
        grid.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject s && Inside(s, grid) && FindParent<CheckBox>(s) is null)
            {
                if (!_folded.Remove(title)) _folded.Add(title);
                Render();
            }
        };
        var top = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        top.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        return top;
    }

    private static T? FindParent<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? current = element; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is T found) return found;
        return null;
    }

    private static Geometry KindGlyph(LeftoverKind kind) => kind switch { LeftoverKind.Folder => LucideIcons.Folder, LeftoverKind.Key => UninstallerIcons.KeyRound, _ => LucideIcons.File };

    private UIElement ItemRow(LeftoverItem item)
    {
        var grid = Columns(Widths, 10);
        grid.Margin = new Thickness(16, 4, 16, 4);
        grid.MinHeight = 44;
        if (item.CanTick)
        {
            var check = Check(_ticked.Contains(item.Id), "Select " + item.Path);
            check.Tag = "item:" + item.Id;
            check.Click += (_, _) => { if (check.IsChecked == true) _ticked.Add(item.Id); else _ticked.Remove(item.Id); Render(); };
            Add(grid, check, At(0));
        }
        var kind = Glyph(KindGlyph(item.Kind), 16);
        kind.HorizontalAlignment = HorizontalAlignment.Center;
        Add(grid, kind, At(1));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var path = Text(item.Path, 13);
        path.ToolTip = item.Path;
        text.Children.Add(path);
        if (item.Class == LeftoverClass.Shared && item.SharedWith is { Length: > 0 } with)
        {
            var why = new StackPanel { Orientation = Orientation.Horizontal };
            var lockGlyph = Glyph(LucideIcons.Lock, 12, "TextTertiaryBrush");
            lockGlyph.Margin = new Thickness(0, 0, 6, 0);
            why.Children.Add(lockGlyph);
            why.Children.Add(Text(with, 12, "TextTertiaryBrush"));
            text.Children.Add(why);
        }
        else if (item.NoRecycleBin)
        {
            var badge = UninstallerUi.Tag("No Recycle Bin", TagKind.Caution);
            badge.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(badge);
        }
        Add(grid, text, At(2));
        var size = Text(UninstallerUi.Size(item.Bytes), 12, "TextSecondaryBrush");
        size.HorizontalAlignment = HorizontalAlignment.Right;
        Add(grid, size, At(3));
        Add(grid, ShieldCell(item.NeedsAdmin && item.CanTick, 16), At(4));
        var row = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid, Opacity = item.Class == LeftoverClass.Shared ? .62 : 1 };
        row.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        AutomationProperties.SetName(row, item.Path);
        return row;
    }

    private IReadOnlyList<LeftoverItem> TickedItems() => [.. _scan!.Items.Where(i => i.CanTick && _ticked.Contains(i.Id))];

    private FrameworkElement ReviewBar()
    {
        var items = TickedItems();
        var cancel = Verb("Cancel", Cancel);
        cancel.Tag = "bar:cancel";
        var remove = Verb("Remove", Ask, VerbKind.Accent, shield: items.Any(i => i.NeedsAdmin));
        remove.Tag = "bar:remove";
        remove.IsEnabled = items.Count > 0;
        return BarButtons(BarText(items.Count + " ticked", items.Count > 0 ? UninstallerUi.Size(items.Sum(i => i.Bytes ?? 0)) : null), cancel, remove);
    }

    private static FrameworkElement BarText(string text, string? small)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Text(text, 14));
        if (!string.IsNullOrEmpty(small)) { var d = Text(small, 12, "TextTertiaryBrush"); d.Margin = new Thickness(6, 0, 0, 0); line.Children.Add(d); }
        return line;
    }

    private static FrameworkElement BarButtons(FrameworkElement? left, params Button[] buttons)
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (left is not null) Add(bar, left, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; i++) { if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0); row.Children.Add(buttons[i]); }
        Add(bar, row, 1);
        return bar;
    }

    // ---- the question ----

    internal void Ask()
    {
        var items = TickedItems();
        if (Step != LeftoverStep.Review || items.Count == 0) return;
        ClosePopover();
        var title = new TextBlock { Text = UninstallerText.RemoveQuestion(items.Count), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var cancel = Verb("Cancel", ClosePopover, small: true);
        cancel.Tag = "popover:cancel";
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var confirm = Verb("Remove", () => _ = RemoveAsync(), VerbKind.Critical, small: true, shield: items.Any(i => i.NeedsAdmin));
        confirm.Tag = "popover:confirm";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, confirm } };
        var box = UninstallerUi.Popover(new StackPanel { Children = { title, buttons } }, 300);
        _overlay.Children.Add(box);
        _popover = box;
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pageWidth = ActualWidth > 0 ? ActualWidth : 768, pageHeight = ActualHeight > 0 ? ActualHeight : 600;
        Canvas.SetLeft(box, pageWidth - box.DesiredSize.Width);
        Canvas.SetTop(box, Math.Max(0, pageHeight - 60 - box.DesiredSize.Height - 8));
        FocusLater(cancel);
        _focusKey = "popover:cancel";
    }

    internal void ClosePopover()
    {
        if (_popover is null) return;
        _overlay.Children.Remove(_popover);
        _popover = null;
        if (_focusKey == "popover:cancel") _focusKey = null;
    }

    private static bool Inside(DependencyObject element, DependencyObject container)
    {
        for (DependencyObject? current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current == container) return true;
        return false;
    }

    // ---- the removal ----

    /// <summary>The confirmed question: removes what is ticked. Nothing is removed before this.</summary>
    internal async Task RemoveAsync(IReadOnlyCollection<string>? only = null)
    {
        if (_scan is null || Step is LeftoverStep.Removing) return;
        ClosePopover();
        var ids = only ?? [.. TickedItems().Select(i => i.Id)];
        if (ids.Count == 0) return;
        _removing = [.. _scan.Items.Where(i => ids.Contains(i.Id))];
        _done.Clear();
        Step = LeftoverStep.Removing;
        Render();
        var progress = new Progress<LeftoverResult>(r => { _done[r.Item.Id] = r; if (Step == LeftoverStep.Removing) Render(); });
        try { _run = await _engine.RemoveAsync(_scan, ids, progress, CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _run = new LeftoverRun([.. _removing.Select(i => new LeftoverResult(i, LeftoverState.Failed))], null);
        }
        _changed = true;
        Step = LeftoverStep.Results;
        if (!_disposed) Render();
    }

    private IReadOnlyList<LeftoverItem> _removing = [];

    private UIElement RemovingRows()
    {
        var stack = new StackPanel();
        stack.Children.Add(ColumnHeader());
        var scan = _scan!;
        int total = _removing.Count;
        var firstPending = _removing.FirstOrDefault(i => !_done.ContainsKey(i.Id));
        foreach (var (cls, title) in Groups)
        {
            var items = scan.Items.Where(i => i.Class == cls && _removing.Contains(i)).ToList();
            if (items.Count == 0) continue;
            stack.Children.Add(GroupHeader(title, "Removing " + _done.Count + " of " + total, null, cls, null, tick: false));
            foreach (var item in items)
            {
                var grid = Columns(Widths, 10);
                grid.Margin = new Thickness(16, 4, 16, 4);
                grid.MinHeight = 44;
                bool done = _done.TryGetValue(item.Id, out var result);
                FrameworkElement lead;
                if (done) lead = Glyph(result!.Ok ? LucideIcons.CircleCheck : LucideIcons.CircleAlert, 16, result.Ok ? "SuccessBrush" : "WarningTextBrush");
                else if (item == firstPending) lead = Spinner(16);
                else lead = Check(true, "Waiting " + item.Path, false);
                Add(grid, lead, At(0));
                var kind = Glyph(KindGlyph(item.Kind), 16);
                kind.HorizontalAlignment = HorizontalAlignment.Center;
                Add(grid, kind, At(1));
                Add(grid, Text(item.Path, 13), At(2));
                var size = Text(UninstallerUi.Size(item.Bytes), 12, "TextSecondaryBrush");
                size.HorizontalAlignment = HorizontalAlignment.Right;
                Add(grid, size, At(3));
                Add(grid, ShieldCell(!done && item.NeedsAdmin, 16), At(4));
                var row = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid, Opacity = !done && item != firstPending ? .6 : 1 };
                row.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
                stack.Children.Add(row);
            }
        }
        return stack;
    }

    private FrameworkElement RemovingBar()
    {
        int total = Math.Max(1, _removing.Count);
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Text("Removing " + Math.Min(total, _done.Count + 1) + " of " + total, 14));
        var track = new Border { Width = 200, Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
        var fill = new Border { Width = 200.0 * _done.Count / total, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        track.Child = fill;
        line.Children.Add(track);
        return line;
    }

    // ---- the results ----

    private UIElement ResultRows()
    {
        var run = _run!;
        var stack = new StackPanel();
        var bad = run.Results.Where(r => !r.Ok).ToList();
        var good = run.Results.Where(r => r.Ok).ToList();
        var needsAdmin = bad.Where(r => r.State == LeftoverState.NeedsAdmin).ToList();
        bool declined = needsAdmin.Count > 0 && needsAdmin.Count == bad.Count;
        var summary = new Grid { Margin = new Thickness(16, 6, 16, 6), MinHeight = 52 };
        summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        summary.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var mark = Glyph(declined || bad.Count > 0 && good.Count == 0 ? LucideIcons.CircleAlert : LucideIcons.CircleCheck, 20, declined || good.Count == 0 && bad.Count > 0 ? "WarningTextBrush" : "SuccessBrush");
        mark.Margin = new Thickness(0, 0, 12, 0);
        Add(summary, mark, 0);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (declined)
        {
            lines.Children.Add(Text("Needs admin", 14));
            lines.Children.Add(Text(bad.Count + " not removed", 12, "TextTertiaryBrush"));
        }
        else
        {
            lines.Children.Add(Text(good.Count + " removed", 14));
            if (bad.Count > 0) lines.Children.Add(Text(bad.Count + " not removed", 12, "TextTertiaryBrush"));
        }
        Add(summary, lines, 1);
        if (declined)
        {
            var retry = Verb("Remove", () => _ = RemoveAsync([.. needsAdmin.Select(r => r.Item.Id)]), small: true, shield: true);
            retry.Tag = "retry";
            Add(summary, retry, 2);
        }
        stack.Children.Add(summary);
        if (run.BackupFolder is { Length: > 0 } folder)
        {
            var backup = new Grid { Margin = new Thickness(16, 6, 16, 6), MinHeight = 52 };
            backup.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            backup.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            backup.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var file = Glyph(LucideIcons.File, 20);
            file.Margin = new Thickness(0, 0, 12, 0);
            Add(backup, file, 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Text("Registry backup", 14));
            var path = Text(folder, 12, "TextTertiaryBrush");
            path.ToolTip = folder;
            text.Children.Add(path);
            Add(backup, text, 1);
            var open = new Button { VerticalAlignment = VerticalAlignment.Center, Tag = "open-backup" };
            open.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            open.Height = 28;
            open.Padding = new Thickness(10, 0, 10, 0);
            open.FontSize = 13;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Glyph(UninstallerIcons.FolderOpen, 14, "TextPrimaryBrush");
            glyph.Margin = new Thickness(0, 0, 6, 0);
            content.Children.Add(glyph);
            content.Children.Add(new TextBlock { Text = "Open folder", VerticalAlignment = VerticalAlignment.Center });
            open.Content = content;
            AutomationProperties.SetName(open, "Open folder");
            open.Click += (_, _) => _openFolder(folder);
            Add(backup, open, 2);
            var host = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = backup };
            host.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
            stack.Children.Add(host);
        }
        if (bad.Count > 0)
        {
            stack.Children.Add(GroupHeader("Not removed", bad.Count.ToString(), TagKind.Caution, null, null, tick: false));
            if (!_folded.Contains("Not removed")) foreach (var r in bad) stack.Children.Add(ResultRow(r));
        }
        if (good.Count > 0)
        {
            if (!_foldedInit) { _folded.Add("Removed"); _foldedInit = true; }
            stack.Children.Add(GroupHeader("Removed", good.Count.ToString(), TagKind.Good, null, null, tick: false));
            if (!_folded.Contains("Removed")) foreach (var r in good) stack.Children.Add(ResultRow(r));
        }
        return stack;
    }

    private bool _foldedInit;

    private UIElement ResultRow(LeftoverResult result)
    {
        var item = result.Item;
        var grid = new Grid { Margin = new Thickness(16, 4, 16, 4), MinHeight = 44 };
        foreach (var w in new GridLength[] { new(20), new(10), new(20), new(10), new(1, GridUnitType.Star), new(10), new(76), new(10), new(24) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        Add(grid, result.Ok ? Glyph(LucideIcons.CircleCheck, 16, "SuccessBrush") : Glyph(LucideIcons.CircleAlert, 16, "WarningTextBrush"), 0);
        var kind = Glyph(KindGlyph(item.Kind), 16);
        kind.HorizontalAlignment = HorizontalAlignment.Center;
        Add(grid, kind, 2);
        var path = Text(item.Path, 13);
        path.ToolTip = item.Path;
        Add(grid, path, 4);
        if (result.Ok)
        {
            var size = Text(UninstallerUi.Size(item.Bytes), 12, "TextSecondaryBrush");
            size.HorizontalAlignment = HorizontalAlignment.Right;
            Add(grid, size, 6);
        }
        else
        {
            var status = Text(StatusWord(result), 12, "WarningTextBrush");
            status.HorizontalAlignment = HorizontalAlignment.Right;
            status.ToolTip = result.Text;
            Grid.SetColumnSpan(status, 3);
            Add(grid, status, 6);
        }
        var row = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        row.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        return row;
    }

    /// <summary>A status is one or two words: "Kept" for every reason (the reason is the tooltip), "In use", "Remove failed", "Needs admin".</summary>
    private static string StatusWord(LeftoverResult result) => result.State switch
    {
        LeftoverState.Kept => "Kept",
        LeftoverState.InUse => "In use",
        LeftoverState.NeedsAdmin => "Needs admin",
        LeftoverState.Cancelled => "Cancelled",
        _ => "Remove failed",
    };

    private FrameworkElement ResultsBar() => BarButtons(null, Verb("Done", Done, VerbKind.Accent));

    // ---- leaving ----

    private void Cancel() { if (Step != LeftoverStep.Removing) Done(); }

    private void Done()
    {
        ClosePopover();
        _cts.Cancel();
        _back(_changed);
    }

    private static IEnumerable<T> Walk<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Walk<T>(child)) yield return nested;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
