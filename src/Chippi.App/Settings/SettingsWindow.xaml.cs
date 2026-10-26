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
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Chippi.App.Settings;

public sealed record SettingsPageInfo(string Id, string Label, Geometry Icon);

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

    /// <summary>Sound by task: what you set for your output and for your mic, whichever features do it.</summary>
    internal static IReadOnlyList<SharedPage> SharedPages { get; } =
    [
        new("page:sound-output", "Output", LucideIcons.Speaker, FeatureGroup.Sound, [new("audio-routing", "output", null)]),
        new("page:sound-microphone", "Microphone", LucideIcons.Mic, FeatureGroup.Sound,
            [new("audio-routing", "microphone", "Mic in use"), new("mic-processing", null, "Clean-up"), new("mic-mute", null, "Mute shortcut")]),
    ];

    private readonly ShellContext _shell;
    private readonly ILogger _logger;
    private readonly Dictionary<string, FrameworkElement> _views = [];
    private readonly Dictionary<string, SettingsPageContribution> _contributions = [];
    private List<SettingsPageInfo> _featurePages = [];
    private readonly List<ListBox> _featureLists = []; // one per group with installed features
    private bool _selecting;
    private bool _closed;
    private string _currentPage = "";
    private FrameworkElement? _pageView; // the current page's view, kept while search results show
    private const string ModulePrefix = "module:";

    public SettingsWindow(ShellContext shell, string page)
    {
        _shell = shell;
        _logger = shell.LoggerFactory.CreateLogger<SettingsWindow>();
        InitializeComponent();
        Nav.ItemsSource = Pages.Select(Item).ToList();
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

        // The sidebar groups the features' pages as Add features does, and Sound by task.
        var shared = SharedPages.Where(p => p.Sections.Any(section => _contributions.ContainsKey(ModulePrefix + section.ModuleId))).ToList();
        var covered = SharedPages.SelectMany(p => p.Sections).Select(section => section.ModuleId).ToHashSet();
        _featurePages = [];
        _selecting = true;
        FeatureGroups.Children.Clear();
        _featureLists.Clear();
        foreach (var group in Enum.GetValues<FeatureGroup>())
        {
            var pages = shared.Where(p => p.Group == group).Select(p => new SettingsPageInfo(p.Id, p.Label, p.Icon))
                .Concat(entries.Where(f => f.Descriptor.Group == group && !covered.Contains(f.Id))
                    .Select(f => new SettingsPageInfo(ModulePrefix + f.Id, f.Descriptor.Name, LucideIcons.Get(f.Descriptor.Icon))))
                .ToList();
            if (pages.Count == 0)
                continue;
            _featurePages.AddRange(pages);
            var header = new TextBlock { Text = group.DisplayName(), Margin = new Thickness(12, FeatureGroups.Children.Count == 0 ? 0 : 20, 0, 6) };
            header.SetResourceReference(StyleProperty, "NavGroupTextStyle");
            var list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemsSource = pages.Select(Item).ToList() };
            list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "NavItemStyle");
            list.SetResourceReference(ItemsControl.ItemTemplateProperty, "NavItemTemplate");
            AutomationProperties.SetName(list, group.DisplayName());
            list.SelectionChanged += OnNavSelectionChanged;
            FeatureGroups.Children.Add(header);
            FeatureGroups.Children.Add(list);
            _featureLists.Add(list);
        }
        ChippiGroup.Margin = new Thickness(12, FeatureGroups.Children.Count == 0 ? 0 : 20, 0, 6);
        _selecting = false;
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

    private void NavigateNow(string page)
    {
        string baseId = page.Split('/')[0];
        var target = NavigationPages.FirstOrDefault(p => p.Id.Equals(baseId, StringComparison.OrdinalIgnoreCase));
        // A feature on a shared page (Sound's) is opened there.
        if (target is null && baseId.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase)
            && SharedFor(baseId[ModulePrefix.Length..], page.Contains('/') ? page[(page.IndexOf('/') + 1)..] : null) is { } sharedId)
        {
            target = NavigationPages.FirstOrDefault(p => p.Id == sharedId);
            page = sharedId;
        }
        if (target is null)
        {
            if (page.Length > 0)
                _logger.LogWarning("Settings request '{Page}' could not be shown: the page is unavailable", page);
            target = page.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase) ? Pages.First(p => p.Id == AddFeatures) : NavigationPages.First();
        }
        string id = target.Id;
        if (id.StartsWith(ModulePrefix, StringComparison.Ordinal) && page.Contains('/'))
            id += page[page.IndexOf('/')..];
        Select(target.Id);
        _pageView = View(id);
        PageHost.Content = _pageView;
        _currentPage = id;
        Scroller.ScrollToTop();
    }

    /// <summary>Highlights a page in the sidebar and clears the search, without navigating.</summary>
    private void Select(string pageId)
    {
        _selecting = true;
        SettingsSearch.Clear();
        foreach (var list in _featureLists.Append(Nav))
            list.SelectedItem = list.Items.OfType<SegmentItem>().FirstOrDefault(i => Equals(i.Value, pageId));
        _selecting = false;
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
        if (!_selecting && sender is ListBox { SelectedItem: SegmentItem { Value: string id } })
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
        foreach (var list in _featureLists.Append(Nav))
            list.SelectedItem = null;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
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

    /// <summary>Windows 11 rounded corners on the DWM frame. Best effort: older builds ignore it.</summary>
    private unsafe void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }
}
