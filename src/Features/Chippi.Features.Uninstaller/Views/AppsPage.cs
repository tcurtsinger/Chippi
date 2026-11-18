// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.UI.Controls;
using static Chippi.Features.Uninstaller.Views.UninstallerUi;

namespace Chippi.Features.Uninstaller.Views;

/// <summary>One row of the list: the app and the page that draws it.</summary>
internal sealed record AppRowModel(AppEntry App, AppsPage Page);

/// <summary>
/// Settings › Apps › Uninstaller › Apps (docs/phase-7/design/uninstaller.html): the search and its one filter, the sortable list of 48-DIP rows
/// (virtualized), the sticky selection bar, and the confirms as inline popovers. Nothing is uninstalled until the verb in a confirm is pressed.
/// </summary>
internal sealed class AppsPage : Grid, IDisposable
{
    private static readonly GridLength[] Widths = [new(20), new(28), new(1, GridUnitType.Star), new(124), new(64), new(108), new(16), new(112)];
    internal const string AllApps = "All apps", Components = "Windows components";

    private readonly UninstallerEngine _engine;
    private readonly Action<AppRecord> _scan;
    private readonly Action<string> _openFolder;
    private readonly HashSet<string> _ticked = [];
    private HashSet<string> _queue = [];
    private readonly Dictionary<string, AppEntry> _pinned = [];
    private readonly Border _headerHost = new() { Height = 36, BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly ItemsControl _list = new();
    private readonly SearchBox _search = new() { Placeholder = "Search apps", Height = 32, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _filter = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _count = Text("", 12, "TextTertiaryBrush");
    private readonly Border _barHost = new() { Visibility = Visibility.Collapsed, Height = 60, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(-40, 0, -40, -28), Padding = new Thickness(40, 0, 40, 0) };
    private readonly Border _notice = new() { Visibility = Visibility.Collapsed };
    private readonly Canvas _overlay = new();
    private FrameworkElement? _popover;
    private IReadOnlyList<AppEntry> _shown = [];
    private IReadOnlyList<AppEntry>? _source;
    private string _sort = "name";
    private bool _descending;
    private bool _following, _disposed;
    private string? _focusKey;

    public AppsPage(UninstallerEngine engine, Action<AppRecord> scan, Action<string> openFolder)
    {
        _engine = engine;
        _scan = scan;
        _openFolder = openFolder;
        RowDefinitions.Add(new() { Height = GridLength.Auto });   // toolbar
        RowDefinitions.Add(new() { Height = new GridLength(28) }); // count
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });   // list
        RowDefinitions.Add(new() { Height = GridLength.Auto });   // bar
        // Toolbar
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetName(_search, "Search apps");
        _search.TextChanged += (_, _) => { Rebuild(); };
        toolbar.Children.Add(_search);
        _filter.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        _filter.ItemsSource = new[] { AllApps, Components };
        _filter.SelectedIndex = 0;
        AutomationProperties.SetName(_filter, "Show");
        _filter.SelectionChanged += (_, _) => { Rebuild(); };
        Add(toolbar, _filter, 1);
        Add(this, toolbar, 0, 0);
        // Count line and its refresh
        var countRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        countRow.Children.Add(_count);
        var refresh = Icon(LucideIcons.RefreshCw, "Refresh", () => { if (_engine.IsRunning) return; _pinned.Clear(); _ = _engine.RefreshAsync(); }, 12, 22);
        refresh.Margin = new Thickness(4, 0, 0, 0);
        countRow.Children.Add(refresh);
        Add(this, countRow, 0, 1);
        // The list card: header, then the virtualized rows (or a notice).
        var cardBody = new Grid();
        cardBody.RowDefinitions.Add(new() { Height = GridLength.Auto });
        cardBody.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _headerHost.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        Add(cardBody, _headerHost, 0, 0);
        ConfigureList();
        Add(cardBody, _list, 0, 1);
        Add(cardBody, _notice, 0, 1);
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = cardBody, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        card.Margin = new Thickness(0, 0, 0, 0);
        Add(this, card, 0, 2);
        _barHost.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        _barHost.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        Add(this, _barHost, 0, 3);
        Grid.SetRowSpan(_overlay, 4);
        Children.Add(_overlay);
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Rebuild();
    }

    // ---- state the tests and shots read ----

    internal SearchBox SearchBox => _search;
    internal ItemsControl List => _list;
    internal string CountText => _count.Text;
    internal IReadOnlyCollection<string> Ticked => _ticked;
    internal IReadOnlyList<AppEntry> Shown => _shown;
    internal bool AsksQuestion => _popover is not null;
    internal UIElement? InitialFocus => _search;
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;
    internal string? FocusedKeyOverride { get; set; }
    internal FrameworkElement? PopoverElement => _popover;
    internal FrameworkElement Bar => _barHost;
    internal string Filter => (string)_filter.SelectedItem;

    internal void SetSearch(string text) => _search.Text = text;

    /// <summary>Scrolls the list so the named app's row is the first one shown (the shots and tests).</summary>
    internal void ScrollTo(string name)
    {
        int index = _shown.ToList().FindIndex(a => a.Name == name);
        if (index >= 0) Walk<ScrollViewer>(_list).FirstOrDefault()?.ScrollToVerticalOffset(index);
        UpdateLayout();
    }

    internal void SetFilter(string filter) => _filter.SelectedItem = filter;

    internal bool IsQueued(string id) => _queue.Contains(id);

    internal Button? FocusTarget => _focusKey is not null ? Find(_focusKey) : null;

    private void ConfigureList()
    {
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(FocusableProperty, false);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.AppendChild(presenter);
        _list.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroll };
        _list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        _list.Focusable = false;
        var row = new FrameworkElementFactory(typeof(AppRowView));
        _list.ItemTemplate = new DataTemplate(typeof(AppRowModel)) { VisualTree = row };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true);
        Redraw();
        if (_popover is null) FocusLater(_search);
        if (!_engine.IsRunning && !_engine.IsRefreshing)
        {
            if (!_engine.Loaded) _ = Cached();
            else _ = _engine.RefreshAsync();
        }
    }

    private async Task Cached()
    {
        await _engine.LoadCachedAsync();
        if (!_engine.IsRefreshing && !_engine.IsRunning) await _engine.RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Follow(false);

    private void Follow(bool on)
    {
        if (_following == on || _disposed && on) return;
        if (on) _engine.Changed += OnChanged; else _engine.Changed -= OnChanged;
        _following = on;
    }

    private void OnChanged()
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnChanged); return; }
        if (!ReferenceEquals(_source, _engine.Apps) && !_engine.IsRunning) Rebuild(); else Redraw();
    }

    // ---- the list ----

    private bool Matches(AppEntry app, string query, string key)
    {
        if (query.Length == 0) return true;
        return app.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || app.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
            || key.Length > 0 && UninstallerText.NameKey(app.Name).Contains(key, StringComparison.Ordinal);
    }

    private bool InFilter(AppEntry app) => Filter == Components ? app.Kind == AppKind.Component : app.Kind != AppKind.Component;

    private IEnumerable<AppEntry> Sorted(IEnumerable<AppEntry> apps)
    {
        IOrderedEnumerable<AppEntry> ordered = _sort switch
        {
            "publisher" => _descending ? apps.OrderByDescending(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase) : apps.OrderBy(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase),
            "size" => _descending ? apps.OrderByDescending(a => a.SizeBytes ?? -1) : apps.OrderBy(a => a.SizeBytes ?? long.MaxValue),
            "installed" => _descending ? apps.OrderByDescending(a => a.Installed ?? DateTime.MinValue) : apps.OrderBy(a => a.Installed ?? DateTime.MaxValue),
            _ => _descending ? apps.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase) : apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        return ordered.ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>Filters, sorts and sets the rows. The scroll goes back to the top only when the rows changed.</summary>
    internal void Rebuild()
    {
        if (_disposed) return;
        _source = _engine.Apps;
        string query = _search.Text.Trim();
        string key = UninstallerText.NameKey(query);
        var inFilter = All().Where(InFilter).ToList();
        var next = Sorted(inFilter.Where(a => Matches(a, query, key))).ToList();
        _total = inFilter.Count;
        if (!next.SequenceEqual(_shown))
        {
            _shown = next;
            _list.ItemsSource = next.Select(a => new AppRowModel(a, this)).ToList();
        }
        _ticked.IntersectWith(All().Select(a => a.Id));
        Redraw();
    }

    private int _total;

    /// <summary>The apps read, with those just uninstalled still shown (with their result) until the next Refresh.</summary>
    private IEnumerable<AppEntry> All() => _pinned.Count == 0 ? _engine.Apps : _engine.Apps.Concat(_pinned.Values.Where(p => _engine.Apps.All(a => a.Id != p.Id)));

    /// <summary>Draws the header, count, bar and notice again and re-binds the rows on screen; the keyboard stays where it was.</summary>
    internal void Redraw()
    {
        if (_disposed) return;
        string? key = FocusedKeyOverride ?? CurrentFocusKey();
        BuildHeader();
        BuildCount();
        BuildNotice();
        BuildBar();
        foreach (var row in Walk<AppRowView>(_list)) row.Bind();
        _focusKey = key;
        if (key is not null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, RestoreFocus);
    }

    private string? CurrentFocusKey()
    {
        if (!IsKeyboardFocusWithin || _popover is not null && _popover.IsKeyboardFocusWithin) return null;
        return (Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
    }

    private void RestoreFocus()
    {
        if (_disposed || !IsLoaded || FocusTarget is not { IsVisible: true, IsEnabled: true } target || target.IsKeyboardFocusWithin) return;
        target.Focus();
    }

    private Button? Find(string key) => Walk<Button>(this).FirstOrDefault(b => b.Tag as string == key);

    private static IEnumerable<T> Walk<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Walk<T>(child)) yield return nested;
        }
    }

    private void BuildCount()
    {
        string query = _search.Text.Trim();
        int n = _shown.Count;
        string Apps(int c) => c == 1 ? "1 app" : c + " apps";
        _count.Text = _engine.Error is not null && !_engine.Loaded ? ""
            : _engine.IsRunning ? "Uninstalling " + Apps(_queue.Count)
            : !_engine.Loaded ? ""
            : query.Length > 0 ? (n == _total ? Apps(_total) : n + " of " + Apps(_total)) : Apps(_total);
    }

    private void BuildHeader()
    {
        var grid = Columns(Widths, 10);
        grid.Margin = new Thickness(16, 0, 16, 0);
        bool? all = _ticked.Count == 0 ? false : Selectable().All(a => _ticked.Contains(a.Id)) ? true : null;
        var check = Check(all, "Select all", Selectable().Any() && !_engine.IsRunning);
        check.IsThreeState = true;
        check.Tag = "check:all";
        check.Click += (_, _) =>
        {
            bool on = !(_ticked.Count > 0 && Selectable().All(a => _ticked.Contains(a.Id)));
            _ticked.Clear();
            if (on) foreach (var app in Selectable()) _ticked.Add(app.Id);
            Redraw();
        };
        Add(grid, check, At(0));
        Add(grid, SortHeader("Name", "name", HorizontalAlignment.Left), At(2));
        Add(grid, SortHeader("Publisher", "publisher", HorizontalAlignment.Left), At(3));
        Add(grid, SortHeader("Size", "size", HorizontalAlignment.Right), At(4));
        var installed = SortHeader("Installed", "installed", HorizontalAlignment.Left);
        installed.Margin = new Thickness(16, 0, 0, 0);
        Add(grid, installed, At(5));
        _headerHost.Child = grid;
    }

    private static readonly ControlTemplate SortTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Button\"><Border Background=\"Transparent\"><ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"Center\"/></Border></ControlTemplate>");

    private IEnumerable<AppEntry> Selectable() => _shown.Where(a => a.CanUninstall);

    private FrameworkElement SortHeader(string label, string key, HorizontalAlignment align)
    {
        bool on = _sort == key;
        var text = Text(label, 12, on ? "TextPrimaryBrush" : "TextTertiaryBrush");
        if (on) text.FontWeight = FontWeights.SemiBold;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align };
        row.Children.Add(text);
        if (on)
        {
            var arrow = Glyph(_descending ? LucideIcons.ArrowDown : LucideIcons.ArrowUp, 12, "TextPrimaryBrush");
            arrow.Margin = new Thickness(4, 0, 0, 0);
            row.Children.Add(arrow);
        }
        // A plain template: the content sits at the column's edge, not centered in it.
        var button = new Button { Content = row, Padding = new Thickness(0), HorizontalContentAlignment = align, Tag = "sort:" + key, Height = 28, VerticalAlignment = VerticalAlignment.Center, Template = SortTemplate, Cursor = Cursors.Hand };
        AutomationProperties.SetName(button, "Sort by " + label);
        button.Click += (_, _) =>
        {
            if (_sort == key) _descending = !_descending; else { _sort = key; _descending = key is "size" or "installed"; }
            Rebuild();
        };
        return button;
    }

    private void BuildNotice()
    {
        _notice.Visibility = Visibility.Collapsed;
        _list.Visibility = Visibility.Visible;
        if (_engine.Error is { } error && !_engine.Loaded)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            var mark = Glyph(LucideIcons.CircleAlert, 20, "WarningTextBrush");
            mark.Margin = new Thickness(0, 0, 10, 0);
            line.Children.Add(mark);
            var text = Text(error, 14, "WarningTextBrush");
            text.Margin = new Thickness(0, 0, 16, 0);
            line.Children.Add(text);
            line.Children.Add(Verb("Retry", () => _ = _engine.RefreshAsync(), small: true));
            line.Margin = new Thickness(16, 24, 16, 0);
            Show(line);
        }
        else if (!_engine.Loaded)
        {
            var skeleton = new StackPanel();
            for (int i = 0; i < 5; i++)
            {
                var row = Columns(Widths, 10);
                row.Height = 48;
                row.Margin = new Thickness(16, 0, 16, 0);
                row.Opacity = 1 - i * .16;
                Add(row, Skel(20, 20, 4), At(0));
                Add(row, Skel(28, 28, 6), At(1));
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(Skel(130 + i * 37 % 90, 10, 5));
                names.Children.Add(Skel(60, 8, 4, 8));
                Add(row, names, At(2));
                Add(row, Skel(90, 10, 5), At(3));
                skeleton.Children.Add(row);
            }
            Show(skeleton);
        }
        else if (_shown.Count == 0)
        {
            var empty = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            var mark = Glyph(LucideIcons.Search, 16, "TextTertiaryBrush");
            mark.Margin = new Thickness(0, 0, 10, 0);
            empty.Children.Add(mark);
            empty.Children.Add(Text("No apps match", 14, "TextTertiaryBrush"));
            Show(empty);
        }

        void Show(UIElement content)
        {
            _notice.Child = content;
            _notice.Visibility = Visibility.Visible;
            _list.Visibility = Visibility.Collapsed;
        }
    }

    private static Border Skel(double width, double height, double radius, double top = 0)
    {
        var bar = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, top, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush");
        return bar;
    }

    // ---- ticks and the bar ----

    internal void Tick(string id, bool on)
    {
        if (_engine.IsRunning) { Redraw(); return; }
        if (on) _ticked.Add(id); else _ticked.Remove(id);
        Redraw();
    }

    internal bool IsTicked(string id) => _ticked.Contains(id);

    private IReadOnlyList<AppEntry> TickedApps() => [.. _engine.Apps.Where(a => _ticked.Contains(a.Id) && a.CanUninstall)];

    private void BuildBar()
    {
        if (_engine.IsRunning)
        {
            _barHost.Visibility = Visibility.Visible;
            _barHost.Child = BarText("Uninstalling " + Math.Max(1, _engine.Index) + " of " + Math.Max(1, _engine.Count), null);
            return;
        }
        var apps = TickedApps();
        if (apps.Count == 0) { _barHost.Visibility = Visibility.Collapsed; _barHost.Child = null; return; }
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        long bytes = apps.Sum(a => a.SizeBytes ?? 0);
        Add(bar, BarText(apps.Count + " selected", UninstallerText.Size(bytes)), 0);
        var clear = Verb("Clear", () => { _ticked.Clear(); Redraw(); });
        clear.Tag = "bar:clear";
        var uninstall = Verb("Uninstall", AskSelected, VerbKind.Accent, shield: apps.Any(a => a.NeedsAdmin));
        uninstall.Tag = "bar:uninstall";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        clear.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(clear);
        buttons.Children.Add(uninstall);
        Add(bar, buttons, 1);
        _barHost.Child = bar;
        _barHost.Visibility = Visibility.Visible;
    }

    private static FrameworkElement BarText(string text, string? small)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Text(text, 14));
        if (!string.IsNullOrEmpty(small))
        {
            var detail = Text(small, 12, "TextTertiaryBrush");
            detail.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(detail);
        }
        return line;
    }

    // ---- the questions ----

    internal void AskOne(AppEntry app, FrameworkElement anchor)
    {
        if (_engine.IsRunning || !app.CanUninstall) return;
        var title = new TextBlock { Text = UninstallerText.Question([app]), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var cancel = Verb("Cancel", ClosePopover, small: true);
        cancel.Tag = "popover:cancel";
        var confirm = Verb("Uninstall", () => Confirm([app]), VerbKind.Critical, small: true, shield: app.NeedsAdmin);
        confirm.Tag = "popover:confirm";
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(Buttons(cancel, confirm));
        var point = anchor.TranslatePoint(new Point(anchor.ActualWidth, anchor.ActualHeight), this);
        Open(Popover(stack, 300), cancel, right: point.X + 8, top: point.Y + 4);
    }

    internal void AskSelected()
    {
        var apps = TickedApps();
        if (_engine.IsRunning || apps.Count == 0) return;
        var stack = new StackPanel();
        var title = new TextBlock { Text = UninstallerText.Question(apps), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(title);
        if (apps.Count > 1)
        {
            var list = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var app in apps.Take(8))
            {
                var row = Columns([new(20), new(1, GridUnitType.Star), GridLength.Auto], 8);
                row.MinHeight = 30;
                Add(row, Mark(app.Name, app.IconPath, 20), At(0));
                Add(row, Text(app.Name, 13), At(1));
                var version = Text(string.Join('.', app.Version.Split('.').Take(3)), 12, "TextTertiaryBrush");
                Add(row, version, At(2));
                list.Children.Add(row);
            }
            if (apps.Count > 8) list.Children.Add(Text("+" + (apps.Count - 8), 12, "TextTertiaryBrush"));
            var panel = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Child = list, Margin = new Thickness(0, 0, 0, 0) };
            panel.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush");
            stack.Children.Add(panel);
            panel.Margin = new Thickness(0, 0, 0, 14);
            list.Margin = new Thickness(0);
        }
        else stack.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 12));
        var cancel = Verb("Cancel", ClosePopover, small: true);
        cancel.Tag = "popover:cancel";
        var confirm = Verb("Uninstall", () => Confirm(apps), VerbKind.Critical, small: true, shield: apps.Any(a => a.NeedsAdmin));
        confirm.Tag = "popover:confirm";
        stack.Children.Add(Buttons(cancel, confirm));
        var box = Popover(stack, 380);
        double top = Math.Max(0, ActualHeight - (_barHost.Visibility == Visibility.Visible ? 60 : 0));
        Open(box, cancel, right: 0, top: top, above: true);
    }

    private static StackPanel Buttons(Button cancel, Button confirm)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        return buttons;
    }

    private void Open(Border box, UIElement focus, double right, double top, bool above = false)
    {
        ClosePopover();
        _overlay.Children.Add(box);
        _popover = box;
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = box.DesiredSize.Width, height = box.DesiredSize.Height;
        double pageWidth = ActualWidth > 0 ? ActualWidth : 768;
        double left = above ? pageWidth - width : Math.Max(0, Math.Min(pageWidth - width, right - width));
        double y = above ? Math.Max(0, top - height - 8) : Math.Max(0, Math.Min((ActualHeight > 0 ? ActualHeight : 600) - height, top));
        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, y);
        FocusLater(focus);
        _focusKey = "popover:cancel";
    }

    internal void ClosePopover()
    {
        if (_popover is null) return;
        _overlay.Children.Remove(_popover);
        _popover = null;
        if (_focusKey == "popover:cancel") _focusKey = null;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_popover is not null && e.OriginalSource is DependencyObject source && !IsInside(source, _popover)) ClosePopover();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _popover is not null) { ClosePopover(); e.Handled = true; }
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (DependencyObject? current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current == container) return true;
        return false;
    }

    /// <summary>The confirmed question: runs the queue. Nothing runs before this.</summary>
    internal Task Confirm(IReadOnlyList<AppEntry> apps)
    {
        ClosePopover();
        if (_engine.IsRunning || apps.Count == 0) return Task.CompletedTask;
        _queue = [.. apps.Select(a => a.Id)];
        foreach (var app in apps) { _engine.ClearStatus(app.Id); _pinned[app.Id] = app; }
        return RunAsync(apps);
    }

    private async Task RunAsync(IReadOnlyList<AppEntry> apps)
    {
        Redraw();
        try { await _engine.UninstallAsync(apps); }
        finally
        {
            _ticked.Clear();
            _queue = [];
            if (!_disposed) Redraw();
        }
    }

    // ---- what a row does ----

    internal AppStatus StatusOf(AppEntry app) => _engine.StatusOf(app.Id);
    internal bool Running => _engine.IsRunning;
    internal bool InQueue(AppEntry app) => _queue.Contains(app.Id);
    internal bool IsNew(AppEntry app) => app.IsRecent(Now());
    internal void OpenFolder(string? folder) { if (!string.IsNullOrEmpty(folder)) _openFolder(folder); }

    internal void Scan(AppEntry app)
    {
        var record = _engine.Recent.FirstOrDefault(r => r.Source == app.Source && string.Equals(r.Key, app.Key, StringComparison.OrdinalIgnoreCase));
        if (record is not null) _scan(record);
    }

    internal bool CanScan(AppEntry app) => _engine.Recent.Any(r => r.Source == app.Source && string.Equals(r.Key, app.Key, StringComparison.OrdinalIgnoreCase) && !r.Restart);

    public void Dispose()
    {
        if (_disposed) return;
        Follow(false);
        _disposed = true;
        PreviewMouseDown -= OnPreviewMouseDown;
        PreviewKeyDown -= OnPreviewKeyDown;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}

/// <summary>One 48-DIP row. It builds itself from its <see cref="AppRowModel"/> whenever it is bound or its page redraws.</summary>
internal sealed class AppRowView : Border
{
    private static readonly GridLength[] Widths = [new(20), new(28), new(1, GridUnitType.Star), new(124), new(64), new(108), new(16), new(112)];

    public AppRowView()
    {
        Height = 48;
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        Background = Brushes.Transparent;
        DataContextChanged += (_, _) => Bind();
        MouseEnter += (_, _) => SetResourceReference(BackgroundProperty, "SurfaceSelectedBrush");
        MouseLeave += (_, _) => Background = Brushes.Transparent;
    }

    internal void Bind()
    {
        if (DataContext is not AppRowModel model) { Child = null; return; }
        var app = model.App;
        var page = model.Page;
        var status = page.StatusOf(app);
        var state = status.State;
        bool queueRunning = page.Running, inQueue = page.InQueue(app);
        bool dim = queueRunning && !inQueue;
        bool verb = app.CanUninstall && state is not (AppRunState.Queued or AppRunState.Uninstalling or AppRunState.Uninstalled or AppRunState.RestartNeeded);
        var grid = UninstallerUi.Columns(Widths, 10);
        grid.Margin = new Thickness(16, 0, 16, 0);
        // Tick
        var tick = UninstallerUi.Check(page.IsTicked(app.Id), "Select " + app.Name, app.CanUninstall && (!queueRunning || inQueue) && state is not (AppRunState.Uninstalled or AppRunState.RestartNeeded));
        tick.Tag = "check:" + app.Id;
        tick.Click += (_, _) => page.Tick(app.Id, tick.IsChecked == true);
        UninstallerUi.Add(grid, tick, UninstallerUi.At(0));
        UninstallerUi.Add(grid, UninstallerUi.Mark(app.Name, app.IconPath, 28), UninstallerUi.At(1));
        // Name and its second line
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(UninstallerUi.Text(app.Name, 14));
        if (page.IsNew(app)) { var tag = UninstallerUi.Tag("New", TagKind.Accent); tag.Margin = new Thickness(6, 0, 0, 0); line.Children.Add(tag); }
        if (app.IsStore) { var tag = UninstallerUi.Tag("Store", TagKind.Neutral); tag.Margin = new Thickness(6, 0, 0, 0); line.Children.Add(tag); }
        names.Children.Add(line);
        var (second, brush) = Second(app, status);
        var secondLine = new StackPanel { Orientation = Orientation.Horizontal };
        secondLine.Children.Add(UninstallerUi.Text(second, 12, brush));
        if (state == AppRunState.Uninstalling) secondLine.Children.Add(Progress());
        names.Children.Add(secondLine);
        UninstallerUi.Add(grid, names, UninstallerUi.At(2));
        UninstallerUi.Add(grid, UninstallerUi.Text(app.Publisher, 13, "TextSecondaryBrush"), UninstallerUi.At(3));
        var size = UninstallerUi.Text(UninstallerText.Size(app.SizeBytes), 13, "TextSecondaryBrush");
        size.HorizontalAlignment = HorizontalAlignment.Right;
        UninstallerUi.Add(grid, size, UninstallerUi.At(4));
        var installed = UninstallerUi.Text(app.Installed is { } when ? when.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "", 13, "TextSecondaryBrush");
        installed.Margin = new Thickness(16, 0, 0, 0);
        UninstallerUi.Add(grid, installed, UninstallerUi.At(5));
        UninstallerUi.Add(grid, UninstallerUi.ShieldCell(app.Kind == AppKind.App && app.NeedsAdmin && state is not (AppRunState.Uninstalled or AppRunState.RestartNeeded)), UninstallerUi.At(6));
        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (state == AppRunState.Uninstalled || state == AppRunState.RestartNeeded)
        {
            var scan = UninstallerUi.Verb("Scan", () => page.Scan(app), small: true);
            scan.IsEnabled = state == AppRunState.Uninstalled && page.CanScan(app);
            scan.Tag = "scan:" + app.Id;
            actions.Children.Add(scan);
        }
        else if (app.Kind == AppKind.App && state is not (AppRunState.Queued or AppRunState.Uninstalling))
        {
            var run = UninstallerUi.Verb("Uninstall", () => page.AskOne(app, actions), small: true);
            run.Tag = "verb:" + app.Id;
            run.IsEnabled = verb && !queueRunning && state != AppRunState.Running;
            actions.Children.Add(run);
            var open = UninstallerUi.Icon(UninstallerIcons.FolderOpen, "Open folder", () => page.OpenFolder(app.InstallLocation));
            open.Margin = new Thickness(4, 0, 0, 0);
            open.IsEnabled = !queueRunning && !string.IsNullOrEmpty(app.InstallLocation);
            open.Tag = "folder:" + app.Id;
            actions.Children.Add(open);
        }
        UninstallerUi.Add(grid, actions, UninstallerUi.At(7));
        Child = grid;
        Opacity = dim || state == AppRunState.Running ? .62 : 1;
        AutomationProperties.SetName(this, app.Name);
    }

    private static (string Text, string Brush) Second(AppEntry app, AppStatus status)
    {
        if (status.Text is { } text)
            return (text, status.State switch
            {
                AppRunState.Uninstalled => "SuccessBrush",
                AppRunState.Failed or AppRunState.NeedsAdmin or AppRunState.Refused or AppRunState.Running => "WarningTextBrush",
                _ => "TextTertiaryBrush",
            });
        if (app.NoVerbText is { } none) return (none, "WarningTextBrush");
        return (app.Version, "TextTertiaryBrush");
    }

    private static FrameworkElement Progress()
    {
        var track = new Border { Width = 160, Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(8, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
        var fill = new Border { Width = 70, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        track.Child = fill;
        return track;
    }
}
