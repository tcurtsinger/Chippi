// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Chippi.App.Settings;

public sealed record SettingsPageInfo(string Id, string Label, Geometry Icon);

public partial class SettingsWindow : Window
{
    public static IReadOnlyList<SettingsPageInfo> Pages { get; } =
    [
        new("features", "Add features", LucideIcons.Plus),
        new("general", "General", LucideIcons.Settings),
        new("island", "Island & pet", LucideIcons.Droplet),
        new("notifications", "Notifications", LucideIcons.Bell),
        new("about", "About", LucideIcons.Info),
    ];

    private readonly ShellContext _shell;
    private readonly Dictionary<string, FrameworkElement> _views = [];
    private readonly Dictionary<string, SettingsPageContribution> _contributions = [];
    private List<SettingsPageInfo> _featurePages = [];
    private bool _selecting;
    private bool _closed;
    private string _currentPage = "";
    private const string ModulePrefix = "module:";

    public SettingsWindow(ShellContext shell, string page)
    {
        _shell = shell;
        InitializeComponent();
        Nav.ItemsSource = Pages.Skip(1).Select(Item).ToList();
        RefreshFeatures();
        Navigate(page);
        _shell.Registry.Changed += OnRegistryChanged;
        SourceInitialized += (_, _) => RoundCorners();
        StateChanged += (_, _) => OnStateChanged();
    }

    internal FrameworkElement PageContent => PageHost;
    internal IEnumerable<SettingsPageInfo> NavigationPages => _featurePages.Concat(Pages);
    public string CurrentPage => _currentPage;

    private static SegmentItem Item(SettingsPageInfo page) => new() { Label = page.Label, Icon = page.Icon, Value = page.Id };

    private void RefreshFeatures()
    {
        var entries = _shell.Registry.Features.Where(f => f.IsInstalled && f.SettingsPage is not null).ToList();
        foreach (var key in _contributions.Keys.ToList())
        {
            var entry = entries.FirstOrDefault(f => ModulePrefix + f.Id == key);
            if (entry?.SettingsPage == _contributions[key])
                continue;
            _contributions.Remove(key);
            foreach (string cached in _views.Keys.Where(k => k == key || k.StartsWith(key + "/", StringComparison.Ordinal)).ToList())
                _views.Remove(cached);
            if (_currentPage == key || _currentPage.StartsWith(key + "/", StringComparison.Ordinal))
                _currentPage = "";
        }
        foreach (var entry in entries)
            _contributions[ModulePrefix + entry.Id] = entry.SettingsPage!;
        _featurePages = entries.Select(f => new SettingsPageInfo(ModulePrefix + f.Id, f.Descriptor.Name, LucideIcons.Get(f.Descriptor.Icon))).ToList();
        _selecting = true;
        FeatureNav.ItemsSource = _featurePages.Append(Pages[0]).Select(Item).ToList();
        _selecting = false;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_closed)
            return;
        RefreshFeatures();
        Navigate(_currentPage.Length == 0 ? "features" : _currentPage);
    });

    public void Navigate(string page)
    {
        string baseId = page.Split('/')[0];
        var target = NavigationPages.FirstOrDefault(p => p.Id.Equals(baseId, StringComparison.OrdinalIgnoreCase))
            ?? (page.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase) ? Pages[0] : NavigationPages.First());
        string id = target.Id;
        if (id.StartsWith(ModulePrefix, StringComparison.Ordinal) && page.Contains('/'))
            id += page[page.IndexOf('/')..];
        _selecting = true;
        SettingsSearch.Clear();
        FeatureNav.SelectedItem = FeatureNav.Items.OfType<SegmentItem>().FirstOrDefault(i => Equals(i.Value, target.Id));
        Nav.SelectedItem = Nav.Items.OfType<SegmentItem>().FirstOrDefault(i => Equals(i.Value, target.Id));
        _selecting = false;
        PageHost.Content = View(id);
        _currentPage = id;
        Scroller.ScrollToTop();
    }

    private void OnFeatureSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selecting && FeatureNav.SelectedItem is SegmentItem { Value: string id })
            Navigate(id);
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selecting && Nav.SelectedItem is SegmentItem { Value: string id })
            Navigate(id);
    }

    internal void RefreshImportedSettings()
    {
        foreach (string id in _views.Keys.Where(id => id != _currentPage).ToList())
            _views.Remove(id);
    }

    private FrameworkElement View(string id)
    {
        if (_views.TryGetValue(id, out var cached))
            return cached;
        FrameworkElement view;
        try
        {
            if (id.StartsWith(ModulePrefix, StringComparison.Ordinal))
            {
                int slash = id.IndexOf('/');
                string baseId = slash < 0 ? id : id[..slash];
                string? section = slash < 0 ? null : id[(slash + 1)..];
                var contribution = _contributions[baseId];
                var moduleView = contribution.CreateView(section) as FrameworkElement
                    ?? throw new InvalidOperationException("The settings factory did not return a FrameworkElement.");
                view = new ModulePageHost(NavigationPages.First(p => p.Id == baseId).Label, moduleView);
            }
            else
                view = id switch
                {
                    "features" => new AddFeaturesPage(_shell),
                    "island" => new IslandPetPage(_shell),
                    "notifications" => new NotificationsPage(_shell),
                    "about" => new AboutPage(),
                    _ => new GeneralPage(_shell),
                };
        }
        catch (Exception ex)
        {
            Log.Error($"Settings page '{id}' failed to load", ex);
            view = new TextBlock { Text = "Couldn't open this page. Close Settings and try again.", TextWrapping = TextWrapping.Wrap };
        }
        _views[id] = view;
        return view;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_selecting)
            return;
        string query = SettingsSearch.Text.Trim();
        if (query.Length == 0)
        {
            if (_currentPage.Length > 0)
                PageHost.Content = View(_currentPage);
            return;
        }
        _selecting = true;
        FeatureNav.SelectedItem = null;
        Nav.SelectedItem = null;
        _selecting = false;
        var results = new StackPanel();
        results.Children.Add(SettingsUi.Header("Search results", ""));
        int count = 0;
        foreach (var page in NavigationPages)
        {
            var labels = Labels(View(page.Id)).Append(page.Label)
                .Where(label => label.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Distinct().ToList();
            if (labels.Count == 0)
                continue;
            results.Children.Add(SettingsUi.Section(page.Label));
            var card = new Card();
            foreach (string label in labels)
            {
                var button = SettingsUi.Button(label);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0);
                button.MinHeight = 40;
                button.SetResourceReference(StyleProperty, "SearchResultButtonStyle");
                button.Click += (_, _) => Navigate(page.Id);
                card.Children.Add(button);
                count++;
            }
            results.Children.Add(card);
        }
        if (count == 0)
            results.Children.Add(SettingsUi.Styled(new TextBlock { Text = "No settings match" }, "CaptionTextStyle"));
        PageHost.Content = results;
        Scroller.ScrollToTop();
    }

#if DEBUG
    internal void SearchSnapshot(string query) => SettingsSearch.Text = query;

    internal void InvokeSnapshotAction(string name)
    {
        var button = Descendants(PageHost).OfType<Button>().First(b => AutomationProperties.GetName(b) == name || Equals(b.Content, name));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal void VerifyFeatureSearchSnapshot() => Descendants(PageHost).OfType<AddFeaturesPage>().Single().VerifySearchSnapshot();

    internal void ExpandSnapshotFeatures(bool expanded) =>
        Descendants(PageHost).OfType<ExpanderRow>().First().IsExpanded = expanded;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child))
                yield return item;
    }
#endif

    private static IEnumerable<string> Labels(DependencyObject root)
    {
        if (root is SettingRow { Header.Length: > 0 } row)
        {
            yield return row.Header;
            yield break;
        }
        else if (root is ExpanderRow { Header: string label })
            yield return label;
        else if (root is Control control && AutomationProperties.GetName(control) is { Length: > 0 } name)
            yield return name;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (string label in Labels(child))
                yield return label;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _shell.Registry.Changed -= OnRegistryChanged;
        PageHost.Content = null;
        _views.Clear();
        base.OnClosed(e);
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged()
    {
        bool maximized = WindowState == WindowState.Maximized;
        // A maximized WindowChrome window overhangs the monitor by its resize frame; pad it back in.
        var frame = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = maximized ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4) : new Thickness(0);
        MaximizeGlyph.Data = maximized ? RestoreIcon : LucideIcons.Square;
        string name = maximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(MaximizeButton, name);
        MaximizeButton.ToolTip = name;
    }

    private static readonly Geometry RestoreIcon = CreateRestoreIcon();

    private static Geometry CreateRestoreIcon()
    {
        var g = Geometry.Parse("M8,8 H18 a2,2 0 0 1 2,2 V18 a2,2 0 0 1 -2,2 H8 a2,2 0 0 1 -2,-2 V10 a2,2 0 0 1 2,-2 Z M10,4 H20 a2,2 0 0 1 2,2 V14");
        g.Freeze();
        return g;
    }

    /// <summary>Windows 11 rounded corners on the DWM frame. Best effort: older builds ignore it.</summary>
    private unsafe void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }
}
