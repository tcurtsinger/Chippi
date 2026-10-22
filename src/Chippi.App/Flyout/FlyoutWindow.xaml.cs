// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
/// The tray flyout (DESIGN.md §5). Built once and hidden between uses. Shows the tabs installed
/// modules contribute to (plus every placeholder tab in debug mode), the Tools grid of
/// <see cref="ToolTileModel"/>s, and re-renders when the registry changes. It hides on Esc and
/// when it loses activation; <see cref="Dismissed"/> tells the tray controller which.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double CardMaxHeight = 612;

    /// <summary>Room for the window lift (0 10px 26px) around the card, before trimming at the taskbar.</summary>
    private static readonly Thickness ShadowMargin = new(28, 20, 28, 40);

    private readonly ShellContext _shell;
    private readonly FlyoutPages _pages;
    private Func<TrayDisplay>? _display;
    private TrayDisplay _lastDisplay;
    private bool _rebuildQueued;
    private bool _dirty = true;
    private bool _positioning;
    private IReadOnlyList<FlyoutTab> _tabs = [];

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
            ForceForeground(Handle);
        FocusSelectedTab();
    }

    /// <summary>
    /// A tray click lets us take the foreground, but a second launch signals this process from
    /// outside, and Windows then refuses SetForegroundWindow (the flyout would show without focus,
    /// so Esc and click-away could not close it). Joining the foreground thread's input queue for
    /// the call is the usual way through; it is undone straight after.
    /// </summary>
    internal static void ForceForeground(HWND window)
    {
        var foreground = PInvoke.GetForegroundWindow();
        uint theirs = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground, out _);
        uint ours = PInvoke.GetCurrentThreadId();
        bool attached = theirs != 0 && theirs != ours && PInvoke.AttachThreadInput(theirs, ours, true);
        try
        {
            PInvoke.BringWindowToTop(window);
            PInvoke.SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
                PInvoke.AttachThreadInput(theirs, ours, false);
        }
        if (PInvoke.GetForegroundWindow() != window)
            Log.Warn("Flyout: Windows refused the foreground; Esc and click-away need a click on the window first");
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

        var contributions = running.SelectMany(f => f.FlyoutTabs).ToList();
        bool anyTiles = running.Any(f => f.ToolTiles.Count > 0);
        var tabs = Enum.GetValues<FlyoutTab>()
            .Where(t => _shell.DebugMode || contributions.Any(c => c.Tab == t) || (t == FlyoutTab.Tools && anyTiles))
            .ToList();

        var selected = (Tabs.SelectedItem as SegmentItem)?.Value as FlyoutTab?;
        if (!tabs.SequenceEqual(_tabs))
        {
            _tabs = tabs;
            Tabs.SelectionChanged -= OnTabChanged;
            Tabs.ItemsSource = tabs.Select(t => new SegmentItem { Label = FlyoutPages.TabName(t), Icon = FlyoutPages.TabIcon(t), Value = t }).ToList();
            Tabs.SelectionChanged += OnTabChanged;
            // Keep the user's tab; otherwise open on the first one with real content, so a debug
            // run does not land on a placeholder.
            int index = selected is FlyoutTab keep ? tabs.IndexOf(keep) : -1;
            if (index < 0)
                index = tabs.FindIndex(t => contributions.Any(c => c.Tab == t) || (t == FlyoutTab.Tools && anyTiles));
            Tabs.SelectedIndex = tabs.Count == 0 ? -1 : Math.Max(index, 0);
        }

        Tabs.Visibility = tabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var tab = (Tabs.SelectedItem as SegmentItem)?.Value as FlyoutTab?;
        Page.Content = tab is FlyoutTab t ? _pages.Build(t, running) : _pages.BuildNothingInstalled();
    }

    private void UpdateStatusLine(int on)
    {
        // State first (DESIGN.md §3 copy rules); the count is a measurement, so it is mono.
        StatusLine.Inlines.Clear();
        if (on == 0)
        {
            StatusLine.Inlines.Add(new Run("No features on"));
            return;
        }
        var count = new Run(on.ToString(System.Globalization.CultureInfo.CurrentCulture))
        {
            FontWeight = (FontWeight)FindResource("MonoSemiBold"),
        };
        count.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
        StatusLine.Inlines.Add(count);
        StatusLine.Inlines.Add(new Run(on == 1 ? " feature on" : " features on"));
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;
        Rebuild();
        Scroller.ScrollToTop();
    }

    /// <summary>Selects a tab if it is showing (the debug snapshot uses it).</summary>
    internal void SelectTab(FlyoutTab tab)
    {
        int index = _tabs.ToList().IndexOf(tab);
        if (index >= 0)
            Tabs.SelectedIndex = index;
    }

    private void FocusSelectedTab()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Tabs.Visibility == Visibility.Visible && Tabs.ItemContainerGenerator.ContainerFromIndex(Math.Max(Tabs.SelectedIndex, 0)) is ListBoxItem item)
                item.Focus();
            else
                HeaderSettings.Focus();
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
