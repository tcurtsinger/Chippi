// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Chippi.UI.Controls;

/// <summary>
/// Windows' own UAC shield (blue and yellow quarters): it marks a verb that asks Windows for admin. Drawn, not read from the
/// system, so it renders the same in a test; it is a stock mark, not a theme color.
/// </summary>
public sealed class UacShield : Viewbox
{
    private static readonly Geometry Outline = Geometry.Parse("M8,0.8 L2,2.8 V7.8 C2,11 4.4,13.8 8,15.2 C11.6,13.8 14,11 14,7.8 V2.8 Z");
    private static readonly Brush Blue = Frozen(0x1B, 0x6F, 0xD0), Yellow = Frozen(0xF2, 0xC2, 0x1C), Cross = Frozen(0xD9, 0xFF, 0xFF, 0xFF);

    public UacShield(double size = 14)
    {
        Width = Height = size;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
        var canvas = new Canvas { Width = 16, Height = 16, Clip = Outline };
        canvas.Children.Add(Block(0, 0, 16, 16, Blue));
        canvas.Children.Add(Block(8, 0, 8, 8, Yellow));
        canvas.Children.Add(Block(0, 8, 8, 8, Yellow));
        canvas.Children.Add(new Path { Data = Geometry.Parse("M8,0 V16 M0,8 H16"), Stroke = Cross, StrokeThickness = 0.8 });
        Child = canvas;
    }

    private static Rectangle Block(double x, double y, double w, double h, Brush fill)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = fill };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private static Brush Frozen(byte r, byte g, byte b) => Frozen(0xFF, r, g, b);

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
