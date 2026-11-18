// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using static Chippi.Features.Uninstaller.Views.IslandUi;

namespace Chippi.Features.Uninstaller.Views;

/// <summary>What the island page shows.</summary>
public enum UninstallerPageMode { Apps, Scan, Question, Working, Result }

/// <summary>
/// The island's one Uninstaller page (docs/phase-7/design/uninstaller.html, sheets entry, confirm, pickers, run): the Uninstall app picker, the Scan
/// for leftovers picker, the question a Search row or a picker row asks, its run and its result, and "Stop uninstalling and quit?". It never scrolls:
/// the pickers page seven rows at a time. Nothing is uninstalled until the verb in the question is pressed; Cancel has the focus.
/// </summary>
public sealed class UninstallerView : ContentControl, IDisposable
{
    internal const int AppPage = 7, ScanPage = 6;
    /// <summary>The island page Cancel and Done return to when the page was not opened from a picker: Search.</summary>
    internal const string SearchPage = "command-bar";
    private readonly UninstallerEngine _engine;
    private readonly ModuleContext _context;
    private readonly Grid _header = new();
    private readonly Grid _body = new();
    private readonly SearchBox _field = new() { Placeholder = "Search apps", Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly SearchBox _scanField = new() { Placeholder = "Search", Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Button> _keyed = [];
    private UninstallerPageMode _mode = UninstallerPageMode.Apps;
    private AppEntry? _app;
    private AppOutcome? _outcome;
    private TaskCompletionSource<bool>? _quit;
    private List<AppEntry> _apps = [];
    private List<AppRecord> _records = [];
    private int _page, _selected;
    private bool _fromPicker, _following, _disposed, _typing;
    private Button? _initial;
    private string? _focusKey;

    /// <summary>The tests' stand-in for the control that has the keyboard (the page is never shown there).</summary>
    internal string? FocusedKeyOverride { get; set; }

    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public UninstallerView(UninstallerEngine engine, ModuleContext context)
    {
        _engine = engine;
        _context = context;
        Content = _body;
        Focusable = false;
        AutomationProperties.SetName(_field, "Search apps");
        AutomationProperties.SetName(_scanField, "Search");
        _field.TextChanged += (_, _) => Typed();
        _scanField.TextChanged += (_, _) => Typed();
        _field.PreviewKeyDown += OnPickerKey;
        _scanField.PreviewKeyDown += OnPickerKey;
        Loaded += (_, _) => Follow(true);
        Unloaded += (_, _) => Follow(false);
        PreviewKeyDown += OnKey;
        Render();
    }

    public FrameworkElement Header => _header;

    /// <summary>The control the keyboard starts on: the picker's field, or Cancel at a question.</summary>
    public UIElement? InitialFocus => _quit is null && _mode == UninstallerPageMode.Apps ? _field
        : _quit is null && _mode == UninstallerPageMode.Scan && _records.Count > 0 ? _scanField : (UIElement?)_initial ?? this;

    internal UninstallerPageMode Mode => _quit is not null ? UninstallerPageMode.Question : _mode;
    internal AppEntry? App => _app;
    internal AppOutcome? Outcome => _outcome;
    internal bool Quitting => _quit is not null;
    internal int Page => _page;
    internal int Selected => _selected;
    internal IReadOnlyList<AppEntry> Apps => _apps;
    internal IReadOnlyList<AppRecord> Records => _records;
    internal string Filter => _mode == UninstallerPageMode.Scan ? _scanField.Text : _field.Text;
    internal SearchBox Field => _field;
    internal SearchBox ScanField => _scanField;
    internal Button? FocusTarget => _focusKey is not null && _keyed.TryGetValue(_focusKey, out var button) ? button : null;

    /// <summary>Draws the page again now (the tests and shots hold the clock; a live page redraws on its own events).</summary>
    internal void Redraw() => Render();

    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) _engine.Changed += OnEngineChanged; else _engine.Changed -= OnEngineChanged;
        _following = enabled;
    }

    private void OnEngineChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnEngineChanged); return; }
        if (_mode is UninstallerPageMode.Apps or UninstallerPageMode.Working) Render();
    }

    // ---- the doors ----

    /// <summary>The Uninstall app picker, empty; the list is read again behind it.</summary>
    public void ShowPicker()
    {
        if (_disposed) return;
        if (_mode == UninstallerPageMode.Working && _engine.IsRunning) return;   // an uninstall under way keeps the page
        _mode = UninstallerPageMode.Apps;
        _fromPicker = false;
        _app = null;
        _outcome = null;
        ResetField(_field);
        _page = _selected = 0;
        Render();
        _ = RefreshAsync();
    }

    /// <summary>The Scan for leftovers picker of recently uninstalled apps.</summary>
    public void ShowScanPicker()
    {
        if (_disposed) return;
        if (_mode == UninstallerPageMode.Working && _engine.IsRunning) return;
        _mode = UninstallerPageMode.Scan;
        _fromPicker = false;
        _app = null;
        _outcome = null;
        ResetField(_scanField);
        _page = _selected = 0;
        Render();
    }

    private void ResetField(SearchBox field)
    {
        _typing = true;
        field.Text = "";
        _typing = false;
    }

    private async Task RefreshAsync()
    {
        try { await _engine.RefreshAsync(); }
        catch (OperationCanceledException) { }
        if (!_disposed && _mode == UninstallerPageMode.Apps) Render();
    }

    /// <summary>The question for one app (a Search row's Run, or a picker row): "Uninstall Notepad++?". Nothing runs until Uninstall is pressed.</summary>
    public void Ask(AppEntry app, bool fromPicker = false)
    {
        if (_disposed || _mode == UninstallerPageMode.Working && _engine.IsRunning) return;
        _app = app;
        _outcome = null;
        _fromPicker = fromPicker;
        _mode = UninstallerPageMode.Question;
        Follow(IsLoaded || _following);
        Render();
    }

    /// <summary>"Stop uninstalling and quit?": true when Quit was pressed.</summary>
    public Task<bool> AskQuitAsync()
    {
        _quit?.TrySetResult(false);
        _quit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Render();
        return _quit.Task;
    }

    private void AnswerQuit(bool quit)
    {
        var answer = _quit;
        _quit = null;
        Render();
        answer?.TrySetResult(quit);
    }

    /// <summary>Back: leaves a question or a result for the picker it came from; false when the page should go back itself.</summary>
    public bool Back()
    {
        if (_quit is not null) { AnswerQuit(false); return true; }
        if (_mode is UninstallerPageMode.Question or UninstallerPageMode.Result)
        {
            bool toPicker = _fromPicker;
            _mode = UninstallerPageMode.Apps;
            _app = null;
            _outcome = null;
            if (toPicker) Render();
            return toPicker;
        }
        return false;
    }

    /// <summary>Cancel and Done: back to the picker the page came from, else to Search (or closed when Search isn't there).</summary>
    private void Leave()
    {
        bool toPicker = _fromPicker;
        _mode = UninstallerPageMode.Apps;
        _app = null;
        _outcome = null;
        if (toPicker) { Render(); return; }
        if (!_context.TryGoBackIslandPage(SearchPage)) _context.CloseIsland();
    }

    /// <summary>Uninstall at the question, or Retry at a result: runs the app's own uninstaller.</summary>
    internal async void Confirm()
    {
        if (_app is not { } app || _mode is not (UninstallerPageMode.Question or UninstallerPageMode.Result)) return;
        _mode = UninstallerPageMode.Working;
        _outcome = null;
        Render();
        IReadOnlyList<AppOutcome> outcomes;
        try { outcomes = await _engine.UninstallAsync([app]); }
        catch (OperationCanceledException) { outcomes = []; }
        _outcome = outcomes.FirstOrDefault(o => o.App.Id == app.Id) ?? new AppOutcome(app, new AppStatus(AppRunState.Failed, app.Name));
        _mode = UninstallerPageMode.Result;
        if (!_disposed) Render();
    }

    /// <summary>Scan at a result or a picker row: Settings' leftover review for that app.</summary>
    internal void Scan(Guid? recordId)
    {
        _context.OpenSettings(recordId is { } id ? UninstallerModule.ScanSectionFor(id) : UninstallerModule.RecentSection);
    }

    // ---- keys ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_quit is not null) { AnswerQuit(false); e.Handled = true; }
        else if (_mode is UninstallerPageMode.Question or UninstallerPageMode.Result)
        {
            Leave();
            e.Handled = true;
        }
    }

    private void OnPickerKey(object sender, KeyEventArgs e) => e.Handled |= PickerKey(e.Key);

    /// <summary>A key in a picker's field: Up and Down move through the pages, Enter asks (or scans) the selected row. True when it was used.</summary>
    internal bool PickerKey(Key key)
    {
        int count = _mode == UninstallerPageMode.Scan ? _records.Count : _apps.Count;
        switch (key)
        {
            case Key.Down when count > 0: Select(Math.Min(_selected + 1, count - 1)); return true;
            case Key.Up when count > 0: Select(Math.Max(_selected - 1, 0)); return true;
            case Key.PageDown: TurnPage(1); return true;
            case Key.PageUp: TurnPage(-1); return true;
            case Key.Enter when _selected >= 0 && _selected < count: Choose(_selected); return true;
            default: return false;
        }
    }

    private int PageSize => _mode == UninstallerPageMode.Scan ? ScanPage : AppPage;

    private void Select(int index)
    {
        if (index == _selected) return;
        _selected = index;
        _page = index / PageSize;
        Render();
    }

    internal void TurnPage(int delta)
    {
        int count = _mode == UninstallerPageMode.Scan ? _records.Count : _apps.Count;
        int pages = Math.Max(1, (count + PageSize - 1) / PageSize);
        int next = Math.Clamp(_page + delta, 0, pages - 1);
        if (next == _page) return;
        _page = next;
        _selected = next * PageSize;
        Render();
    }

    /// <summary>Enter or a click on a row: the same question, or the leftover review.</summary>
    internal void Choose(int index)
    {
        if (_mode == UninstallerPageMode.Scan)
        {
            if (index >= 0 && index < _records.Count && !_records[index].Restart) Scan(_records[index].Id);
        }
        else if (index >= 0 && index < _apps.Count) Ask(_apps[index], fromPicker: true);
    }

    private void Typed()
    {
        if (_typing || _disposed) return;
        _page = _selected = 0;
        Render();
    }

    // ---- drawing ----

    private void Render()
    {
        if (_disposed) return;
        string? key = FocusedKeyOverride ?? CurrentFocusKey();
        _initial = null;
        _keyed.Clear();
        _body.Children.Clear();
        _body.RowDefinitions.Clear();
        _header.Children.Clear();
        if (_quit is not null) RenderQuit();
        else switch (_mode)
        {
            case UninstallerPageMode.Question: RenderQuestion(); break;
            case UninstallerPageMode.Working: RenderWorking(); break;
            case UninstallerPageMode.Result: RenderResult(); break;
            case UninstallerPageMode.Scan: RenderScan(); break;
            default: RenderApps(); break;
        }
        // Every redraw builds new buttons: the one that had the keyboard gets it back; a question and a result give it to their primary.
        _focusKey = key is not null && _keyed.ContainsKey(key) ? key : _initialKey;
        if (_focusKey is not null) Dispatcher.BeginInvoke(DispatcherPriority.Input, RestoreFocus);
    }

    private string? _initialKey;

    private string? CurrentFocusKey()
    {
        if (!_body.IsKeyboardFocusWithin) return null;
        return (Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
    }

    private void RestoreFocus()
    {
        if (_disposed || !IsLoaded || FocusTarget is not { IsVisible: true, IsEnabled: true } target || target.IsKeyboardFocusWithin) return;
        target.Focus();
    }

    private Button Keyed(Button button, string key)
    {
        button.Tag = key;
        _keyed[key] = button;
        return button;
    }

    private void Title(string text)
    {
        var title = Text(text, 13);
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        _header.Children.Add(title);
    }

    private StackPanel Column() => new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Line(string text, bool caution = false, double size = 14)
    {
        var label = Text(text, size, caution ? "WarningTextBrush" : "IslandTextBrush");
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        return label;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var button in buttons) { if (strip.Children.Count > 0) button.Margin = new Thickness(8, 0, 0, 0); strip.Children.Add(button); }
        return strip;
    }

    /// <summary>The centered states: a 28-DIP mark, one line, what moves under it, and the buttons.</summary>
    private void RenderCentered(UIElement mark, string text, bool caution = false, UIElement? extra = null, params Button[] buttons)
    {
        var column = Column();
        if (mark is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(mark);
        var label = Line(text, caution);
        label.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(label);
        if (extra is not null) column.Children.Add(extra);
        if (buttons.Length > 0) column.Children.Add(Buttons(buttons));
        _body.Children.Add(column);
    }

    private void RenderQuestionLine(string text, Button cancel, Button verb)
    {
        var column = Column();
        column.Children.Add(Line(text));
        column.Children.Add(Buttons(cancel, verb));
        _body.Children.Add(column);
        _initial = cancel;
        _initialKey = "cancel";
    }

    private void RenderQuit()
    {
        var cancel = Keyed(IslandButton("Cancel", () => AnswerQuit(false)), "cancel");
        var quit = Keyed(IslandButton("Quit", () => AnswerQuit(true), critical: true), "quit");
        RenderQuestionLine("Stop uninstalling and quit?", cancel, quit);
    }

    private void RenderQuestion()
    {
        var app = _app!;
        var cancel = Keyed(IslandButton("Cancel", Leave), "cancel");
        var uninstall = Keyed(IslandButton("Uninstall", Confirm, critical: true, shield: app.NeedsAdmin), "uninstall");
        AutomationProperties.SetName(uninstall, UninstallerText.Question([app]).TrimEnd('?'));
        RenderQuestionLine(UninstallerText.Question([app]), cancel, uninstall);
    }

    private void RenderWorking()
    {
        Title("Uninstall");
        _initialKey = null;
        var app = _app!;
        if (_engine.WaitsForAdmin) RenderCentered(new UacShield(28), "Waiting for admin");
        else if (app.IsQuiet) RenderCentered(Spinner(28), "Uninstalling " + app.Name, extra: Indeterminate());
        else RenderCentered(Glyph(LucideIcons.AppWindow, 28), "Uninstalling " + app.Name);
    }

    private void RenderResult()
    {
        Title("Uninstall");
        var outcome = _outcome!;
        var app = outcome.App;
        var done = Keyed(IslandButton("Done", Leave, accent: outcome.Status.State == AppRunState.RestartNeeded), "done");
        _initialKey = "done";
        switch (outcome.Status.State)
        {
            case AppRunState.Uninstalled:
                var scan = Keyed(IslandButton("Scan", () => Scan(outcome.RecordId), accent: true), "scan");
                RenderCentered(Glyph(LucideIcons.CircleCheck, 28), app.Name + " uninstalled", false, null, done, scan);
                _initialKey = "scan";
                break;
            case AppRunState.RestartNeeded:
                RenderCentered(Glyph(UninstallerIcons.RotateCw, 28), "Restart to finish", false, null, done);
                break;
            case AppRunState.NeedsAdmin:
                var admin = Keyed(IslandButton("Retry", Confirm, accent: true, shield: true), "retry");
                RenderCentered(new UacShield(28), "Needs admin", true, null, done, admin);
                _initialKey = "retry";
                break;
            case AppRunState.Running:
                var again = Keyed(IslandButton("Retry", Confirm, accent: true), "retry");
                RenderCentered(Glyph(LucideIcons.AppWindow, 28), app.Name + " is running", true, null, done, again);
                _initialKey = "retry";
                break;
            default:
                var retry = Keyed(IslandButton("Retry", Confirm, accent: true), "retry");
                RenderCentered(Glyph(LucideIcons.CircleAlert, 28), "Uninstall failed", true, null, done, retry);
                _initialKey = "retry";
                break;
        }
        _initial = _keyed.GetValueOrDefault(_initialKey!);
    }

    // ---- the pickers: seven 36-DIP rows a page, as drawn ----

    private IReadOnlyList<AppEntry> Filtered()
    {
        string query = _field.Text.Trim();
        string key = UninstallerText.NameKey(query);
        var all = _engine.Apps.Where(a => a.CanUninstall && (query.Length == 0 || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || key.Length > 0 && UninstallerText.NameKey(a.Name).Contains(key, StringComparison.Ordinal)));
        return [.. all.OrderBy(a => query.Length == 0 || a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private void RenderApps()
    {
        _initialKey = null;
        _header.Children.Add(_field);
        _apps = [.. Filtered()];
        _selected = _apps.Count == 0 ? 0 : Math.Clamp(_selected, 0, _apps.Count - 1);
        int pages = Math.Max(1, (_apps.Count + AppPage - 1) / AppPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        if (_apps.Count == 0)
        {
            if (!_engine.Loaded && _field.Text.Length == 0) { var spin = Column(); spin.Children.Add(Spinner(24)); _body.Children.Add(spin); }
            else Empty(LucideIcons.Search, "No apps match");
            return;
        }
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0) };
        for (int i = _page * AppPage; i < Math.Min(_apps.Count, (_page + 1) * AppPage); i++) list.Children.Add(AppRow(_apps[i], i));
        _body.Children.Add(list);
        _body.Children.Add(Footer(_apps.Count, AppPage, "app", "apps", "Previous apps", "Next apps"));
    }

    private void Empty(System.Windows.Media.Geometry glyph, string text)
    {
        var column = Column();
        var mark = Glyph(glyph, 24);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(mark);
        var label = Line(text);
        label.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(label);
        _body.Children.Add(column);
    }

    /// <summary>"1–7 of 41" with Previous and Next; "5 apps" when everything fits.</summary>
    private FrameworkElement Footer(int count, int size, string one, string many, string previousName, string nextName)
    {
        int pages = Math.Max(1, (count + size - 1) / size);
        var footer = new Grid { Height = 26, VerticalAlignment = VerticalAlignment.Bottom };
        footer.ColumnDefinitions.Add(new());
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        string text = count > size
            ? string.Create(CultureInfo.InvariantCulture, $"{_page * size + 1}–{Math.Min((_page + 1) * size, count)} of {count}")
            : string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
        Add(footer, Text(text, 11, "IslandTextTertiaryBrush"));
        if (count > size)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var previous = Keyed(IslandIcon(LucideIcons.ChevronLeft, previousName, () => TurnPage(-1)), "previous");
            var next = Keyed(IslandIcon(LucideIcons.ChevronRight, nextName, () => TurnPage(1)), "next");
            previous.IsEnabled = _page > 0;
            next.IsEnabled = _page + 1 < pages;
            buttons.Children.Add(previous);
            buttons.Children.Add(next);
            Add(footer, buttons, column: 1);
        }
        return footer;
    }

    /// <summary>One 36-DIP row: lead, name with its badges over "publisher · size", and on the selected row the shield column and the verb.</summary>
    private Border Row(FrameworkElement lead, FrameworkElement name, string detail, bool warn, int index, string? verb, bool shield, string accessibleName, Action choose)
    {
        var line = new Grid { Height = 36 };
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(16), GridLength.Auto })
            line.ColumnDefinitions.Add(new() { Width = width });
        lead.Margin = new Thickness(0, 0, 10, 0);
        Add(line, lead);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(name);
        var second = Text(detail, 11, warn ? "WarningTextBrush" : "IslandTextSecondaryBrush");
        copy.Children.Add(second);
        Add(line, copy, column: 1);
        bool selected = index == _selected;
        if (selected && verb is not null)
        {
            if (shield) Add(line, new UacShield(12) { HorizontalAlignment = HorizontalAlignment.Center }, column: 2);
            var word = Text(verb, 11);
            word.FontWeight = FontWeights.SemiBold;
            word.Margin = new Thickness(4, 0, 0, 0);
            Add(line, word, column: 3);
        }
        var host = new Border { Child = line, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 0, 10, 0), Background = System.Windows.Media.Brushes.Transparent };
        if (selected) host.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        AutomationProperties.SetName(host, accessibleName);
        host.MouseMove += (_, _) => { if (_selected != index) { _selected = index; Render(); } };
        host.MouseLeftButtonUp += (_, _) => choose();
        return host;
    }

    private Border AppRow(AppEntry app, int index)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(Text(app.Name));
        if (app.IsStore) name.Children.Add(Badge("Store", false));
        if (app.IsRecent(Now())) name.Children.Add(Badge("New", true));
        string size = UninstallerText.Size(app.SizeBytes);
        string detail = string.IsNullOrEmpty(app.Publisher) ? size : size.Length == 0 ? app.Publisher : app.Publisher + " · " + size;
        var row = Row(Mark(app.Name, app.IconPath, 22), name, detail, false, index, "Uninstall", app.NeedsAdmin, "Uninstall " + app.Name, () => Choose(index));
        row.ToolTip = app.Name;
        return row;
    }

    // ---- Scan for leftovers ----

    private string Caption(AppRecord record)
    {
        var when = record.CreatedUtc.ToLocalTime().Date;
        var today = Now().Date;
        string day = when == today ? "Today" : when == today.AddDays(-1) ? "Yesterday" : when.ToString("MMM d", CultureInfo.InvariantCulture);
        if (record.Restart) return day + " · Restart to finish";
        return record.HasRecord ? day : day + " · No record";
    }

    private void RenderScan()
    {
        _initialKey = null;
        Title("Scan for leftovers");
        string query = _scanField.Text.Trim();
        var all = _engine.Recent.Where(r => !r.Cleaned).ToList();
        _records = [.. all.Where(r => query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
        if (all.Count == 0) { Empty(UninstallerIcons.History, "Nothing uninstalled"); return; }
        _selected = _records.Count == 0 ? 0 : Math.Clamp(_selected, 0, _records.Count - 1);
        int pages = Math.Max(1, (_records.Count + ScanPage - 1) / ScanPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        _body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new());
        _scanField.Margin = new Thickness(0, 0, 0, 8);
        Add(_body, _scanField);
        if (_records.Count == 0)
        {
            var empty = Column();
            var mark = Glyph(LucideIcons.Search, 24);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(mark);
            var label = Line("No apps match");
            label.Margin = new Thickness(0, 10, 0, 0);
            empty.Children.Add(label);
            Add(_body, empty, row: 1);
            return;
        }
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        for (int i = _page * ScanPage; i < Math.Min(_records.Count, (_page + 1) * ScanPage); i++)
        {
            var record = _records[i];
            int index = i;
            list.Children.Add(Row(Mark(record.Name, null, 22), Text(record.Name), Caption(record), !record.HasRecord || record.Restart, i,
                record.Restart ? null : "Scan", false, "Scan " + record.Name, () => Choose(index)));
        }
        Add(_body, list, row: 1);
        var footer = Footer(_records.Count, ScanPage, "app", "apps", "Previous apps", "Next apps");
        if (_records.Count > ScanPage) Add(_body, footer, row: 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Follow(false);
        _disposed = true;
        _quit?.TrySetResult(false);
    }
}
