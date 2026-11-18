// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using static Chippi.Features.PortManager.Views.PortUi;

namespace Chippi.Features.PortManager.Views;

/// <summary>
/// The island's one Port manager page (docs/phase-7/design/port-manager.html): the Free port picker, the question a verb row or a
/// picker row asks, its closing, force and waiting states, and the result drawn as a Search row. It never scrolls: the picker pages
/// five rows at a time. Nothing ends until the verb in a question is pressed; Cancel has the focus.
/// </summary>
public sealed class PortManagerView : ContentControl, IDisposable
{
    private const int PageSize = 5;
    private readonly PortManagerEngine _engine;
    private readonly ModuleContext _context;
    private readonly PortFlow _flow;
    private readonly Grid _header = new();
    private readonly Grid _body = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private List<PickEntry> _entries = [];
    private int _page, _selected;
    private bool _fromPicker = true, _picking = true, _following, _disposed;
    private Button? _initial;

    internal sealed record PickEntry(PortRow Row, int Ports);

    public PortManagerView(PortManagerEngine engine, ModuleContext context, PortFlow? flow = null)
    {
        _engine = engine;
        _context = context;
        _flow = flow ?? new PortFlow(engine);
        Content = _body;
        Focusable = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnKey;
        _tick.Tick += (_, _) => { if (_flow.Step == FlowStep.Working) Render(); else _tick.Stop(); };
        Render();
    }

    public FrameworkElement Header => _header;
    public UIElement? InitialFocus => (UIElement?)_initial ?? this;
    internal PortFlow Flow => _flow;

    /// <summary>Draws the page again now (the tests and shots hold the clock; a live page redraws on its own timer).</summary>
    internal void Redraw() => Render();
    internal int Page => _page;
    internal IReadOnlyList<PickEntry> Entries => _entries;
    internal bool Picking => _picking && _flow.Step == FlowStep.Idle;

    private void OnLoaded(object sender, RoutedEventArgs e) => Follow(true);
    private void OnUnloaded(object sender, RoutedEventArgs e) => Follow(false);

    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) { _engine.Changed += OnEngineChanged; _flow.Changed += OnFlowChanged; }
        else { _engine.Changed -= OnEngineChanged; _flow.Changed -= OnFlowChanged; _tick.Stop(); }
        _following = enabled;
    }

    private void OnEngineChanged() { if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(OnEngineChanged); else if (Picking) Render(); }

    private void OnFlowChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnFlowChanged); return; }
        if (_flow.Step == FlowStep.Working) _tick.Start();
        Render();
    }

    /// <summary>Shows the Free port picker, reading the list again.</summary>
    public void ShowPicker()
    {
        if (_disposed) return;
        _picking = true;
        _fromPicker = true;
        _page = 0;
        _selected = 0;
        if (_flow.Step != FlowStep.Working) _flow.Dismiss();
        Render();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try { await _engine.RefreshAsync(); }
        catch (OperationCanceledException) { }
        if (!_disposed && Picking) Render();
    }

    /// <summary>The question for one row (a Search row's Free or Stop, or a picker row).</summary>
    public void Ask(PortRow row, bool fromPicker = false)
    {
        if (_disposed) return;
        _fromPicker = fromPicker;
        _picking = false;
        _flow.Ask(row);
        Follow(IsLoaded || _following);
        Render();
    }

    /// <summary>Back: leaves a question or a result for the picker it came from; false when the page should go back itself.</summary>
    public bool Back()
    {
        switch (_flow.Step)
        {
            case FlowStep.Working:
                _flow.Cancel();
                return true;
            case FlowStep.Asking or FlowStep.ForceAsking or FlowStep.Result:
                _flow.Dismiss();
                if (_fromPicker) { _picking = true; Render(); return true; }
                Render();
                return false;
            default:
                return false;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _flow.Step is FlowStep.Asking or FlowStep.ForceAsking or FlowStep.Working)
        {
            _flow.Cancel();
            if (_flow.Step == FlowStep.Idle && _fromPicker) { _picking = true; Render(); }
            e.Handled = true;
            return;
        }
        if (Picking)
        {
            switch (e.Key)
            {
                case Key.Down when _entries.Count > 0: Select(Math.Min(_selected + 1, _entries.Count - 1)); e.Handled = true; break;
                case Key.Up when _entries.Count > 0: Select(Math.Max(_selected - 1, 0)); e.Handled = true; break;
                case Key.PageDown: TurnPage(1); e.Handled = true; break;
                case Key.PageUp: TurnPage(-1); e.Handled = true; break;
                case Key.Enter when _selected < _entries.Count && _selected >= 0 && e.OriginalSource is not Button: Ask(_entries[_selected].Row, fromPicker: true); e.Handled = true; break;
            }
            return;
        }
        if (_flow.Step == FlowStep.Result && _resultRows.Count > 0)
        {
            switch (e.Key)
            {
                case Key.Down: ResultSelect(Math.Min(_resultSelected + 1, _resultRows.Count - 1)); e.Handled = true; break;
                case Key.Up: ResultSelect(Math.Max(_resultSelected - 1, 0)); e.Handled = true; break;
                case Key.Enter when _resultSelected >= 0 && _resultSelected < _resultRows.Count: _resultRows[_resultSelected].Run(); e.Handled = true; break;
            }
        }
    }

    private void Select(int index)
    {
        if (index == _selected) return;
        _selected = index;
        _page = index / PageSize;
        Render();
    }

    private void TurnPage(int delta)
    {
        int pages = Math.Max(1, (_entries.Count + PageSize - 1) / PageSize);
        int next = Math.Clamp(_page + delta, 0, pages - 1);
        if (next == _page) return;
        _page = next;
        _selected = next * PageSize;
        Render();
    }

    private void Render()
    {
        if (_disposed) return;
        _initial = null;
        _body.Children.Clear();
        _body.RowDefinitions.Clear();
        _resultRows.Clear();
        switch (_flow.Step)
        {
            case FlowStep.Asking or FlowStep.ForceAsking: RenderQuestion(); break;
            case FlowStep.Working: RenderWorking(); break;
            case FlowStep.Result: RenderResult(); break;
            default: RenderPicker(); break;
        }
    }

    private void SetHeader(FrameworkElement? content)
    {
        _header.Children.Clear();
        if (content is not null) _header.Children.Add(content);
    }

    // ---- the Free port picker: five 46-DIP rows a page, as App updates ----

    private static IEnumerable<PickEntry> Group(IEnumerable<PortListRow> rows) => rows
        .Where(r => r.Mark != PortMark.Ended && r.Row.Verb != PortVerb.None && r.Row.Endpoint.IsHeld)
        .GroupBy(r => (r.Row.Holder.Pid, r.Row.Holder.StartTime, r.Row.Verb, r.Row.ServiceName))
        .Select(g => new PickEntry(g.Select(r => r.Row).OrderBy(r => r.Endpoint.LocalPort).ThenBy(r => r.Endpoint.Protocol).First(),
            g.Select(r => r.Row.Endpoint.LocalPort).Distinct().Count()))
        .OrderBy(e => e.Row.Endpoint.LocalPort);

    private void RenderPicker()
    {
        _entries = [.. Group(_engine.Rows)];
        _selected = _entries.Count == 0 ? 0 : Math.Clamp(_selected, 0, _entries.Count - 1);
        int pages = Math.Max(1, (_entries.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        var title = Text("Free port", 13);
        title.FontWeight = FontWeights.SemiBold;
        SetHeader(title);
        if (_entries.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var mark = Glyph(LucideIcons.Network, 24, "IslandTextTertiaryBrush");
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            var line = Text("No ports to free", 12, "IslandTextTertiaryBrush");
            line.Margin = new Thickness(0, 6, 0, 0);
            empty.Children.Add(mark);
            empty.Children.Add(line);
            _body.Children.Add(empty);
            return;
        }
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        for (int i = _page * PageSize; i < Math.Min(_entries.Count, (_page + 1) * PageSize); i++)
            list.Children.Add(PickerRow(_entries[i], i));
        _body.Children.Add(list);
        var footer = new Grid { Height = 28, VerticalAlignment = VerticalAlignment.Bottom };
        footer.ColumnDefinitions.Add(new());
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (_entries.Count > PageSize)
        {
            var range = Text($"{_page * PageSize + 1}–{Math.Min((_page + 1) * PageSize, _entries.Count)} of {_entries.Count}", 11, "IslandTextSecondaryBrush");
            Add(footer, range);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var previous = IslandIcon(LucideIcons.ChevronLeft, "Previous ports", () => TurnPage(-1));
            var next = IslandIcon(LucideIcons.ChevronRight, "Next ports", () => TurnPage(1));
            previous.IsEnabled = _page > 0;
            next.IsEnabled = _page + 1 < pages;
            buttons.Children.Add(previous);
            buttons.Children.Add(next);
            Add(footer, buttons, column: 1);
        }
        _body.Children.Add(footer);
    }

    private UIElement PickerRow(PickEntry entry, int index)
    {
        var row = entry.Row;
        string name = row.Verb == PortVerb.Stop ? PortFlow.ServiceName(row) : row.Holder.Name;
        string detail = $"Port {PortsText(row.Endpoint.LocalPort, entry.Ports)} · " + (row.Verb == PortVerb.Stop ? "Service" : "PID " + row.Holder.Pid);
        var line = new Grid { Height = 46, Margin = new Thickness(0) };
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(16), GridLength.Auto })
            line.ColumnDefinitions.Add(new() { Width = width });
        var mark = Mark(row.Holder, 28);
        mark.Margin = new Thickness(10, 0, 10, 0);
        Add(line, mark);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = Text(name);
        nameText.ToolTip = row.Holder.Path ?? name;
        copy.Children.Add(nameText);
        copy.Children.Add(Text(detail, 11, "IslandTextSecondaryBrush"));
        Add(line, copy, column: 1);
        if (row.NeedsAdmin)
        {
            var shield = new UacShield(14) { HorizontalAlignment = HorizontalAlignment.Center };
            Add(line, shield, column: 2);
        }
        string verb = row.Verb == PortVerb.Stop ? "Stop" : "Free";
        var button = IslandButton(verb, () => Ask(row, fromPicker: true), small: true);
        button.Margin = new Thickness(0, 0, 10, 0);
        AutomationProperties.SetName(button, $"{verb} {name}");
        Add(line, button, column: 3);
        var host = new Border { Child = line, CornerRadius = new CornerRadius(4) };
        if (index == _selected) host.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        host.MouseMove += (_, _) => { if (_selected != index) { _selected = index; Render(); } };
        return host;
    }

    // ---- the question and its steps ----

    private StackPanel Centered(PortRow row)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mark = Mark(row.Holder, 40);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(mark);
        return column;
    }

    private static TextBlock Title(string text)
    {
        var title = Text(text, 16);
        title.FontWeight = FontWeights.SemiBold;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new Thickness(0, 12, 0, 0);
        return title;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var button in buttons) { if (strip.Children.Count > 0) button.Margin = new Thickness(8, 0, 0, 0); strip.Children.Add(button); }
        return strip;
    }

    private void RenderQuestion()
    {
        SetHeader(null);
        var row = _flow.Row!;
        var column = Centered(row);
        column.Children.Add(Title(_flow.Title));
        if (_flow.Detail.Length > 0)
        {
            var detail = Text(_flow.Detail, 11, "IslandTextTertiaryBrush");
            detail.HorizontalAlignment = HorizontalAlignment.Center;
            detail.Margin = new Thickness(0, 4, 0, 0);
            column.Children.Add(detail);
        }
        string verb = row.Verb == PortVerb.Stop ? "Stop" : _flow.Force ? "Force end" : "End";
        var cancel = IslandButton("Cancel", CancelQuestion);
        var confirm = IslandButton(verb, () => _ = _flow.ConfirmAsync(), critical: true, shield: row.NeedsAdmin);
        AutomationProperties.SetName(confirm, _flow.Title.TrimEnd('?'));
        column.Children.Add(Buttons(cancel, confirm));
        _initial = cancel;
        _body.Children.Add(column);
    }

    private void CancelQuestion()
    {
        _flow.Cancel();
        if (_flow.Step == FlowStep.Idle) { _picking = _fromPicker; Render(); }
    }

    private void RenderWorking()
    {
        SetHeader(null);
        var row = _flow.Row!;
        var column = Centered(row);
        string name = row.Verb == PortVerb.Stop ? PortFlow.ServiceName(row) : row.Holder.Name;
        if (_flow.WaitsForAdmin)
        {
            column.Children.Add(Title("Waiting for admin"));
        }
        else if (_flow.ShowsClosing)
        {
            column.Children.Add(Title("Closing " + name));
            var fill = new Border { Height = 4, Width = 180 * _flow.Progress, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
            fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
            var track = new Border { Width = 180, Height = 4, CornerRadius = new CornerRadius(2), Child = fill, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
            column.Children.Add(track);
            var cancel = IslandButton("Cancel", () => _flow.Cancel());
            column.Children.Add(Buttons(cancel));
            _initial = cancel;
        }
        else
        {
            column.Children.Add(Title(_flow.Title.TrimEnd('?')));
        }
        _body.Children.Add(column);
    }

    // ---- the result, drawn as a Search row ----

    private readonly List<(Action Run, bool Verb)> _resultRows = [];
    private int _resultSelected = -1;

    private void RenderResult()
    {
        var outcome = _flow.Outcome!;
        int port = _flow.Port;
        var field = new Border { Height = 28, BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 0, 10, 0) };
        field.SetResourceReference(Border.BackgroundProperty, "IslandButtonBrush");
        field.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush");
        field.Child = Text($"port {port}", 13);
        SetHeader(field);

        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        _resultRows.Clear();
        UIElement lead;
        string? verb = outcome.Verb;
        bool dim = outcome.Kind == OutcomeKind.Neutral && outcome.Detail == "Already closed";
        switch (outcome.Kind)
        {
            case OutcomeKind.Freed: lead = Wrap(Glyph(LucideIcons.CircleCheck, 18, "SuccessBrush")); break;
            case OutcomeKind.Failed: lead = Wrap(Glyph(LucideIcons.TriangleAlert, 18, "WarningTextBrush")); break;
            default: lead = outcome.Row is { } held ? Mark(held.Holder, 20) : _flow.Row is { } was ? Mark(was.Holder, 20) : Wrap(Glyph(LucideIcons.Network, 16)); break;
        }
        string? detail = outcome.Kind == OutcomeKind.Freed ? null : outcome.Detail;
        bool caution = outcome.Kind is OutcomeKind.Kept;
        Action run = outcome.Verb switch
        {
            "Retry" => () => { _ = _flow.RetryAsync(); },
            "Free" or "Stop" when outcome.Row is { } row => () => { _fromPicker = false; _flow.Ask(row); },
            _ => () => OpenPorts(port),
        };
        _resultRows.Add((run, verb is not null));
        list.Children.Add(ResultRow(lead, outcome.Title, detail, verb, _resultRows.Count - 1, dim, caution, shield: outcome.Row is { NeedsAdmin: true } && verb is "Free" or "Stop"));
        _resultRows.Add((() => OpenPorts(port), true));
        list.Children.Add(ResultRow(Wrap(Glyph(LucideIcons.Network, 16)), "Ports", "Settings › System", "Open", _resultRows.Count - 1, false, false, false));
        _resultSelected = verb is not null ? 0 : -1;
        UpdateResultSelection(list);
        _body.Children.Add(list);
        _resultList = list;
    }

    private StackPanel? _resultList;

    private void ResultSelect(int index)
    {
        _resultSelected = index;
        if (_resultList is not null) UpdateResultSelection(_resultList);
    }

    private void UpdateResultSelection(StackPanel list)
    {
        for (int i = 0; i < list.Children.Count; i++)
        {
            if (list.Children[i] is not Button row) continue;
            row.Tag = i == _resultSelected ? "Selected" : null;
            if (row.Content is Grid grid)
                foreach (UIElement child in grid.Children)
                    if (child is FrameworkElement { Name: "verb" or "shield" } part)
                        part.Visibility = i == _resultSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static FrameworkElement Wrap(FrameworkElement glyph)
    {
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        return new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Children = { glyph } };
    }

    private Button ResultRow(UIElement lead, string name, string? detail, string? verb, int index, bool dim, bool caution, bool shield)
    {
        var line = new Grid();
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            line.ColumnDefinitions.Add(new() { Width = width });
        if (lead is FrameworkElement leadElement) leadElement.Margin = new Thickness(0, 0, 10, 0);
        Add(line, lead);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(Text(name));
        if (detail is not null) copy.Children.Add(Text(detail, 11, caution ? "WarningTextBrush" : "IslandTextSecondaryBrush"));
        Add(line, copy, column: 1);
        if (verb is not null)
        {
            if (shield)
            {
                var s = new UacShield(12) { Name = "shield", Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                Add(line, s, column: 2);
            }
            var v = Text(verb, 11);
            v.Name = "verb";
            v.FontWeight = FontWeights.SemiBold;
            v.Margin = new Thickness(shield ? 0 : 10, 0, 0, 0);
            Add(line, v, column: 3);
        }
        var button = new Button { Content = line, Height = 34, Padding = new Thickness(10, 0, 10, 0), Opacity = dim ? 0.6 : 1 };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(button, detail is null ? name : $"{name}, {detail}");
        button.Click += (_, _) => { if (verb is not null) _resultRows[index].Run(); };
        button.MouseMove += (_, _) => { if (verb is not null && _resultSelected != index) ResultSelect(index); };
        if (_initial is null && verb is not null) _initial = button;
        return button;
    }

    private void OpenPorts(int port) => _context.OpenSettings(PortManagerModule.SectionFor(port));

    public void Dispose()
    {
        if (_disposed) return;
        Follow(false);
        _disposed = true;
        _tick.Stop();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        PreviewKeyDown -= OnKey;
    }
}
