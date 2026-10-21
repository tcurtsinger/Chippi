// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.App.Controls;

/// <summary>
/// Input-colored trough (11px radius, 4px padding, 4px gaps) of equal-width segments. Arrow keys
/// move the selection; Tab enters at the selected segment. The default style is the 36px text
/// segment (presets, folder modes); <c>TabSegmentedControlStyle</c> is the flyout's 46px
/// icon-over-label tabs. Items can be strings or <see cref="SegmentItem"/>.
/// </summary>
public class SegmentedControl : ListBox
{
    static SegmentedControl() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedControl), new FrameworkPropertyMetadata(typeof(SegmentedControl)));

    public SegmentedControl() => SelectionMode = SelectionMode.Single;
}

/// <summary>A segment with an optional icon (used by the flyout tabs) and a value to bind against.</summary>
public sealed class SegmentItem
{
    public string Label { get; init; } = "";

    public Geometry? Icon { get; init; }

    public object? Value { get; init; }

    public override string ToString() => Label;
}
