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
/// The dock's window on the main display: a glass shelf of 48-DIP icons, 16 corners, a dot under each running app, with
/// Windows' acrylic blurring what's behind it (DockBackdrop). Icons near the pointer grow (DockLayout.Magnified), easing in
/// and out. An AppBar while it's always shown, so maximized windows stop above it. Never takes the keyboard. Its clear
/// margin around the shelf passes clicks through (a layered window's clear pixels do).
/// </summary>
internal sealed class DockWindow : Window
{
    /// <summary>The window's depth from its edge: the shelf, its gap, the magnified icons' growth and the name above them.</summary>
    private const double Depth = DockLayout.Reserved + 34 + 40;

    private readonly DockController _dock;
    private readonly Grid _root = new();
    private readonly Border _shelf = new();
    private readonly Border _shadow = new() { IsHitTestVisible = false };
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _label = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 4), BorderThickness = new Thickness(1) };
    private readonly Border _status = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 4, 3), BorderThickness = new Thickness(1) };
    private readonly TextBlock _statusText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _statusUndo = new() { Content = "Undo", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 1, 8, 2), FontSize = 12 };
    private DispatcherTimer? _statusTimer;
    private Action? _undo;
    private DockTile? _labelTile;
    private DockBackdrop? _backdrop;
    private bool _framing;
    private TimeSpan _lastFrame;
    private DateTime _edgeSince = DateTime.MaxValue;
    private DateTime _slideUntil = DateTime.MinValue;
    private Popup? _confirm;
    private readonly TextBlock _labelText = new() { FontSize = 12, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _watch;
    private readonly List<DockTile> _tiles = [];
    private AppBar? _appBar;
    private nint _hwnd;
    private Rect _monitor;
    private double _scale = 1;

    /// <summary>Device pixels per DIP on the dock's monitor.</summary>
    internal double Scale => _scale;
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
        // The tint and shadow are their own layer behind the shelf, so the icons growing don't redraw through an effect.
        _shadow.CornerRadius = new CornerRadius(DockLayout.Corner);
        _shadow.RenderTransform = _slide;
        _shadow.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = _shelf });
        _shadow.SetBinding(HeightProperty, new System.Windows.Data.Binding(nameof(ActualHeight)) { Source = _shelf });
        _shelf.SizeChanged += (_, _) => SyncBackdrop();
        _root.Children.Add(_shadow);
        _shelf.AllowDrop = true;
        _shelf.Drop += OnShelfDrop;
        _shelf.DragOver += (_, e) => { e.Effects = Files(e) is { } f && f.All(IsProgram) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        _root.Children.Add(_shelf);
        _label.Child = _labelText;
        _label.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.28 };
        _overlay.Children.Add(_label);
        _root.Children.Add(_overlay);
        _statusUndo.SetResourceReference(StyleProperty, "SubtleButtonStyle");
        _statusUndo.Click += (_, _) => { var undo = _undo; HideStatus(); undo?.Invoke(); };
        _status.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _statusText, _statusUndo } };
        _status.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.28 };
        _status.MouseEnter += (_, _) => _statusTimer?.Stop();
        _status.MouseLeave += (_, _) => _statusTimer?.Start();
        _root.Children.Add(_status);
        ApplyTheme();
        ThemeManager.ThemeChanged += OnThemeChanged;

        _root.MouseMove += (_, e) => Magnify(e.GetPosition(_row));
        _root.MouseLeave += (_, _) => { if (!_dragging) Magnify(null); _leftAt = DateTime.UtcNow; HideLabel(); };
        _root.MouseEnter += (_, _) => _leftAt = DateTime.MaxValue;
        // Files dragged over the dock grow the icons under them too, as the pointer does.
        _root.AllowDrop = true;
        _root.PreviewDragOver += (_, e) => { _leftAt = DateTime.MaxValue; Magnify(e.GetPosition(_row)); };
        _root.DragLeave += (_, e) =>
        {
            // Leave bubbles up from each icon as the drag crosses it; only leaving the shelf puts them at rest.
            if (OverShelf(e.GetPosition(_row)))
                return;
            Magnify(null);
            HideLabel();
            _leftAt = DateTime.UtcNow;
        };
        _root.Drop += (_, _) => Magnify(null);

        _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Watch(), Dispatcher);
        SourceInitialized += (_, _) =>
        {
            _hwnd = Native.MakeNoActivate(this, Hook);
            _backdrop = DockBackdrop.Create(ThemeManager.IsLight);
            ApplyTheme();
            Place();
            _watch.Start();
        };
        IsVisibleChanged += (_, _) => SyncBackdrop();
        Closed += (_, _) =>
        {
            _watch.Stop();
            _statusTimer?.Stop();
            StopFrames();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _appBar?.Dispose();
            ClosePreview();
            if (_confirm is not null)
                _confirm.IsOpen = false;
            _backdrop?.Dispose();
            _backdrop = null;
        };
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private DockEdge Edge => _dock.Settings.DockPosition;

    private bool Horizontal => Edge == DockEdge.Bottom;

    /// <summary>
    /// The glass in the app theme: the mockup's tint over Windows' acrylic, or a denser tint to read the same where there's
    /// no acrylic (transparency effects off). The shadow is the mockup's, deeper in dark.
    /// </summary>
    private void ApplyTheme()
    {
        bool light = ThemeManager.IsLight;
        bool blurred = _backdrop is not null && _backdrop.SetTint(light);
        byte fill = blurred ? (byte)0x73 : (byte)0xC4;
        // The tint is drawn by the shadow layer under the shelf, which casts the shadow from it: a drop shadow takes the
        // caster's alpha, so its opacity is the mockup's divided by the tint's. The shelf draws only its edge and the icons.
        _shadow.Background = Frozen(light ? Color.FromArgb(fill, 0xF6, 0xF6, 0xF8) : Color.FromArgb(fill, 0x28, 0x28, 0x2E));
        _shelf.Background = Brushes.Transparent;
        _shelf.BorderBrush = Frozen(light ? Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF));
        _shadow.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = Math.Min(1, (light ? 0.18 : 0.45) / (fill / 255.0)), Color = Colors.Black };
        var tip = Frozen(light ? Color.FromArgb(0xF0, 0xF6, 0xF6, 0xF8) : Color.FromArgb(0xF0, 0x28, 0x28, 0x2C));
        var tipEdge = Frozen(light ? Color.FromArgb(0x14, 0x00, 0x00, 0x00) : Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        var ink = Frozen(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        _label.Background = _status.Background = tip;
        _label.BorderBrush = _status.BorderBrush = tipEdge;
        _labelText.Foreground = _statusText.Foreground = ink;
        if (_label.Effect is DropShadowEffect labelShadow)
            labelShadow.Opacity = light ? 0.14 : 0.28;
        if (_status.Effect is DropShadowEffect statusShadow)
            statusShadow.Opacity = light ? 0.14 : 0.28;
        var dot = Frozen(light ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xE6, 0xE6, 0xE6));
        foreach (var tile in _tiles)
        {
            tile.Dot.Fill = dot;
            tile.SetLine(_shelf.BorderBrush);
        }
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
        _shelf.HorizontalAlignment = _shadow.HorizontalAlignment = Edge switch { DockEdge.Left => HorizontalAlignment.Left, DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        _shelf.VerticalAlignment = _shadow.VerticalAlignment = Horizontal ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        _shelf.Margin = _shadow.Margin = Edge switch
        {
            DockEdge.Left => new Thickness(DockLayout.EdgeGap, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, DockLayout.EdgeGap, 0),
            _ => new Thickness(0, 0, 0, DockLayout.EdgeGap),
        };
        _status.HorizontalAlignment = Horizontal ? HorizontalAlignment.Center : _shelf.HorizontalAlignment;
        _status.VerticalAlignment = Horizontal ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        _status.Margin = Edge switch
        {
            DockEdge.Left => new Thickness(DockLayout.Reserved + 16, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, DockLayout.Reserved + 16, 0),
            _ => new Thickness(0, 0, 0, DockLayout.Reserved + 34),
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
        SyncBackdrop();
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

    /// <summary>Sets each icon's size for the pointer (null: all at rest); the frames ease them there.</summary>
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
            tile.Target = on ? DockLayout.Magnified((along - RestToNow(center)) / (DockLayout.Icon + DockLayout.Gap), _dock.Settings.MagnifiedSize) : DockLayout.Icon;
        }
        if (Motion.IsReduced)
        {
            foreach (var tile in _tiles.Where(t => !t.IsDivider))
                tile.SetSize(tile.Target);
            _root.UpdateLayout();
            PlaceLabel();
            SyncBackdrop();
        }
        else
            StartFrames();
    }

    /// <summary>Puts every icon at rest at once (a drag starting).</summary>
    private void Rest()
    {
        foreach (var tile in _tiles.Where(t => !t.IsDivider))
        {
            tile.Target = DockLayout.Icon;
            tile.SetSize(DockLayout.Icon);
        }
    }

    // ----- Frames: icons easing to their size, the name following its icon, the blur following the shelf -----

    private void StartFrames()
    {
        if (_framing)
            return;
        _framing = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopFrames()
    {
        if (!_framing)
            return;
        _framing = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>
    /// Each frame: icons ease toward their size (about 60 ms to most of the way, so they keep up with the pointer and settle
    /// softly when it leaves), the name stays over its icon, and the blur follows the shelf while it slides.
    /// </summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        double dt = _lastFrame == TimeSpan.Zero ? 16 : Math.Clamp((now - _lastFrame).TotalMilliseconds, 1, 50);
        _lastFrame = now;
        double k = 1 - Math.Exp(-dt / 30);
        bool moving = false;
        foreach (var tile in _tiles)
        {
            if (tile.IsDivider || tile.Size == tile.Target)
                continue;
            double next = tile.Size + (tile.Target - tile.Size) * k;
            if (Math.Abs(tile.Target - next) < 0.25)
                next = tile.Target;
            else
                moving = true;
            tile.SetSize(next);
        }
        bool sliding = DateTime.UtcNow < _slideUntil;
        if (moving || sliding)
        {
            _root.UpdateLayout();
            PlaceLabel();
            SyncBackdrop();
        }
        if (!moving && !sliding)
        {
            StopFrames();
            _root.UpdateLayout();
            PlaceLabel();
            SyncBackdrop();
        }
    }

    /// <summary>Puts the blur under the shelf where it is now, inside its 1px edge; hidden while the dock is.</summary>
    private void SyncBackdrop()
    {
        if (_backdrop is null || _hwnd == 0)
            return;
        if (!IsVisible || _fullscreen || (!_shown && DateTime.UtcNow >= _slideUntil) || PresentationSource.FromVisual(_shelf) is null || _shelf.ActualWidth <= 0)
        {
            _backdrop.Hide();
            return;
        }
        var topLeft = _shelf.PointToScreen(new Point(1, 1));
        var bottomRight = _shelf.PointToScreen(new Point(_shelf.ActualWidth - 1, _shelf.ActualHeight - 1));
        _backdrop.Place(_hwnd, new Rect(topLeft, bottomRight), (DockLayout.Corner - 1) * _scale);
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
        _labelTile = tile;
        PlaceLabel();
    }

    /// <summary>Keeps the name 10 off the icon's grown edge, centered on it, as the icons ease.</summary>
    private void PlaceLabel()
    {
        if (_labelTile is not { } tile || _label.Visibility != Visibility.Visible || PresentationSource.FromVisual(tile) is null)
            return;
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _label.DesiredSize;
        var art = tile.Art;
        switch (Edge)
        {
            case DockEdge.Left:
            {
                var at = art.TranslatePoint(new Point(art.ActualWidth, art.ActualHeight / 2), _overlay);
                Canvas.SetLeft(_label, at.X + 10);
                Canvas.SetTop(_label, at.Y - size.Height / 2);
                break;
            }
            case DockEdge.Right:
            {
                var at = art.TranslatePoint(new Point(0, art.ActualHeight / 2), _overlay);
                Canvas.SetLeft(_label, at.X - size.Width - 10);
                Canvas.SetTop(_label, at.Y - size.Height / 2);
                break;
            }
            default:
            {
                var at = art.TranslatePoint(new Point(art.ActualWidth / 2, 0), _overlay);
                Canvas.SetLeft(_label, at.X - size.Width / 2);
                Canvas.SetTop(_label, at.Y - size.Height - 10);
                break;
            }
        }
    }

    internal void HideLabel()
    {
        _label.Visibility = Visibility.Collapsed;
        _labelTile = null;
    }

    // ----- Status: what a drop or action did, and Undo for a removed pin -----

    /// <summary>A one- or two-word status over the dock for 5 seconds, with Undo when <paramref name="undo"/> is given.</summary>
    internal void ShowStatus(string text, Action? undo = null)
    {
        _statusText.Text = text;
        _undo = undo;
        _statusUndo.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        _status.Padding = undo is null ? new Thickness(10, 3, 10, 4) : new Thickness(10, 3, 4, 3);
        _status.Visibility = Visibility.Visible;
        _statusTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) => HideStatus(), Dispatcher);
        _statusTimer.Stop();
        _statusTimer.Start();
        if (!_shown)
            SetShown(true, animate: true);
    }

    private void HideStatus()
    {
        _statusTimer?.Stop();
        _status.Visibility = Visibility.Collapsed;
        _undo = null;
    }

    private bool? _binFull;

    /// <summary>Redraws the Recycle Bin's icon when it fills or empties (read every 5 seconds and after the dock's own drops).</summary>
    internal void CheckBin()
    {
        var full = DockController.RecycleBinFull();
        if (full is null || full == _binFull)
            return;
        bool first = _binFull is null;
        _binFull = full;
        if (!first && _tiles.FirstOrDefault(t => t.IsBin) is { } bin)
            bin.ReloadIcon(DockController.RecycleBin);
    }

    // ----- Empty Recycle Bin: the question, Cancel and Empty -----

    internal void ConfirmEmptyRecycleBin()
    {
        if (_tiles.FirstOrDefault(t => t.IsBin) is not { } bin)
            return;
        if (_confirm is not null)
            _confirm.IsOpen = false;
        var cancel = new Button { Content = "Cancel" };
        cancel.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        var empty = new Button { Content = "Empty", Margin = new Thickness(8, 0, 0, 0) };
        empty.SetResourceReference(StyleProperty, "CriticalButtonStyle");
        var question = new TextBlock { Text = "Empty Recycle Bin?", FontSize = 13 };
        question.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var card = new Border
        {
            Width = 260, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Direction = 270, Opacity = ThemeManager.IsLight ? 0.14 : 0.28 },
            Child = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    question,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, empty } },
                },
            },
        };
        card.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        var popup = new Popup
        {
            PlacementTarget = bin,
            Placement = Edge switch { DockEdge.Left => PlacementMode.Right, DockEdge.Right => PlacementMode.Left, _ => PlacementMode.Top },
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Child = card,
        };
        cancel.Click += (_, _) => popup.IsOpen = false;
        empty.Click += (_, _) =>
        {
            popup.IsOpen = false;
            _dock.EmptyRecycleBin();
        };
        popup.Closed += (_, _) => { if (_confirm == popup) _confirm = null; };
        _confirm = popup;
        popup.IsOpen = true;
    }

    // ----- Clicks, menus and drags -----

    internal void Pressed(DockTile tile, MouseButtonEventArgs e)
    {
        _pressed = tile;
        _pressAt = e.GetPosition(_root);
        tile.CaptureMouse();
        tile.SetPressed(true);
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
        if (!_dragging)
        {
            // The icons settle at rest for the drag, so the gaps it opens are one slot wide; the press point follows.
            StopFrames();
            Rest();
            _root.UpdateLayout();
            _dragging = true;
            HideLabel();
            ClosePreview();
        }
        var delta = at - _pressAt;
        bool away = Away(at);
        tile.Lift(Horizontal ? new Vector(delta.X, Math.Min(0, delta.Y)) : new Vector(Edge == DockEdge.Left ? Math.Max(0, delta.X) : Math.Min(0, delta.X), delta.Y), away);
        OpenGap(tile, away ? null : Landing(tile, at));
        // Far enough off to come off the dock: it says so over the icon.
        if (away && tile.App is { Pinned: true })
            ShowLabel(tile, "Remove");
        else
            HideLabel();
    }

    /// <summary>
    /// Where a dragged app would land among the pins (an index for DockApps.Move), or null where it wouldn't: an app that
    /// isn't pinned is pinned only when let go among the pins, so a small nudge doesn't pin it.
    /// </summary>
    private int? Landing(DockTile dragged, Point at)
    {
        var pins = _tiles.Where(t => t.App is { Pinned: true }).ToList();
        var centers = pins.Select(t => Along(t.TranslatePoint(new Point(t.ActualWidth / 2, t.ActualHeight / 2), _root))).ToArray();
        double along = Along(at);
        if (dragged.App is { Pinned: false })
        {
            double end = pins.Count > 0
                ? Along(pins[^1].TranslatePoint(new Point(pins[^1].ActualWidth, pins[^1].ActualHeight), _root)) + DockLayout.Gap
                : Along(_tiles[0].TranslatePoint(new Point(_tiles[0].ActualWidth, _tiles[0].ActualHeight), _root)) + DockLayout.Icon / 2;
            if (along > end)
                return null;
        }
        return DockApps.InsertIndex(centers, pins.IndexOf(dragged), along);
    }

    private double Along(Point p) => Horizontal ? p.X : p.Y;

    /// <summary>Moves the tiles between the dragged one and where it would land by one slot, opening its place there.</summary>
    private void OpenGap(DockTile dragged, int? landing)
    {
        int from = _tiles.IndexOf(dragged);
        // Tiles' places once it lands: Start, then the pins with it at its index.
        int to = landing is { } index ? 1 + index : from;
        double slot = DockLayout.Icon + DockLayout.Gap;
        for (int i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            if (tile == dragged)
                continue;
            double move = to > from && i > from && i <= to ? -slot : to < from && i >= to && i < from ? slot : 0;
            tile.Shift(Horizontal ? new Vector(move, 0) : new Vector(0, move));
        }
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
        tile.SetPressed(false);
        if (!_dragging)
        {
            tile.Click();
            return;
        }
        var at = e.GetPosition(_root);
        int? landing = Away(at) ? null : Landing(tile, at);
        _dragging = false;
        tile.Lift(default, false);
        foreach (var other in _tiles)
            other.Unshift();
        if (tile.App is { } app)
        {
            if (Away(at))
            {
                if (app.Pinned)
                    _dock.Unpin(app.Launch, offerUndo: true);
            }
            else if (landing is { } index)
                _dock.Pin(app.Launch, index);
        }
        _dock.SettingsChanged();
    }

    /// <summary>Where among the pins a program dropped from Explorer at <paramref name="at"/> goes.</summary>
    private int PinIndexAt(Point at)
    {
        var centers = _tiles.Where(t => t.App is { Pinned: true }).Select(t => Along(t.TranslatePoint(new Point(t.ActualWidth / 2, t.ActualHeight / 2), _root))).ToArray();
        return DockApps.InsertIndex(centers, -1, Along(at));
    }

    internal void OpenMenu(DockTile tile, IReadOnlyList<BarMenuItem> items)
    {
        ClosePreview();
        HideLabel();
        if (_menu is { IsOpen: true })
            _menu.IsOpen = false;
        var placement = Edge switch { DockEdge.Left => PlacementMode.Right, DockEdge.Right => PlacementMode.Left, _ => PlacementMode.Top };
        // The dock never takes the foreground, and a menu in a window that hasn't it can't hold the mouse: a click elsewhere
        // never reached it and Esc went to the app in front. The menu takes the foreground while it's open, and hands it
        // back when it closes without a click elsewhere.
        nint before = Native.GetForegroundWindow();
        _menu = BarMenu.Open(tile, items, placement, horizontalOffset: Horizontal ? -12 : 0, verticalOffset: Horizontal ? 0 : -16, closed: () =>
        {
            nint now = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(now, out uint owner);
            if (before != 0 && Native.IsWindow(before) && (now == 0 || owner == (uint)Environment.ProcessId))
                Chippi.UI.Actions.ForegroundRight.Take(before);
        });
        var opened = _menu;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (opened.IsOpen && PresentationSource.FromVisual(opened) is System.Windows.Interop.HwndSource source)
                Chippi.UI.Actions.ForegroundRight.Take(source.Handle);
        });
        _menuPressed = Pressing();
    }

    private bool _menuPressed;

    private static bool Pressing() => (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;

    /// <summary>A press outside the open menu closes it, wherever the foreground is.</summary>
    private void WatchMenu()
    {
        if (_menu is not { IsOpen: true } menu)
            return;
        bool pressing = Pressing();
        if (pressing && !_menuPressed && PresentationSource.FromVisual(menu) is not null)
        {
            var p = menu.PointFromScreen(Native.Cursor());
            if (!new Rect(menu.RenderSize).Contains(p) && !SubmenuUnder())
                menu.IsOpen = false;
        }
        _menuPressed = pressing;
    }

    /// <summary>Whether the pointer is over one of the menu's open submenus, which are popups of their own.</summary>
    private bool SubmenuUnder() => _menu is { } menu && menu.Items.OfType<MenuItem>().Any(item =>
        item.IsSubmenuOpen && item.Template.FindName("PART_Popup", item) is Popup { Child: FrameworkElement child }
        && PresentationSource.FromVisual(child) is not null && new Rect(child.RenderSize).Contains(child.PointFromScreen(Native.Cursor())));

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
        // Hung off the icon's edge at its largest, so the previews never cover it while it's magnified.
        double grown = _dock.Settings.Magnify ? Math.Max(0, _dock.Settings.MagnifiedSize - (DockLayout.Shelf - DockLayout.Padding)) : 0;
        var anchor = tile.PointToScreen(new Point(tile.ActualWidth / 2, -grown));
        if (Edge != DockEdge.Bottom)
            anchor = tile.PointToScreen(new Point(Edge == DockEdge.Left ? tile.ActualWidth + grown : -grown, tile.ActualHeight / 2));
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
        WatchMenu();
        if (_tick % 50 == 0)
            CheckBin();
        if (_tick % 10 == 0 && _backdrop is not null)
            _backdrop.Restack(_hwnd); // another topmost window may have come between them
        if (++_tick % 5 == 0)
        {
            nint front = Native.GetForegroundWindow();
            bool fullscreen = FullscreenCheck.IsFullscreen(front) && Native.MonitorFromWindow(front, Native.MonitorDefaultToNearest) == DockController.MainDisplay
                && !Overlay(front);
            // A fullscreen window hides the dock only once it's stayed a second, so a moment's overlay doesn't flash it away.
            _fullscreenSeen = fullscreen ? _fullscreenSeen + 1 : 0;
            if (fullscreen && !_fullscreen && _fullscreenSeen < 2)
                fullscreen = false;
            if (fullscreen != _fullscreen)
            {
                _fullscreen = fullscreen;
                Visibility = fullscreen ? Visibility.Hidden : Visibility.Visible;
                SyncBackdrop();
            }
        }
        var hide = _dock.Settings.DockHide;
        if (hide == DockHiding.AlwaysShown || _fullscreen)
            return;
        bool busy = _dragging || _menu is { IsOpen: true } || _preview is not null || _confirm is { IsOpen: true } || _status.IsVisible || IsMouseOver;
        if (!_shown)
        {
            // The pointer rests at the edge a moment first, so reaching for a scrollbar or a window's edge doesn't show it.
            if (!PointerAtEdge())
                _edgeSince = DateTime.MaxValue;
            else if (_edgeSince == DateTime.MaxValue)
                _edgeSince = DateTime.UtcNow;
            else if (DateTime.UtcNow - _edgeSince >= TimeSpan.FromMilliseconds(150))
            {
                _edgeSince = DateTime.MaxValue;
                SetShown(true, animate: true);
            }
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

    private int _fullscreenSeen;

    /// <summary>
    /// A screenshot overlay rather than a fullscreen app: Chippi's own capture, the Snipping Tool's or Windows' screen clip.
    /// It covers the screen with a picture of it, the dock included, so hiding the dock only makes it blink.
    /// </summary>
    private static bool Overlay(nint window)
    {
        Native.GetWindowThreadProcessId(window, out uint pid);
        if (pid == (uint)Environment.ProcessId)
            return true;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName is "SnippingTool" or "ScreenClippingHost" or "ScreenSketch";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
        _slideUntil = DateTime.UtcNow + duration + TimeSpan.FromMilliseconds(34);
        if (duration > TimeSpan.Zero)
            StartFrames(); // the blur follows the shelf as it slides
        else
            SyncBackdrop();
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
