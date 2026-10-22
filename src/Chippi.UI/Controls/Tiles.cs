// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.Core.Modules;

namespace Chippi.UI.Controls;

/// <summary>
/// Flyout tool tile (DESIGN.md §6): 92px, 12px radius, 10px padding, 30px icon chip over the name
/// and its state line. On: violet border at 50%, 16% gradient wash, full-gradient chip.
/// A toggle button, so Space and Enter work and Narrator reads the on/off state.
/// </summary>
public class ToolTile : ToggleButton
{
    static ToolTile() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ToolTile), new FrameworkPropertyMetadata(typeof(ToolTile)));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ToolTile), new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty StateLineProperty = DependencyProperty.Register(
        nameof(StateLine), typeof(string), typeof(ToolTile), new PropertyMetadata(""));

    public string StateLine
    {
        get => (string)GetValue(StateLineProperty);
        set => SetValue(StateLineProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ToolTile), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}

/// <summary>
/// Features hub card (DESIGN.md §6): at least 128px, 14px radius, 13px padding; 34px chip
/// (gradient when installed) with the install switch top-right; name, description, badges.
/// Installed: Surface with a violet 40% border. Uninstalled: Panel with a 5% hairline.
/// Coming soon: the switch is disabled and a "Coming soon" badge is added.
/// </summary>
public class FeatureCard : Control
{
    static FeatureCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FeatureCard), new FrameworkPropertyMetadata(typeof(FeatureCard)));
        FocusableProperty.OverrideMetadata(typeof(FeatureCard), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty FeatureNameProperty = DependencyProperty.Register(
        nameof(FeatureName), typeof(string), typeof(FeatureCard), new PropertyMetadata(""));

    public string FeatureName
    {
        get => (string)GetValue(FeatureNameProperty);
        set => SetValue(FeatureNameProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(FeatureCard), new PropertyMetadata(""));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(FeatureCard), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty BadgesProperty = DependencyProperty.Register(
        nameof(Badges), typeof(IEnumerable<FeatureBadge>), typeof(FeatureCard), new PropertyMetadata(null));

    public IEnumerable<FeatureBadge>? Badges
    {
        get => (IEnumerable<FeatureBadge>?)GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
    }

    public static readonly DependencyProperty IsInstalledProperty = DependencyProperty.Register(
        nameof(IsInstalled), typeof(bool), typeof(FeatureCard),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsInstalled
    {
        get => (bool)GetValue(IsInstalledProperty);
        set => SetValue(IsInstalledProperty, value);
    }

    public static readonly DependencyProperty IsComingSoonProperty = DependencyProperty.Register(
        nameof(IsComingSoon), typeof(bool), typeof(FeatureCard), new PropertyMetadata(false));

    public bool IsComingSoon
    {
        get => (bool)GetValue(IsComingSoonProperty);
        set => SetValue(IsComingSoonProperty, value);
    }
}
