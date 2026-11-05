// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Chippi.Features.WindowLayouts.Views;

/// <summary>
/// The Shift+drag zones (wave-3.html, sheet-snap-zones): over the display under the pointer, each zone of its layout as a
/// block with its name; the one under the pointer takes the accent. The window never activates, lets every click through,
/// stays out of Alt+Tab and the taskbar, and is drawn in the display's own pixels so the zones line up with where the
/// window will land. <see cref="Point"/> only restyles blocks, so it's cheap on every mouse move.
/// </summary>
public sealed class ZoneOverlay : IZoneOverlay
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4;

    private readonly Canvas _canvas = new();
    private readonly Viewbox _scale;
    private readonly List<(Border Block, TextBlock Name)> _blocks = [];
    private Window? _window;
    private int _lit = -1;

    public ZoneOverlay() => _scale = new Viewbox { Stretch = Stretch.Fill, Child = _canvas };

    /// <summary>The blocks as drawn, for shots and tests: a panel the size of the display in its pixels.</summary>
    internal FrameworkElement Content => _scale;

    public void Show(SnapDisplay display, IReadOnlyList<SnapZone> zones)
    {
        Build(display, zones);
        var window = _window ??= Create();
        var helper = new WindowInteropHelper(window);
        if (helper.Handle == 0)
            helper.EnsureHandle();
        if (!window.IsVisible)
            window.Show();
        // In the display's own pixels: WPF's position is in DIPs of whichever display the window was last on.
        var b = display.Bounds;
        SetWindowPos(helper.Handle, 0, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE | SWP_NOZORDER);
    }

    /// <summary>Lays the blocks out in the display's pixels, inset by the window gap so neighbours read as separate.</summary>
    internal void Build(SnapDisplay display, IReadOnlyList<SnapZone> zones)
    {
        var b = display.Bounds;
        _canvas.Width = b.Width;
        _canvas.Height = b.Height;
        _canvas.Children.Clear();
        _blocks.Clear();
        _lit = -1;
        // Taken again each time, so a theme or accent changed while Chippi runs shows on the next drag.
        AddBrushes(_scale);
        double scale = Math.Max(1, b.Height / 1080.0);
        var dim = new Border { Width = b.Width, Height = b.Height, Opacity = 0.18 };
        dim.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        _canvas.Children.Add(dim);
        foreach (var zone in zones)
        {
            var r = SnapGeometry.Place(display.WorkArea, zone, (int)Math.Round(6 * scale));
            var name = new TextBlock { Text = zone.Name, FontSize = 18 * scale, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var block = new Border { Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(8 * scale), BorderThickness = new Thickness(2 * scale), Child = name };
            Canvas.SetLeft(block, r.X - b.X);
            Canvas.SetTop(block, r.Y - b.Y);
            _canvas.Children.Add(block);
            _blocks.Add((block, name));
            Style(block, name, false);
        }
    }

    public void Point(int index)
    {
        if (index == _lit) return;
        if (_lit >= 0 && _lit < _blocks.Count) Style(_blocks[_lit].Block, _blocks[_lit].Name, false);
        _lit = index;
        if (_lit >= 0 && _lit < _blocks.Count) Style(_blocks[_lit].Block, _blocks[_lit].Name, true);
    }

    public void Hide()
    {
        _lit = -1;
        _window?.Hide();
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }

    private static void Style(Border block, TextBlock name, bool lit)
    {
        block.SetResourceReference(Border.BackgroundProperty, lit ? "SnapZoneLitBrush" : "SnapZoneBrush");
        block.SetResourceReference(Border.BorderBrushProperty, lit ? "AccentBrush" : "SnapZoneBorderBrush");
        name.SetResourceReference(TextBlock.ForegroundProperty, lit ? "TextPrimaryBrush" : "TextSecondaryBrush");
    }

    /// <summary>The zone fills: the island's surface, see-through, and a tint of the accent for the lit one.</summary>
    internal static void AddBrushes(FrameworkElement owner)
    {
        Color Of(string key, Color fallback) => owner.TryFindResource(key) is SolidColorBrush s ? s.Color : fallback;
        var surface = Of("SurfaceBrush", Color.FromRgb(0x2B, 0x2B, 0x2B));
        var accent = Of("AccentBrush", Color.FromRgb(0xB7, 0x9C, 0xFF));
        var border = Of("BorderStrongBrush", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        owner.Resources["SnapZoneBrush"] = new SolidColorBrush(Color.FromArgb(0xCC, surface.R, surface.G, surface.B));
        owner.Resources["SnapZoneLitBrush"] = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B));
        owner.Resources["SnapZoneBorderBrush"] = new SolidColorBrush(border);
    }

    private Window Create()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            Title = "Snap zones",
            Content = _scale,
            Left = -32000,
            Top = -32000,
            Width = 1,
            Height = 1,
        };
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE);
        };
        return window;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
