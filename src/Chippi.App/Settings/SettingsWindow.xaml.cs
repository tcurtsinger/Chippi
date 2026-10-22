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

/// <summary>A sidebar entry. <see cref="Id"/> is what <see cref="ShellCommands.OpenSettings"/> takes.</summary>
public sealed record SettingsPageInfo(string Id, string Label, Geometry Icon)
{
    public override string ToString() => Label;
}

public partial class SettingsWindow : Window
{
    public static IReadOnlyList<SettingsPageInfo> Pages { get; } =
    [
        new("general", "General", LucideIcons.Settings),
        new("features", "Features", LucideIcons.LayoutGrid),
        new("island", "Island & pet", LucideIcons.Droplet),
        new("shortcuts", "Shortcuts", LucideIcons.Keyboard),
        new("permissions", "Permissions", LucideIcons.Shield),
        new("appearance", "Appearance", LucideIcons.Sun),
        new("about", "About", LucideIcons.Info),
    ];

    private readonly ShellContext _shell;
    private FeatureEntry? _modulePage;

    public SettingsWindow(ShellContext shell, string page)
    {
        _shell = shell;
        InitializeComponent();
        VersionLine.Text = $"v{AppInfo.Version} · GPL-3.0-or-later";
        Nav.ItemsSource = Pages;
        Navigate(page);
        SourceInitialized += (_, _) => RoundCorners();
        StateChanged += (_, _) => OnStateChanged();
    }

    /// <summary>The page's own element (inside the scroller), for the debug snapshot.</summary>
    internal FrameworkElement PageContent => PageHost;

    /// <summary>The sidebar page showing, for reopening at the same place.</summary>
    public string CurrentPage => (Nav.SelectedItem as SettingsPageInfo)?.Id ?? "general";

    public void Navigate(string page)
    {
        var target = Pages.FirstOrDefault(p => p.Id == page) ?? Pages[0];
        if (ReferenceEquals(Nav.SelectedItem, target) && _modulePage is null)
            return;
        if (ReferenceEquals(Nav.SelectedItem, target))
            ShowPage(target);
        else
            Nav.SelectedItem = target;
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is SettingsPageInfo page)
            ShowPage(page);
    }

    private void ShowPage(SettingsPageInfo page)
    {
        LeaveModulePage();
        PageHost.Content = page.Id switch
        {
            "features" => new FeaturesPage(_shell, ShowModulePage),
            "island" => new IslandPetPage(_shell),
            "shortcuts" => new ShortcutsPage(),
            "permissions" => new PermissionsPage(_shell),
            "appearance" => new AppearancePage(() => Navigate("general"), () => Navigate("island")),
            "about" => new AboutPage(),
            _ => new GeneralPage(_shell),
        };
        Scroller.ScrollToTop();
    }

    /// <summary>A feature's own settings page, reached from its card. Esc or Back returns to Features.</summary>
    private void ShowModulePage(FeatureEntry entry)
    {
        if (entry.SettingsPage is not { } contribution)
            return;
        FrameworkElement view;
        try
        {
            view = contribution.CreateView() as FrameworkElement
                ?? throw new InvalidOperationException("The settings page factory did not return a FrameworkElement.");
        }
        catch (Exception ex)
        {
            Log.Error($"Settings page for {entry.Id} failed to load", ex);
            return;
        }
        LeaveModulePage(); // never two subscriptions
        _modulePage = entry;
        PageHost.Content = new ModulePageHost(entry.Descriptor.Name, view, BackToFeatures);
        Scroller.ScrollToTop();
        _shell.Registry.Changed += OnRegistryChangedWhileModulePage;
    }

    /// <summary>Forgets the module page and its registry handler. Every way off a module page goes through here.</summary>
    private void LeaveModulePage()
    {
        _shell.Registry.Changed -= OnRegistryChangedWhileModulePage;
        _modulePage = null;
    }

    // A module that stops (uninstalled from the flyout, say) takes its page with it.
    private void OnRegistryChangedWhileModulePage(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_modulePage is { } entry && entry.SettingsPage is null)
            BackToFeatures();
    });

    private void BackToFeatures()
    {
        LeaveModulePage();
        Navigate("features");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape)
            return;
        e.Handled = true;
        if (_modulePage is not null)
            BackToFeatures();
        else
            Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        LeaveModulePage();
        PageHost.Content = null; // pages unhook their registry events on Unloaded
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
