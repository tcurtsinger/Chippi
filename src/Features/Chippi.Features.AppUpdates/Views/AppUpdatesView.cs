// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using static Chippi.Features.AppUpdates.Views.AppUpdatesUi;

namespace Chippi.Features.AppUpdates.Views;

/// <summary>The compact update list and its exact-batch confirmation. No installer starts until Update is pressed.</summary>
public sealed class AppUpdatesView : ContentControl, IDisposable
{
    private readonly AppUpdatesEngine _engine;
    private readonly ModuleContext _context;
    private readonly Func<UpdateBatch, bool, Task> _execute;
    private readonly Func<PackageUpdate, bool> _isRunning;
    private readonly bool _loadIcons;
    private readonly Grid _header = new(), _body = new(), _listBody = new(), _rows = new();
    private readonly TextBlock _summary = Text("", 12, "IslandTextTertiaryBrush"), _range = Text("", 11, "IslandTextTertiaryBrush");
    private readonly TextBlock _batchStatus = Text("Batch in progress", 11, "IslandTextTertiaryBrush");
    private readonly StackPanel _headerButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button _check, _all, _previous, _next, _skipped;
    private readonly List<RowParts> _shown = [];
    private readonly TextBlock _failure = Text("", 11, "WarningTextBrush");
    private UpdateBatch? _confirmation;
    private bool _closeRunning, _following, _disposed, _checkingFromLoad, _submitting;
    private int _page, _confirmPage;
    private string? _structure;
    private Border? _menu;
    private string? _localError;
    private Button? _confirmButton;

    private sealed record RowParts(string Key, PackageUpdate Package, TextBlock Detail, TextBlock Status, StackPanel StatusHost, LucideIcon Check,
        Button Update, Button More, Border Track, Border Fill);

    public AppUpdatesView(AppUpdatesEngine engine, ModuleContext context, Func<UpdateBatch, bool, Task> executeBatch,
        Func<PackageUpdate, bool>? isRunning = null, bool loadIcons = true)
    {
        _engine = engine; _context = context; _execute = executeBatch;
        _isRunning = isRunning ?? (_ => false); _loadIcons = loadIcons;
        _header.ColumnDefinitions.Add(new()); _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Text("Update apps", 13); title.FontWeight = FontWeights.SemiBold; _header.Children.Add(title);
        _check = Icon(LucideIcons.RefreshCw, "Check again", () => _ = CheckAsync());
        _all = Button("Update all", () => ShowConfirmation(_engine.CaptureBatch()), true); _all.Margin = new(4, 0, 0, 0);
        _batchStatus.Visibility = Visibility.Collapsed;
        _headerButtons.Children.Add(_check); _headerButtons.Children.Add(_all); _headerButtons.Children.Add(_batchStatus); Add(_header, _headerButtons, column: 1);
        _listBody.RowDefinitions.Add(new() { Height = new(28) });
        _listBody.RowDefinitions.Add(new());
        _listBody.RowDefinitions.Add(new() { Height = new(28) });
        _summary.ToolTip = ""; Add(_listBody, _summary);
        _rows.VerticalAlignment = VerticalAlignment.Top; Add(_listBody, _rows, 1);
        var footer = new Grid(); footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _skipped = Button("Skipped", () => _context.OpenSettings("skipped"));
        _skipped.MinWidth = 0; _skipped.Padding = new(0); _skipped.FontSize = 11; _skipped.Height = 24;
        _skipped.BorderBrush = Brushes.Transparent; _skipped.Background = Brushes.Transparent;
        _skipped.SetResourceReference(ForegroundProperty, "IslandAccentBrush"); _skipped.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Children.Add(_skipped);
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _previous = Icon(LucideIcons.ChevronLeft, "Previous apps", () => TurnPage(-1));
        _next = Icon(LucideIcons.ChevronRight, "Next apps", () => TurnPage(1));
        _range.Margin = new(4, 0, 4, 0); footerButtons.Children.Add(_range); footerButtons.Children.Add(_previous); footerButtons.Children.Add(_next);
        footerButtons.Children.Add(Icon(LucideIcons.Settings, "App updates settings", () => _context.OpenSettings(null)));
        Add(footer, footerButtons, column: 1); Add(_listBody, footer, 2);
        _body.Children.Add(_listBody); Content = _body;
        AutomationProperties.SetLiveSetting(_summary, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_failure, AutomationLiveSetting.Polite);
        Loaded += OnLoaded; Unloaded += OnUnloaded;
        PreviewKeyDown += OnKey;
        _body.PreviewMouseDown += (_, e) =>
        {
            if (_menu is not null && e.OriginalSource is DependencyObject target && !Inside(target, _menu)) CloseMenu();
        };
        Refresh();
    }

    public FrameworkElement Header => _header;
    public UIElement InitialFocus => _confirmation is not null ? _confirmButton ?? _check : _all.IsEnabled ? _all : _check;
    internal int Page => _page;
    internal UpdateBatch? Confirmation => _confirmation;
    internal IReadOnlyList<string> VisibleKeys => [.. _shown.Select(r => r.Key)];
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;
    internal Func<UIElement?> CurrentFocus { get; set; } = () => Keyboard.FocusedElement as UIElement;
    internal Action<UIElement> SetFocus { get; set; } = element => element.Focus();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true); Refresh();
        if (_checkingFromLoad || _disposed) return;
        _checkingFromLoad = true;
        try { await CheckAsync(); }
        finally { _checkingFromLoad = false; }
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) { Follow(false); CloseMenu(); }
    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) _engine.Changed += Refresh; else _engine.Changed -= Refresh;
        _following = enabled;
    }
    private async Task CheckAsync()
    {
        if (_disposed || _engine.IsRunning || _submitting) return;
        _localError = null;
        try { await _engine.CheckAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _localError = "Check failed · " + ex.Message; }
        Refresh();
    }

    public void ShowConfirmation(UpdateBatch batch, bool closeRunning = false)
    {
        if (_disposed || _engine.IsRunning || _submitting || batch.Packages.Count == 0) return;
        // The review owns its array while discovery can continue to refresh the list behind it.
        _confirmation = new UpdateBatch(batch.Packages.ToArray()); _closeRunning = closeRunning;
        _confirmPage = 0; _localError = null; CloseMenu(); RenderConfirmation();
    }
    public bool Back()
    {
        if (_menu is not null) { CloseMenu(); return true; }
        if (_confirmation is null) return false;
        _confirmation = null; _confirmButton = null; _structure = null;
        _body.Children.Clear(); _body.Children.Add(_listBody); Refresh(); return true;
    }

    private void Refresh()
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _check.IsEnabled = !_engine.IsChecking && !_engine.IsRunning && !_submitting;
        _all.IsEnabled = !_engine.IsRunning && !_submitting && _engine.Updates.Count > 0;
        _all.Visibility = _engine.IsRunning || _engine.Updates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _check.Visibility = _engine.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        _batchStatus.Visibility = _engine.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        _headerButtons.Visibility = _confirmation is null ? Visibility.Visible : Visibility.Collapsed;
        if (_confirmation is not null) return;
        IReadOnlyList<UpdateRow> rows = _engine.Rows;
        int pages = Math.Max(1, (rows.Count + 4) / 5); _page = Math.Clamp(_page, 0, pages - 1);
        string structure = _page + ":" + string.Join("|", rows.Skip(_page * 5).Take(5).Select(r => $"{r.Package.Key}:{r.Package.Name}:{r.Package.InstalledVersion}:{r.Package.AvailableVersion}:{r.Package.IconPath}"))
            + ":" + (rows.Count == 0 ? (_engine.Error ?? _localError ?? "") + _engine.IsChecking : "rows");
        if (_structure != structure)
        {
            _structure = structure; CloseMenu(); BuildRows(rows);
        }
        foreach (var shown in _shown)
        {
            var row = rows.FirstOrDefault(r => r.Package.Key == shown.Key);
            if (row is null) continue;
            PaintRow(shown, row);
        }
        IReadOnlyList<UpdateRow> batchRows = _engine.BatchRows;
        int updated = _engine.Completed;
        int failed = batchRows.Count(r => r.State is UpdateState.Failed or UpdateState.NeedsAdmin);
        bool hasRun = _engine.Total > 0;
        string checkedAt = CheckedAt(_engine.LastChecked, Now());
        string primary = _engine.IsRunning ? $"{updated} of {_engine.Total} updated"
            : hasRun ? $"{updated} of {_engine.Total} updated"
            : $"{_engine.Updates.Count} {(_engine.Updates.Count == 1 ? "update" : "updates")}";
        string context = _engine.IsRunning ? batchRows.FirstOrDefault(r => r.State == UpdateState.Updating) is { } current ? "Updating " + current.Package.Name : ""
            : hasRun ? failed == 0 ? "" : $"{failed} failed" : checkedAt;
        if (_engine.IsChecking) context += (context.Length == 0 ? "" : " · ") + "Checking…";
        _summary.Inlines.Clear();
        string? warning = _localError ?? _engine.Error;
        if (warning is not null && rows.Count == 0)
        {
            _summary.Text = warning;
            _summary.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        }
        else
        {
            _summary.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
            var count = new Run(primary); count.SetResourceReference(TextElement.ForegroundProperty, "IslandTextBrush"); _summary.Inlines.Add(count);
            if (context.Length > 0)
            {
                var detail = new Run(" · " + context);
                if (hasRun && failed > 0 && !_engine.IsRunning) detail.SetResourceReference(TextElement.ForegroundProperty, "WarningTextBrush");
                _summary.Inlines.Add(detail);
            }
            if (warning is not null)
            {
                var caution = new Run(" · " + warning); caution.SetResourceReference(TextElement.ForegroundProperty, "WarningTextBrush");
                _summary.Inlines.Add(caution);
            }
        }
        string summary = warning is not null && rows.Count == 0 ? warning
            : primary + (context.Length == 0 ? "" : " · " + context) + (warning is null ? "" : " · " + warning);
        _summary.ToolTip = summary;
        AutomationProperties.SetName(_summary, summary);
        _skipped.Content = $"Skipped ({_engine.Preferences.Skipped.Count})";
        _skipped.Visibility = _engine.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        _range.Text = rows.Count > 5 ? $"{_page * 5 + 1}–{Math.Min((_page + 1) * 5, rows.Count)} of {rows.Count}" : "";
        _previous.Visibility = _next.Visibility = rows.Count > 5 ? Visibility.Visible : Visibility.Collapsed;
        _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < pages;
    }

    private void BuildRows(IReadOnlyList<UpdateRow> rows)
    {
        string? focused = FindButtons(_body).FirstOrDefault(b => ReferenceEquals(b, CurrentFocus()) || b.IsKeyboardFocusWithin)?.Tag as string;
        _rows.Children.Clear(); _rows.RowDefinitions.Clear(); _shown.Clear();
        if (rows.Count == 0)
        {
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            bool failed = _engine.Error is not null || _localError is not null;
            var mark = Glyph(failed ? LucideIcons.TriangleAlert : _engine.IsChecking ? LucideIcons.RefreshCw : LucideIcons.Check, 26,
                failed ? "WarningTextBrush" : "IslandTextSecondaryBrush"); mark.HorizontalAlignment = HorizontalAlignment.Center;
            var title = Text(failed ? (_engine.Error?.StartsWith("App updates need winget", StringComparison.Ordinal) == true ? "App updates need winget" : "Check failed")
                : _engine.IsChecking ? "Checking…" : _engine.LastChecked is null ? "Check app updates" : "Everything is up to date", 14);
            title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new(0, 8, 0, 0);
            center.Children.Add(mark); center.Children.Add(title);
            if (!_engine.IsChecking && (failed || _engine.LastChecked is null))
            {
                var check = Button("Check again", () => _ = CheckAsync()); check.HorizontalAlignment = HorizontalAlignment.Center; check.Margin = new(0, 12, 0, 0); center.Children.Add(check);
            }
            _rows.VerticalAlignment = VerticalAlignment.Stretch; _rows.Children.Add(center); return;
        }
        _rows.VerticalAlignment = VerticalAlignment.Top;
        foreach (var row in rows.Skip(_page * 5).Take(5))
        {
            var package = row.Package;
            var line = new Grid { Height = 46, Margin = new(4, 0, 0, 0) };
            foreach (GridLength width in new[] { new GridLength(26), new GridLength(1, GridUnitType.Star), new GridLength(100) }) line.ColumnDefinitions.Add(new() { Width = width });
            Add(line, AppMark(package.Name, package.IconPath, _loadIcons));
            var copy = new StackPanel { Margin = new(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = Text(package.Name); name.ToolTip = package.Name; copy.Children.Add(name);
            var detail = Text("", 11, "IslandTextTertiaryBrush"); copy.Children.Add(detail);
            var fill = new Border { Height = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(2) };
            fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
            var track = new Border { Child = fill, Height = 3, MaxWidth = 180, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new(0, 2, 0, 0), CornerRadius = new(2), Visibility = Visibility.Collapsed };
            copy.SizeChanged += (_, _) => track.Width = Math.Min(180, copy.ActualWidth);
            track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
            track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * (fill.Tag is double fraction ? fraction : 0);
            copy.Children.Add(track); Add(line, copy, column: 1);
            var actions = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var update = Button("Update", () => ShowConfirmation(_engine.CaptureBatch([package]), _isRunning(package)));
            update.FontSize = 11; update.Height = 25; update.MinWidth = 60; update.Padding = new(8, 0, 8, 0);
            update.Tag = "update:" + package.Key; AutomationProperties.SetName(update, "Update " + package.Name);
            Button? more = null;
            more = Icon(LucideIcons.Ellipsis, "More options for " + package.Name, () => ShowMenu(more!, package)); more.Width = 26; more.Tag = "more:" + package.Key;
            buttons.Children.Add(update); buttons.Children.Add(more); actions.Children.Add(buttons);
            var status = Text("", 11, "IslandTextSecondaryBrush");
            var check = Glyph(LucideIcons.Check, 12, "SuccessBrush"); check.Margin = new(0, 0, 5, 0);
            var statusHost = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            statusHost.Children.Add(check); statusHost.Children.Add(status); actions.Children.Add(statusHost);
            Add(line, actions, column: 2);
            int at = _rows.RowDefinitions.Count; _rows.RowDefinitions.Add(new() { Height = new(46) }); Add(_rows, line, at);
            var parts = new RowParts(package.Key, package, detail, status, statusHost, check, update, more, track, fill); _shown.Add(parts); PaintRow(parts, row);
        }
        if (focused is not null)
        {
            var replacement = FindButtons(_body).FirstOrDefault(b => Equals(b.Tag, focused));
            if (replacement is { IsEnabled: true }) SetFocus(replacement);
            else { _body.Focusable = true; KeyboardNavigation.SetIsTabStop(_body, false); SetFocus(_body); }
        }
    }

    private void PaintRow(RowParts parts, UpdateRow row)
    {
        bool active = row.State is UpdateState.Updating or UpdateState.Queued;
        bool complete = row.State is UpdateState.Updated or UpdateState.RestartRequired or UpdateState.Skipped or UpdateState.Cancelled;
        bool warning = row.State is UpdateState.Failed or UpdateState.NeedsAdmin;
        parts.Detail.Text = row.State switch
        {
            UpdateState.Updating => (row.Reason ?? "Updating") + (row.Progress is { } p ? $" · {p:P0}" : "…"),
            UpdateState.Failed => "Update failed" + (string.IsNullOrWhiteSpace(row.Reason) ? "" : " · " + row.Reason),
            UpdateState.NeedsAdmin => "Needs admin",
            UpdateState.RestartRequired => "Restart to finish",
            _ => Versions(row.Package.InstalledVersion, row.Package.AvailableVersion, row.Package.Source),
        };
        parts.Detail.ToolTip = parts.Detail.Text;
        parts.Detail.SetResourceReference(TextBlock.ForegroundProperty, warning ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        parts.Status.Text = row.State switch
        {
            UpdateState.Queued => "Queued", UpdateState.Updating => "Updating", UpdateState.Updated => "Updated",
            UpdateState.RestartRequired => "Updated", UpdateState.Skipped => "Skipped", UpdateState.Cancelled => "Cancelled", _ => "",
        };
        parts.Status.SetResourceReference(TextBlock.ForegroundProperty, row.State is UpdateState.Updated or UpdateState.RestartRequired ? "SuccessBrush" : "IslandTextTertiaryBrush");
        parts.StatusHost.Visibility = active || complete ? Visibility.Visible : Visibility.Collapsed;
        parts.Check.Visibility = row.State is UpdateState.Updated or UpdateState.RestartRequired ? Visibility.Visible : Visibility.Collapsed;
        parts.Update.Visibility = active || complete ? Visibility.Collapsed : Visibility.Visible;
        parts.More.Visibility = active || complete ? Visibility.Collapsed : Visibility.Visible;
        parts.Update.IsEnabled = parts.More.IsEnabled = !_engine.IsRunning && !_submitting;
        parts.Update.Content = warning ? "Retry" : "Update";
        parts.Track.Visibility = row.State == UpdateState.Updating && row.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        parts.Fill.Tag = Math.Clamp(row.Progress ?? 0, 0, 1);
        parts.Fill.Width = parts.Track.ActualWidth * Math.Clamp(row.Progress ?? 0, 0, 1);
        AutomationProperties.SetName(parts.Track, row.Package.Name + " update progress");
        AutomationProperties.SetHelpText(parts.Track, row.Progress is { } value ? value.ToString("P0", System.Globalization.CultureInfo.CurrentCulture) : "");
    }

    private void RenderConfirmation()
    {
        if (_confirmation is not { } batch) return;
        bool hadFocus = IsKeyboardFocusWithin || _header.IsKeyboardFocusWithin;
        _headerButtons.Visibility = Visibility.Collapsed;
        var question = new Grid { Margin = new(12, 8, 12, 0) };
        foreach (GridLength height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, new GridLength(40) }) question.RowDefinitions.Add(new() { Height = height });
        string text = batch.Packages.Count == 1 ? (_closeRunning ? $"Close {batch.Packages[0].Name} and update?" : $"Update {batch.Packages[0].Name}?") : $"Update {batch.Packages.Count} apps?";
        var heading = Text(text, 16); heading.FontWeight = FontWeights.SemiBold; heading.TextWrapping = TextWrapping.Wrap; heading.MaxHeight = 48;
        heading.Margin = new(0, 0, 0, 10); heading.ToolTip = text; Add(question, heading);
        var names = new StackPanel();
        int pages = Math.Max(1, (batch.Packages.Count + 4) / 5); _confirmPage = Math.Clamp(_confirmPage, 0, pages - 1);
        foreach (var package in batch.Packages.Skip(_confirmPage * 5).Take(5))
        {
            var line = new Grid { Height = 29 };
            line.ColumnDefinitions.Add(new() { Width = new(20) }); line.ColumnDefinitions.Add(new()); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Add(line, AppMark(package.Name, package.IconPath, _loadIcons, 20));
            var name = Text(package.Name); name.ToolTip = package.Name; name.Margin = new(8, 0, 8, 0); Add(line, name, column: 1);
            string versionText = package.UnknownVersion ? "Unknown → " + package.AvailableVersion : package.AvailableVersion;
            var version = Text(versionText, 11, "IslandTextTertiaryBrush"); version.ToolTip = versionText; version.HorizontalAlignment = HorizontalAlignment.Right;
            version.MinWidth = 64; version.MaxWidth = 140; Add(line, version, column: 2);
            names.Children.Add(line);
        }
        var review = new Border { Child = names, Padding = new(10, 4, 10, 4), CornerRadius = new(6), VerticalAlignment = VerticalAlignment.Top };
        review.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush"); Add(question, review, 1);
        if (pages > 1)
        {
            var pager = new DockPanel { Height = 28 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var previous = Icon(LucideIcons.ChevronLeft, "Previous apps in batch", () => { _confirmPage--; RenderConfirmation(); }); previous.IsEnabled = _confirmPage > 0;
            var next = Icon(LucideIcons.ChevronRight, "Next apps in batch", () => { _confirmPage++; RenderConfirmation(); }); next.IsEnabled = _confirmPage < pages - 1;
            buttons.Children.Add(previous); buttons.Children.Add(next); DockPanel.SetDock(buttons, Dock.Right); pager.Children.Add(buttons);
            pager.Children.Add(Text($"{_confirmPage * 5 + 1}–{Math.Min((_confirmPage + 1) * 5, batch.Packages.Count)} of {batch.Packages.Count}", 11, "IslandTextTertiaryBrush")); Add(question, pager, 2);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var cancel = Button("Cancel", () => Back());
        var update = Button("Update", () => _ = SubmitAsync(), true); update.Margin = new(8, 0, 0, 0);
        _confirmButton = cancel;
        actions.Children.Add(cancel); actions.Children.Add(update); Add(question, actions, 4);
        _body.Children.Clear(); _body.Children.Add(question);
        if (hadFocus) SetFocus(cancel);
    }

    private async Task SubmitAsync()
    {
        if (_confirmation is not { } batch || _engine.IsRunning || _disposed || _submitting) return;
        _submitting = true;
        bool close = _closeRunning; _confirmation = null; _confirmButton = null; _structure = null;
        _body.Children.Clear(); _body.Children.Add(_listBody);
        Refresh();
        try { await _execute(batch, close); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _localError = "Update failed · " + ex.Message; }
        finally { _submitting = false; Refresh(); }
    }

    private void ShowMenu(Button anchor, PackageUpdate package)
    {
        if (_engine.IsRunning || _submitting) return;
        CloseMenu();
        var choices = new StackPanel();
        void AddChoice(string name, Action action)
        {
            var choice = Button(name, () => { CloseMenu(); try { action(); } catch (Exception ex) { _localError = "Save failed · " + ex.Message; Refresh(); } });
            choice.MinWidth = 0; choice.HorizontalContentAlignment = HorizontalAlignment.Left; choice.Height = 30;
            choices.Children.Add(choice);
        }
        AddChoice("Skip this version", () => _engine.Skip(package, false));
        AddChoice("Always skip", () => _engine.Skip(package, true));
        if (_isRunning(package)) AddChoice("Close and update", () => ShowConfirmation(_engine.CaptureBatch([package]), true));
        double top = anchor.TransformToAncestor(_body).TransformBounds(new Rect(anchor.RenderSize)).Bottom;
        _menu = new Border { Child = choices, Width = 180, Padding = new(4), CornerRadius = new(6), BorderThickness = new(1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(0, Math.Clamp(top, 0, Math.Max(0, _body.ActualHeight - choices.Children.Count * 30 - 10)), 0, 0) };
        _menu.SetResourceReference(Border.BackgroundProperty, "IslandBrush"); _menu.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        KeyboardNavigation.SetTabNavigation(_menu, KeyboardNavigationMode.Cycle); Panel.SetZIndex(_menu, 10); _body.Children.Add(_menu);
        if (anchor.IsKeyboardFocused && choices.Children[0] is Button first) SetFocus(first);
    }
    private void CloseMenu() { if (_menu is not null) _body.Children.Remove(_menu); _menu = null; }
    private void TurnPage(int delta)
    {
        int next = Math.Clamp(_page + delta, 0, Math.Max(0, (_engine.Rows.Count - 1) / 5));
        if (next == _page) return;
        _page = next; _structure = null; CloseMenu(); Refresh();
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Back()) { e.Handled = true; return; }
        if (_confirmation is null && e.Key is Key.PageDown or Key.PageUp)
        { TurnPage(e.Key == Key.PageDown ? 1 : -1); e.Handled = true; }
    }
    private static bool Inside(DependencyObject target, DependencyObject parent)
    {
        for (DependencyObject? node = target; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, parent)) return true;
        return false;
    }
    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Follow(false); _disposed = true; CloseMenu();
        Loaded -= OnLoaded; Unloaded -= OnUnloaded; PreviewKeyDown -= OnKey;
    }
}
