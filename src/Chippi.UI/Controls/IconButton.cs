// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.UI.Controls;

/// <summary>
/// Icon-only button: 32px square, 4px radius, transparent until hover (DESIGN.md §6).
/// Narrator needs a name, so set <c>AutomationProperties.Name</c>; when it is missing the
/// tooltip text is used, and a debug build logs the omission.
/// </summary>
public class IconButton : Button
{
    static IconButton() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IconButton), new FrameworkPropertyMetadata(typeof(IconButton)));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(IconButton), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(IconButton), new PropertyMetadata(16.0));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += (_, _) => AccessibleName.Ensure(this);
    }
}

internal static class AccessibleName
{
    public static void Ensure(FrameworkElement element)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)))
            return;
        if (element.ToolTip is string tip && !string.IsNullOrWhiteSpace(tip))
        {
            AutomationProperties.SetName(element, tip);
            return;
        }
        System.Diagnostics.Debug.WriteLine($"Accessibility: icon button '{element.Name}' has no AutomationProperties.Name.");
    }
}
