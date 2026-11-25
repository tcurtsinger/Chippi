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
    private readonly Action? _click;
    private readonly Func<IReadOnlyList<MenuBar.BarMenuItem>>? _menu;
    private string _name = "";

    public string Key { get; }
    public DockApp? App { get; private set; }
    public bool IsDivider { get; private init; }
    public double Size { get; private set; } = DockLayout.Icon;

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
        _art.RenderTransform = _lift;
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
        var tile = new DockTile(null, "|", "", null, null) { IsDivider = true, IsHitTestVisible = false };
        tile.Children.Clear();
        tile.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 0, 4, 0) });
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
        AutomationProperties.SetItemStatus(this, app.Running ? "Running" : "");
        bool launching = !app.Running && DateTime.UtcNow - _launchedAt < TimeSpan.FromSeconds(15);
        if (app.Running)
            _launchedAt = DateTime.MinValue;
        Dot.Opacity = app.Running ? 1 : 0.5;
        Dot.Visibility = app.Running || launching ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>The slot's size along and across the shelf for its edge; the icon sits against the edge, its dot under it.</summary>
    public void Orient(MacDesktop.DockEdge edge)
    {
        if (IsDivider)
        {
            var line = (Border)Children[0];
            bool across = edge == MacDesktop.DockEdge.Bottom;
            line.Width = across ? 1 : double.NaN;
            line.Height = across ? double.NaN : 1;
            line.Margin = across ? new Thickness(4, 12, 4, 12) : new Thickness(12, 4, 12, 4);
            Width = across ? 9 : DockLayout.Slot;
            Height = across ? DockLayout.Slot : 9;
            Margin = across ? new Thickness(0, 0, DockLayout.Gap, 0) : new Thickness(0, 0, 0, DockLayout.Gap);
            return;
        }
        _edge = edge;
        SetSize(Size);
    }

    private MacDesktop.DockEdge _edge = MacDesktop.DockEdge.Bottom;

    /// <summary>The icon at <paramref name="size"/> DIPs (48 at rest), grown out of the shelf away from the edge.</summary>
    public void SetSize(double size)
    {
        if (IsDivider)
            return;
        Size = size;
        _art.Width = _art.Height = size;
        ShowFrame();
        bool bottom = _edge == MacDesktop.DockEdge.Bottom;
        // The slot is the shelf's depth inside its edge line, so the row never clips it; a grown icon overflows it outward,
        // past the shelf (GetLayoutClip lets it). The icon keeps the shelf's padding from the outer edge, the dot under it.
        double pad = DockLayout.Padding - DockLayout.Edge;
        double inner = DockLayout.Slot - pad;
        ClipToBounds = false;
        Width = bottom ? size : DockLayout.Slot;
        Height = bottom ? DockLayout.Slot : size;
        Margin = bottom ? new Thickness(0, 0, DockLayout.Gap, 0) : new Thickness(0, 0, 0, DockLayout.Gap);
        _art.HorizontalAlignment = _edge switch { MacDesktop.DockEdge.Left => HorizontalAlignment.Left, MacDesktop.DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        _art.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        _art.Margin = _edge switch
        {
            MacDesktop.DockEdge.Left => new Thickness(pad, 0, inner - size, 0),
            MacDesktop.DockEdge.Right => new Thickness(inner - size, 0, pad, 0),
            _ => new Thickness(0, inner - size, 0, pad),
        };
        Dot.HorizontalAlignment = _edge switch { MacDesktop.DockEdge.Left => HorizontalAlignment.Left, MacDesktop.DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        Dot.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        Dot.Margin = _edge switch
        {
            MacDesktop.DockEdge.Left => new Thickness(2, 0, 0, 0),
            MacDesktop.DockEdge.Right => new Thickness(0, 0, 2, 0),
            _ => new Thickness(0, 0, 0, 2),
        };
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
