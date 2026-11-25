// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.UI.Controls;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>One place in the dock: Start, an app, the divider, Downloads or the Recycle Bin.</summary>
internal sealed class DockTile : Grid
{
    private readonly DockWindow? _dock;
    private readonly Grid _art = new();
    private readonly Image _image = new();
    private readonly TranslateTransform _lift = new();
    private readonly Action? _click;
    private readonly Func<IReadOnlyList<MenuBar.BarMenuItem>>? _menu;
    private string _name = "";

    public string Key { get; }
    public DockApp? App { get; private set; }
    public bool IsDivider { get; private init; }
    public double Size { get; private set; } = DockLayout.Icon;
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
        _art.Children.Add(_image);
        _art.RenderTransform = _lift;
        Children.Add(_art);
        Children.Add(Dot);
        AutomationProperties.SetName(this, name);
        if (dock is null)
            return;
        MouseEnter += (_, _) => { if (!dock.HasPreview(this)) dock.ShowLabel(this, _name); dock.Hovered(this, true); };
        MouseLeave += (_, _) => { dock.HideLabel(); dock.Hovered(this, false); dock.PreviewLeft(); };
        MouseLeftButtonDown += (_, e) => { e.Handled = true; dock.Pressed(this, e); };
        MouseMove += (_, e) => dock.Moved(this, e);
        MouseLeftButtonUp += (_, e) => { e.Handled = true; dock.Released(this, e); };
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
            CornerRadius = new CornerRadius(11),
            Background = new LinearGradientBrush(Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x00, 0x5F, 0xB8), new Point(0, 0), new Point(1, 1)),
            Child = new LucideIcon { Data = LucideIcons.LayoutGrid, Size = 24, Brush = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tile._art.Children.Insert(0, face);
        return tile;
    }

    public static DockTile ForApp(DockWindow dock, DockApp app)
    {
        var tile = new DockTile(dock, app.Key, app.Name, null, () => dock.Controller.Menu(Current(dock, app.Key) ?? app));
        tile.App = app;
        tile.AllowDrop = true;
        tile.DragOver += (_, e) => { e.Effects = DockWindow.Files(e) is not null && app.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        tile.Drop += (_, e) => { if (DockWindow.Files(e) is { } files && tile.App is { } now) { e.Handled = true; dock.Controller.OpenWith(now, files); } };
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
        tile.Drop += (_, e) =>
        {
            if (DockWindow.Files(e) is not { } files)
                return;
            e.Handled = true;
            if (bin) DockController.Recycle(files);
            else controller.MoveToDownloads(files);
        };
        tile.Load(path);
        return tile;
    }

    private bool _clickMenu;

    public static DockTile Divider()
    {
        var tile = new DockTile(null, "|", "", null, null) { IsDivider = true, IsHitTestVisible = false };
        tile.Children.Clear();
        tile.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 0, 4, 0) });
        return tile;
    }

    private void Load(string path)
    {
        var pending = AppIcon.LoadAsync(path, 192);
        if (pending.IsCompletedSuccessfully)
            _image.Source = pending.Result;
        else
            _ = Paint();

        async Task Paint()
        {
            var source = await pending;
            if (source is not null)
                _image.Source = source;
            else if (_image.Source is null)
                ShowMonogram();
        }
    }

    private void ShowMonogram()
    {
        string letter = _name.Length > 0 ? _name[..1].ToUpperInvariant() : "?";
        _art.Children.Insert(0, new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x76)),
            Child = new TextBlock { Text = letter, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
    }

    public void Update(DockApp app)
    {
        App = app;
        _name = app.Name;
        AutomationProperties.SetName(this, app.Name);
        AutomationProperties.SetItemStatus(this, app.Running ? "Running" : "");
        Dot.Visibility = app.Running ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>The slot's size along and across the shelf for its edge; the icon sits against the edge, its dot under it.</summary>
    public void Orient(MacDesktop.DockEdge edge)
    {
        if (IsDivider)
        {
            var line = (Border)Children[0];
            line.SetResourceReference(Border.BackgroundProperty, "BorderStrongBrush");
            bool across = edge == MacDesktop.DockEdge.Bottom;
            line.Width = across ? 1 : double.NaN;
            line.Height = across ? double.NaN : 1;
            line.Margin = across ? new Thickness(4, 12, 4, 12) : new Thickness(12, 4, 12, 4);
            Width = across ? 9 : DockLayout.Shelf;
            Height = across ? DockLayout.Shelf : 9;
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
        bool bottom = _edge == MacDesktop.DockEdge.Bottom;
        // The slot stays the shelf's depth; a grown icon overflows it outward, past the shelf.
        double inner = DockLayout.Shelf - DockLayout.Padding;
        ClipToBounds = false;
        Width = bottom ? size : DockLayout.Shelf;
        Height = bottom ? DockLayout.Shelf : size;
        Margin = bottom ? new Thickness(0, 0, DockLayout.Gap, 0) : new Thickness(0, 0, 0, DockLayout.Gap);
        _art.HorizontalAlignment = _edge switch { MacDesktop.DockEdge.Left => HorizontalAlignment.Left, MacDesktop.DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        _art.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        _art.Margin = _edge switch
        {
            MacDesktop.DockEdge.Left => new Thickness(DockLayout.Padding, 0, inner - size, 0),
            MacDesktop.DockEdge.Right => new Thickness(inner - size, 0, DockLayout.Padding, 0),
            _ => new Thickness(0, inner - size, 0, DockLayout.Padding),
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

    /// <summary>Follows a drag by <paramref name="offset"/>; faded while it's far enough off the shelf to come off it.</summary>
    public void Lift(Vector offset, bool away)
    {
        _lift.X = offset.X;
        _lift.Y = offset.Y;
        _art.Opacity = away ? 0.4 : 1;
        Panel.SetZIndex(this, offset == default ? 0 : 1);
    }

    public void Click()
    {
        if (_dock is null)
            return;
        if (App is { } app)
            _dock.Controller.Click(_dock.Controller.Apps.FirstOrDefault(a => a.Key == app.Key) ?? app);
        else if (_clickMenu && _menu?.Invoke() is { Count: > 0 } items)
            _dock.OpenMenu(this, items);
        else
            _click?.Invoke();
    }
}
