// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>One place in the dock: Start, an app, the divider, Downloads or the Recycle Bin.</summary>
internal sealed class DockTile : Grid
{
    private readonly DockWindow? _dock;
    private readonly Grid _art = new();
    private readonly Image _image = new();
    private readonly TranslateTransform _lift = new();
    private readonly TranslateTransform _shift = new();
    // Magnification is drawn, not laid out: the icon keeps its 48 box and is scaled and moved by fractions of a pixel.
    private readonly ScaleTransform _grow = new();
    private readonly TranslateTransform _glide = new();
    private readonly TranslateTransform _dotGlide = new();
    private readonly Action? _click;
    private readonly Func<IReadOnlyList<MenuBar.BarMenuItem>>? _menu;
    private string _name = "";

    public string Key { get; }
    public DockApp? App { get; private set; }
    public bool IsDivider { get; private init; }
    public double Size { get; private set; } = DockLayout.Icon;

    /// <summary>The tile's length along the shelf at rest.</summary>
    public double Rest => IsDivider ? DockLayout.Divider : DockLayout.Icon;

    /// <summary>The size magnification is easing the icon toward.</summary>
    public double Target { get; set; } = DockLayout.Icon;
    public Ellipse Dot { get; } = new() { Width = 4, Height = 4, Visibility = Visibility.Hidden, IsHitTestVisible = false };

    private DockTile(DockWindow? dock, string key, string name, Action? click, Func<IReadOnlyList<MenuBar.BarMenuItem>>? menu)
    {
        _dock = dock;
        Key = key;
        _name = name;
        _click = click;
        _menu = menu;
        Background = Brushes.Transparent; // the whole slot takes the pointer, not only the icon's opaque pixels
        AppIcon.Configure(_image);
        // Its frames are already scaled to the sizes it rests at; in between it's drawn a little smaller than one, which
        // plain filtering does well and at no cost per frame.
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.Linear);
        _art.Children.Add(_image);
        _art.Width = _art.Height = DockLayout.Icon;
        _art.RenderTransform = new TransformGroup { Children = { _grow, _glide, _lift } };
        Dot.RenderTransform = _dotGlide;
        Children.Add(_art);
        Children.Add(Dot);
        RenderTransform = _shift;
        AutomationProperties.SetName(this, name);
        if (dock is null)
            return;
        MouseEnter += (_, _) => { if (!dock.HasPreview(this)) dock.ShowLabel(this, _name); dock.Hovered(this, true); };
        MouseLeave += (_, _) => { dock.HideLabel(); dock.Hovered(this, false); dock.PreviewLeft(); };
        MouseLeftButtonDown += (_, e) => { e.Handled = true; dock.Pressed(this, e); };
        MouseMove += (_, e) => dock.Moved(this, e);
        MouseLeftButtonUp += (_, e) => { e.Handled = true; dock.Released(this, e); };
        LostMouseCapture += (_, _) => { SetPressed(false); dock.CaptureLost(this); };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_menu?.Invoke() is { Count: > 0 } items)
                dock.OpenMenu(this, items);
        };
    }

    public static DockTile Start(DockWindow dock)
    {
        var tile = new DockTile(dock, "start", "Start", dock.Controller.OpenStart, dock.Controller.StartMenu);
        var face = new Border
        {
            Width = DockLayout.Icon, Height = DockLayout.Icon,
            CornerRadius = new CornerRadius(DockLayout.Icon * 0.225),
            Background = new LinearGradientBrush(Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x00, 0x5F, 0xB8), new Point(0, 0), new Point(1, 1)),
            Child = new LucideIcon { Data = LucideIcons.LayoutGrid, Size = 24, Brush = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tile._art.Children.Insert(0, Scaled(face));
        return tile;
    }

    public static DockTile ForApp(DockWindow dock, DockApp app)
    {
        var tile = new DockTile(dock, app.Key, app.Name, null, () => dock.Controller.Menu(Current(dock, app.Key) ?? app));
        tile.App = app;
        tile.AllowDrop = true;
        tile.DragOver += (_, e) => { e.Effects = DockWindow.Files(e) is not null && app.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        tile.Drop += (_, e) => { dock.HideLabel(); if (DockWindow.Files(e) is { } files && tile.App is { } now) { e.Handled = true; dock.Controller.OpenWith(now, files); } };
        tile.DragEnter += (_, e) => { if (DockWindow.Files(e) is not null && app.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) dock.ShowLabel(tile, "Open with " + tile._name); };
        tile.DragLeave += (_, _) => dock.HideLabel();
        tile.Load(app.Launch);
        tile.Update(app);
        return tile;
    }

    // The menu is built from the app as it is when it opens, not as it was when the tile was made.
    private static DockApp? Current(DockWindow dock, string key) => dock.Controller.Apps.FirstOrDefault(a => a.Key == key);

    public static DockTile Folder(DockWindow dock, string key, string name, string path)
    {
        bool bin = key == "bin";
        var controller = dock.Controller;
        var tile = new DockTile(dock, key, name,
            bin ? () => controller.Open("shell:RecycleBinFolder") : null,
            bin ? controller.RecycleBinMenu : controller.DownloadsMenu);
        if (!bin)
            tile._clickMenu = true; // a click opens the stack of newest files, as the right click does
        tile.AllowDrop = true;
        tile.DragOver += (_, e) => { e.Effects = DockWindow.Files(e) is not null ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        tile.DragEnter += (_, e) => { if (DockWindow.Files(e) is not null) dock.ShowLabel(tile, bin ? "Delete" : "Move to Downloads"); };
        tile.DragLeave += (_, _) => dock.HideLabel();
        tile.Drop += (_, e) =>
        {
            dock.HideLabel();
            if (DockWindow.Files(e) is not { } files)
                return;
            e.Handled = true;
            if (bin) controller.Recycle(files);
            else controller.MoveToDownloads(files);
        };
        tile.Load(path);
        if (!bin)
            tile.MouseEnter += (_, _) => DockController.PrefetchDownloadIcons();
        return tile;
    }

    private bool _clickMenu;

    /// <summary>The Recycle Bin's tile, which anchors its Empty confirm.</summary>
    public bool IsBin => Key == "bin";

    /// <summary>The icon itself, for placing the name and previews against its grown edge.</summary>
    public FrameworkElement Art => _art;

    /// <summary>The divider's line, in the shelf's own edge color.</summary>
    public void SetLine(Brush brush)
    {
        if (IsDivider && Children.Count > 0 && Children[0] is Border line)
            line.Background = brush;
    }

    public static DockTile Divider()
    {
        var tile = new DockTile(null, "|", "", null, null) { IsDivider = true, IsHitTestVisible = false, _slot = new(DockLayout.Divider, 0, 1) };
        tile.Children.Clear();
        tile.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 0, 4, 0), RenderTransform = tile._glide });
        return tile;
    }

    /// <summary>A tile that only shows <paramref name="picture"/>: the row drawn without a dock, for tests and shots.</summary>
    internal static DockTile Still(string key, BitmapSource picture)
    {
        var tile = new DockTile(null, key, key, null, null);
        tile._rest = tile._large = picture;
        tile._image.Source = picture;
        return tile;
    }

    /// <summary>A magnified icon reaches past the slot, and past the shelf; nothing clips it to its place in the row.</summary>
    protected override Geometry GetLayoutClip(Size layoutSlotSize) => null!;

    private BitmapSource? _rest, _large;
    private string? _icon;
    private int _restPixels, _largePixels, _loads;
    private bool _monogram;

    private void Load(string path)
    {
        _icon = path;
        Reload(changed: false);
    }

    /// <summary>The frames read again where the display's scale or the magnified size has changed.</summary>
    public void Refresh() => Reload(changed: false);

    /// <summary>
    /// Two frames of the one drawing (the icon's largest artwork, AppIcon.LoadFittedAsync): one at the resting size in
    /// device pixels and one at the magnified size, each scaled once. Between the two it's only ever drawn a little
    /// smaller than a frame, so it stays sharp at rest and at full size and the drawing never changes as it grows.
    /// </summary>
    private void Reload(bool changed)
    {
        if (_icon is not { } path)
            return;
        double scale = _dock?.Scale ?? 1;
        double grown = _dock is { Controller.Settings: { Magnify: true } settings } ? Math.Max(DockLayout.Icon, settings.MagnifiedSize) : DockLayout.Icon;
        int rest = (int)Math.Round(DockLayout.Icon * scale), large = (int)Math.Round(grown * scale);
        if (!changed && rest == _restPixels && large == _largePixels)
            return;
        (_restPixels, _largePixels) = (rest, large);
        if (changed)
            AppIcon.Forget(path);
        var small = AppIcon.LoadFittedAsync(path, rest);
        var big = large == rest ? small : AppIcon.LoadFittedAsync(path, large);
        int load = ++_loads;
        _ = Paint();

        async Task Paint()
        {
            var first = await small;
            if (load != _loads)
                return;
            _rest = first ?? _rest;
            ShowFrame();
            var second = await big;
            if (load != _loads)
                return;
            _large = second ?? _large;
            ShowFrame();
            if (_rest is null && _large is null && _image.Source is null)
                ShowMonogram();
        }
    }

    private void ShowFrame()
    {
        var frame = Size > DockLayout.Icon + 0.5 ? _large ?? _rest : _rest ?? _large;
        if (frame is not null && _image.Source != frame)
            _image.Source = frame;
    }

    private void ShowMonogram()
    {
        if (_monogram)
            return;
        _monogram = true;
        string letter = _name.Length > 0 ? _name[..1].ToUpperInvariant() : "?";
        _art.Children.Insert(0, Scaled(new Border
        {
            Width = DockLayout.Icon, Height = DockLayout.Icon,
            CornerRadius = new CornerRadius(DockLayout.Icon * 0.225),
            Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x76)),
            Child = new TextBlock { Text = letter, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        }));
    }

    /// <summary>A 48-DIP drawn tile that grows with magnification as an app's icon does: its corners and glyph scale too.</summary>
    private static Viewbox Scaled(Border face) => new() { Stretch = Stretch.Uniform, Child = face };

    // ----- States -----

    private DateTime _launchedAt = DateTime.MinValue;

    /// <summary>Darker while pressed, as a pressed icon is.</summary>
    public void SetPressed(bool pressed) => _art.Opacity = pressed ? 0.7 : 1;

    /// <summary>
    /// Clicked open and not running yet: its dot shows at half strength until its window appears, for up to 15 seconds,
    /// so a slow start reads as starting.
    /// </summary>
    public void MarkLaunching()
    {
        if (App is { Running: true })
            return;
        _launchedAt = DateTime.UtcNow;
        Dot.Opacity = 0.5;
        Dot.Visibility = Visibility.Visible;
    }

    /// <summary>The icon read again (the Recycle Bin's, as it fills and empties). The old one stays until the new one is in.</summary>
    public void ReloadIcon() => Reload(changed: true);

    public void Update(DockApp app)
    {
        App = app;
        _name = app.Name;
        AutomationProperties.SetName(this, app.Name);
        SetBadge(_dock?.Controller.BadgeOf(app));
        bool launching = !app.Running && DateTime.UtcNow - _launchedAt < TimeSpan.FromSeconds(15);
        if (app.Running)
            _launchedAt = DateTime.MinValue;
        Dot.Opacity = app.Running ? 1 : 0.5;
        Dot.Visibility = app.Running || launching ? Visibility.Visible : Visibility.Hidden;
    }

    // ----- Badge -----

    /// <summary>Critical (DESIGN.md §2) in both themes, with white text, as on the menu bar's muted mic.</summary>
    private static readonly Brush BadgeFill = Frozen(new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)));

    private Border? _badge;

    /// <summary>The badge it shows now: null for none, "" for a dot, else a count.</summary>
    public string? Badge { get; private set; }

    /// <summary>
    /// The red badge on the icon's top-right corner: an 18 disc with the count (11 / 600, a pill past one digit), or a 10 dot
    /// when the app gives no number. It's part of the icon's drawing, so it grows with the magnification.
    /// </summary>
    public void SetBadge(string? badge)
    {
        string status = App is { Running: true } ? "Running" : "";
        if (badge is not null)
            status = (status.Length > 0 ? status + ", " : "") + (badge.Length == 0 ? "needs attention" : badge + " unread");
        AutomationProperties.SetItemStatus(this, status);
        if (badge == Badge)
            return;
        Badge = badge;
        if (badge is null)
        {
            if (_badge is not null)
                _badge.Visibility = Visibility.Collapsed;
            return;
        }
        _badge ??= MakeBadge();
        bool dot = badge.Length == 0;
        var text = (TextBlock)_badge.Child;
        text.Text = badge;
        text.Visibility = dot ? Visibility.Collapsed : Visibility.Visible;
        _badge.Height = _badge.MinWidth = dot ? 10 : 18;
        _badge.CornerRadius = new CornerRadius(dot ? 5 : 9);
        _badge.Padding = dot ? new Thickness(0) : new Thickness(5, 0, 5, 0);
        _badge.Margin = dot ? new Thickness(0, 1, 1, 0) : new Thickness(0, -3, -3, 0);
        _badge.Visibility = Visibility.Visible;
    }

    private Border MakeBadge()
    {
        var badge = new Border
        {
            Background = BadgeFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        _art.Children.Add(badge);
        return badge;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>The slot's size along and across the shelf for its edge; the icon sits against the edge, its dot under it.</summary>
    public void Orient(MacDesktop.DockEdge edge)
    {
        _edge = edge;
        if (IsDivider)
        {
            var line = (Border)Children[0];
            bool across = edge == MacDesktop.DockEdge.Bottom;
            line.Width = across ? 1 : double.NaN;
            line.Height = across ? double.NaN : 1;
            line.Margin = across ? new Thickness(4, 12, 4, 12) : new Thickness(12, 4, 12, 4);
            Width = across ? DockLayout.Divider : DockLayout.Slot;
            Height = across ? DockLayout.Slot : DockLayout.Divider;
            Margin = across ? new Thickness(0, 0, DockLayout.Gap, 0) : new Thickness(0, 0, 0, DockLayout.Gap);
            return;
        }
        bool bottom = edge == MacDesktop.DockEdge.Bottom;
        // The slot is the shelf's depth inside its edge line, so the row never clips it; a grown icon overflows it outward,
        // past the shelf (GetLayoutClip lets it). The icon keeps the shelf's padding from the outer edge, the dot under it.
        // Both sit at the slot's start along the shelf, where Place moves them from; the icon grows from its outer edge.
        double pad = DockLayout.Padding - DockLayout.Edge;
        double inner = DockLayout.Slot - pad;
        double dot = (DockLayout.Icon - Dot.Width) / 2;
        ClipToBounds = false;
        if (bottom) Height = DockLayout.Slot;
        else Width = DockLayout.Slot;
        Margin = bottom ? new Thickness(0, 0, DockLayout.Gap, 0) : new Thickness(0, 0, 0, DockLayout.Gap);
        _art.HorizontalAlignment = edge == MacDesktop.DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _art.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _art.RenderTransformOrigin = edge switch { MacDesktop.DockEdge.Left => new Point(0, 0.5), MacDesktop.DockEdge.Right => new Point(1, 0.5), _ => new Point(0.5, 1) };
        _art.Margin = edge switch
        {
            MacDesktop.DockEdge.Left => new Thickness(pad, 0, inner - DockLayout.Icon, 0),
            MacDesktop.DockEdge.Right => new Thickness(inner - DockLayout.Icon, 0, pad, 0),
            _ => new Thickness(0, inner - DockLayout.Icon, 0, pad),
        };
        Dot.HorizontalAlignment = edge == MacDesktop.DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Dot.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Dot.Margin = edge switch
        {
            MacDesktop.DockEdge.Left => new Thickness(2, dot, 0, 0),
            MacDesktop.DockEdge.Right => new Thickness(0, dot, 2, 0),
            _ => new Thickness(dot, 0, 0, 2),
        };
        Place(_slot);
    }

    private MacDesktop.DockEdge _edge = MacDesktop.DockEdge.Bottom;
    private DockSlot _slot = new(DockLayout.Icon, 0, 1);

    /// <summary>The icon's size in DIPs (48 at rest). It's drawn at it once the dock places the tile (<see cref="Place"/>).</summary>
    public void SetSize(double size)
    {
        if (IsDivider)
            return;
        Size = size;
        ShowFrame();
    }

    /// <summary>
    /// Puts the tile in its slot for this frame (DockLayout.Slots): the slot is whole pixels along the shelf, and the icon
    /// in it is drawn at its exact size and place, grown out of the shelf away from the edge, its dot under its middle.
    /// </summary>
    public void Place(DockSlot slot)
    {
        _slot = slot;
        bool bottom = _edge == MacDesktop.DockEdge.Bottom;
        if (bottom) Width = slot.Extent;
        else Height = slot.Extent;
        _glide.X = _dotGlide.X = bottom ? slot.Shift : 0;
        _glide.Y = _dotGlide.Y = bottom ? 0 : slot.Shift;
        if (!IsDivider)
            _grow.ScaleX = _grow.ScaleY = slot.Scale;
    }

    /// <summary>Moves the tile <paramref name="offset"/> along the shelf, out of the way of a dragged icon's landing place.</summary>
    public void Shift(Vector offset)
    {
        if (_shift.X == offset.X && _shift.Y == offset.Y && !_shifting)
            return;
        _shifting = offset != default;
        var duration = TimeSpan.FromMilliseconds(Motion.IsReduced ? 0 : 120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset.X, duration) { EasingFunction = ease });
        _shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset.Y, duration) { EasingFunction = ease });
    }

    private bool _shifting;

    /// <summary>Back in place at once, as the drag that shifted it ends.</summary>
    public void Unshift()
    {
        _shifting = false;
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.BeginAnimation(TranslateTransform.YProperty, null);
        _shift.X = _shift.Y = 0;
    }

    /// <summary>Follows a drag by <paramref name="offset"/>; faded while it's far enough off the shelf to come off it.</summary>
    public void Lift(Vector offset, bool away)
    {
        _lift.X = offset.X;
        _lift.Y = offset.Y;
        _art.Opacity = away ? 0.4 : 1;
        Panel.SetZIndex(this, offset == default ? 0 : 1);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TilePeer(this);

    /// <summary>Screen readers and UI Automation see each tile as a button with its name that a click invokes.</summary>
    private sealed class TilePeer(DockTile tile) : FrameworkElementAutomationPeer(tile), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => "DockTile";

        protected override bool IsContentElementCore() => !tile.IsDivider;

        protected override bool IsControlElementCore() => !tile.IsDivider;

        public override object? GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.Invoke && !tile.IsDivider ? this : base.GetPattern(pattern);

        public void Invoke() => tile.Click();
    }

    public void Click()
    {
        if (_dock is null)
            return;
        if (App is { } app)
        {
            var now = _dock.Controller.Apps.FirstOrDefault(a => a.Key == app.Key) ?? app;
            if (!now.Running)
                MarkLaunching();
            _dock.Controller.Click(now);
        }
        else if (_clickMenu && _menu?.Invoke() is { Count: > 0 } items)
            _dock.OpenMenu(this, items);
        else
            _click?.Invoke();
    }
}
