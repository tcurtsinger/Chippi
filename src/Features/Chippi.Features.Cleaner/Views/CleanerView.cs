// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.UI.Controls;
using static Chippi.Features.Cleaner.Views.CleanerUi;

namespace Chippi.Features.Cleaner.Views;

/// <summary>
/// The island's Clean junk files page (docs/phase-7/design/cleaner.html): the list with its ticks, the confirm, the run and its result. It never
/// scrolls: seven 36-DIP rows fit the 520 x 360 panel. Nothing is deleted until the confirm's Clean is pressed; Cancel has the focus.
/// </summary>
public sealed class CleanerView : ContentControl, IDisposable
{
    private readonly CleanerEngine _engine;
    private readonly Func<IReadOnlyList<string>?, Task> _request;
    private readonly Func<CancellationToken> _token;
    private readonly Grid _header = new();
    private readonly Grid _body = new();
    private CleanerPlan? _question, _plan;
    private TaskCompletionSource? _answer;
    private TaskCompletionSource<bool>? _quit;
    private CleanerRunResult? _result;
    private bool _empty, _following, _disposed;
    private Button? _initial;

    /// <param name="engine">The engine the page draws and drives.</param>
    /// <param name="request">Clean junk files for the given categories (all ticked when null): measure, then ask. The module's verb.</param>
    /// <param name="lifetime">The cancellation token of a clean: the module's, so stopping the module stops the run between files.</param>
    public CleanerView(CleanerEngine engine, Func<IReadOnlyList<string>?, Task> request, Func<CancellationToken>? lifetime = null)
    {
        _engine = engine;
        _request = request;
        _token = lifetime ?? (() => CancellationToken.None);
        Content = _body;
        Focusable = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnKey;
        Render();
    }

    public FrameworkElement Header => _header;
    public UIElement? InitialFocus => (UIElement?)_initial ?? this;

    internal CleanerPlan? Question => _question;
    internal CleanerRunResult? Result => _result;
    internal bool Empty => _empty;
    internal bool Quitting => _quit is not null;

    /// <summary>Draws the page again now (the tests and shots hold the clock; a live page redraws on its own events).</summary>
    internal void Redraw() => Render();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true);
        Render();
        if (_question is null && _quit is null && !_empty && _result is null && !_engine.IsMeasuring && !_engine.IsPreparing && !_engine.IsCleaning)
            _ = ScanAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Follow(false);

    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) _engine.Changed += OnEngineChanged; else _engine.Changed -= OnEngineChanged;
        _following = enabled;
    }

    private void OnEngineChanged()
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(OnEngineChanged); else Render();
    }

    private async Task ScanAsync()
    {
        _empty = false;
        _result = null;
        try { await _engine.MeasureAsync(_token()); }
        catch (OperationCanceledException) { }
        if (!_disposed) Render();
    }

    /// <summary>Shows the list again, measuring.</summary>
    public void Scan() => _ = ScanAsync();

    /// <summary>The confirm: "Clean 1.0 GB?" with each category. Completes when it was answered (Cancel) or the clean it asked for finished.</summary>
    public Task AskAsync(CleanerPlan plan)
    {
        if (_disposed) return Task.CompletedTask;
        _empty = false;
        _result = null;
        _question = plan;
        _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Render();
        return _answer.Task;
    }

    /// <summary>Nothing to clean: the plan was empty.</summary>
    public void ShowEmpty()
    {
        if (_disposed) return;
        _question = null;
        _result = null;
        _empty = true;
        Render();
    }

    /// <summary>"Stop cleaning and quit?": true when Quit was pressed.</summary>
    public Task<bool> AskQuitAsync()
    {
        _quit?.TrySetResult(false);
        _quit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Render();
        return _quit.Task;
    }

    /// <summary>Back: leaves a question or the quit question; false when the page should go back itself.</summary>
    public bool Back()
    {
        if (_quit is not null) { AnswerQuit(false); return true; }
        if (_question is not null) { Cancel(); return true; }
        return false;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_quit is not null) { AnswerQuit(false); e.Handled = true; }
        else if (_question is not null) { Cancel(); e.Handled = true; }
    }

    private void AnswerQuit(bool quit)
    {
        var answer = _quit;
        _quit = null;
        Render();
        answer?.TrySetResult(quit);
    }

    /// <summary>Cancel at the question: nothing is deleted and the helper session the measure left open ends.</summary>
    internal async void Cancel()
    {
        var answer = _answer;
        _question = null;
        _answer = null;
        Render();
        try { await _engine.CancelPlanAsync(); }
        finally { answer?.TrySetResult(); }
    }

    /// <summary>Clean at the question: deletes the plan.</summary>
    internal async void Confirm()
    {
        if (_question is not { } plan) return;
        var answer = _answer;
        _question = null;
        _answer = null;
        _plan = plan;
        Render();
        try { _result = await _engine.CleanAsync(plan, new Progress<CleanerRunProgress>(_ => Render()), _token()); }
        catch (OperationCanceledException) { }
        _plan = null;
        if (!_disposed) Render();
        answer?.TrySetResult();
    }

    // ---- drawing ----

    private void Render()
    {
        if (_disposed) return;
        _initial = null;
        _body.Children.Clear();
        var title = Text("Clean junk files", 13);
        title.FontWeight = FontWeights.SemiBold;
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        _header.ColumnDefinitions.Add(new());
        _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Add(_header, title);
        if (_quit is not null) RenderQuit();
        else if (_engine.WaitsForAdmin) RenderCentered(Glyph(LucideIcons.Shield, 28), "Waiting for admin");
        else if (_question is { } plan) RenderQuestion(plan);
        else if (_engine.IsCleaning && _plan is not null) RenderRun(null);
        else if (_engine.IsPreparing) RenderCentered(Spinner(28), "Measuring");
        else if (_result is { } result) RenderRun(result);
        else if (_empty) RenderEmpty();
        else RenderList();
    }

    private void HeaderButtons(params Button[] buttons)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons) { if (strip.Children.Count > 0) button.Margin = new Thickness(6, 0, 0, 0); strip.Children.Add(button); }
        Add(_header, strip, column: 1);
    }

    private void RenderCentered(UIElement mark, string text)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (mark is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(mark);
        var label = Text(text, 14);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 8, 0, 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(label);
        _body.Children.Add(column);
    }

    private void RenderEmpty()
    {
        HeaderButtons(IslandButton("Scan", Scan));
        RenderCentered(Glyph(LucideIcons.CircleCheck, 28), "Nothing to clean");
    }

    private void RenderQuit()
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var label = Text("Stop cleaning and quit?", 14);
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(label);
        var cancel = IslandButton("Cancel", () => AnswerQuit(false));
        var quit = IslandButton("Quit", () => AnswerQuit(true), critical: true);
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        quit.Margin = new Thickness(8, 0, 0, 0);
        strip.Children.Add(cancel);
        strip.Children.Add(quit);
        column.Children.Add(strip);
        _initial = cancel;
        _body.Children.Add(column);
    }

    private void RenderQuestion(CleanerPlan plan)
    {
        var column = new StackPanel { Margin = new Thickness(12, 8, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        var question = Text(plan.Question, 16);
        question.FontWeight = FontWeights.SemiBold;
        question.Margin = new Thickness(0, 0, 0, 10);
        column.Children.Add(question);
        var items = new StackPanel();
        foreach (var item in plan.Items)
        {
            var line = new Grid { Height = 29 };
            line.ColumnDefinitions.Add(new());
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Add(line, Text(item.Category.Name));
            Add(line, Text(CleanerText.Size(item.Bytes, item.LowerBound), 12, "IslandTextSecondaryBrush"), column: 1);
            items.Children.Add(line);
        }
        var card = new Border { Child = items, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4) };
        card.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");
        column.Children.Add(card);
        _body.Children.Add(column);
        var cancel = IslandButton("Cancel", Cancel);
        var clean = IslandButton("Clean", Confirm, critical: true, shield: plan.NeedsAdmin);
        AutomationProperties.SetName(clean, plan.Question.TrimEnd('?'));
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        clean.Margin = new Thickness(8, 0, 0, 0);
        foot.Children.Add(cancel);
        foot.Children.Add(clean);
        _body.Children.Add(foot);
        _initial = cancel;
    }

    private static UIElement Meta(string bold, string? rest, bool spinning)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 24, Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Top };
        if (spinning)
        {
            var spin = Spinner(12);
            spin.Margin = new Thickness(0, 0, 6, 0);
            line.Children.Add(spin);
        }
        line.Children.Add(Text(bold));
        if (rest is not null)
        {
            var more = Text("· " + rest, 12, "IslandTextTertiaryBrush");
            more.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(more);
        }
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        return line;
    }

    private void Place(StackPanel list, UIElement meta)
    {
        list.Children.Insert(0, meta);
        _body.Children.Add(list);
    }

    // ---- the list ----

    private void RenderList()
    {
        var rows = _engine.Rows;
        bool measuring = _engine.IsMeasuring;
        var scan = IslandButton("Scan", Scan);
        scan.IsEnabled = !(measuring && !_engine.HasSizes);
        var clean = IslandButton("Clean", () => _ = _request(null), shield: _engine.AnyAdminTicked, accent: true);
        clean.IsEnabled = rows.Any(r => r.Ticked) && !measuring && !_engine.IsCleaning;
        HeaderButtons(scan, clean);
        _initial = clean.IsEnabled ? clean : scan;
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        foreach (var row in rows) list.Children.Add(ListRow(row, measuring));
        string meta = _engine.HasSizes || !measuring ? _engine.TotalText : "Measuring";
        Place(list, Meta(meta, null, measuring));
    }

    private UIElement ListRow(CleanerRow row, bool measuring)
    {
        bool unavailable = row.State == CleanerRowState.Unavailable;
        bool skeleton = measuring && !_engine.Rows.Any(r => r.FromEarlier) && row.Bytes is null && row.State is CleanerRowState.Measuring or CleanerRowState.Unmeasured;
        bool dim = row.FromEarlier && row.State == CleanerRowState.Measuring;
        var lead = Check(row.Ticked && !unavailable, unavailable);
        UIElement? tag = row.Category.Tag == CleanerTag.CheckFirst ? Badge("Check first") : null;
        var grid = RowGrid(lead, row.Category.Name, tag, row.Category.NeedsAdmin, row, skeleton, dim);
        var button = new Button { Content = grid, Height = 36, Padding = new Thickness(8, 0, 8, 0), IsEnabled = !unavailable };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(button, row.Category.Name);
        AutomationProperties.SetItemStatus(button, row.Ticked ? "Ticked" : "Not ticked");
        button.Click += (_, _) => { _engine.SetTicked(row.Category.Id, !row.Ticked); Render(); };
        return button;
    }

    private static UIElement Badge(string text)
    {
        var label = Text(text, 11, "WarningTextBrush");
        label.FontWeight = FontWeights.SemiBold;
        var badge = new Border { Height = 18, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(4), Child = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        badge.SetResourceReference(Border.BackgroundProperty, "IslandWaitingTintBrush");
        return badge;
    }

    /// <summary>lead | name | tag or action | admin shield | size or status, as the mockup draws every row.</summary>
    private static Grid RowGrid(UIElement lead, string name, UIElement? tag, bool shield, CleanerRow row, bool skeleton, bool dim,
        double? progress = null, string? text = null)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        foreach (var width in new[] { new GridLength(16), new GridLength(1, GridUnitType.Star), new GridLength(80), new GridLength(30), new GridLength(76) })
            grid.ColumnDefinitions.Add(new() { Width = width });
        var leadHost = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        leadHost.Children.Add(lead);
        Add(grid, leadHost);
        var copy = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(Text(name, 12, dim ? "IslandTextTertiaryBrush" : "IslandTextBrush"));
        if (progress is { } fraction)
        {
            var fill = new Border { Height = 3, Width = 180 * fraction, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
            fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
            var track = new Border { Height = 3, Width = 180, CornerRadius = new CornerRadius(2), Child = fill, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
            copy.Children.Add(track);
        }
        Add(grid, copy, column: 1);
        if (tag is not null)
        {
            if (tag is FrameworkElement t) { t.Margin = new Thickness(10, 0, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Right; }
            Add(grid, tag, column: 2);
        }
        if (shield) Add(grid, new UacShield(14) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, column: 3);
        UIElement size;
        if (skeleton)
        {
            var bar = new Border { Width = 46, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            bar.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
            size = bar;
        }
        else
        {
            string value = text ?? row.Text;
            bool caution = text is null && row.State is CleanerRowState.InUse or CleanerRowState.NeedsAdmin or CleanerRowState.Failed or CleanerRowState.Kept or CleanerRowState.Cancelled;
            string brush = caution ? "WarningTextBrush"
                : value is "—" or "Unavailable" or "Queued" || dim ? "IslandTextTertiaryBrush" : "IslandTextSecondaryBrush";
            var label = Text(value, 12, brush);
            label.HorizontalAlignment = HorizontalAlignment.Right;

            size = label;
        }
        Add(grid, size, column: 4);
        return grid;
    }

    // ---- a run and its result ----

    private void RenderRun(CleanerRunResult? result)
    {
        // In the order the run goes: admin categories first, while the helper session the measure opened is warm.
        IReadOnlyList<CleanerRow> rows = [.. (result?.Rows ?? _engine.Rows.Where(r => _plan!.Asked.Contains(r.Category.Id))).OrderBy(r => r.Category.NeedsAdmin ? 0 : 1)];
        string meta;
        string? rest = null;
        if (result is null) meta = CleanerText.Size(rows.Where(r => r.State == CleanerRowState.Cleaned).Sum(r => r.Bytes ?? 0), false) + " freed";
        else { meta = result.Freed; rest = result.Meta; HeaderButtons(IslandButton("Scan", Scan)); }
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        double fraction = Fraction(rows);
        foreach (var row in rows) list.Children.Add(RunRow(row, result is null, fraction));
        Place(list, Meta(meta, rest, result is null));
    }

    private double Fraction(IReadOnlyList<CleanerRow> rows)
    {
        long total = _plan?.Bytes ?? 0;
        if (total <= 0) return 0;
        long done = rows.Where(r => r.State == CleanerRowState.Cleaned).Sum(r => r.Bytes ?? 0);
        return Math.Min(.99, done / (double)total);
    }

    private UIElement RunRow(CleanerRow row, bool running, double fraction)
    {
        UIElement lead;
        UIElement? action = null;
        double? progress = null;
        string? text = null;
        switch (row.State)
        {
            case CleanerRowState.Cleaned:
                lead = Glyph(LucideIcons.Check, 14, "SuccessBrush");
                break;
            case CleanerRowState.Cleaning when _engine.RunningId == row.Category.Id:
                lead = Spinner(14);
                progress = fraction;
                text = (int)(fraction * 100) + "%";
                break;
            case CleanerRowState.Cleaning:
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Border.BorderBrushProperty, "IslandTextTertiaryBrush");
                lead = dot;
                text = "Queued";
                break;
            case CleanerRowState.Unavailable:
                lead = Glyph(LucideIcons.CircleAlert, 14, "IslandTextTertiaryBrush");
                break;
            default:
                lead = Glyph(LucideIcons.CircleAlert, 14, "WarningTextBrush");
                if (!running && row.State == CleanerRowState.Failed) action = IslandButton("Retry", () => _ = Retry(row), small: true);
                else if (!running && row.State == CleanerRowState.NeedsAdmin) action = IslandButton("Clean", () => _ = Retry(row), small: true);
                break;
        }
        var grid = RowGrid(lead, row.Category.Name, action, row.Category.NeedsAdmin, row, false, false, progress, text);
        return new Border { Height = progress is null ? 36 : 44, Padding = new Thickness(8, 0, 8, 0), Child = grid };
    }

    private async Task Retry(CleanerRow row)
    {
        _result = null;
        await _request([row.Category.Id]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Follow(false);
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        PreviewKeyDown -= OnKey;
        _quit?.TrySetResult(false);
        _answer?.TrySetResult();
    }
}
