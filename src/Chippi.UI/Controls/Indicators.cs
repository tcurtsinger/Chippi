// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.Core.Modules;

namespace Chippi.UI.Controls;

/// <summary>20px tall, 6px radius, 10.5/700. Source is violet, Overlap amber, Needs neutral (DESIGN.md §6).</summary>
public class Badge : Control
{
    static Badge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(typeof(Badge)));
        FocusableProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Badge), new PropertyMetadata(""));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(BadgeKind), typeof(Badge), new PropertyMetadata(BadgeKind.Needs));

    public BadgeKind Kind
    {
        get => (BadgeKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}

public enum StatusKind
{
    /// <summary>Signal Green: active, online, routing.</summary>
    Active,

    /// <summary>Signal Amber: needs attention.</summary>
    Attention,

    /// <summary>Ink Muted: offline.</summary>
    Offline,
}

/// <summary>7px dot; green or amber get a soft glow, offline does not.</summary>
public class StatusDot : Control
{
    static StatusDot()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusDot), new FrameworkPropertyMetadata(typeof(StatusDot)));
        FocusableProperty.OverrideMetadata(typeof(StatusDot), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(StatusKind), typeof(StatusDot), new PropertyMetadata(StatusKind.Active));

    public StatusKind Kind
    {
        get => (StatusKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}

/// <summary>
/// Label role (DESIGN.md §3): 10.5/700, UPPERCASE, +0.13em tracking. WPF's TextBlock has no
/// letter-spacing, so this draws each character with the tracking added. Text is uppercased here,
/// so callers pass sentence case.
/// </summary>
public class Eyebrow : FrameworkElement
{
    private const double Tracking = 0.13;
    private List<FormattedText>? _glyphs;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Eyebrow),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Invalidate));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Eyebrow), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits, Invalidate));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(Eyebrow), new FrameworkPropertyMetadata(10.5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits, Invalidate));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var glyphs = Glyphs();
        if (glyphs.Count == 0)
            return new Size(0, Math.Round(FontSize * 1.2, 1));
        double width = glyphs.Sum(g => g.WidthIncludingTrailingWhitespace) + Tracking * FontSize * (glyphs.Count - 1);
        return new Size(Math.Ceiling(width), Math.Round(FontSize * 1.2, 1));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double x = 0;
        double lineHeight = FontSize * 1.2;
        foreach (var glyph in Glyphs())
        {
            dc.DrawText(glyph, new Point(x, (lineHeight - glyph.Height) / 2));
            x += glyph.WidthIncludingTrailingWhitespace + Tracking * FontSize;
        }
    }

    private static void Invalidate(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Eyebrow)d)._glyphs = null;

    private List<FormattedText> Glyphs()
    {
        if (_glyphs is not null)
            return _glyphs;
        var family = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _glyphs = [.. (Text ?? "").ToUpper(CultureInfo.CurrentCulture).Select(ch =>
            new FormattedText(ch.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground, ppd))];
        return _glyphs;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _glyphs = null;
        base.OnDpiChanged(oldDpi, newDpi);
    }
}
