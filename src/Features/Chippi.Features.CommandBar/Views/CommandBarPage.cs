// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.Features.CommandBar.Search;
using Chippi.UI.Controls;

namespace Chippi.Features.CommandBar.Views;

/// <summary>
/// The Command bar's page (docs/phase-5.5/command-bar.md, The page): the field is the header, Back then a 32-tall search
/// field; under it whole 34-tall rows, 8 at most, never scrolling. Up and Down (or the pointer) move the selection, Enter
/// runs it and the panel closes, Esc clears the field and then closes, Shift+Delete or the eye-off button hides it.
/// </summary>
internal sealed class CommandBarPage : UserControl
{
    /// <summary>How long typing pauses before Windows Search is asked.</summary>
    internal static readonly TimeSpan FilesAfter = TimeSpan.FromMilliseconds(120);

    /// <summary>The most files asked for: they share the rows with everything else.</summary>
    private const int FileCount = 8;

    private const double RowHeight = 34;

    private readonly CommandBarModule _module;
    private readonly SearchResultList _rows = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid _status = new() { Height = 24, Margin = new Thickness(10, 0, 6, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _statusText = Ui.Text(size: 11, brush: "IslandTextSecondaryBrush");
    private readonly DispatcherTimer _filesTimer;
    private readonly List<RowView> _rendered = [];
    private IPc? _subscribedPc;
    private Action? _appsChanged;
    private Action? _settingsChanged;
    private bool _filesEnabled;
    private Button? _statusButton;
    private string? _statusActionKey;
    private IReadOnlyList<Result> _shown = [];
    private IReadOnlyList<OpenWindow> _windows = [];
    private IReadOnlyList<FoundFile> _files = [];
    private string _filesFor = "";
    private CancellationTokenSource? _finding;
    private int _selected;
    private bool _deliberateSelection;
    private bool _active;
    private bool _searchingFiles;
    private bool _filesUnavailable;
    private long _visit;
    private long _queryVersion;
    private long _runVersion;
    private long _renderVersion;
    private string? _running;
    private string? _failed; // the key of the row whose run failed, until the next change
    private string? _failure;
    private string? _saveFailure;
    private Result? _hidden;
    private Point? _pointer;

    public CommandBarPage(CommandBarModule module)
    {
        _module = module;
        Field = new SearchBox { Placeholder = "Search", Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(Field, "Search");
        Field.TextChanged += (_, _) => Typed();
        Field.PreviewKeyDown += OnKey;
        AutomationProperties.SetName(_rows, "Search results");
        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Polite);
        _status.ColumnDefinitions.Add(new ColumnDefinition());
        _status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Children.Add(_statusText);
        Content = new StackPanel { Children = { _rows, _status } };
        _filesTimer = new DispatcherTimer(FilesAfter, DispatcherPriority.Input, (_, _) => FindFiles(), Dispatcher) { IsEnabled = false };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Opened();
            else
                Deactivate();
        };
        Unloaded += (_, _) => Deactivate();
    }

    /// <summary>The page's header, the field: it takes the keyboard as the page opens.</summary>
    public SearchBox Field { get; }

    public FrameworkElement Header => Field;

    /// <summary>The rows showing, top first (tests).</summary>
    internal IReadOnlyList<Result> Shown => _shown;

    /// <summary>The selected row's index (tests).</summary>
    internal int Selected => _selected;

    /// <summary>The modifier keys down; tests stand in for the real keyboard.</summary>
    internal Func<ModifierKeys> Modifiers { get; set; } = () => Keyboard.Modifiers;

    /// <summary>The first Esc clears the field; true when it did, and the island stays open.</summary>
    public bool Escape()
    {
        if (Field.Text.Length == 0)
            return false;
        Field.Text = "";
        return true;
    }

    /// <summary>Each open starts empty, on your most used, with the windows open now and the Start menu read again.</summary>
    private void Opened()
    {
        Deactivate();
        _active = true;
        Subscribe();
        _failed = null;
        _failure = null;
        _saveFailure = null;
        _hidden = null;
        _files = [];
        _filesFor = "";
        _filesUnavailable = false;
        _searchingFiles = false;
        _pointer = null;
        try { _windows = _module.Pc?.Windows() ?? []; }
        catch (Exception ex)
        {
            Log.Warn("Command bar: listing the windows failed", ex);
            _windows = [];
        }
        _module.Pc?.RefreshApps();
        if (Field.Text.Length > 0)
            Field.Text = ""; // refreshes
        else
            Refresh(keepSelection: false);
    }

    /// <summary>Invalidate work from this visit before the page is hidden or its module stops.</summary>
    internal void Deactivate()
    {
        _active = false;
        Interlocked.Increment(ref _visit);
        _running = null;
        CancelFiles();
        if (_subscribedPc is { } pc && _appsChanged is { } appsChanged)
            pc.AppsChanged -= appsChanged;
        _subscribedPc = null;
        _appsChanged = null;
        if (_settingsChanged is { } settingsChanged)
            _module.Changed -= settingsChanged;
        _settingsChanged = null;
    }

    private void CancelFiles()
    {
        _filesTimer.Stop();
        // A module stop can arrive off the UI thread as a completed request disposes its own source.
        var finding = Interlocked.Exchange(ref _finding, null);
        try { finding?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Subscribe()
    {
        long visit = _visit;
        _filesEnabled = !_module.Settings.Off.Contains(ResultSwitch.Files);
        if (_module.Pc is { } pc)
        {
            _subscribedPc = pc;
            _appsChanged = () => Dispatch(() => { if (_active && visit == _visit) Refresh(); });
            pc.AppsChanged += _appsChanged;
        }
        _settingsChanged = () => Dispatch(() => { if (_active && visit == _visit) SettingsChanged(); });
        _module.Changed += _settingsChanged;
    }

    private void Dispatch(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    private void SettingsChanged()
    {
        bool filesEnabled = !_module.Settings.Off.Contains(ResultSwitch.Files);
        if (_filesEnabled != filesEnabled)
        {
            _filesEnabled = filesEnabled;
            CancelFiles();
            _files = [];
            _filesFor = "";
            _filesUnavailable = false;
            _searchingFiles = filesEnabled && Field.Text.Trim().Length >= 2;
            if (_searchingFiles) _filesTimer.Start();
        }
        if (_hidden is { } hidden && !_module.Settings.Hidden.Exists(result => result.Key == hidden.Key))
            _hidden = null;
        Refresh();
    }

    private void Typed()
    {
        if (!_active) return;
        _queryVersion++;
        _failed = null;
        _failure = null;
        _saveFailure = null;
        _hidden = null;
        _filesUnavailable = false;
        CancelFiles();
        _searchingFiles = Field.Text.Trim().Length >= 2 && !_module.Settings.Off.Contains(ResultSwitch.Files);
        Refresh(keepSelection: false);
        if (_searchingFiles)
            _filesTimer.Start();
    }

    private async void FindFiles()
    {
        CancelFiles();
        if (!_active || _module.Pc is not { } pc)
            return;
        string text = Field.Text.Trim();
        if (text.Length < 2 || _module.Settings.Off.Contains(ResultSwitch.Files))
            return;
        long visit = _visit;
        long queryVersion = _queryVersion;
        using var finding = new CancellationTokenSource();
        _finding = finding;
        _searchingFiles = true;
        _filesUnavailable = false;
        RenderStatus();
        try
        {
            var files = await pc.FindFilesAsync(text, FileCount, finding.Token);
            if (finding.IsCancellationRequested || visit != _visit || queryVersion != _queryVersion)
                return;
            _files = files;
            _filesFor = text;
            _searchingFiles = false;
            Refresh();
        }
        catch (OperationCanceledException) when (finding.IsCancellationRequested || visit != _visit || queryVersion != _queryVersion) { }
        catch (Exception ex)
        {
            if (finding.IsCancellationRequested || visit != _visit || queryVersion != _queryVersion)
                return;
            Log.Warn("Command bar: finding files failed", ex);
            _searchingFiles = false;
            _filesUnavailable = true;
            RenderStatus();
        }
        finally
        {
            if (ReferenceEquals(_finding, finding))
                _finding = null;
        }
    }

    /// <summary>
    /// Typing chooses the first row; arriving local results can replace that automatic choice. Once the user navigates,
    /// refreshes preserve the chosen identity, including the empty-query list and an explicitly chosen web fallback.
    /// </summary>
    internal void Refresh(bool keepSelection = true)
    {
        if (_module.Search is not { } search)
            return;
        string text = Field.Text.Trim();
        if (!keepSelection)
            _deliberateSelection = false;
        string? selected = _deliberateSelection && _selected < _shown.Count ? _shown[_selected].Key : null;
        _shown = search.Find(text, _windows, _filesFor == text ? _files : []);
        int at = selected is null ? -1 : IndexOf(selected);
        _selected = at >= 0 ? at : 0;
        Render(rebuild: true);
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < _shown.Count; i++)
            if (_shown[i].Key == key)
                return i;
        return -1;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _shown.Count > 0:
                Select(Math.Min(_selected + 1, _shown.Count - 1));
                e.Handled = true;
                break;
            case Key.Up when _shown.Count > 0:
                Select(Math.Max(_selected - 1, 0));
                e.Handled = true;
                break;
            case Key.Enter when _selected < _shown.Count:
                Run(_shown[_selected]);
                e.Handled = true;
                break;
            case Key.Delete when Modifiers() == ModifierKeys.Shift && _selected < _shown.Count && _shown[_selected].CanHide:
                Hide(_shown[_selected]);
                e.Handled = true;
                break;
        }
    }

    private void Select(int index)
    {
        if (_running is not null) return;
        _deliberateSelection = true;
        if (index == _selected)
            return;
        _selected = index;
        _failed = null;
        _failure = null;
        Render();
    }

    /// <summary>Runs <paramref name="result"/>: the panel closes after, unless the row moved the island to another page.</summary>
    internal async void Run(Result result)
    {
        if (!_active || _running is not null)
            return;
        long visit = _visit;
        long queryVersion = _queryVersion;
        long runVersion = ++_runVersion;
        var search = _module.Search;
        string text = Field.Text;
        _running = result.Key;
        _failed = null;
        _failure = null;
        Render();
        try
        {
            await result.Run();
            if (ReferenceEquals(search, _module.Search))
                search?.Picked(text, result);
            if (_active && visit == _visit && queryVersion == _queryVersion)
                _module.Close();
        }
        catch (Exception ex)
        {
            Log.Warn($"Command bar: {result.Verb} failed", ex);
            if (!_active || visit != _visit || queryVersion != _queryVersion)
                return;
            _failed = result.Key;
            _failure = Failure(result, ex);
        }
        finally
        {
            if (visit == _visit && runVersion == _runVersion)
            {
                _running = null;
                Render();
            }
        }
    }

    private static string Failure(Result result, Exception ex) => ex switch
    {
        FileNotFoundException => "File not found",
        DirectoryNotFoundException => "Folder not found",
        UnauthorizedAccessException => "Access denied",
        Win32Exception { NativeErrorCode: 2 or 3 } => "File not found",
        Win32Exception { NativeErrorCode: 5 } => "Access denied",
        InvalidOperationException { Message: "Window closed" } => "Window closed",
        InvalidOperationException { Message: "Clipboard unavailable" } => "Clipboard unavailable",
        InvalidOperationException { Message: "Snippet not inserted" or "Not working in admin windows" } => ex.Message,
        _ => $"{result.Verb} failed · Try again",
    };

    private void Hide(Result result)
    {
        if (_running is not null)
            return;
        if (_module.Hide(result))
        {
            _hidden = result;
            _saveFailure = null;
            _failed = null;
            _failure = null;
        }
        else
            _saveFailure = _module.SettingsError ?? "Couldn’t hide result";
        Refresh();
    }

    internal void UndoHidden()
    {
        if (_hidden is not { } hidden)
            return;
        if (!_module.Unhide(hidden.Key))
        {
            _saveFailure = _module.SettingsError ?? "Couldn’t restore result";
            RenderStatus();
            return;
        }
        _hidden = null;
        _saveFailure = null;
        Refresh();
        int index = IndexOf(hidden.Key);
        if (index >= 0)
            Select(index);
        Field.Focus();
    }

    private void Render(bool rebuild = false)
    {
        if (rebuild)
        {
            _renderVersion++;
            _rows.Children.Clear();
            _rows.Items.Clear();
            _rendered.Clear();
            foreach (var result in _shown)
            {
                var row = Row(result);
                _rendered.Add(row);
                _rows.Children.Add(row.Root);
                _rows.Items.Add(row.Button);
            }
        }
        for (int i = 0; i < _rendered.Count; i++)
            UpdateRow(_rendered[i], i);
        RenderStatus();
        AnnounceSelection();
    }

    private void RenderStatus()
    {
        string text;
        string? action = null;
        Action? invoke = null;
        if (_running is not null && !_shown.Any(result => result.Key == _running))
            text = "Working…";
        else if (_saveFailure is { } saveFailure)
        {
            text = saveFailure;
            if (_hidden is not null) { action = "Undo"; invoke = UndoHidden; }
        }
        else if (_failure is { } failure)
            text = failure;
        else if (_hidden is not null)
        {
            text = "Hidden";
            action = "Undo";
            invoke = UndoHidden;
        }
        else if (_filesUnavailable)
        {
            text = "Files unavailable";
            action = "Retry";
            invoke = () => { FindFiles(); Field.Focus(); };
        }
        else if (_searchingFiles)
            text = "Searching files…";
        else
            text = _shown.Count == 0 ? "No results" : "";

        bool changed = _statusText.Text != text;
        _statusText.Text = text;
        _statusText.ToolTip = text;
        _status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        string? actionKey = action is null ? null : $"{_visit}|{_queryVersion}|{action}|{_hidden?.Key}";
        if (actionKey != _statusActionKey)
        {
            bool focused = _statusButton?.IsKeyboardFocusWithin == true;
            if (_statusButton is not null) _status.Children.Remove(_statusButton);
            _statusButton = null;
            _statusActionKey = actionKey;
            if (action is not null && invoke is not null)
            {
                long visit = _visit, queryVersion = _queryVersion;
                string? hiddenKey = _hidden?.Key;
                var button = new Button { Content = action, Height = 24, MinWidth = 44, Padding = new Thickness(8, 0, 8, 0), FontSize = 11 };
                button.SetResourceReference(StyleProperty, "IslandListRowStyle");
                AutomationProperties.SetName(button, action == "Undo" ? $"Undo hiding {_hidden?.Name}" : "Retry file search");
                button.Click += (_, _) =>
                {
                    if (ReferenceEquals(_statusButton, button) && _active && visit == _visit && queryVersion == _queryVersion
                        && (action == "Undo" ? hiddenKey == _hidden?.Key : _filesUnavailable))
                        invoke();
                };
                Grid.SetColumn(button, 1);
                _status.Children.Add(button);
                _statusButton = button;
            }
            if (focused) Field.Focus();
        }
        if (changed)
            UIElementAutomationPeer.FromElement(_statusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void AnnounceSelection()
    {
        string help = _running is not null ? "Working. Wait for this action to finish."
            : _selected < _shown.Count
            ? $"{AccessibleName(_shown[_selected])}, {_selected + 1} of {_shown.Count}. Up and Down to choose; Enter to {_shown[_selected].Verb.ToLowerInvariant()}."
            : _statusText.Text;
        bool changed = AutomationProperties.GetHelpText(Field) != help;
        AutomationProperties.SetHelpText(Field, help);
        if (changed && UIElementAutomationPeer.FromElement(Field) is { } fieldPeer)
            fieldPeer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, help, "Search selection");
        if (_selected < _rows.Items.Count)
            _rows.Items[_selected].AnnounceSelected();
    }

    private string AccessibleName(Result result) => $"{result.Name}, {result.FullPath ?? result.Detail}, {result.Verb}" +
        (result.Key == _failed && _failure is not null ? $", {_failure}" : "");

    // A row: the 20 lead, the name in 12 over its detail in 11, a level bar for a volume, and the selected row's
    // verb in primary semibold ink at its right with the eye-off button at its end.
    private RowView Row(Result result)
    {
        long visit = _visit, queryVersion = _queryVersion, renderVersion = _renderVersion;
        void Choose()
        {
            int at = IndexOf(result.Key);
            if (_active && visit == _visit && queryVersion == _queryVersion && renderVersion == _renderVersion && at >= 0) Select(at);
        }
        void Execute()
        {
            if (_active && visit == _visit && queryVersion == _queryVersion && renderVersion == _renderVersion && IndexOf(result.Key) >= 0) Run(result);
        }
        var name = Ui.Text(result.Name, 12);
        var detail = Ui.Text(result.Detail, 11, "IslandTextSecondaryBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } };
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lead = Lead(result);
        lead.Margin = new Thickness(0, 0, 10, 0);
        line.Children.Add(lead);
        Grid.SetColumn(text, 1);
        line.Children.Add(text);
        if (result.Level is { } level)
        {
            var bar = Level(level);
            Grid.SetColumn(bar, 2);
            line.Children.Add(bar);
        }
        var verb = Ui.Text(result.Verb, 11);
        verb.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(verb, 3);
        line.Children.Add(verb);
        var button = new SearchResultButton(_rows, Choose, Execute)
        {
            Content = line, Height = RowHeight, Padding = new Thickness(10, 0, 10, 0), Focusable = false,
            ToolTip = result.FullPath ?? $"{result.Name}\n{result.Detail}",
        };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        if (result.CanHide)
            AutomationProperties.SetHelpText(button, "Shift+Delete to hide. Undo is available after hiding.");
        button.Click += (_, _) => Execute();
        button.MouseMove += (_, e) =>
        {
            // Only a pointer that moved selects: rows changing under a still pointer leave the selection alone.
            var at = e.GetPosition(this);
            if (_pointer == at)
                return;
            _pointer = at;
            Choose();
        };
        if (!result.CanHide)
            return new RowView(result, button, button, detail, verb, null);
        var hide = Ui.Icon(LucideIcons.EyeOff, $"Hide {result.Name}", "Hide");
        hide.HorizontalAlignment = HorizontalAlignment.Right;
        hide.VerticalAlignment = VerticalAlignment.Center;
        hide.Margin = new Thickness(0, 0, 6, 0);
        hide.Click += (_, _) =>
        {
            if (_active && visit == _visit && queryVersion == _queryVersion && renderVersion == _renderVersion) Hide(result);
        };
        button.HideAction = hide;
        return new RowView(result, new Grid { Children = { button, hide } }, button, detail, verb, hide);
    }

    private void UpdateRow(RowView row, int index)
    {
        bool selected = index == _selected;
        bool running = _running == row.Result.Key;
        bool hide = selected && row.Result.CanHide && _running is null;
        row.Detail.SetResourceReference(TextBlock.ForegroundProperty, selected ? "IslandTextBrush" : "IslandTextSecondaryBrush");
        row.Detail.Opacity = selected ? 0.82 : 1;
        row.Verb.Text = running ? "Working…" : row.Result.Verb;
        row.Verb.Visibility = selected && (_running is null || running) ? Visibility.Visible : Visibility.Collapsed;
        row.Verb.Margin = new Thickness(10, 0, hide ? 28 : 0, 0);
        if (row.Hide is not null)
        {
            var visibility = hide ? Visibility.Visible : Visibility.Collapsed;
            bool changed = row.Hide.Visibility != visibility;
            row.Hide.Visibility = visibility;
            if (changed) UIElementAutomationPeer.FromElement(row.Button)?.InvalidatePeer();
        }
        row.Button.Tag = selected ? "Selected" : null;
        row.Button.IsEnabled = _running is null;
        row.Button.SetSelected(selected);
        AutomationProperties.SetName(row.Button, AccessibleName(row.Result));
        AutomationProperties.SetPositionInSet(row.Button, index + 1);
        AutomationProperties.SetSizeOfSet(row.Button, _shown.Count);
        AutomationProperties.SetItemStatus(row.Button, row.Result.Key == _failed ? _failure : running ? "Working" : selected ? "Selected" : "");
        if (row.Result.CanHide)
            AutomationProperties.SetHelpText(row.Button, _running is null ? "Shift+Delete to hide. Undo is available after hiding." : "");
    }

    private sealed record RowView(Result Result, UIElement Root, SearchResultButton Button, TextBlock Detail, TextBlock Verb, Button? Hide);

    private FrameworkElement Lead(Result result)
    {
        if (result.Emoji is { } emoji)
            return new TextBlock { Text = emoji, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16, Width = 20, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (result.App is { } parsing)
            return AppMark(result.Name, (pc, pixels) => pc.AppIconAsync(new StartApp(result.Name, parsing), pixels));
        if (result.Window is { } window)
            return AppMark(window.App, (pc, pixels) => pc.WindowIconAsync(window, pixels));
        var box = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(Ui.Glyph(LucideIcons.Get(result.Icon), 16, "IslandTextSecondaryBrush"));
        return box;
    }

    /// <summary>The app's icon, its letters on the tint until it's read (at once when already read).</summary>
    private FrameworkElement AppMark(string name, Func<IPc, int, Task<ImageSource?>> read)
    {
        var mark = Ui.Monogram(name);
        var image = new Image { Width = 20, Height = 20 };
        AppIcon.Configure(image);
        var box = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Children = { mark, image } };
        if (_module.Pc is not { } pc)
            return box;
        int pixels = (int)Math.Round(20 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _ = Show();
        return box;

        async Task Show()
        {
            try
            {
                if (await read(pc, pixels) is { } icon)
                {
                    image.Source = icon;
                    mark.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception) { /* its letters stay */ }
        }
    }

    /// <summary>A volume's level: 48 × 4, the track with the accent fill.</summary>
    private static FrameworkElement Level(double level)
    {
        var fill = new Border { Width = 48 * Math.Clamp(level, 0, 1), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
        var track = new Border { Width = 48, Height = 4, CornerRadius = new CornerRadius(2), Child = fill, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
        return track;
    }
}
