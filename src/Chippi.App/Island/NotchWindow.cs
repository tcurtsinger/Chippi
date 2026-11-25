// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Pet;
using Chippi.UI.Themes;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.App.Island;

/// <summary>What the notch's ears show: the highest activity's mark on the left, its value on the right.</summary>
internal sealed record NotchEars(Geometry? Glyph, ImageSource? Cover, bool Attention, bool Recording, bool Equalizer, string Value)
{
    /// <summary>Nothing going on: a tiny Chippi in the left ear.</summary>
    public static NotchEars Rest { get; } = new(null, null, false, false, false, "");

    public bool IsRest => this == Rest;

    /// <summary>The bar's choice of activity (DESIGN.md §5), told in a glyph and a short value.</summary>
    public static NotchEars For(IReadOnlyList<IslandActivity> activities, IReadOnlyList<IslandTileGroup> tiles, MediaTrack? song)
    {
        var shown = activities.Where(a => a.ModuleId != "mic-mute").ToArray();
        if (shown.FirstOrDefault(a => a.Kind == IslandActivityKind.Recording) is { } recording)
            return new(null, null, false, true, false, recording.Title);
        if (shown.FirstOrDefault(a => a.IsFeedback) is { } feedback)
        {
            var opens = feedback.OpensTile is { } page ? tiles.FirstOrDefault(t => t.Key == page) : null;
            var glyph = feedback.Icon is { } icon ? LucideIcons.Get(icon) : opens is not null ? LucideIcons.Get(opens.Icon) : LucideIcons.ClipboardType;
            return new(glyph, null, feedback.NeedsAttention, false, false, feedback.Title);
        }
        var top = shown.FirstOrDefault();
        if (top is null)
            return Rest;
        if (top.Kind == IslandActivityKind.Media)
            return song is { } track
                ? new(LucideIcons.Music, track.Artwork, false, false, track.Playing, track.Playing ? "" : Short(track.Title))
                : new(LucideIcons.Music, null, top.NeedsAttention, false, false, Short(top.Title));
        var tile = top.TileKey is { } key ? tiles.FirstOrDefault(t => t.Key == key) : null;
        var mark = tile is not null ? LucideIcons.Get(tile.Toggle?.Icon ?? tile.Icon)
            : top.Icon is { } own ? LucideIcons.Get(own)
            : top.Kind switch
            {
                IslandActivityKind.Download or IslandActivityKind.Stash => LucideIcons.Folder,
                IslandActivityKind.Timer => LucideIcons.Get("timer"),
                IslandActivityKind.Agent => LucideIcons.Get("bot"),
                _ => LucideIcons.Get("circle-dot"),
            };
        string status = tile is null ? "" : tile.Toggle?.Status?.Invoke() ?? tile.Status;
        return new(mark, null, top.NeedsAttention, false, false, Short(status.Length > 0 ? status : top.Title));
    }

    private static string Short(string text) => text.Length <= 18 ? text : text[..17].TrimEnd() + "…";
}

/// <summary>
/// The widget's form while the Mac-style desktop is on (docs/mac-style/brief.md, Notch): pure black, attached to the top
/// of a display's menu bar, 184 wide (280 with an activity) and 28 tall. Hover or click opens the panel out of it.
/// Picked up, it is the pill: riding the top edge, or pulled down off the bar, following the pointer with a landing line
/// showing where it will drop. It lands as the notch on any display's bar, snapping to the center within 40 DIPs.
/// </summary>
internal sealed class NotchWindow : Window
{
    public const double Tall = 28, RestWidth = 184, WideWidth = 280, Corner = 10;
    public const double PillTall = 22, PillMin = 38;
    private const double Room = 24, PickUp = 4, Ride = 3, Detach = 12, Snap = 40, LiftScale = 1.06, LiftTilt = -3;
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300), LandTime = TimeSpan.FromMilliseconds(160);

    private readonly IslandWindow _island;
    private readonly Action<string?, double> _save;
    private readonly Action _moved;
    private readonly Border _body = new() { Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
    private readonly ColumnDefinition _gap = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ChippiPet _pet = new() { Size = 16, Mood = PetMood.Sleepy, IsDecorative = true, IsHitTestVisible = false };
    private readonly LucideIcon _glyph = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _cover = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _recordingDot = new() { Width = 9, Height = 9, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _value = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 96 };
    private readonly StackPanel _equalizer = new() { Orientation = Orientation.Horizontal, Height = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _tilt = new();
    private readonly DropShadowEffect _shadow = new() { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.35 };
    private readonly LandingLine _line = new();
    private readonly DispatcherTimer _hover;
    private HWND _hwnd;
    private NotchEars _ears = NotchEars.Rest;

    private string? _display;
    private double _place = 0.5;
    private bool _hoverOpens = true;
    private bool _shown;

    // A press and, past 4 DIPs, the carry.
    private Point? _pressAt;
    private double _grip; // the pointer's distance from the pill's center along the bar, physical pixels
    private bool _detached;
    private (MonitorArea Area, Rect Notch, double Place, bool Main)? _landing;
    private EventHandler? _slide;

    public NotchWindow(IslandWindow island, Action<string?, double> save, Action moved)
    {
        _island = island;
        _save = save;
        _moved = moved;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Title = "Chippi notch";
        System.Windows.Automation.AutomationProperties.SetName(this, "Chippi");
        // The dark island ink, whatever the app theme: the notch is black in both.
        Resources.MergedDictionaries.Add(IslandPanelWindow.NotchLook());

        _glyph.Brush = Brushes.White;
        foreach (var (height, seconds) in new[] { (6.0, 0.42), (12.0, 0.36), (8.0, 0.5) })
        {
            var bar = new Rectangle { Width = 3, Height = height, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom };
            bar.SetResourceReference(Shape.FillProperty, "IslandAccentFillBrush");
            bar.Tag = (height, seconds);
            _equalizer.Children.Add(bar);
        }

        var left = new Grid { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_pet);
        left.Children.Add(_glyph);
        left.Children.Add(_cover);
        left.Children.Add(_recordingDot);
        var right = new Grid { Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_value);
        right.Children.Add(_equalizer);
        var ears = new Grid();
        ears.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ears.ColumnDefinitions.Add(_gap);
        ears.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 2);
        ears.Children.Add(left);
        ears.Children.Add(right);
        _body.Child = ears;
        _body.RenderTransformOrigin = new Point(0.5, 0.5);
        _body.RenderTransform = new TransformGroup { Children = { _scale, _tilt } };
        Content = new Grid { Children = { _body } };
        ShowEars();
        Rest();

        _hover = new DispatcherTimer(HoverDelay, DispatcherPriority.Input, (_, _) => HoverElapsed(), Dispatcher);
        _hover.Stop();
        SourceInitialized += (_, _) => _hwnd = IslandNative.MakeNoActivate(this);
        _body.MouseEnter += (_, _) => { if (_hoverOpens && _pressAt is null) _hover.Start(); };
        _body.MouseLeave += (_, _) => _hover.Stop();
        _body.MouseLeftButtonDown += OnPress;
        _body.MouseMove += OnMove;
        _body.MouseLeftButtonUp += OnRelease;
        _body.LostMouseCapture += (_, _) => { if (_pressAt is not null) Land(); };
        _body.MouseRightButtonUp += (_, e) => { e.Handled = true; OpenMenu(); };
        Closed += (_, _) => { StopSlide(); _line.Close(); };
    }

    // ----- What the shell tells it -----

    /// <summary>The saved place: a display (null for the main one) and a fraction of the bar's room; hover opening.</summary>
    public void SetPlace(string? display, double place, bool hoverOpens)
    {
        _hoverOpens = hoverOpens;
        if (!hoverOpens)
            _hover.Stop();
        if (display == _display && place.Equals(_place))
            return;
        _display = display;
        _place = double.IsFinite(place) ? Math.Clamp(place, 0, 1) : 0.5;
        Place();
    }

    /// <summary>Whether the widget may show at all (on, and not hidden for a fullscreen app); the island decides.</summary>
    public void SetShown(bool shown)
    {
        if (shown == _shown)
            return;
        _shown = shown;
        if (!shown)
        {
            if (_pressAt is not null)
                _body.ReleaseMouseCapture();
            _hover.Stop();
            _line.Hide();
            Hide();
            _moved();
            return;
        }
        Show();
        Place();
        _moved();
    }

    public void SetActivities(IReadOnlyList<IslandActivity> activities, IReadOnlyList<IslandTileGroup> tiles, MediaTrack? song, PetMood mood)
    {
        _pet.ShowMood(mood);
        var ears = NotchEars.For(activities, tiles, song);
        if (ears == _ears)
            return;
        bool widthChanged = ears.IsRest != _ears.IsRest;
        _ears = ears;
        ShowEars();
        if (widthChanged && _pressAt is null)
        {
            Place();
            _moved();
        }
    }

    public void SetPetColor(PetColor color) => _pet.PetColor = color;

    private IReadOnlyList<(string Label, Action Action)?> _menu = [];

    /// <summary>The puck's right-click menu (the tray icon's), so Quit stays reachable with the notch showing.</summary>
    public void SetMenu(IEnumerable<(string Label, Action Action)?> items) => _menu = items.ToArray();

    // ----- Where it is -----

    /// <summary>The notch at rest (or where a carried one will land), in physical pixels; null while it isn't showing.</summary>
    public Rect? Bounds => !_shown ? null : _landing?.Notch ?? (_area is null ? null : _bounds);

    /// <summary>The display the notch is on (or will land on).</summary>
    public MonitorArea? Area => _landing?.Area ?? _area;

    /// <summary>For the menu bar: the notch's display and its edges, so its items keep clear (during a carry too).</summary>
    public unsafe (nint Monitor, double Left, double Right)? BarBounds =>
        Area is { } area && Bounds is { } notch ? ((nint)area.Handle.Value, notch.Left, notch.Right) : null;

    /// <summary>Picked up and being carried.</summary>
    public bool IsCarried => _landing is not null;

    /// <summary>Whether <paramref name="physical"/> is on the notch (or the carried pill).</summary>
    public bool Covers(Point physical) =>
        _shown && IsVisible && _body.RenderSize.Width > 0 && PresentationSource.FromVisual(_body) is not null
        && new Rect(_body.RenderSize).Contains(_body.PointFromScreen(physical));

    private MonitorArea? _area;
    private Rect _bounds;

    private double Width0 => _ears.IsRest ? RestWidth : WideWidth;

    private HMONITOR Monitor => Displays.Find(_display) ?? Displays.Main;

    /// <summary>The notch at its saved place, at rest.</summary>
    private void Place()
    {
        if (_pressAt is not null || _slide is not null)
            return;
        _area = IslandNative.Area(Monitor);
        if (_area is not { } area)
            return;
        _bounds = NotchGeometry.Rest(area.Monitor, area.Scale, Width0, _place);
        Rest();
        if (_hwnd != default && _shown)
            IslandNative.Move(_hwnd, _bounds);
    }

    /// <summary>The notch's own shape: the window is the notch, square at the top and round below.</summary>
    private void Rest()
    {
        double scale = _area?.Scale ?? 1;
        Width = _bounds.Width > 0 ? _bounds.Width / scale : Width0;
        Height = Tall;
        _body.Margin = new Thickness(0);
        _body.Width = Width;
        _body.Height = Tall;
        _body.HorizontalAlignment = HorizontalAlignment.Left;
        _body.VerticalAlignment = VerticalAlignment.Top;
        _body.CornerRadius = new CornerRadius(0, 0, Corner, Corner);
        _body.Effect = null;
        _scale.ScaleX = _scale.ScaleY = 1;
        _tilt.Angle = 0;
        _gap.Width = new GridLength(1, GridUnitType.Star);
    }

    /// <summary>The pill: round, hugging what it shows, in a window with room for its lift and shadow.</summary>
    private void Pill()
    {
        Width = WideWidth + 2 * Room;
        Height = PillTall + 2 * Room;
        _body.Width = double.NaN;
        _body.MinWidth = PillMin;
        _body.Height = PillTall;
        _body.HorizontalAlignment = HorizontalAlignment.Center;
        _body.VerticalAlignment = VerticalAlignment.Center;
        _body.CornerRadius = new CornerRadius(PillTall / 2);
        _body.Effect = _shadow;
        _gap.Width = new GridLength(_ears.IsRest ? 0 : 6);
    }

    private void ShowEars()
    {
        var ears = _ears;
        _pet.Visibility = Shown(ears.IsRest);
        _recordingDot.Visibility = Shown(ears.Recording);
        _cover.Visibility = Shown(ears.Cover is not null);
        _cover.Fill = ears.Cover is { } art ? new ImageBrush(art) { Stretch = Stretch.UniformToFill } : null;
        _glyph.Visibility = Shown(ears.Glyph is not null && ears.Cover is null && !ears.Recording);
        if (ears.Glyph is { } glyph)
            _glyph.Data = glyph;
        if (ears.Attention)
            _glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandCautionBrush");
        else
            _glyph.Brush = Brushes.White;
        _value.Text = ears.Value;
        _value.Visibility = Shown(!ears.Equalizer && ears.Value.Length > 0);
        _equalizer.Visibility = Shown(ears.Equalizer);
        foreach (Rectangle bar in _equalizer.Children)
        {
            var (height, seconds) = ((double, double))bar.Tag;
            if (ears.Equalizer && !Motion.IsReduced)
                bar.BeginAnimation(HeightProperty, new DoubleAnimation(3, height, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            else
                bar.BeginAnimation(HeightProperty, null);
        }
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    // ----- Hover, click and the menu -----

    private void HoverElapsed()
    {
        _hover.Stop();
        if (_hoverOpens && _pressAt is null && _shown && Covers(IslandNative.Cursor()))
            _island.HoverFromNotch();
    }

    private void OpenMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _body, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var entry in _menu)
        {
            if (entry is not { } item)
            {
                menu.Items.Add(new Separator());
                continue;
            }
            var menuItem = new MenuItem { Header = item.Label };
            menuItem.Click += (_, _) => item.Action();
            menu.Items.Add(menuItem);
        }
        if (menu.Items.Count > 0)
            menu.IsOpen = true;
    }

    // ----- The carry -----

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _hover.Stop();
        StopSlide();
        _pressAt = IslandNative.Cursor();
        _body.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } start || _area is not { } home)
            return;
        var at = IslandNative.Cursor();
        if (_landing is null)
        {
            if ((at - start).Length < PickUp * home.Scale)
                return;
            // Picked up: the pill keeps the point of the notch under the pointer, within its own width.
            _grip = Math.Clamp(start.X - (_bounds.Left + _bounds.Width / 2), -_bounds.Width / 2, _bounds.Width / 2);
            _island.ClosePages();
            Pill();
            _landing = Landing(at);
        }
        Carry(at);
    }

    private void Carry(Point at)
    {
        var landing = Landing(at);
        _landing = landing;
        var area = landing.Area;
        double s = area.Scale;
        double barBottom = area.Monitor.Top + Tall * s;
        // Off the bar past 12 it detaches; back onto the bar it rides the top edge again.
        if (!_detached && at.Y > barBottom + Detach * s)
            _detached = true;
        else if (_detached && at.Y <= barBottom)
            _detached = false;
        double pillWidth = Math.Max(PillMin, _body.ActualWidth) * s;
        var center = _detached
            ? new Point(at.X - _grip * Math.Min(1, pillWidth / Math.Max(1, _bounds.Width)), at.Y)
            : new Point(Math.Clamp(at.X - _grip, area.Monitor.Left + pillWidth / 2, area.Monitor.Right - pillWidth / 2), area.Monitor.Top + (Ride + PillTall / 2) * s);
        bool lifted = _detached && !Motion.IsReduced;
        _scale.ScaleX = _scale.ScaleY = lifted ? LiftScale : 1;
        _tilt.Angle = lifted ? LiftTilt : 0;
        _shadow.BlurRadius = _detached ? 22 : 12;
        _shadow.ShadowDepth = _detached ? 8 : 2;
        _shadow.Opacity = _detached ? 0.45 : 0.3;
        IslandNative.Move(_hwnd, new Rect(center.X - Width * s / 2, center.Y - Height * s / 2, Width * s, Height * s));
        if (_detached)
            _line.ShowAt(landing.Notch, s);
        else
            _line.Hide();
        _moved();
    }

    /// <summary>Where a drop at <paramref name="at"/> lands: the bar of the display under the pointer.</summary>
    private (MonitorArea Area, Rect Notch, double Place, bool Main) Landing(Point at)
    {
        var monitor = IslandNative.MonitorAt(at);
        var area = IslandNative.Area(monitor) ?? _area!.Value;
        double width = Width0 * area.Scale;
        double place = NotchGeometry.PlaceFor(area.Monitor, area.Scale, width, at.X - _grip, Snap);
        return (area, NotchGeometry.Rest(area.Monitor, area.Scale, Width0, place), place, monitor == Displays.Main);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_pressAt is null)
            return;
        bool carried = _landing is not null;
        if (!carried)
        {
            _pressAt = null;
            _body.ReleaseMouseCapture();
            _island.ToggleFromNotch();
            return;
        }
        _body.ReleaseMouseCapture(); // LostMouseCapture drops it
    }

    /// <summary>Lands the carried pill as the notch where it is heading, and saves the place.</summary>
    private void Land()
    {
        _pressAt = null;
        if (_landing is not { } landing)
            return;
        _line.Hide();
        _detached = false;
        unsafe
        {
            _display = landing.Main ? null : Displays.Connected().FirstOrDefault(d => d.Handle == landing.Area.Handle)?.Id;
        }
        _place = landing.Place;
        _save(_display, _place);
        // It slides up onto the bar, then is the notch again.
        var from = IslandNative.WindowRect(_hwnd);
        double s = landing.Area.Scale;
        var to = new Rect(landing.Notch.Left + landing.Notch.Width / 2 - Width * s / 2, landing.Area.Monitor.Top + PillTall / 2 * s - Height * s / 2, Width * s, Height * s);
        if (Motion.IsReduced || _hwnd == default)
        {
            Landed();
            return;
        }
        _scale.ScaleX = _scale.ScaleY = 1;
        _tilt.Angle = 0;
        var clock = Stopwatch.StartNew();
        _slide = (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / LandTime.TotalMilliseconds);
            double k = 1 - Math.Pow(1 - t, 3);
            IslandNative.MoveTo(_hwnd, new Point(from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * k));
            if (t >= 1)
                Landed();
        };
        CompositionTarget.Rendering += _slide;

        void Landed()
        {
            StopSlide();
            _landing = null;
            Place();
            _moved();
        }
    }

    private void StopSlide()
    {
        if (_slide is null)
            return;
        CompositionTarget.Rendering -= _slide;
        _slide = null;
    }

    /// <summary>The 3px accent line on a display's top edge, the notch's width, where a detached pill will land.</summary>
    private sealed class LandingLine : Window
    {
        private HWND _hwnd;

        public LandingLine()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Resources.MergedDictionaries.Add(IslandPanelWindow.NotchLook());
            var line = new Border { CornerRadius = new CornerRadius(0, 0, 1.5, 1.5) };
            line.SetResourceReference(Border.BackgroundProperty, "IslandAccentFillBrush");
            Content = line;
            SourceInitialized += (_, _) => _hwnd = IslandNative.MakeNoActivate(this);
        }

        public void ShowAt(Rect notch, double scale)
        {
            if (!IsVisible)
                Show();
            Width = notch.Width / scale;
            Height = 3;
            IslandNative.Move(_hwnd, new Rect(notch.Left, notch.Top, notch.Width, 3 * scale));
        }
    }
}

/// <summary>The notch's place on a display's bar, in physical pixels. Pure arithmetic, so it's unit-tested.</summary>
internal static class NotchGeometry
{
    /// <summary>The notch at <paramref name="place"/> (0 flush left, 1 flush right) across a display's top edge.</summary>
    public static Rect Rest(Rect monitor, double scale, double width, double place)
    {
        double w = width * scale, h = NotchWindow.Tall * scale;
        double room = Math.Max(0, monitor.Width - w);
        return new Rect(monitor.Left + Math.Clamp(place, 0, 1) * room, monitor.Top, w, h);
    }

    /// <summary>
    /// The place for a notch <paramref name="width"/> wide (physical) centered at <paramref name="center"/>: within
    /// <paramref name="snap"/> DIPs of the display's center it is exactly the center.
    /// </summary>
    public static double PlaceFor(Rect monitor, double scale, double width, double center, double snap)
    {
        double middle = monitor.Left + monitor.Width / 2;
        if (Math.Abs(center - middle) <= snap * scale)
            return 0.5;
        double room = monitor.Width - width;
        return room <= 0 ? 0.5 : Math.Clamp((center - width / 2 - monitor.Left) / room, 0, 1);
    }
}
