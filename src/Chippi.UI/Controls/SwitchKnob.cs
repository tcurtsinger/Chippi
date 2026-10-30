// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Chippi.UI.Controls;

/// <summary>
/// Places a switch's knob (SwitchTrackTemplate). The first value a knob gets is where the switch
/// already is, so it is drawn there at once; only a later change slides it, over 150ms. A trigger's
/// EnterActions can't tell the two apart, so a list that rebuilds its switches made every On knob
/// slide in from Off.
/// </summary>
public static class SwitchKnob
{
    /// <summary>How far the knob travels from Off to On on the 40 × 20 switch.</summary>
    public const double Travel = 19;

    /// <summary>The travel on a switch of another size (the island's is 34 × 18). Set it before IsOn.</summary>
    public static readonly DependencyProperty DistanceProperty = DependencyProperty.RegisterAttached(
        "Distance", typeof(double), typeof(SwitchKnob), new PropertyMetadata(Travel));

    public static double GetDistance(DependencyObject element) => (double)element.GetValue(DistanceProperty);

    public static void SetDistance(DependencyObject element, double value) => element.SetValue(DistanceProperty, value);

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.RegisterAttached(
        "IsOn", typeof(bool?), typeof(SwitchKnob), new PropertyMetadata(null, OnIsOnChanged));

    public static bool? GetIsOn(DependencyObject element) => (bool?)element.GetValue(IsOnProperty);

    public static void SetIsOn(DependencyObject element, bool? value) => element.SetValue(IsOnProperty, value);

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement knob)
            return;
        // The knob's offset is made here, not in XAML: the template binds this before it would set a RenderTransform.
        if (knob.RenderTransform is not TranslateTransform offset)
            knob.RenderTransform = offset = new TranslateTransform();
        double to = e.NewValue as bool? == true ? GetDistance(knob) : 0;
        if (e.OldValue is null)
        {
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            offset.X = to; // where the switch already is: no slide
            return;
        }
        var slide = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        offset.BeginAnimation(TranslateTransform.XProperty, slide);
    }
}
