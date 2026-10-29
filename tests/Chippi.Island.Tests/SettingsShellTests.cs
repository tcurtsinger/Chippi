// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.App.Settings;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>The Settings window's shell (brief F1, shots s1-s5). Windows show off screen and never activate.</summary>
public sealed class SettingsShellTests
{
    /// <summary>s1's seven features, out of order, so the sidebar's own order shows.</summary>
    internal static readonly FakeFeature[] Seven =
    [
        new("scratchpad", "Scratchpad", FeatureGroup.ClipboardText, "NotebookPen"),
        new("paste-plain-text", "Paste as plain text", FeatureGroup.ClipboardText, "ClipboardType"),
        new("clean-url", "Clean URL", FeatureGroup.ClipboardText, "Link"),
        new("clipboard-history", "Clipboard history", FeatureGroup.ClipboardText, "ClipboardList"),
        new("system-monitor", "System monitor", FeatureGroup.System, "Activity"),
        new("keep-awake", "Keep awake", FeatureGroup.System, "Coffee"),
        new("app-volume", "App volume", FeatureGroup.Sound, "Sliders"),
        new("downloads-organizer", "File organizer", FeatureGroup.Files, "Folder"),
    ];

    /// <summary>Every feature with a Settings page today: Sound's Output and Microphone come from Audio routing.</summary>
    internal static readonly FakeFeature[] All =
    [
        .. Seven,
        new("shelf", "Shelf", FeatureGroup.Files, "Inbox"),
        new("audio-routing", "Audio routing", FeatureGroup.Sound, "Headphones"),
    ];

    [Fact]
    public void The_sidebar_is_one_grouped_list_of_32_rows_36_apart_in_the_brief_s_order() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene(Seven);
        var window = scene.Show("general");

        Assert.Single(Descendants(window.Sidebar).OfType<ListBox>());
        string[] expected =
        [
            "module:downloads-organizer", "now-playing", "module:app-volume", "module:keep-awake", "module:system-monitor",
            "module:clipboard-history", "module:clean-url", "module:paste-plain-text", "module:scratchpad",
            "general", "island", "notifications", "features", "about",
        ];
        Assert.Equal(expected, window.Nav.Items.OfType<NavItem>().Select(i => i.Id));
        Assert.Equal(["Files", "Sound", "System", "Clipboard & text", "Chippi"],
            window.Nav.Items.Groups!.OfType<CollectionViewGroup>().Select(g => ((NavGroup)g.Name).Label));

        var rows = window.Nav.Items.OfType<NavItem>().Select(i => (ListBoxItem)window.Nav.ItemContainerGenerator.ContainerFromItem(i)).ToList();
        Assert.All(rows, row => Assert.Equal(32, row.ActualHeight));
        double Top(ListBoxItem row) => row.TranslatePoint(default, window.Nav).Y;
        Assert.Equal(36, Top(rows[2]) - Top(rows[1])); // within a group (Now Playing, App volume)
        Assert.Equal(36 + 16 + 12, Top(rows[3]) - Top(rows[2]), 0.5); // across one: the label and 12 above it
        Assert.Equal(16 + 2 + 2, Top(rows[0]), 0.5); // the first label has nothing above it

        Assert.Equal(32, window.SettingsSearch.ActualHeight);
    });

    [Fact]
    public void At_1120_x_720_s1_s_seven_features_and_Now_Playing_fit_without_scrolling() => WpfTestThread.Run(() =>
    {
        // s1's seven have no Scratchpad page; with it (Seven) the list scrolls 12, as the mockup's fuller sidebars do.
        using var scene = new SettingsScene(Seven.Where(f => f.Id != "scratchpad").ToArray());
        var window = scene.Show("general");
        Assert.Equal(13, window.Nav.Items.Count);
        Assert.Equal(0, Scroller(window.Nav).ScrollableHeight);
    });

    [Fact]
    public void Opening_a_page_scrolls_its_row_into_view() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene(All);
        void InView(SettingsWindow window)
        {
            var scroller = Scroller(window.Nav);
            Assert.True(scroller.ScrollableHeight > 0);
            var row = (ListBoxItem)window.Nav.ItemContainerGenerator.ContainerFromItem(window.Nav.SelectedItem);
            Assert.InRange(row.TranslatePoint(default, scroller).Y, 0, scroller.ViewportHeight - row.ActualHeight);
        }

        InView(scene.Show("about", height: 560)); // opened on it

        var window = scene.Show("general", height: 560);
        var scroller = Scroller(window.Nav);
        window.Navigate("about");
        window.UpdateLayout();
        InView(window);

        window.Navigate("module:downloads-organizer");
        window.UpdateLayout();
        Assert.Equal(0, scroller.VerticalOffset);
    });

    [Fact]
    public void Esc_does_not_close_Settings_and_still_clears_the_search() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene(Seven);
        var window = scene.Show("general");
        window.SettingsSearch.Text = "theme";
        Press(window.SettingsSearch, Key.Escape);
        Assert.Equal("", window.SettingsSearch.Text);
        Press(window.SettingsSearch, Key.Escape);
        Press(window, Key.Escape);
        Assert.True(window.IsVisible);
    });

    [Fact]
    public void The_window_reopens_where_it_closed_and_centers_on_the_main_display_when_that_spot_is_gone() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene([]);
        var area = SystemParameters.WorkArea;
        // Centered, but never above or left of the work area (CI's display is smaller than the window).
        double Center(double start, double span, double size) => start + Math.Max(0, (span - size) / 2);

        var first = new SettingsWindow(scene.Shell, "general");
        Assert.Equal((1120, 720), (first.Width, first.Height));
        Assert.Equal(Center(area.Left, area.Width, 1120), first.Left, 0.5);
        Assert.Equal(Center(area.Top, area.Height, 720), first.Top, 0.5);
        first.Close();

        SettingsUi.SaveApp(scene.Shell.Config, s => s.SettingsWindow = new WindowPlacement { Left = area.Left + 20, Top = area.Top + 30, Width = 1000, Height = 600 });
        var saved = new SettingsWindow(scene.Shell, "general");
        Assert.Equal((area.Left + 20, area.Top + 30, 1000d, 600d), (saved.Left, saved.Top, saved.Width, saved.Height));
        Assert.Equal(WindowState.Normal, saved.WindowState);
        saved.Close();

        SettingsUi.SaveApp(scene.Shell.Config, s => s.SettingsWindow!.Maximized = true);
        var maximized = new SettingsWindow(scene.Shell, "general");
        Assert.Equal(WindowState.Maximized, maximized.WindowState);
        Assert.Equal("Restore", System.Windows.Automation.AutomationProperties.GetName(maximized.MaximizeButton));
        Assert.NotEqual(new Thickness(0), maximized.Root.Margin); // pulled in from the monitor's edge
        maximized.Close();

        // Closing saves where it is: here off every monitor, so the next one centers on the main display.
        SettingsUi.SaveApp(scene.Shell.Config, s => s.SettingsWindow = null); // a maximized one would show on screen
        var shown = scene.Show("general", width: 900, height: 600);
        shown.Close();
        var placement = SettingsUi.ReadApp(scene.Shell.Config).SettingsWindow!;
        Assert.Equal((-20000d, 900d, 600d, false), (placement.Left, placement.Width, placement.Height, placement.Maximized));
        var moved = new SettingsWindow(scene.Shell, "general");
        Assert.Equal((900d, 600d), (moved.Width, moved.Height));
        Assert.Equal(Center(area.Left, area.Width, 900), moved.Left, 0.5);
        moved.Close();
    });

    [Fact]
    public void Page_content_stops_at_1000_aligned_left_and_cards_keep_their_width_when_the_page_scrolls() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene([.. Seven, new("tall", "Tall page", FeatureGroup.Apps, "Info", Tall: true)]);
        // Windows caps a window at about its display's size, so the widths come from the window it got
        // (1120 here gives 768; CI's smaller display gives less).
        var window = scene.Show("general");
        double Expected() => Math.Min(1000, window.Scroller.ActualWidth - 80);
        Assert.Equal(window.ActualWidth - 272 - 80, window.PageHost.ActualWidth);
        double shortPage = window.PageHost.ActualWidth;
        window.Navigate("module:tall");
        window.UpdateLayout();
        Assert.True(window.Scroller.ScrollableHeight > 0);
        Assert.Equal(shortPage, window.PageHost.ActualWidth); // the floating scrollbar takes no width

        window.Width = 1800;
        window.UpdateLayout();
        Assert.Equal(1000, window.PageHost.MaxWidth);
        Assert.Equal(Expected(), window.PageHost.ActualWidth); // 1000 wherever the display lets the window past 1080 + 272
        Assert.Equal(40, window.PageHost.TranslatePoint(default, window.Scroller).X);
    });

    [Fact]
    public void Below_1008_the_sidebar_becomes_a_rail_whose_menu_opens_it_over_the_page() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene(Seven);
        var window = scene.Show("general", width: 1008);
        Assert.Equal(Visibility.Collapsed, window.Rail.Visibility);

        window.Width = 900;
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, window.Rail.Visibility);
        Assert.Equal(48, window.SidebarColumn.ActualWidth);
        Assert.Equal(Visibility.Collapsed, window.Sidebar.Visibility);
        Assert.Equal(window.Nav.Items.Count, window.RailNav.Items.Count);
        Assert.Equal("general", ((NavItem)window.RailNav.SelectedItem).Id);
        Assert.Equal(900 - 48 - 80, window.PageHost.ActualWidth);

        Click(Descendants(window.Rail).OfType<Button>().First());
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, window.Sidebar.Visibility);
        Assert.Equal(272, window.Sidebar.ActualWidth);
        Assert.Equal(0, window.Sidebar.TranslatePoint(default, window.Rail).X + 4); // over the rail, from the window's edge

        Press(window, Key.Escape); // Esc closes it, not Settings
        Assert.Equal(Visibility.Collapsed, window.Sidebar.Visibility);
        Assert.True(window.IsVisible);

        Click(Descendants(window.Rail).OfType<Button>().First());
        window.Nav.SelectedItem = window.Nav.Items.OfType<NavItem>().First(i => i.Id == "about"); // a pick closes it
        Assert.Equal("about", window.CurrentPage);
        Assert.Equal(Visibility.Collapsed, window.Sidebar.Visibility);
        Assert.Equal("about", ((NavItem)window.RailNav.SelectedItem).Id);

        window.Width = 1120;
        window.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, window.Rail.Visibility);
        Assert.Equal(Visibility.Visible, window.Sidebar.Visibility);
        Assert.Equal(272, window.SidebarColumn.ActualWidth);
    });

    [Fact]
    public void The_maximize_button_reports_itself_to_Windows_for_snap_layouts() => WpfTestThread.Run(() =>
    {
        using var scene = new SettingsScene([]);
        var window = scene.Show("general");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var maximize = window.MaximizeButton;
        nint HitTest(UIElement element, Point at)
        {
            var screen = element.PointToScreen(at);
            return SendMessage(hwnd, 0x0084, 0, ((int)screen.Y << 16) | ((int)screen.X & 0xFFFF));
        }

        Assert.Equal(9, HitTest(maximize, new Point(23, 20))); // HTMAXBUTTON
        Assert.Equal("Hover", maximize.Tag);
        Assert.NotEqual(9, HitTest(window.MinimizeButton, new Point(23, 20)));
        Assert.Null(maximize.Tag);
        Assert.Equal(new Thickness(0), ((Border)((Grid)window.Root.Child).Children[0]).BorderThickness); // no line under the titlebar
    });

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    private static void Press(IInputElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    internal static ScrollViewer Scroller(DependencyObject root) => VisualDescendants(root).OfType<ScrollViewer>().First();

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var item in VisualDescendants(child))
                yield return item;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child))
                yield return item;
    }

    private sealed class FakeSource : PresentationSource
    {
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
    }

    internal sealed record FakeFeature(string Id, string Name, FeatureGroup Group, string Icon, bool Tall = false) : IFeatureModule
    {
        public FeatureDescriptor Descriptor { get; } = new(Id, Name, "Test page", Group) { Icon = Icon };

        public Task StartAsync(ModuleContext context, CancellationToken ct)
        {
            context.Ui.SetSettingsPage(_ => Tall ? new Border { Height = 3000 } : new TextBlock { Text = Name });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>A profile with the given features installed, and Settings windows that show off screen.</summary>
    internal sealed class SettingsScene : IDisposable
    {
        private readonly string _root;
        private readonly string _profile = "settings-shell-" + Guid.NewGuid().ToString("N");
        private readonly List<Window> _windows = [];

        public SettingsScene(IEnumerable<FakeFeature> features)
        {
            _root = Path.Combine(Path.GetTempPath(), _profile);
            Directory.CreateDirectory(_root);
            Shell = new ShellContext(new ConfigStore(Path.Combine(_root, "settings.json")), NullLoggerFactory.Instance, false, _profile);
            foreach (var feature in features)
            {
                Shell.Registry.Register(feature);
                Shell.Registry.SetInstalledAsync(feature.Id, true).GetAwaiter().GetResult();
            }
        }

        public ShellContext Shell { get; }

        public SettingsWindow Show(string page, double width = 1120, double height = 720)
        {
            var window = new SettingsWindow(Shell, page) { ShowActivated = false, Width = width, Height = height };
            (window.Left, window.Top) = (-20000, 0);
            _windows.Add(window);
            window.Show();
            window.UpdateLayout();
            return window;
        }

        public void Dispose()
        {
            foreach (var window in _windows)
                window.Close();
            Shell.Registry.StopAllAsync().GetAwaiter().GetResult();
            Shell.Clipboard.Dispose();
            foreach (string folder in new[] { _root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", _profile) })
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
        }
    }
}
