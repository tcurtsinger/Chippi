// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
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
    private readonly StackPanel _rows = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly DispatcherTimer _filesTimer;
    private IReadOnlyList<Result> _shown = [];
    private IReadOnlyList<OpenWindow> _windows = [];
    private IReadOnlyList<FoundFile> _files = [];
    private string _filesFor = "";
    private CancellationTokenSource? _finding;
    private int _selected;
    private string? _failed; // the key of the row whose run failed, until the next change
    private Point? _pointer;

    public CommandBarPage(CommandBarModule module)
    {
        _module = module;
        Field = new SearchBox { Placeholder = "Search", Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(Field, "Search");
        Field.TextChanged += (_, _) => Typed();
        Field.PreviewKeyDown += OnKey;
        Content = _rows;
        _filesTimer = new DispatcherTimer(FilesAfter, DispatcherPriority.Input, (_, _) => FindFiles(), Dispatcher) { IsEnabled = false };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Opened();
            else
                _finding?.Cancel();
        };
        Loaded += (_, _) => { if (_module.Pc is { } pc) pc.AppsChanged += OnAppsChanged; };
        Unloaded += (_, _) => { if (_module.Pc is { } pc) pc.AppsChanged -= OnAppsChanged; };
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
        _failed = null;
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

    private void OnAppsChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (IsVisible)
            Refresh();
    });

    private void Typed()
    {
        _failed = null;
        _finding?.Cancel();
        _filesTimer.Stop();
        Refresh(keepSelection: false);
        if (Field.Text.Trim().Length >= 2 && !_module.Settings.Off.Contains(ResultSwitch.Files))
            _filesTimer.Start();
    }

    private async void FindFiles()
    {
        _filesTimer.Stop();
        if (_module.Pc is not { } pc)
            return;
        string text = Field.Text.Trim();
        var finding = _finding = new CancellationTokenSource();
        try
        {
            var files = await pc.FindFilesAsync(text, FileCount, finding.Token);
            if (finding.IsCancellationRequested || Field.Text.Trim() != text)
                return;
            _files = files;
            _filesFor = text;
            Refresh();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Finds the rows again. Typing selects the first row (an answer that appears selects itself); a refresh of the same
    /// text (files found, the Start menu read) keeps the selected row selected while it still shows.
    /// </summary>
    internal void Refresh(bool keepSelection = true)
    {
        if (_module.Search is not { } search)
            return;
        string text = Field.Text.Trim();
        string? selected = keepSelection && _selected < _shown.Count ? _shown[_selected].Key : null;
        _shown = search.Find(text, _windows, _filesFor == text ? _files : []);
        int at = selected is null ? -1 : IndexOf(selected);
        _selected = at >= 0 && text.Length > 0 ? at : 0;
        Render();
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
        if (index == _selected)
            return;
        _selected = index;
        Render();
    }

    /// <summary>Runs <paramref name="result"/>: the panel closes after, unless the row moved the island to another page.</summary>
    internal async void Run(Result result)
    {
        string text = Field.Text;
        try
        {
            await result.Run();
            _module.Search?.Picked(text, result);
            if (IsVisible)
                _module.Close();
        }
        catch (Exception ex)
        {
            Log.Warn($"Command bar: {result.Verb} failed", ex);
            _failed = result.Key;
            Render();
        }
    }

    private void Hide(Result result)
    {
        _module.Hide(result);
        Refresh();
    }

    private void Render()
    {
        _rows.Children.Clear();
        for (int i = 0; i < _shown.Count; i++)
            _rows.Children.Add(Row(_shown[i], i));
    }

    // A row: the 20 lead, the name in 12 over its detail in 11 tertiary, a level bar for a volume, and the selected row's
    // verb in the accent at its right with the eye-off button at its end.
    private UIElement Row(Result result, int index)
    {
        bool selected = index == _selected;
        bool failed = result.Key == _failed;
        var name = Ui.Text(result.Name, 12);
        var detail = Ui.Text(failed ? $"{result.Verb} failed" : result.Detail, 11, failed ? "IslandCautionBrush" : "IslandTextTertiaryBrush");
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
        if (selected)
        {
            var verb = Ui.Text(result.Verb, 11, "IslandAccentBrush");
            verb.Margin = new Thickness(10, 0, result.CanHide ? 28 : 0, 0);
            Grid.SetColumn(verb, 3);
            line.Children.Add(verb);
        }
        var button = new Button { Content = line, Height = RowHeight, Padding = new Thickness(10, 0, 10, 0), Focusable = false, ToolTip = result.Name, Tag = selected ? "Selected" : null };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(button, $"{result.Name}, {result.Detail}");
        button.Click += (_, _) => Run(result);
        button.MouseMove += (_, e) =>
        {
            // Only a pointer that moved selects: rows changing under a still pointer leave the selection alone.
            var at = e.GetPosition(this);
            if (_pointer == at)
                return;
            _pointer = at;
            Select(index);
        };
        if (!selected || !result.CanHide)
            return button;
        var hide = Ui.Icon(LucideIcons.EyeOff, $"Hide {result.Name}", "Hide");
        hide.HorizontalAlignment = HorizontalAlignment.Right;
        hide.VerticalAlignment = VerticalAlignment.Center;
        hide.Margin = new Thickness(0, 0, 6, 0);
        hide.Click += (_, _) => Hide(result);
        return new Grid { Children = { button, hide } };
    }

    private FrameworkElement Lead(Result result)
    {
        if (result.Emoji is { } emoji)
            return new TextBlock { Text = emoji, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16, Width = 20, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (result.App is { } parsing)
            return AppIcon(new StartApp(result.Name, parsing));
        var box = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(Ui.Glyph(LucideIcons.Get(result.Icon), 16, "IslandTextSecondaryBrush"));
        return box;
    }

    /// <summary>The app's icon, its letters on the tint until it's read (at once when already read).</summary>
    private FrameworkElement AppIcon(StartApp app)
    {
        var mark = Ui.Monogram(app.Name);
        var image = new Image { Width = 20, Height = 20 };
        var box = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Children = { mark, image } };
        if (_module.Pc is not { } pc)
            return box;
        int pixels = (int)Math.Round(20 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _ = Show(pc.AppIconAsync(app, pixels));
        return box;

        async Task Show(Task<ImageSource?> reading)
        {
            try
            {
                if (await reading is { } icon)
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
