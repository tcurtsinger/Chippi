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

    /// <summary>
    /// Where a gradient fill falls when one fill runs across several controls (the home's split button: the toggle
    /// and its arrow share one accent fill). The transform maps the whole's unit square into this element's. It's
    /// inherited, so set it on the control and its template's fill follows. Solid fills ignore it.
    /// </summary>
    public static readonly DependencyProperty SpanProperty =
        DependencyProperty.RegisterAttached("Span", typeof(Transform), typeof(FillFade),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnSpanChanged));

    public static Transform? GetSpan(DependencyObject element) => (Transform?)element.GetValue(SpanProperty);

    public static void SetSpan(DependencyObject element, Transform? value) => element.SetValue(SpanProperty, value);

    private static DependencyProperty? FillOf(DependencyObject d) => d switch
    {
        Border => Border.BackgroundProperty,
        Panel => Panel.BackgroundProperty,
        Shape => Shape.FillProperty,
        _ => null,
    };

    private static void OnToChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (FillOf(d) is { } property && d is FrameworkElement element)
            Blend(element, property, Spanned(d, (Brush?)e.OldValue), Spanned(d, (Brush?)e.NewValue));
    }

    // The span is known once the controls are laid out: the fill they show already moves to it, without a fade.
    private static void OnSpanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (FillOf(d) is { } property && GetTo(d) is { } to)
            d.SetValue(property, Spanned(d, to));
    }

    private static Brush? Spanned(DependencyObject element, Brush? brush)
    {
        if (brush is not GradientBrush gradient || GetSpan(element) is not { } span)
            return brush;
        var spanned = gradient.Clone();
        spanned.RelativeTransform = span;
        if (spanned.CanFreeze)
            spanned.Freeze();
        return spanned;
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
        // Both fills on one unit square the brush stretches over the element: the old one fades out as the new one fades
        // in, so a hover wash going back to a clear fill fades away too, and two washes never stack.
        var square = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var outgoing = new DrawingGroup { Children = { new GeometryDrawing(from, null, square) } };
        var incoming = new DrawingGroup { Opacity = 0, Children = { new GeometryDrawing(to, null, square) } };
        var blend = new DrawingBrush(new DrawingGroup { Children = { outgoing, incoming } }) { Stretch = Stretch.Fill };
        element.SetValue(property, blend);
        var fadeIn = new DoubleAnimation(1, Time);
        fadeIn.Completed += (_, _) =>
        {
            if (ReferenceEquals(element.GetValue(property), blend))
                settle();
        };
        outgoing.BeginAnimation(DrawingGroup.OpacityProperty, new DoubleAnimation(0, Time));
        incoming.BeginAnimation(DrawingGroup.OpacityProperty, fadeIn);
    }

    private static bool OnScreen(FrameworkElement element) => element.IsLoaded && element.IsVisible;
}
