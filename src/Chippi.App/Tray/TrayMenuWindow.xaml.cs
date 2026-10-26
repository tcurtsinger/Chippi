// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Chippi.App.Flyout;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Tray;

/// <summary>
/// The tray icon's themed right-click menu (Open settings, Quit). Opens at the click, kept off the
/// taskbar by the tray's 12px gap, and hides on Esc, on a choice or when it loses activation.
/// </summary>
public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place, System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>Open settings was chosen.</summary>
    internal event EventHandler? SettingsRequested;

    /// <summary>Quit was chosen.</summary>
    internal event EventHandler? QuitRequested;

    private HWND Handle => new(new WindowInteropHelper(this).Handle);

    internal void ShowAt(int x, int y, TrayDisplay display)
    {
        new WindowInteropHelper(this).EnsureHandle();
        _anchor = (x, y, display);
        Place();
        Show();
        Activate();
        if (PInvoke.GetForegroundWindow() != Handle)
            PInvoke.SetForegroundWindow(Handle);
        SettingsItem.Focus();
    }

    private static readonly Thickness ShadowMargin = new(28, 20, 28, 40);

    private (int X, int Y, TrayDisplay Display) _anchor;

    /// <summary>Re-run after a DPI change, which resizes the window around a different origin.</summary>
    private void Place()
    {
        var (x, y, display) = _anchor;
        Frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double scale = display.Scale;
        double w = Card.DesiredSize.Width * scale, h = Card.DesiredSize.Height * scale;
        double gap = FlyoutPlacement.TaskbarGap * scale;
        var work = display.Work;

        // Grow away from the taskbar, starting at the click.
        double left = x, top = y - h;
        switch (display.Edge)
        {
            case TaskbarEdge.Bottom:
                top = Math.Min(y - h, display.TaskbarInner - gap - h);
                break;
            case TaskbarEdge.Top:
                top = Math.Max(y, display.TaskbarInner + gap);
                break;
            case TaskbarEdge.Left:
                left = display.TaskbarInner + gap;
                break;
            case TaskbarEdge.Right:
                left = display.TaskbarInner - gap - w;
                break;
        }
        int cardX = (int)Math.Round(Math.Max(work.Left, Math.Min(left, work.Right - w)));
        int cardY = (int)Math.Round(Math.Max(work.Top, Math.Min(top, work.Bottom - h)));

        var margin = FlyoutPlacement.ShadowMargin(ShadowMargin, display, cardX, cardY, Card.DesiredSize.Width, Card.DesiredSize.Height);
        if (Frame.Margin != margin)
            Frame.Margin = margin;
        PInvoke.SetWindowPos(Handle, HWND.Null,
            cardX - (int)Math.Round(margin.Left * scale), cardY - (int)Math.Round(margin.Top * scale), 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Hide();
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }
}
