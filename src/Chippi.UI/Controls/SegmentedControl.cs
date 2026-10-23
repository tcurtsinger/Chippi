// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.UI.Controls;

/// <summary>
/// A single-choice row of items. Arrow keys move the selection; Tab enters at the selected item.
/// The default style is the segmented trough sized to its labels (Settings);
/// <c>FlyoutSegmentedControlStyle</c> splits the width equally, <c>SelectorBarStyle</c> is the text
/// tabs under a page title and <c>FlyoutTabsStyle</c> the flyout's icon tabs. Items can be strings
/// or <see cref="SegmentItem"/>.
/// </summary>
public class SegmentedControl : ListBox
{
    static SegmentedControl() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedControl), new FrameworkPropertyMetadata(typeof(SegmentedControl)));

    public SegmentedControl() => SelectionMode = SelectionMode.Single;
}

/// <summary>An item with an optional icon (flyout tabs, nav items) and a value to bind against.</summary>
public sealed class SegmentItem
{
    public string Label { get; init; } = "";

    public Geometry? Icon { get; init; }

    public object? Value { get; init; }

    public override string ToString() => Label;
}
