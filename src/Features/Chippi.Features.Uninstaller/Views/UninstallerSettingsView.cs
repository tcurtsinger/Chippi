// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using static Chippi.Features.Uninstaller.Views.UninstallerUi;

namespace Chippi.Features.Uninstaller.Views;

/// <summary>
/// Settings › Apps › Uninstaller (docs/phase-7/design/uninstaller.html): the Apps list, the recently Uninstalled apps whose leftovers can still be
/// scanned, and the Shortcuts read-only with Edit; a Scan opens the leftover review in the page's place. The page fills the Settings viewport, so the
/// list scrolls inside it and the selection bar stays at the bottom. Nothing is uninstalled or removed before the verb of a question is pressed.
/// </summary>
public sealed class UninstallerSettingsView : ContentControl, IDisposable
{
    private readonly UninstallerEngine _engine;
    private readonly ModuleContext _context;
    private readonly Action<string> _openFolder;
    private readonly Grid _frame = new();
    private readonly Border _tabHost = new();
    private readonly Border _content = new();
    private readonly SegmentedControl _tabs = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, -2, 0, 8) };
    private readonly AppsPage _apps;
    private readonly List<(string Name, ShortcutSetting View)> _shortcuts = [];
    private readonly Border _recent = new();
    private readonly Border _keys = new();
    private TextBlock _recentCount = new();
    private LeftoverPage? _review;
    private ScrollViewer? _scroller;
    private bool _disposed, _following, _settingTab;

    public UninstallerSettingsView(UninstallerEngine engine, ModuleContext context, string? section = null, Action<string>? openFolder = null)
    {
        _engine = engine;
        _context = context;
        _openFolder = openFolder ?? OpenInExplorer;
        _apps = new AppsPage(engine, OpenReview, folder => _openFolder(folder));
        _frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _frame.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _tabs.SetResourceReference(StyleProperty, "SelectorBarStyle");
        AutomationProperties.SetName(_tabs, "Uninstaller");
        _tabs.Items.Add(TabItem("Apps", null));
        _recentCount = Text("", 12, "TextTertiaryBrush");
        _tabs.Items.Add(TabItem("Uninstalled", _recentCount));
        _tabs.Items.Add(TabItem("Shortcuts", null));
        _tabs.SelectedIndex = 0;
        _tabs.SelectionChanged += (_, _) => { if (!_settingTab) ShowTab(); };
        _tabHost.Child = _tabs;
        Grid.SetRow(_tabHost, 0);
        Grid.SetRow(_content, 1);
        _frame.Children.Add(_tabHost);
        _frame.Children.Add(_content);
        BuildKeys();
        Content = _frame;
        Focusable = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Land(section);
        ShowTab();
    }

    private static FrameworkElement TabItem(string label, TextBlock? count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        // The tab's own ink (its secondary, primary while selected or pointed) comes from the item that holds this.
        name.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
        row.Children.Add(name);
        if (count is not null) { count.Margin = new Thickness(6, 0, 0, 0); count.FontWeight = FontWeights.Normal; row.Children.Add(count); }
        AutomationProperties.SetName(row, label);
        return row;
    }

    // ---- tabs ----

    internal AppsPage Apps => _apps;
    internal LeftoverPage? Review => _review;
    internal int SelectedTab => _tabs.SelectedIndex;
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;
    internal FrameworkElement RecentTab => _recent;

    internal void SelectTab(int index)
    {
        _tabs.SelectedIndex = index;
        ShowTab();
    }

    private void Land(string? section)
    {
        if (section is null) return;
        if (section == UninstallerModule.RecentSection) { _settingTab = true; _tabs.SelectedIndex = 1; _settingTab = false; return; }
        if (section.StartsWith(UninstallerModule.SearchSection + ":", StringComparison.Ordinal)) _apps.SetSearch(section[(UninstallerModule.SearchSection.Length + 1)..]);
        else if (section.StartsWith(UninstallerModule.ScanSection + ":", StringComparison.Ordinal) && Guid.TryParse(section[(UninstallerModule.ScanSection.Length + 1)..], out var id))
        {
            // A Scan from the island: the review of that app. A record that is gone leaves the Uninstalled list.
            if (_engine.Recent.FirstOrDefault(r => r.Id == id) is { Restart: false } record) OpenReview(record);
            else { _settingTab = true; _tabs.SelectedIndex = 1; _settingTab = false; }
        }
    }

    private void ShowTab()
    {
        if (_disposed || _review is not null) return;
        _apps.ClosePopover();
        switch (_tabs.SelectedIndex)
        {
            case 0: _content.Child = _apps; break;
            case 1: BuildRecent(); _content.Child = _recent; break;
            default: ShowShortcuts(); _content.Child = _keys; break;
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        int n = _engine.Recent.Count;
        _recentCount.Text = n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "";
    }

    // ---- the Uninstalled tab ----

    private void BuildRecent()
    {
        var card = new StackPanel();
        var records = _engine.Recent;
        if (records.Count == 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 14), MinHeight = 20 };
            var mark = Glyph(UninstallerIcons.History, 16, "TextTertiaryBrush");
            mark.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(mark);
            row.Children.Add(Text("Nothing uninstalled lately", 14, "TextTertiaryBrush"));
            card.Children.Add(row);
        }
        bool first = true;
        foreach (var record in records)
        {
            var grid = new Grid { MinHeight = 56, Margin = new Thickness(16, 6, 16, 6) };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var mark = Mark(record.Name, null, 32);
            mark.Margin = new Thickness(0, 0, 12, 0);
            Add(grid, mark, 0);
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(Text(record.Name, 14));
            lines.Children.Add(Text(Caption(record), 12, record.Restart ? "WarningTextBrush" : "TextTertiaryBrush"));
            Add(grid, lines, 1);
            if (record.Cleaned)
            {
                var done = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var check = Glyph(LucideIcons.Check, 14, "SuccessBrush");
                check.Margin = new Thickness(0, 0, 5, 0);
                done.Children.Add(check);
                done.Children.Add(Text("Cleaned", 12, "SuccessBrush"));
                Add(grid, done, 2);
            }
            else
            {
                var scan = Verb("Scan", () => OpenReview(record), small: true);
                scan.IsEnabled = !record.Restart;
                scan.Tag = "scan:" + record.Id;
                Add(grid, scan, 2);
            }
            var row = new Border { Child = grid, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
            row.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
            AutomationProperties.SetName(row, record.Name);
            card.Children.Add(row);
            first = false;
        }
        var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = card, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        _recent.Child = new ScrollViewer { Content = box, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    }

    private string Caption(AppRecord record)
    {
        var when = record.CreatedUtc.ToLocalTime().Date;
        var today = Now().Date;
        string day = when == today ? "Today" : when == today.AddDays(-1) ? "Yesterday" : when.ToString("MMM d", CultureInfo.InvariantCulture);
        if (record.Restart) return day + " · Restart to finish";
        return record.HasRecord ? day : day + " · No record";
    }

    // ---- the Shortcuts tab ----

    private void BuildKeys()
    {
        var card = new Card();
        foreach (var (name, icon) in new[] { (UninstallerModule.UninstallApp, UninstallerIcons.PackageX), (UninstallerModule.ScanName, UninstallerIcons.ScanSearch) })
        {
            var view = new ShortcutSetting(name, () => _context.EditShortcut(name));
            card.Children.Add(new SettingRow { Header = name, Icon = icon, Content = view });
            _shortcuts.Add((name, view));
        }
        _keys.Child = card;
        _keys.VerticalAlignment = VerticalAlignment.Top;
    }

    private void ShowShortcuts()
    {
        foreach (var (name, view) in _shortcuts)
        {
            var slot = _context.Shortcuts.List.FirstOrDefault(s => s.Owner == UninstallerModule.Id && s.Name == name);
            view.Show(slot?.Shortcut, slot?.Error);
        }
    }

    // ---- the review ----

    internal void OpenReview(AppRecord record)
    {
        if (_disposed) return;
        CloseReview();
        _apps.ClosePopover();
        var page = new LeftoverPage(_engine, record, Closed, _openFolder);
        _review = page;
        _tabHost.Visibility = Visibility.Collapsed;
        _content.Child = page;
        Grid.SetRow(_content, 0);
        Grid.SetRowSpan(_content, 2);
    }

    private void Closed(bool changed)
    {
        CloseReview();
        _tabHost.Visibility = Visibility.Visible;
        Grid.SetRow(_content, 1);
        Grid.SetRowSpan(_content, 1);
        ShowTab();
        _apps.Redraw();
    }

    private void CloseReview()
    {
        if (_review is not { } review) return;
        _review = null;
        review.Dispose();
    }

    // ---- lifetime and fit ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_following) { _engine.Changed += OnEngineChanged; _following = true; }
        _scroller = FindScroller();
        if (_scroller is not null) { _scroller.SizeChanged += OnScrollerSize; Fit(); }
        ShowTab();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_following) { _engine.Changed -= OnEngineChanged; _following = false; }
        if (_scroller is not null) { _scroller.SizeChanged -= OnScrollerSize; _scroller = null; }
    }

    private ScrollViewer? FindScroller()
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(this); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer scroller) return scroller;
        return null;
    }

    private void OnScrollerSize(object sender, SizeChangedEventArgs e) => Fit();

    private void Fit()
    {
        if (_scroller is not { } scroller) return;
        double top = TransformToAncestor(scroller).Transform(new Point(0, 0)).Y + scroller.VerticalOffset;
        Height = Math.Max(360, scroller.ViewportHeight - top - 28);
    }

    private void OnEngineChanged()
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnEngineChanged); return; }
        UpdateCount();
    }

    internal static void OpenInExplorer(string folder)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnUnloaded(this, new RoutedEventArgs());
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        CloseReview();
        _apps.Dispose();
    }
}
