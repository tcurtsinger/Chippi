// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// A top-down list that shows whole rows only (refresh brief Part A #4): a row that doesn't fully fit is
/// left out, never cut off, and nothing scrolls. <see cref="Keep"/> stays shown by leaving out rows above
/// it instead. A heading shows only with the row under it.
/// </summary>
internal sealed class WholeRows : Panel
{
    public static readonly DependencyProperty IsHeadingProperty = DependencyProperty.RegisterAttached(
        "IsHeading", typeof(bool), typeof(WholeRows), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetIsHeading(UIElement row, bool heading) => row.SetValue(IsHeadingProperty, heading);

    public static bool GetIsHeading(UIElement row) => (bool)row.GetValue(IsHeadingProperty);

    private readonly HashSet<UIElement> _shown = [];

    public WholeRows() => ClipToBounds = true;

    /// <summary>The row that must stay shown, such as the selected entry; null for the rows from the top.</summary>
    public UIElement? Keep
    {
        get => _keep;
        set { _keep = value; InvalidateMeasure(); }
    }

    private UIElement? _keep;

    /// <summary>Whether <paramref name="row"/> is laid out whole; false for a row left out.</summary>
    public bool Shows(UIElement row) => _shown.Contains(row);

    protected override Size MeasureOverride(Size available)
    {
        var rows = InternalChildren.Cast<UIElement>().Where(r => r.Visibility != Visibility.Collapsed).ToList();
        foreach (var row in rows)
            row.Measure(new Size(available.Width, double.PositiveInfinity));
        int keep = _keep is null ? -1 : rows.IndexOf(_keep);
        int first = 0;
        // Leave out rows from the top until the kept row fits.
        while (keep > first && Span(rows, first, keep) > available.Height)
            first++;
        _shown.Clear();
        double used = 0;
        for (int i = first; i < rows.Count && used + rows[i].DesiredSize.Height <= available.Height; i++)
        {
            used += rows[i].DesiredSize.Height;
            _shown.Add(rows[i]);
        }
        // A heading never shows alone at the bottom.
        while (_shown.Count > 0 && rows.Last(_shown.Contains) is var last && GetIsHeading(last))
        {
            _shown.Remove(last);
            used -= last.DesiredSize.Height;
        }
        return new Size(double.IsInfinity(available.Width) ? rows.Select(r => r.DesiredSize.Width).DefaultIfEmpty(0).Max() : available.Width, used);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        foreach (UIElement row in InternalChildren)
        {
            if (!_shown.Contains(row))
            {
                row.Arrange(new Rect(0, final.Height + 10_000, final.Width, row.DesiredSize.Height)); // out of the clip
                continue;
            }
            row.Arrange(new Rect(0, y, final.Width, row.DesiredSize.Height));
            y += row.DesiredSize.Height;
        }
        return final;
    }

    private static double Span(List<UIElement> rows, int from, int through)
    {
        double height = 0;
        for (int i = from; i <= through; i++)
            height += rows[i].DesiredSize.Height;
        return height;
    }
}
