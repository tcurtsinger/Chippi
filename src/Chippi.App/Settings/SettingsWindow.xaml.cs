// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Chippi.App.Settings;

public sealed record SettingsPageInfo(string Id, string Label, Geometry Icon);

/// <summary>A sidebar group: its label, and whether it comes first (no gap above it).</summary>
public sealed record NavGroup(string Label, bool First);

/// <summary>A sidebar row, under its group.</summary>
public sealed record NavItem(string Id, string Label, Geometry Icon, NavGroup Group);

/// <summary>
/// A Settings page organized by task that several features share (DESIGN.md §5 Settings): Sound's
/// Output and Microphone. Each section is one installed feature's page, or one section of it.
/// </summary>
internal sealed record SharedPage(string Id, string Label, Geometry Icon, FeatureGroup Group, IReadOnlyList<SharedSection> Sections);

/// <param name="Section">The section of the feature's page, as its factory takes it; null for its first.</param>
/// <param name="Title">The heading over it, or null for none.</param>
internal sealed record SharedSection(string ModuleId, string? Section, string? Title);

public partial class SettingsWindow : Window
{
    /// <summary>The Chippi group, in sidebar order.</summary>
    public static IReadOnlyList<SettingsPageInfo> Pages { get; } =
    [
        new("general", "General", LucideIcons.Settings),
        new("island", "Island & pet", LucideIcons.Droplet),
        new("notifications", "Notifications", LucideIcons.Bell),
        new("features", "Add features", LucideIcons.Plus),
        new("about", "About", LucideIcons.Info),
    ];

    private const string AddFeatures = "features";

    /// <summary>Now Playing is the shell's, so the Sound group always shows it, first.</summary>
    private static readonly SettingsPageInfo NowPlaying = new("now-playing", "Now Playing", LucideIcons.Music);

    /// <summary>Sound by task: what you set for your output and for your mic, whichever features do it.</summary>
    internal static IReadOnlyList<SharedPage> SharedPages { get; } =
    [
        new("page:sound-output", "Output", LucideIcons.Speaker, FeatureGroup.Sound, [new("audio-routing", "output", null)]),
        new("page:sound-microphone", "Microphone", LucideIcons.Mic, FeatureGroup.Sound,
            [new("mic-mute", "mute", null), new("audio-routing", "microphone", null), new("mic-processing", null, "Mic processing"), new("mic-mute", null, "Mute shortcut")]),
    ];

    /// <summary>The features' pages in sidebar order within their groups; any not listed follow in catalog order.</summary>
    private static readonly string[] SidebarOrder =
    [
        "module:downloads-organizer", "module:shelf",
        "now-playing", "page:sound-output", "page:sound-microphone", "module:app-volume",
        "module:keep-awake", "module:system-monitor",
        "module:clipboard-history", "module:clean-url", "module:paste-plain-text", "module:scratchpad",
    ];

    /// <summary>Narrower than this, the sidebar becomes the icon rail.</summary>
    internal const double RailBelow = 1008;

    private readonly ShellContext _shell;
    private readonly ILogger _logger;
    private readonly Dictionary<string, FrameworkElement> _views = [];
    private readonly Dictionary<string, SettingsPageContribution> _contributions = [];
    private List<SettingsPageInfo> _featurePages = [];
    private bool _selecting;
    private bool _closed;
    private bool _overlay; // the rail's menu has the sidebar open over the page
    private bool _maximizePressed;
    private string _currentPage = "";
    private FrameworkElement? _pageView; // the current page's view, kept while search results show
    private const string ModulePrefix = "module:";

    /// <param name="remembered">The page is the one Settings last closed on: if it has gone since, open the first page.</param>
    public SettingsWindow(ShellContext shell, string page, bool remembered = false)
    {
        _shell = shell;
        _logger = shell.LoggerFactory.CreateLogger<SettingsWindow>();
        InitializeComponent();
        RestorePlacement(SettingsUi.ReadApp(shell.Config).SettingsWindow);
        RefreshFeatures();
        string request = page;
        if (remembered && Find(ref request) is null)
            page = "";
        Navigate(page);
        _shell.Registry.Changed += OnRegistryChanged;
        SourceInitialized += (_, _) =>
        {
            RoundCorners();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnHook);
        };
        StateChanged += (_, _) => OnStateChanged();
        OnStateChanged(); // a window restored maximized gets its inset and Restore glyph without a change to raise it
        SizeChanged += (_, e) => UpdateRail(e.NewSize.Width);
        // A row selected before the list was laid out scrolls into view once it is.
        Loaded += (_, _) =>
        {
            UpdateLayout();
            ScrollSelectedIntoView();
        };
    }

    internal FrameworkElement PageContent => PageHost;
    internal IEnumerable<SettingsPageInfo> NavigationPages => _featurePages.Concat(Pages);
    public string CurrentPage => _currentPage;

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

        // The sidebar groups the features' pages as Add features does, and Sound by task, then Chippi's own:
        // one list, so one scroller, one Tab stop, and arrow keys that cross groups.
        var shared = SharedPages.Where(p => p.Sections.Any(section => _contributions.ContainsKey(ModulePrefix + section.ModuleId))).ToList();
        var covered = SharedPages.SelectMany(p => p.Sections).Select(section => section.ModuleId).ToHashSet();
        _featurePages = [];
        var rows = new List<NavItem>();
        foreach (var group in Enum.GetValues<FeatureGroup>())
        {
            var pages = (group == FeatureGroup.Sound ? new[] { NowPlaying } : [])
                .Concat(shared.Where(p => p.Group == group).Select(p => new SettingsPageInfo(p.Id, p.Label, p.Icon)))
                .Concat(entries.Where(f => f.Descriptor.Group == group && !covered.Contains(f.Id))
                    .Select(f => new SettingsPageInfo(ModulePrefix + f.Id, f.Descriptor.Name, LucideIcons.Get(f.Descriptor.Icon))))
                .OrderBy(p => Array.IndexOf(SidebarOrder, p.Id) is var i and >= 0 ? i : int.MaxValue)
                .ToList();
            _featurePages.AddRange(pages);
            AddGroup(group.DisplayName(), pages);
        }
        AddGroup("Chippi", Pages);

        _selecting = true;
        Nav.ItemsSource = Grouped(rows);
        RailNav.ItemsSource = Grouped(rows);
        _selecting = false;

        void AddGroup(string label, IReadOnlyList<SettingsPageInfo> pages)
        {
            var group = new NavGroup(label, rows.Count == 0);
            rows.AddRange(pages.Select(p => new NavItem(p.Id, p.Label, p.Icon, group)));
        }

        static ListCollectionView Grouped(List<NavItem> rows)
        {
            var view = new ListCollectionView(rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));
            return view;
        }
    }

    /// <summary>The shared page a feature's deep link lands on: the one with that section, else its first.</summary>
    private static string? SharedFor(string moduleId, string? section) =>
        (SharedPages.FirstOrDefault(p => p.Sections.Any(x => x.ModuleId == moduleId && x.Section == section))
            ?? SharedPages.FirstOrDefault(p => p.Sections.Any(x => x.ModuleId == moduleId)))?.Id;

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_closed)
            return;
        RefreshFeatures();
        // A page still here keeps its unsaved work; one whose module went has nothing left to save.
        if (_currentPage.Length > 0 && LeaveGuard() is { } guard && !guard.ConfirmLeave(() => Navigate(_currentPage)))
        {
            ShowCurrent();
            return;
        }
        NavigateNow(_currentPage.Length == 0 ? "features" : _currentPage);
    });

    /// <summary>Shows <paramref name="page"/>, once the current page agrees to be left (it may ask first).</summary>
    public void Navigate(string page)
    {
        if (LeaveGuard() is { } guard && !guard.ConfirmLeave(() => Navigate(page)))
        {
            ShowCurrent();
            return;
        }
        NavigateNow(page);
    }

    /// <summary>The sidebar page a request opens, or null when it isn't there; a feature on a shared page (Sound's) is opened there.</summary>
    private SettingsPageInfo? Find(ref string page)
    {
        string baseId = page.Split('/')[0];
        var target = NavigationPages.FirstOrDefault(p => p.Id.Equals(baseId, StringComparison.OrdinalIgnoreCase));
        if (target is null && baseId.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase)
            && SharedFor(baseId[ModulePrefix.Length..], page.Contains('/') ? page[(page.IndexOf('/') + 1)..] : null) is { } sharedId)
        {
            target = NavigationPages.FirstOrDefault(p => p.Id == sharedId);
            page = sharedId;
        }
        return target;
    }

    private void NavigateNow(string page)
    {
        var target = Find(ref page);
        if (target is null)
        {
            if (page.Length > 0)
                _logger.LogWarning("Settings request '{Page}' could not be shown: the page is unavailable", page);
            target = page.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase) ? Pages.First(p => p.Id == AddFeatures) : NavigationPages.First();
        }
        string id = target.Id;
        if (id.StartsWith(ModulePrefix, StringComparison.Ordinal) && page.Contains('/'))
            id += page[page.IndexOf('/')..];
        ShowOverlay(false);
        Select(target.Id);
        _pageView = View(id);
        PageHost.Content = _pageView;
        _currentPage = id;
        Scroller.ScrollToTop();
    }

    /// <summary>Highlights a page in the sidebar (scrolled into view) and clears the search, without navigating.</summary>
    private void Select(string pageId)
    {
        _selecting = true;
        SettingsSearch.Clear();
        foreach (var list in new[] { Nav, RailNav })
            list.SelectedItem = list.Items.OfType<NavItem>().FirstOrDefault(i => i.Id == pageId);
        _selecting = false;
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        foreach (var list in new[] { Nav, RailNav })
        {
            if (list.SelectedItem is not NavItem item)
                continue;
            list.ScrollIntoView(item);
            // A group's first row brings its label into view with it.
            if (list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row
                && list.Items.OfType<NavItem>().First(i => i.Group == item.Group) == item)
                row.BringIntoView(new Rect(0, -30, row.ActualWidth, row.ActualHeight + 30));
        }
    }

    /// <summary>The current page stays (it is asking before it is left): its sidebar item and view come back.</summary>
    private void ShowCurrent()
    {
        Select(_currentPage.Split('/')[0]);
        PageHost.Content = _pageView;
    }

    /// <summary>The current page's view that asks before it is left, if any (found even while search results show).</summary>
    private ISettingsLeaveGuard? LeaveGuard() => _pageView is null ? null : FindGuard(_pageView);

    private static ISettingsLeaveGuard? FindGuard(DependencyObject root) =>
        root as ISettingsLeaveGuard
        ?? LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Select(FindGuard).FirstOrDefault(g => g is not null);

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selecting && sender is ListBox { SelectedItem: NavItem { Id: var id } })
            Navigate(id);
    }

    internal void RefreshImportedSettings()
    {
        foreach (string id in _views.Keys.Where(id => id != _currentPage).ToList())
            _views.Remove(id);
    }

    private FrameworkElement View(string id)
    {
        // A module view can switch tabs internally, so its cached route no longer describes it.
        // Every navigation must reapply the section through the module's factory. A shared page is
        // made of module views too.
        var shared = SharedPages.FirstOrDefault(p => p.Id == id);
        bool module = id.StartsWith(ModulePrefix, StringComparison.Ordinal) || shared is not null;
        if (!module && _views.TryGetValue(id, out var cached))
            return cached;
        FrameworkElement view;
        try
        {
            if (shared is not null)
                view = SharedView(shared);
            else if (id.StartsWith(ModulePrefix, StringComparison.Ordinal))
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
                    "now-playing" => new NowPlayingPage(_shell.Media),
                    _ => new GeneralPage(_shell),
                };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings request '{Page}' could not be shown: the page failed to load", id);
            view = new TextBlock { Text = "Couldn't open this page", TextWrapping = TextWrapping.Wrap };
        }
        if (!module)
            _views[id] = view;
        return view;
    }

    /// <summary>A shared page: its title, then each installed feature's section under its heading.</summary>
    private FrameworkElement SharedView(SharedPage shared)
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header(shared.Label, ""));
        foreach (var section in shared.Sections)
        {
            if (!_contributions.TryGetValue(ModulePrefix + section.ModuleId, out var contribution))
                continue;
            if (section.Title is { } title)
            {
                var heading = SettingsUi.Section(title);
                if (page.Children.Count == 1)
                    heading.Margin = new Thickness(0, 0, 0, 8); // the title already sits 14px above
                page.Children.Add(heading);
            }
            page.Children.Add(contribution.CreateView(section.Section) as FrameworkElement
                ?? throw new InvalidOperationException("The settings factory did not return a FrameworkElement."));
        }
        return page;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_selecting)
            return;
        string query = SettingsSearch.Text.Trim();
        if (query.Length == 0)
        {
            if (_currentPage.Length > 0)
                PageHost.Content = _pageView ?? View(_currentPage);
            return;
        }
        _selecting = true;
        Nav.SelectedItem = RailNav.SelectedItem = null;
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

    /// <summary>Clicks the first button whose name starts with <paramref name="prefix"/>; false when there is none.</summary>
    internal bool InvokeSnapshotActionStartingWith(string prefix)
    {
        if (Descendants(PageHost).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b)?.StartsWith(prefix, StringComparison.Ordinal) == true) is not { } button)
            return false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
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

    /// <summary>Esc doesn't close Settings (Windows 11 windows don't); it only closes the rail's open sidebar.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && _overlay)
        {
            e.Handled = true;
            ShowOverlay(false);
        }
    }

    /// <summary>A click outside the rail's open sidebar closes it, and still reaches what it clicked (a search result).</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        var point = e.GetPosition(Sidebar);
        if (_overlay && point.Y >= 0 && (point.X < 0 || point.X >= Sidebar.ActualWidth || point.Y >= Sidebar.ActualHeight))
            ShowOverlay(false);
    }

    /// <summary>A page with unsaved work asks in place first; its Discard closes the window.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && LeaveGuard() is { } guard && !guard.ConfirmLeave(Close))
        {
            e.Cancel = true;
            ShowCurrent();
        }
        if (!e.Cancel)
            SavePlacement();
    }

    /// <summary>
    /// Opens where Settings last closed, at that size and maximized or not. The first time, or when that
    /// spot is on no monitor now, it opens centered on the main display.
    /// </summary>
    private void RestorePlacement(WindowPlacement? saved)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (saved is { Width: > 0, Height: > 0 })
        {
            Width = Math.Max(saved.Width, MinWidth);
            Height = Math.Max(saved.Height, MinHeight);
        }
        if (saved is not null && OnAMonitor(new Rect(saved.Left, saved.Top, Width, Height)))
            (Left, Top) = (saved.Left, saved.Top);
        else
        {
            var area = SystemParameters.WorkArea; // the main display's
            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        }
        if (saved?.Maximized == true)
            WindowState = WindowState.Maximized;
    }

    /// <summary>Whether the window's titlebar would be on a monitor (DIPs at the system DPI, as WPF places a new window).</summary>
    private static unsafe bool OnAMonitor(Rect bounds)
    {
        double scale = PInvoke.GetDpiForSystem() / 96.0;
        var titlebar = new RECT
        {
            left = (int)(bounds.Left * scale),
            top = (int)(bounds.Top * scale),
            right = (int)(bounds.Right * scale),
            bottom = (int)((bounds.Top + 40) * scale),
        };
        return !PInvoke.MonitorFromRect(&titlebar, Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL).IsNull;
    }

    private void SavePlacement()
    {
        var bounds = RestoreBounds;
        bool maximized = WindowState == WindowState.Maximized;
        SettingsUi.SaveApp(_shell.Config, s =>
        {
            var saved = s.SettingsWindow ??= new WindowPlacement();
            saved.Page = _currentPage;
            if (!bounds.IsEmpty) // a window never shown keeps the bounds saved before
                (saved.Left, saved.Top, saved.Width, saved.Height, saved.Maximized) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized);
        });
    }

    /// <summary>Below <see cref="RailBelow"/> wide the sidebar becomes a 48px icon rail.</summary>
    private void UpdateRail(double width)
    {
        bool rail = width < RailBelow;
        if (rail == (Rail.Visibility == Visibility.Visible))
            return;
        Rail.Visibility = rail ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(rail ? 48 : 272);
        ShowOverlay(false);
        ScrollSelectedIntoView();
    }

    private static readonly System.Windows.Media.Effects.DropShadowEffect OverlayShadow = CreateOverlayShadow();

    private static System.Windows.Media.Effects.DropShadowEffect CreateOverlayShadow()
    {
        var shadow = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 32, ShadowDepth = 8, Direction = 0, Opacity = 0.35 };
        shadow.Freeze();
        return shadow;
    }

    /// <summary>The rail's menu: the full sidebar over the page, with a shadow, or back to the rail alone.</summary>
    private void ShowOverlay(bool open)
    {
        open &= Rail.Visibility == Visibility.Visible;
        _overlay = open;
        Sidebar.Visibility = open || Rail.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(Sidebar, open ? 2 : 1);
        Sidebar.HorizontalAlignment = open ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Sidebar.Width = open ? 272 : double.NaN;
        SidebarBack.SetResourceReference(Border.BackgroundProperty, open ? "FlyoutBrush" : "WindowBackgroundBrush");
        SidebarBack.BorderThickness = new Thickness(0, 0, open ? 1 : 0, 0);
        SidebarBack.Effect = open ? OverlayShadow : null;
        OverlayMenuButton.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SidebarPanel.Margin = new Thickness(12, open ? 4 : 14, 12, 0);
        if (open)
            Dispatcher.BeginInvoke(ScrollSelectedIntoView, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The page fills the width up to its MaxWidth; past it, it stays left rather than centering as a stretched one would.</summary>
    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e) =>
        PageHost.Width = Math.Max(0, e.NewSize.Width - PageHost.Margin.Left - PageHost.Margin.Right);

    private void OnMenu(object sender, RoutedEventArgs e) => ShowOverlay(!_overlay);

    private void OnRailSearch(object sender, RoutedEventArgs e)
    {
        ShowOverlay(true);
        Dispatcher.BeginInvoke(() => SettingsSearch.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _pageView = null;
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

    /// <summary>
    /// Snap layouts: Windows shows its flyout only over a window's own maximize button, so the hit test
    /// reports ours as one (HTMAXBUTTON). Windows then sends its mouse input as non-client messages,
    /// so this also draws its hover and pressed states and clicks it.
    /// </summary>
    private unsafe nint OnHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case PInvoke.WM_NCHITTEST:
                if (OverMaximize(lParam))
                {
                    SetMaximizeState(_maximizePressed ? "Pressed" : "Hover");
                    handled = true;
                    return (nint)PInvoke.HTMAXBUTTON;
                }
                SetMaximizeState(null);
                break;
            case PInvoke.WM_NCMOUSEMOVE when wParam == (nint)PInvoke.HTMAXBUTTON:
                // Ask for WM_NCMOUSELEAVE, so leaving the window from the button clears its hover.
                var track = new Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT
                {
                    cbSize = (uint)sizeof(Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT),
                    dwFlags = Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT_FLAGS.TME_LEAVE
                        | Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT_FLAGS.TME_NONCLIENT,
                    hwndTrack = (HWND)hwnd,
                };
                PInvoke.TrackMouseEvent(ref track);
                break;
            case PInvoke.WM_NCLBUTTONDOWN when wParam == (nint)PInvoke.HTMAXBUTTON:
                _maximizePressed = true;
                SetMaximizeState("Pressed");
                handled = true;
                break;
            case PInvoke.WM_NCLBUTTONUP when wParam == (nint)PInvoke.HTMAXBUTTON:
                if (_maximizePressed)
                    OnMaximize(MaximizeButton, new RoutedEventArgs());
                _maximizePressed = false;
                SetMaximizeState(null);
                handled = true;
                break;
            case PInvoke.WM_NCMOUSELEAVE:
                _maximizePressed = false;
                SetMaximizeState(null);
                break;
        }
        return 0;
    }

    private bool OverMaximize(nint lParam)
    {
        if (PresentationSource.FromVisual(MaximizeButton) is null)
            return false;
        // The screen point, in physical pixels, packed as two signed 16-bit values.
        var point = MaximizeButton.PointFromScreen(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
        return point.X >= 0 && point.Y >= 0 && point.X < MaximizeButton.ActualWidth && point.Y < MaximizeButton.ActualHeight;
    }

    private void SetMaximizeState(string? state)
    {
        if (!Equals(MaximizeButton.Tag, state))
            MaximizeButton.Tag = state;
    }

    /// <summary>Windows 11 rounded corners on the DWM frame. Best effort: older builds ignore it.</summary>
    private unsafe void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }
}
