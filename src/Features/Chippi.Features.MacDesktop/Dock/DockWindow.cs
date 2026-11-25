// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Chippi.Core.Input;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// The dock's window on the main display: a glass shelf of 48-DIP icons, 16 corners, a dot under each running app. Icons
/// near the pointer grow (DockLayout.Magnified). An AppBar while it's always shown, so maximized windows stop above it.
/// Never takes the keyboard. Its clear margin around the shelf passes clicks through (a layered window's clear pixels do).
/// </summary>
internal sealed class DockWindow : Window
{
    /// <summary>The window's depth from its edge: the shelf, its gap, the magnified icons' growth and the name above them.</summary>
    private const double Depth = DockLayout.Reserved + 34 + 40;

    private readonly DockController _dock;
    private readonly Grid _root = new();
    private readonly Border _shelf = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _label = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 4) };
    private readonly TextBlock _labelText = new() { FontSize = 12 };
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _watch;
    private readonly List<DockTile> _tiles = [];
    private AppBar? _appBar;
    private nint _hwnd;
    private Rect _monitor;
    private double _scale = 1;
    private bool _shown = true;
    private bool _fullscreen;
    private DateTime _leftAt = DateTime.MaxValue;
    private int _tick;
    private DockTile? _pressed;
    private Point _pressAt;
    private bool _dragging;
    private DockPreview? _preview;
    private DockTile? _previewTile;
    private DispatcherTimer? _previewDelay;
    private ContextMenu? _menu;

    public DockWindow(DockController dock)
    {
        _dock = dock;
        Title = "Chippi dock";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = _root;

        _shelf.CornerRadius = new CornerRadius(DockLayout.Corner);
        _shelf.BorderThickness = new Thickness(1);
        _shelf.Child = _row;
        _shelf.RenderTransform = _slide;
        _shelf.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = 0.3, Color = Colors.Black };
        _shelf.AllowDrop = true;
        _shelf.Drop += OnShelfDrop;
        _shelf.DragOver += (_, e) => { e.Effects = Files(e) is { } f && f.All(IsProgram) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        _root.Children.Add(_shelf);
        _label.Child = _labelText;
        _overlay.Children.Add(_label);
        _root.Children.Add(_overlay);
        ApplyTheme();
        ThemeManager.ThemeChanged += OnThemeChanged;

        _root.MouseMove += (_, e) => Magnify(e.GetPosition(_row));
        _root.MouseLeave += (_, _) => { Magnify(null); _leftAt = DateTime.UtcNow; HideLabel(); };
        _root.MouseEnter += (_, _) => _leftAt = DateTime.MaxValue;

        _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Watch(), Dispatcher);
        SourceInitialized += (_, _) =>
        {
            _hwnd = Native.MakeNoActivate(this, Hook);
            Place();
            _watch.Start();
        };
        Closed += (_, _) =>
        {
            _watch.Stop();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _appBar?.Dispose();
            ClosePreview();
        };
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private DockEdge Edge => _dock.Settings.DockPosition;

    private bool Horizontal => Edge == DockEdge.Bottom;

    /// <summary>
    /// The glass in the app theme. No blur behind a layered window in WPF, so the fill is a little denser than the mockup's to
    /// read the same.
    /// </summary>
    private void ApplyTheme()
    {
        bool light = ThemeManager.IsLight;
        _shelf.Background = Frozen(light ? Color.FromArgb(0xC4, 0xF6, 0xF6, 0xF8) : Color.FromArgb(0xC4, 0x28, 0x28, 0x2E));
        _shelf.BorderBrush = Frozen(light ? Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF));
        _label.Background = Frozen(light ? Color.FromArgb(0xF0, 0xF6, 0xF6, 0xF8) : Color.FromArgb(0xF0, 0x28, 0x28, 0x2C));
        _labelText.Foreground = Frozen(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        var dot = Frozen(light ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xE6, 0xE6, 0xE6));
        foreach (var tile in _tiles)
            tile.Dot.Fill = dot;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == AppBar.CallbackMessage && (uint)wParam == Native.AbnPosChanged)
            Dispatcher.BeginInvoke(() => { _appBar?.Invalidate(); Place(); });
        return 0;
    }

    public void SettingsChanged()
    {
        Place();
        ApplyTheme();
    }

    /// <summary>Puts the window along the dock's edge of the main display and reserves its strip while it's always shown.</summary>
    public void Place()
    {
        if (_hwnd == 0 || Native.Area(DockController.MainDisplay) is not { } area)
            return;
        _monitor = area.Monitor;
        _scale = area.Scale;
        double depth = Math.Round(Depth * _scale);
        var bounds = Edge switch
        {
            DockEdge.Left => new Rect(_monitor.Left, area.Work.Top, depth, area.Work.Height),
            DockEdge.Right => new Rect(_monitor.Right - depth, area.Work.Top, depth, area.Work.Height),
            _ => new Rect(_monitor.Left, _monitor.Bottom - depth, _monitor.Width, depth),
        };
        bool reserve = _dock.Settings.DockHide == DockHiding.AlwaysShown;
        if (reserve)
        {
            _appBar ??= AppBar.Register(_hwnd);
            _appBar?.Reserve(Edge switch { DockEdge.Left => ScreenEdge.Left, DockEdge.Right => ScreenEdge.Right, _ => ScreenEdge.Bottom },
                _monitor, Math.Round(DockLayout.Reserved * _scale));
        }
        else
        {
            _appBar?.Dispose();
            _appBar = null;
        }
        Native.Place(_hwnd, bounds);
        _root.Width = bounds.Width / _scale;
        _root.Height = bounds.Height / _scale;
        _row.Orientation = Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        _shelf.HorizontalAlignment = Edge switch { DockEdge.Left => HorizontalAlignment.Left, DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        _shelf.VerticalAlignment = Horizontal ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        _shelf.Margin = Edge switch
        {
            DockEdge.Left => new Thickness(DockLayout.EdgeGap, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, DockLayout.EdgeGap, 0),
            _ => new Thickness(0, 0, 0, DockLayout.EdgeGap),
        };
        _shelf.Padding = Horizontal ? new Thickness(DockLayout.Padding, 0, DockLayout.Padding, 0) : new Thickness(0, DockLayout.Padding, 0, DockLayout.Padding);
        if (Horizontal) { _shelf.Height = DockLayout.Shelf; _shelf.Width = double.NaN; }
        else { _shelf.Width = DockLayout.Shelf; _shelf.Height = double.NaN; }
        foreach (var tile in _tiles)
            tile.Orient(Edge);
        if (!reserve && _shown && !PointerNear())
            SetShown(false, animate: false);
        else if (reserve && !_shown)
            SetShown(true, animate: false);
    }

    // ----- Tiles -----

    public void SetApps(IReadOnlyList<DockApp> apps)
    {
        var wanted = new List<(string Key, Func<DockTile> Make, Action<DockTile> Update)>
        {
            ("start", () => DockTile.Start(this), _ => { }),
        };
        foreach (var app in apps)
            wanted.Add((app.Key, () => DockTile.ForApp(this, app), tile => tile.Update(app)));
        wanted.Add(("|", () => DockTile.Divider(), _ => { }));
        wanted.Add(("downloads", () => DockTile.Folder(this, "downloads", "Downloads", DockController.Downloads), _ => { }));
        wanted.Add(("bin", () => DockTile.Folder(this, "bin", "Recycle Bin", DockController.RecycleBin), _ => { }));

        var existing = _tiles.ToDictionary(t => t.Key);
        var next = new List<DockTile>();
        foreach (var (key, make, update) in wanted)
        {
            if (existing.TryGetValue(key, out var tile))
                update(tile);
            else
                tile = make();
            tile.Orient(Edge);
            next.Add(tile);
        }
        if (_dragging)
            return; // the drag ends with its own refresh
        _tiles.Clear();
        _tiles.AddRange(next);
        _row.Children.Clear();
        foreach (var tile in _tiles)
            _row.Children.Add(tile);
        ApplyTheme();
    }

    // ----- Magnification -----

    private void Magnify(Point? pointer)
    {
        if (_dragging)
            return;
        bool on = _dock.Settings.Magnify && pointer is { } p && OverShelf(p);
        double along = pointer is { } q ? (Horizontal ? q.X : q.Y) : 0;
        double offset = 0;
        foreach (var tile in _tiles)
        {
            double extent = tile.IsDivider ? 9 : DockLayout.Icon;
            double center = offset + extent / 2;
            offset += extent + DockLayout.Gap;
            if (tile.IsDivider)
                continue;
            tile.SetSize(on ? DockLayout.Magnified((along - RestToNow(center)) / (DockLayout.Icon + DockLayout.Gap), _dock.Settings.MagnifiedSize) : DockLayout.Icon);
        }
    }

    // Icons are measured against where they sit at rest, so the row doesn't chase the pointer: rest centers are offsets from
    // the row's start at rest, and the row's start moves as the shelf grows about its middle.
    private double RestToNow(double restCenter)
    {
        double rest = 0, now = 0;
        foreach (var tile in _tiles)
        {
            double extent = tile.IsDivider ? 9 : DockLayout.Icon;
            rest += extent + DockLayout.Gap;
            now += (tile.IsDivider ? 9 : tile.Size) + DockLayout.Gap;
        }
        return restCenter + (now - rest) / 2;
    }

    private bool OverShelf(Point inRow)
    {
        var size = _row.RenderSize;
        double reach = 28;
        return Horizontal
            ? inRow.X >= -reach && inRow.X <= size.Width + reach && inRow.Y >= -40 && inRow.Y <= size.Height + DockLayout.EdgeGap + 2
            : inRow.Y >= -reach && inRow.Y <= size.Height + reach;
    }

    // ----- The name above an icon -----

    internal void ShowLabel(DockTile tile, string name)
    {
        _labelText.Text = name;
        _label.Visibility = Visibility.Visible;
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var at = tile.TranslatePoint(new Point(tile.ActualWidth / 2, 0), _overlay);
        var size = _label.DesiredSize;
        switch (Edge)
        {
            case DockEdge.Left:
                at = tile.TranslatePoint(new Point(tile.ActualWidth, tile.ActualHeight / 2), _overlay);
                Canvas.SetLeft(_label, at.X + 10);
                Canvas.SetTop(_label, at.Y - size.Height / 2);
                break;
            case DockEdge.Right:
                at = tile.TranslatePoint(new Point(0, tile.ActualHeight / 2), _overlay);
                Canvas.SetLeft(_label, at.X - size.Width - 10);
                Canvas.SetTop(_label, at.Y - size.Height / 2);
                break;
            default:
                Canvas.SetLeft(_label, at.X - size.Width / 2);
                Canvas.SetTop(_label, at.Y - size.Height - 10);
                break;
        }
    }

    internal void HideLabel() => _label.Visibility = Visibility.Collapsed;

    // ----- Clicks, menus and drags -----

    internal void Pressed(DockTile tile, MouseButtonEventArgs e)
    {
        _pressed = tile;
        _pressAt = e.GetPosition(_root);
        tile.CaptureMouse();
        ClosePreview();
    }

    internal void Moved(DockTile tile, MouseEventArgs e)
    {
        if (_pressed != tile || e.LeftButton != MouseButtonState.Pressed)
            return;
        var at = e.GetPosition(_root);
        if (!_dragging && (at - _pressAt).Length < 4)
            return;
        if (tile.App is null)
            return; // Start, Downloads and the Recycle Bin stay put
        _dragging = true;
        HideLabel();
        var delta = at - _pressAt;
        tile.Lift(Horizontal ? new Vector(delta.X, Math.Min(0, delta.Y)) : new Vector(Edge == DockEdge.Left ? Math.Max(0, delta.X) : Math.Min(0, delta.X), delta.Y), Away(at));
    }

    /// <summary>Dragged this far from the shelf, a pin comes off it when let go.</summary>
    private bool Away(Point at)
    {
        var shelf = new Rect(_shelf.TranslatePoint(new Point(), _root), _shelf.RenderSize);
        double distance = Edge switch
        {
            DockEdge.Left => at.X - shelf.Right,
            DockEdge.Right => shelf.Left - at.X,
            _ => shelf.Top - at.Y,
        };
        return distance > 80;
    }

    internal void Released(DockTile tile, MouseButtonEventArgs e)
    {
        if (_pressed != tile)
            return;
        _pressed = null;
        tile.ReleaseMouseCapture();
        if (!_dragging)
        {
            tile.Click();
            return;
        }
        _dragging = false;
        var at = e.GetPosition(_root);
        tile.Lift(default, false);
        if (tile.App is { } app)
        {
            if (Away(at))
            {
                if (app.Pinned)
                    _dock.Unpin(app.Launch);
            }
            else
                _dock.Pin(app.Launch, PinIndexAt(at));
        }
        _dock.SettingsChanged();
    }

    /// <summary>Where among the pins a tile dropped at <paramref name="at"/> goes.</summary>
    private int PinIndexAt(Point at)
    {
        int index = 0;
        foreach (var tile in _tiles.Where(t => t.App is { Pinned: true }))
        {
            var center = tile.TranslatePoint(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2), _root);
            if (Horizontal ? at.X > center.X : at.Y > center.Y)
                index++;
        }
        return index;
    }

    internal void OpenMenu(DockTile tile, IReadOnlyList<BarMenuItem> items)
    {
        ClosePreview();
        HideLabel();
        if (_menu is { IsOpen: true })
            _menu.IsOpen = false;
        var placement = Edge switch { DockEdge.Left => PlacementMode.Right, DockEdge.Right => PlacementMode.Left, _ => PlacementMode.Top };
        _menu = BarMenu.Open(tile, items, placement, horizontalOffset: Horizontal ? -12 : 0, verticalOffset: Horizontal ? 0 : -16);
    }

    internal DockController Controller => _dock;

    // ----- Previews of an app's windows -----

    internal void Hovered(DockTile tile, bool over)
    {
        _previewDelay?.Stop();
        if (!over)
            return;
        if (tile.App is not { Windows.Count: > 1 } || _dragging || _menu is { IsOpen: true })
            return;
        _previewDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Normal, (_, _) =>
        {
            _previewDelay?.Stop();
            if (tile.IsMouseOver && tile.App is { } app)
                OpenPreview(tile, app);
        }, Dispatcher);
        _previewDelay.Start();
    }

    private void OpenPreview(DockTile tile, DockApp app)
    {
        ClosePreview();
        HideLabel();
        var anchor = tile.PointToScreen(new Point(tile.ActualWidth / 2, 0));
        if (Edge != DockEdge.Bottom)
            anchor = tile.PointToScreen(new Point(Edge == DockEdge.Left ? tile.ActualWidth : 0, tile.ActualHeight / 2));
        _preview = new DockPreview(app, anchor, Edge, _scale, window => { ClosePreview(); _dock.Activate(window); });
        _previewTile = tile;
        _preview.Show();
    }

    private void ClosePreview()
    {
        _preview?.Close();
        _preview = null;
        _previewTile = null;
    }

    // ----- Drops from Explorer -----

    internal static IReadOnlyList<string>? Files(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files ? files : null;

    private static bool IsProgram(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Programs and shortcuts dropped between the icons are pinned there.</summary>
    private void OnShelfDrop(object sender, DragEventArgs e)
    {
        if (e.Handled || Files(e) is not { } files || !files.All(IsProgram))
            return;
        e.Handled = true;
        int at = PinIndexAt(e.GetPosition(_root));
        foreach (var file in files)
            _dock.Pin(file, at++);
    }

    // ----- Hiding -----

    /// <summary>
    /// Every 100 ms: auto-hide shows the dock when the pointer reaches its edge and hides it 600 ms after it leaves; "When a
    /// window touches it" hides it while the window in front overlaps it; a fullscreen app on the main display hides it.
    /// </summary>
    private void Watch()
    {
        if (_hwnd == 0)
            return;
        if (++_tick % 5 == 0)
        {
            nint front = Native.GetForegroundWindow();
            bool fullscreen = FullscreenCheck.IsFullscreen(front) && Native.MonitorFromWindow(front, Native.MonitorDefaultToNearest) == DockController.MainDisplay;
            if (fullscreen != _fullscreen)
            {
                _fullscreen = fullscreen;
                Visibility = fullscreen ? Visibility.Hidden : Visibility.Visible;
            }
        }
        var hide = _dock.Settings.DockHide;
        if (hide == DockHiding.AlwaysShown || _fullscreen)
            return;
        bool busy = _dragging || _menu is { IsOpen: true } || _preview is not null || IsMouseOver;
        if (!_shown)
        {
            if (PointerAtEdge())
                SetShown(true, animate: true);
            return;
        }
        if (busy)
            return;
        bool leave = hide == DockHiding.AutoHide
            ? DateTime.UtcNow - _leftAt > TimeSpan.FromMilliseconds(600) && !PointerNear()
            : FrontTouches() && !PointerNear();
        if (leave)
            SetShown(false, animate: true);
    }

    private bool PointerAtEdge()
    {
        var p = Native.Cursor();
        if (!_monitor.Contains(new Point(Math.Clamp(p.X, _monitor.Left, _monitor.Right - 1), Math.Clamp(p.Y, _monitor.Top, _monitor.Bottom - 1))) || !_monitor.Contains(p))
            return false;
        double reach = 2 * _scale;
        return Edge switch
        {
            DockEdge.Left => p.X <= _monitor.Left + reach,
            DockEdge.Right => p.X >= _monitor.Right - 1 - reach,
            _ => p.Y >= _monitor.Bottom - 1 - reach,
        };
    }

    private bool PointerNear()
    {
        if (_hwnd == 0 || PresentationSource.FromVisual(_shelf) is null)
            return false;
        var p = _shelf.PointFromScreen(Native.Cursor());
        return new Rect(-12, -40, _shelf.ActualWidth + 24, _shelf.ActualHeight + 60).Contains(p);
    }

    /// <summary>Whether the window in front overlaps the shelf's place.</summary>
    private bool FrontTouches()
    {
        nint front = Native.GetForegroundWindow();
        if (front == 0 || front == _hwnd || Native.IsIconic(front) || !Native.GetWindowRect(front, out var rect) || PresentationSource.FromVisual(_shelf) is null)
            return false;
        if (Native.ClassOf(front) is "Progman" or "WorkerW")
            return false;
        var topLeft = _shelf.PointToScreen(new Point(-_slide.X, -_slide.Y));
        var shelf = new Rect(topLeft, new Size(_shelf.ActualWidth * _scale, _shelf.ActualHeight * _scale));
        return shelf.IntersectsWith(rect.ToWpf());
    }

    private void SetShown(bool shown, bool animate)
    {
        _shown = shown;
        double away = DockLayout.Reserved + 16;
        var (x, y) = shown ? (0.0, 0.0) : Edge switch { DockEdge.Left => (-away, 0.0), DockEdge.Right => (away, 0.0), _ => (0.0, away) };
        var duration = TimeSpan.FromMilliseconds(animate && !Motion.IsReduced ? 200 : 0);
        var ease = new CubicEase { EasingMode = shown ? EasingMode.EaseOut : EasingMode.EaseIn };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = ease });
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = ease });
        if (!shown)
        {
            HideLabel();
            ClosePreview();
        }
    }

    internal void PreviewLeft()
    {
        // The pointer left both the icon and the preview: it closes.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_preview is { IsMouseOver: false } && _previewTile is { IsMouseOver: false })
                ClosePreview();
        });
    }

    internal bool HasPreview(DockTile tile) => _previewTile == tile;
}
