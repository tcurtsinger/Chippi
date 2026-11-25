// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Chippi.Features.AppSwitcher.Windows;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// An app's windows over its dock icon, as the App switcher shows them: Windows' own live previews (DWM thumbnails, never
/// captured), each with its title. A click brings that window to the front.
/// </summary>
internal sealed class DockPreview : Window
{
    private const double SlotWidth = 200, SlotHeight = 124, TitleHeight = 24, Pad = 10, ShadowRoom = 24;

    private readonly DockApp _app;
    private readonly Point _anchor;
    private readonly MacDesktop.DockEdge _edge;
    private readonly double _scale;
    private readonly Action<nint> _chosen;
    private readonly List<(WindowPreview? Preview, Border Slot)> _slots = [];
    private nint _hwnd;

    public DockPreview(DockApp app, Point anchor, MacDesktop.DockEdge edge, double scale, Action<nint> chosen)
    {
        _app = app;
        _anchor = anchor;
        _edge = edge;
        _scale = scale;
        _chosen = chosen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var window in app.Windows.Take(6))
        {
            var title = new TextBlock { Text = window.Title.Length > 0 ? window.Title : app.Name, ToolTip = window.Title.Length > 0 ? window.Title : null, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 6, 4, 0), Height = TitleHeight - 6 };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var slot = new Border
            {
                Width = SlotWidth, Height = SlotHeight + TitleHeight, Padding = new Thickness(4), CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Child = new DockPanel { LastChildFill = true, Children = { title } },
            };
            DockPanel.SetDock(title, System.Windows.Controls.Dock.Bottom);
            slot.SetResourceReference(Border.TagProperty, "SurfaceSelectedBrush");
            slot.MouseEnter += (_, _) => slot.Background = (Brush)slot.Tag;
            slot.MouseLeave += (_, _) => slot.Background = Brushes.Transparent;
            nint handle = window.Handle;
            slot.MouseLeftButtonUp += (_, _) => _chosen(handle);
            row.Children.Add(slot);
            _slots.Add((null, slot));
        }
        var card = new Border
        {
            Margin = new Thickness(ShadowRoom), Padding = new Thickness(Pad), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.3 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        Content = card;
        SizeToContent = SizeToContent.WidthAndHeight;
        SourceInitialized += (_, _) => _hwnd = Native.MakeNoActivate(this);
        MouseLeave += (_, _) => Dispatcher.BeginInvoke(() => { if (!IsMouseOver) Close(); });
        ContentRendered += (_, _) => PlaceAndDraw();
        Closed += (_, _) => { foreach (var (preview, _) in _slots) preview?.Dispose(); };
    }

    /// <summary>Hangs the card over the icon, inside the display, then has Windows draw each window into its slot.</summary>
    private void PlaceAndDraw()
    {
        if (_hwnd == 0)
            return;
        double width = ActualWidth * _scale, height = ActualHeight * _scale;
        var monitor = Native.Area(Native.MonitorFromPoint(new Native.Point { X = (int)_anchor.X, Y = (int)_anchor.Y }, Native.MonitorDefaultToNearest));
        var bounds = _edge switch
        {
            MacDesktop.DockEdge.Left => new Rect(_anchor.X, _anchor.Y - height / 2, width, height),
            MacDesktop.DockEdge.Right => new Rect(_anchor.X - width, _anchor.Y - height / 2, width, height),
            _ => new Rect(_anchor.X - width / 2, _anchor.Y - height + ShadowRoom * _scale - 6 * _scale, width, height),
        };
        if (monitor is { } m)
            bounds.X = Math.Clamp(bounds.X, m.Monitor.Left, Math.Max(m.Monitor.Left, m.Monitor.Right - width));
        Native.Place(_hwnd, bounds);
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i].Slot;
            var preview = WindowPreview.Create(_hwnd, _app.Windows[i].Handle);
            _slots[i] = (preview, slot);
            if (preview is null)
                continue;
            var topLeft = slot.TranslatePoint(new Point(4, 4), this);
            var (sourceWidth, sourceHeight) = preview.SourceSize;
            double room = (SlotWidth - 8) * _scale, roomHeight = (SlotHeight - 8) * _scale;
            double fit = sourceWidth > 0 && sourceHeight > 0 ? Math.Min(room / sourceWidth, roomHeight / sourceHeight) : 1;
            double w = sourceWidth * fit, h = sourceHeight * fit;
            double left = topLeft.X * _scale + (room - w) / 2, top = topLeft.Y * _scale + (roomHeight - h) / 2;
            preview.Place((int)left, (int)top, (int)(left + w), (int)(top + h));
        }
    }
}
