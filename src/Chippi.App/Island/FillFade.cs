// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Chippi.App.Island;

/// <summary>
/// A control's fill easing from one state to the next over 83ms, the time Windows 11's own controls take
/// (DESIGN.md §5 Island, Motion). In a template, set <c>FillFade.To</c> on a Border, a Panel or a Shape where it
/// would set the Background or the Fill, and a trigger that changes it blends the old fill into the new; solid
/// and gradient fills both blend. <c>FillFade.Opacity</c> eases the element's opacity the same way. Under reduced
/// motion the fades stay: they change no place or size. A fill set while the element isn't on screen applies at once.
/// </summary>
internal static class FillFade
{
    internal static readonly TimeSpan Time = TimeSpan.FromMilliseconds(83);

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.RegisterAttached("To", typeof(Brush), typeof(FillFade), new PropertyMetadata(null, OnToChanged));

    public static Brush? GetTo(DependencyObject element) => (Brush?)element.GetValue(ToProperty);

    public static void SetTo(DependencyObject element, Brush? value) => element.SetValue(ToProperty, value);

    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.RegisterAttached("Opacity", typeof(double), typeof(FillFade), new PropertyMetadata(1.0, OnOpacityChanged));

    public static double GetOpacity(DependencyObject element) => (double)element.GetValue(OpacityProperty);

    public static void SetOpacity(DependencyObject element, double value) => element.SetValue(OpacityProperty, value);

    private static void OnToChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var property = d switch
        {
            Border => Border.BackgroundProperty,
            Panel => Panel.BackgroundProperty,
            Shape => Shape.FillProperty,
            _ => null,
        };
        if (property is not null && d is FrameworkElement element)
            Blend(element, property, (Brush?)e.OldValue, (Brush?)e.NewValue);
    }

    private static void OnOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        double to = (double)e.NewValue;
        if (!OnScreen(element))
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, Time), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Shows <paramref name="to"/> in <paramref name="property"/>, blended in from <paramref name="from"/> over 83ms
    /// while the element is on screen. <paramref name="settle"/> sets the final value (by default, <paramref name="to"/>
    /// itself), so a caller can put back a resource reference that follows the theme.
    /// </summary>
    internal static void Blend(FrameworkElement element, DependencyProperty property, Brush? from, Brush? to, Action? settle = null)
    {
        settle ??= () => element.SetValue(property, to);
        if (from is null || to is null || ReferenceEquals(from, to) || !OnScreen(element))
        {
            settle();
            return;
        }
        // Both fills on one unit square the brush stretches over the element; the new one fades in over the old.
        var square = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var incoming = new DrawingGroup { Opacity = 0, Children = { new GeometryDrawing(to, null, square) } };
        var blend = new DrawingBrush(new DrawingGroup { Children = { new GeometryDrawing(from, null, square), incoming } }) { Stretch = Stretch.Fill };
        element.SetValue(property, blend);
        var fade = new DoubleAnimation(1, Time);
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(element.GetValue(property), blend))
                settle();
        };
        incoming.BeginAnimation(DrawingGroup.OpacityProperty, fade);
    }

    private static bool OnScreen(FrameworkElement element) => element.IsLoaded && element.IsVisible;
}
