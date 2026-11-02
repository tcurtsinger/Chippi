// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Chippi.Core.Config;
using Chippi.Features.RadialMenu.Pc;
using Chippi.UI.Themes;
using static Chippi.Features.RadialMenu.Wheel.WheelGeometry;

namespace Chippi.Features.RadialMenu.Wheel;

/// <summary>
/// The wheel at the pointer: a topmost window that never takes focus and lets clicks through (the hook hears them), sized
/// to the wheels it shows, placed in physical pixels on the screen the wheel opened on. Kept between openings, hidden.
/// </summary>
internal sealed class WheelWindow : Window
{
    /// <summary>Room around a wheel for the panel's shadow (40 blur, 16 down).</summary>
    private const double ShadowRoom = 64;

    public static readonly TimeSpan GrowIn = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan FadeOnClose = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan FadeOnPick = TimeSpan.FromMilliseconds(150);

    private readonly Canvas _stage = new();
    private readonly ScaleTransform _grow = new(1, 1);
    private Screen _screen;
    private Rect _bounds;
    private nint _handle;
    private int _fade; // a fade's number, so a fade that ends after the next opening doesn't hide it

    public WheelWindow(PetColor color)
    {
        Title = "Radial menu";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _stage;
        View = new WheelView(floating: true, color) { RenderTransform = _grow, RenderTransformOrigin = new Point(0.5, 0.5) };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            // WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, WS_EX_TRANSPARENT: out of Alt+Tab, never in front, clicks go through.
            const nint styles = 0x80 | 0x08000000 | 0x20;
            Native.SetWindowLongPtr(_handle, -20, Native.GetWindowLongPtr(_handle, -20) | styles);
            Place();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    /// <summary>The top wheel.</summary>
    public WheelView View { get; }

    /// <summary>A window with its handle made and its wheel laid out, hidden, so the first opening only shows it.</summary>
    public static WheelWindow Made(PetColor color)
    {
        var window = new WheelWindow(color);
        new WindowInteropHelper(window).EnsureHandle();
        window.View.Show([], 0);
        window.View.Measure(new Size(Diameter, Diameter));
        window.View.Arrange(new Rect(0, 0, Diameter, Diameter));
        return window;
    }

    /// <summary>Shows the wheels at <paramref name="levels"/> on <paramref name="screen"/>, the top one growing in.</summary>
    public void Present(Screen screen, IReadOnlyList<WheelLevel> levels, Func<WheelLevel, Geometry> crumbGlyph)
    {
        _screen = screen;
        _fade++;
        Arrange(levels, crumbGlyph);
        BeginAnimation(OpacityProperty, null);
        _stage.BeginAnimation(OpacityProperty, null);
        _stage.Opacity = 1;
        if (!IsVisible)
            Show();
        Place();
        GrowTop();
    }

    /// <summary>A submenu opened or closed: the wheels again, the new top one growing in.</summary>
    public void Rearrange(IReadOnlyList<WheelLevel> levels, Func<WheelLevel, Geometry> crumbGlyph)
    {
        Arrange(levels, crumbGlyph);
        Place();
        GrowTop();
    }

    /// <summary>Fades out over <paramref name="time"/> (at once for zero), then hides; <paramref name="done"/> after.</summary>
    public void FadeOut(TimeSpan time, Action? done = null)
    {
        int fade = ++_fade;
        if (time <= TimeSpan.Zero)
        {
            Hide();
            done?.Invoke();
            return;
        }
        var animation = new DoubleAnimation(0, time);
        animation.Completed += (_, _) =>
        {
            if (fade == _fade)
                Hide();
            done?.Invoke();
        };
        _stage.BeginAnimation(OpacityProperty, animation);
    }

    private void GrowTop()
    {
        // From 90% as it fades in, over 140ms ease-out; under reduced motion it only fades.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        View.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, GrowIn) { EasingFunction = ease });
        if (Motion.IsReduced)
        {
            _grow.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _grow.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _grow.ScaleX = _grow.ScaleY = 1;
            return;
        }
        _grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, GrowIn) { EasingFunction = ease });
        _grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, GrowIn) { EasingFunction = ease });
    }

    /// <summary>Lays the top wheel, the crumbs it left and the hairlines joining them inside the smallest box that holds them.</summary>
    private void Arrange(IReadOnlyList<WheelLevel> levels, Func<WheelLevel, Geometry> crumbGlyph)
    {
        var top = levels[^1];
        var bounds = new Rect(top.Middle.X - Middle - ShadowRoom, top.Middle.Y - Middle - ShadowRoom, Diameter + 2 * ShadowRoom, Diameter + 2 * ShadowRoom);
        foreach (var level in levels.Take(levels.Count - 1))
            bounds.Union(new Rect(level.Middle.X - CrumbSize / 2 - 24, level.Middle.Y - CrumbSize / 2 - 24, CrumbSize + 48, CrumbSize + 48));
        bounds.Intersect(new Rect(0, 0, _screen.Width / _screen.Scale, _screen.Height / _screen.Scale));
        _bounds = bounds;
        _stage.Children.Clear();
        _stage.Width = bounds.Width;
        _stage.Height = bounds.Height;
        var origin = (Vector)bounds.TopLeft;
        for (int i = 0; i < levels.Count - 1; i++)
        {
            var from = levels[i].Middle - origin;
            var to = levels[i + 1].Middle - origin;
            var line = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "IslandMutedFillBrush");
            _stage.Children.Add(line);
        }
        for (int i = 0; i < levels.Count - 1; i++)
        {
            var crumb = new WheelCrumb(crumbGlyph(levels[i + 1]));
            Canvas.SetLeft(crumb, levels[i].Middle.X - origin.X - CrumbSize / 2);
            Canvas.SetTop(crumb, levels[i].Middle.Y - origin.Y - CrumbSize / 2);
            _stage.Children.Add(crumb);
        }
        Canvas.SetLeft(View, top.Middle.X - origin.X - Middle);
        Canvas.SetTop(View, top.Middle.Y - origin.Y - Middle);
        _stage.Children.Add(View);
    }

    // In physical pixels: the box's place on its screen times the screen's scale.
    private void Place()
    {
        if (_handle == 0 || _bounds.IsEmpty)
            return;
        int x = _screen.Left + (int)Math.Round(_bounds.X * _screen.Scale), y = _screen.Top + (int)Math.Round(_bounds.Y * _screen.Scale);
        int width = (int)Math.Ceiling(_bounds.Width * _screen.Scale), height = (int)Math.Ceiling(_bounds.Height * _screen.Scale);
        // HWND_TOPMOST; SWP_NOACTIVATE
        Native.SetWindowPos(_handle, -1, x, y, width, height, 0x0010);
    }
}
