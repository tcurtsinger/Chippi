// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Chippi.UI.Controls;

/// <summary>
/// Renders a <see cref="Geometry"/> as a stroke icon. The geometry is authored in a 24x24 box
/// (see <see cref="LucideIcons"/>); a Viewbox scales everything, stroke included, so icons keep
/// their weight at any size. Ported from Hopper. Unless <see cref="Thickness"/> is set, the stroke
/// is 30 / Size in the 24px grid, so every size draws the same 1.25px line.
/// </summary>
public sealed class LucideIcon : Viewbox
{
    private readonly Path _path;

    public LucideIcon()
    {
        _path = new Path
        {
            StrokeThickness = StrokeFor(16),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = Brushes.White,
        };
        var canvas = new Canvas { Width = 24, Height = 24, Background = null };
        canvas.Children.Add(_path);
        Child = canvas;
        Stretch = Stretch.Uniform;
        Width = 16;
        Height = 16;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(LucideIcon),
        new PropertyMetadata(null, (d, e) => ((LucideIcon)d)._path.Data = (Geometry?)e.NewValue));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(LucideIcon),
        new PropertyMetadata(Brushes.White, (d, _) => ((LucideIcon)d).Paint()));

    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(LucideIcon), new PropertyMetadata(false, (d, _) => ((LucideIcon)d).Paint()));

    /// <summary>Fills the shapes with <see cref="Brush"/> as well: Now Playing's solid transport glyphs.</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    private void Paint()
    {
        _path.Stroke = Brush;
        _path.Fill = Filled ? Brush : null;
    }

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LucideIcon),
        new PropertyMetadata(16.0, (d, e) =>
        {
            var icon = (LucideIcon)d;
            icon.Width = (double)e.NewValue;
            icon.Height = (double)e.NewValue;
            if (icon.ReadLocalValue(ThicknessProperty) == DependencyProperty.UnsetValue)
                icon._path.StrokeThickness = StrokeFor((double)e.NewValue);
        }));

    /// <summary>Drawn size in DIPs. DESIGN.md draws icons at 16px (20px for a Settings row lead glyph).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(LucideIcon),
        new PropertyMetadata(StrokeFor(16), (d, e) => ((LucideIcon)d)._path.StrokeThickness = (double)e.NewValue));

    /// <summary>Stroke width in the 24px design grid; unset, 30 / Size.</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>The stroke in the 24px grid that draws 1.25px at <paramref name="size"/>.</summary>
    public static double StrokeFor(double size) => size > 0 ? 30 / size : 1.875;
}
