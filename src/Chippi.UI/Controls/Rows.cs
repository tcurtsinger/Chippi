// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.UI.Controls;

/// <summary>
/// A card (DESIGN.md §6): one rounded panel per section, its rows stacked inside with a 1px divider
/// between visible rows, and the rows clipped to the corners. Never put a card inside a card. The
/// default style is a Settings card; <c>FlyoutCardStyle</c> is the flyout's, and <c>CardRowsStyle</c>
/// draws only the dividers (an expander's sub-rows).
/// </summary>
public class Card : Panel
{
    private readonly List<double> _dividers = [];

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(Card), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public static readonly DependencyProperty DividerBrushProperty = DependencyProperty.Register(
        nameof(DividerBrush), typeof(Brush), typeof(Card), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? DividerBrush
    {
        get => (Brush?)GetValue(DividerBrushProperty);
        set => SetValue(DividerBrushProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(Card), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private double Stroke => BorderBrush is null ? 0 : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        double inset = 2 * Stroke;
        var inner = new Size(Math.Max(0, availableSize.Width - inset), double.PositiveInfinity);
        double width = 0, height = 0;
        bool first = true;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(inner);
            if (child.Visibility == Visibility.Collapsed)
                continue;
            height += child.DesiredSize.Height + (first ? 0 : 1);
            width = Math.Max(width, child.DesiredSize.Width);
            first = false;
        }
        return new Size(width + inset, height + inset);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double stroke = Stroke, y = stroke, width = Math.Max(0, finalSize.Width - 2 * stroke);
        _dividers.Clear();
        bool first = true;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;
            if (!first)
            {
                _dividers.Add(y);
                y += 1;
            }
            child.Arrange(new Rect(stroke, y, width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
            first = false;
        }
        Clip = new RectangleGeometry(new Rect(finalSize), CornerRadius, CornerRadius);
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double stroke = Stroke;
        var bounds = new Rect(RenderSize);
        if (Background is not null || BorderBrush is not null)
        {
            var edge = new Rect(stroke / 2, stroke / 2, Math.Max(0, bounds.Width - stroke), Math.Max(0, bounds.Height - stroke));
            dc.DrawRoundedRectangle(Background, BorderBrush is null ? null : new Pen(BorderBrush, stroke), edge, CornerRadius, CornerRadius);
        }
        if (DividerBrush is not null)
            foreach (double y in _dividers)
                dc.DrawRectangle(DividerBrush, null, new Rect(stroke, y, Math.Max(0, bounds.Width - 2 * stroke), 1));
    }
}

/// <summary>
/// A Settings row (DESIGN.md §5): an optional 20px lead glyph, the label over an optional caption,
/// and the control (the content) on the right. At least 60px with a caption, 48px without. The
/// control is named for the row and described by the caption unless it has a name of its own.
/// </summary>
public class SettingRow : ContentControl
{
    static SettingRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));
        FocusableProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingRow), new PropertyMetadata("", (d, _) => ((SettingRow)d).NameContent()));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata("", (d, _) => ((SettingRow)d).NameContent()));

    /// <summary>One short line under the label.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(SettingRow), new PropertyMetadata(null));

    /// <summary>The lead glyph, drawn in <c>Foreground</c> (Text by default; set the accent for a feature that is on).</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private readonly HashSet<DependencyObject> _named = [];

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        NameContent();
    }

    private void NameContent()
    {
        if (Content is not DependencyObject control)
            return;
        // Name a control only if it has no name of its own, or still has the one this row gave it.
        if (_named.Contains(control) || string.IsNullOrEmpty(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, Header);
            AutomationProperties.SetHelpText(control, Description);
            _named.Add(control);
        }
    }
}

/// <summary>
/// An expander row (DESIGN.md §6): a row that is a button, with the label over a caption, an
/// optional value and a chevron down (closed) or up (open). Its content, usually a
/// <see cref="Card"/> in <c>CardRowsStyle</c> holding <see cref="SettingRow"/>s, opens in the same
/// card under a divider, with the sub-rows indented 50px.
/// </summary>
public class ExpanderRow : Expander
{
    static ExpanderRow() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ExpanderRow), new FrameworkPropertyMetadata(typeof(ExpanderRow)));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ExpanderRow), new PropertyMetadata(""));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ExpanderRow), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(ExpanderRow), new PropertyMetadata(null));

    /// <summary>Shown left of the chevron: a summary string ("Silent") or a control.</summary>
    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
