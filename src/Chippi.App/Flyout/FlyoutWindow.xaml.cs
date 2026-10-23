// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Chippi.App.Shell;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Flyout;

/// <summary>
/// The tray flyout (DESIGN.md §5). Built once and hidden between uses. Shows a tab for each panel
/// running modules contribute (<see cref="FlyoutPanelContribution"/>) and re-renders when the
/// registry changes. It hides on Esc and when it loses activation; <see cref="Dismissed"/> tells
/// the tray controller which.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double CardMaxHeight = 640;

    /// <summary>Tabs that fit at 380 wide; with more panels the last one becomes More.</summary>
    private const int MaxTabs = 8;

    /// <summary>Room for the window lift (0 10px 26px) around the card, before trimming at the taskbar.</summary>
    private static readonly Thickness ShadowMargin = new(28, 20, 28, 40);

    private readonly ShellContext _shell;
    private readonly FlyoutPages _pages;
    private Func<TrayDisplay>? _display;
    private TrayDisplay _lastDisplay;
    private bool _rebuildQueued;
    private bool _dirty = true;
    private bool _positioning;
    private IReadOnlyList<FlyoutPanelContribution> _panels = [];
    private string? _lastTab;

    internal FlyoutWindow(ShellContext shell)
    {
        _shell = shell;
        _pages = new FlyoutPages(this);
        InitializeComponent();

        Deactivated += (_, _) => Dismiss(byDeactivation: true);
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Render);
        shell.Registry.Changed += (_, _) => QueueRebuild();
    }

    /// <summary>The flyout hid itself. True when it lost activation (a click elsewhere, maybe on the tray icon).</summary>
    internal event EventHandler<bool>? Dismissed;

    private HWND Handle => new(new WindowInteropHelper(this).Handle);

    /// <summary>
    /// Shows the flyout anchored to the tray, or re-activates it if it is already open.
    /// <paramref name="display"/> is re-evaluated whenever the card's size or DPI changes.
    /// </summary>
    internal void ShowAnchored(Func<TrayDisplay> display)
    {
        _display = display;
        if (_dirty)
            Rebuild();

        if (!IsVisible)
        {
            // Place the hidden window first, so it never flashes at its old spot or DPI.
            new WindowInteropHelper(this).EnsureHandle();
            Reposition();
            Show();
            Reposition();
        }

        Activate();
        if (PInvoke.GetForegroundWindow() != Handle)
            PInvoke.SetForegroundWindow(Handle);
        FocusSelectedTab();
    }

    internal void Dismiss(bool byDeactivation = false)
    {
        if (!IsVisible)
            return;
        Hide();
        Dismissed?.Invoke(this, byDeactivation);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    /// <summary>Card plus shadow margins, positioned so the card lands per <see cref="FlyoutPlacement"/>.</summary>
    private void Reposition()
    {
        if (_display is null || _positioning)
            return;
        _positioning = true;
        try
        {
            var display = _display();
            _lastDisplay = display;
            double maxCard = Math.Min(CardMaxHeight, FlyoutPlacement.MaxCardHeight(display));
            if (Math.Abs(Card.MaxHeight - maxCard) > 0.5)
            {
                Card.MaxHeight = Math.Max(200, maxCard);
                UpdateLayout();
            }

            // Measure with the same unbounded constraint SizeToContent uses, so the size is known
            // even before the first Show.
            Frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var card = Card.DesiredSize;

            var (x, y) = FlyoutPlacement.Place(display, card.Width, card.Height);
            var margin = FlyoutPlacement.ShadowMargin(ShadowMargin, display, x, y, card.Width, card.Height);
            if (Frame.Margin != margin)
                Frame.Margin = margin;
            double scale = display.Scale;
            int left = x - (int)Math.Round(margin.Left * scale);
            int top = y - (int)Math.Round(margin.Top * scale);
            PInvoke.SetWindowPos(Handle, HWND.Null, left, top, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Log.Error("Flyout: placing the window failed", ex);
        }
        finally
        {
            _positioning = false;
        }
    }

    internal string DescribePlacement()
    {
        PInvoke.GetWindowRect(Handle, out var r);
        return $"monitor {_lastDisplay.Monitor} work {_lastDisplay.Work} edge {_lastDisplay.Edge} inner {_lastDisplay.TaskbarInner} dpi {_lastDisplay.Dpi}; window ({r.left},{r.top})-({r.right},{r.bottom}); card {Card.ActualWidth}x{Card.ActualHeight} DIP";
    }

    private void QueueRebuild()
    {
        // Registry.Changed fires on any thread and in bursts (every tile a module adds).
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildQueued = false;
            if (IsVisible)
                Rebuild();
            else
                _dirty = true;
        }, DispatcherPriority.Background);
    }

    /// <summary>Re-reads the registry: status line, the visible tabs and the selected page.</summary>
    internal void Rebuild()
    {
        _dirty = false;
        var running = _shell.Registry.Features.Where(f => f.IsRunning).ToList();
        UpdateStatusLine(_shell.Registry.Features.Count(f => f.IsInstalled));

        // One tab per panel, in sidebar (registry) order. Past eight, the rest go under More.
        var panels = running.SelectMany(f => f.FlyoutPanels).ToList();
        if (!panels.SequenceEqual(_panels))
        {
            _panels = panels;
            var shown = panels.Count > MaxTabs ? panels.Take(MaxTabs - 1).ToList() : panels;
            var items = shown.Select(p => new SegmentItem { Label = p.Name, Icon = LucideIcons.Get(p.Icon), Value = p }).ToList();
            if (shown.Count < panels.Count)
                items.Add(new SegmentItem { Label = "More", Icon = LucideIcons.Ellipsis, Value = panels.Skip(shown.Count).ToList() });

            Tabs.SelectionChanged -= OnTabChanged;
            Tabs.ItemsSource = items;
            Tabs.SelectionChanged += OnTabChanged;
            // Reopen on the last tab used, across module restarts too.
            int index = items.FindIndex(i => i.Value is FlyoutPanelContribution p ? FlyoutPages.Key(p) == _lastTab
                : ((List<FlyoutPanelContribution>)i.Value!).Any(p => FlyoutPages.Key(p) == _lastTab));
            Tabs.SelectedIndex = items.Count == 0 ? -1 : Math.Max(index, 0);
        }

        TabStrip.Visibility = panels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Page.Content = (Tabs.SelectedItem as SegmentItem)?.Value switch
        {
            FlyoutPanelContribution panel => _pages.Build(panel, running),
            List<FlyoutPanelContribution> more when more.FirstOrDefault(p => FlyoutPages.Key(p) == _lastTab) is { } picked => _pages.Build(picked, running),
            List<FlyoutPanelContribution> more => _pages.BuildMore(more, OpenFromMore),
            _ => _pages.BuildNothingInstalled(),
        };
    }

    private void OpenFromMore(FlyoutPanelContribution panel)
    {
        _lastTab = FlyoutPages.Key(panel);
        Rebuild();
        Scroller.ScrollToTop();
    }

    private void UpdateStatusLine(int on)
    {
        // State first (DESIGN.md §3 copy rules). The window's tabular figures cover the count.
        StatusLine.Text = on switch
        {
            0 => "No features on",
            1 => "1 feature on",
            _ => on.ToString(System.Globalization.CultureInfo.CurrentCulture) + " features on",
        };
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;
        // More opens on its list; a panel picked there is remembered by OpenFromMore.
        _lastTab = (Tabs.SelectedItem as SegmentItem)?.Value is FlyoutPanelContribution panel ? FlyoutPages.Key(panel) : null;
        Rebuild();
        Scroller.ScrollToTop();
    }

    /// <summary>The showing tabs' names, in order (the debug snapshot uses them).</summary>
    internal IReadOnlyList<string> TabNames => Tabs.Items.OfType<SegmentItem>().Select(i => i.Label).ToList();

    /// <summary>Selects a tab by its panel name if it is showing.</summary>
    internal void SelectTab(string name)
    {
        int index = TabNames.ToList().IndexOf(name);
        if (index >= 0)
            Tabs.SelectedIndex = index;
    }

    private void FocusSelectedTab()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (TabStrip.Visibility == Visibility.Visible && Tabs.ItemContainerGenerator.ContainerFromIndex(Math.Max(Tabs.SelectedIndex, 0)) is ListBoxItem item)
                item.Focus();
            else
                FooterSettings.Focus();
        }, DispatcherPriority.Input);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings(null);

    internal void OpenSettings(string? page)
    {
        Dismiss();
        _shell.Commands.OpenSettings(page);
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Dismiss();
        _shell.Commands.Quit();
    }
}
