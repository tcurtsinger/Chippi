// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.Features.Clipboard.Views;

/// <summary>A display-only link, at most three lines, retaining both ends. The source is never shortened.</summary>
internal sealed class MiddleTrimmedText : ContentControl
{
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 16 };
    private const double LineHeight = 16;
    public static readonly DependencyProperty FullTextProperty = DependencyProperty.Register(nameof(FullText), typeof(string), typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((MiddleTrimmedText)d)._text.Text = (string)e.NewValue));
    public string FullText { get => (string)GetValue(FullTextProperty); set => SetValue(FullTextProperty, value); }

    public MiddleTrimmedText()
    {
        FontSize = 12; MaxHeight = 48; HorizontalContentAlignment = HorizontalAlignment.Stretch; Content = _text;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        string display = Fit(FullText, constraint.Width);
        if (_text.Text != display) _text.Text = display;
        return base.MeasureOverride(constraint);
    }

    private string Fit(string text, double width)
    {
        if (!double.IsFinite(width) || width <= 0 || text.Length == 0) return text;
        bool Fits(string candidate) => new FormattedText(candidate, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = width, LineHeight = LineHeight }.Height <= LineHeight * 3 + 0.1;
        if (Fits(text)) return text;
        string Middle(int keep) => text[..((keep + 1) / 2)] + "\u2026" + (keep / 2 == 0 ? "" : text[^(keep / 2)..]);
        int low = 0, high = text.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (Fits(Middle(mid))) low = mid; else high = mid - 1;
        }
        return Middle(low);
    }
}
