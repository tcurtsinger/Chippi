// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Core.Modules;

namespace Chippi.UI.Controls;

/// <summary>18px tall, 4px radius, 11/600. Overlap is caution on a caution tint; every other kind is neutral (DESIGN.md §6).</summary>
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

    /// <summary>Screen readers read the badge's words ("Other drive"); its text lives inside the template, where they would not look.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new BadgeAutomationPeer(this);

    private sealed class BadgeAutomationPeer(Badge owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(Badge);

        protected override string GetNameCore() => owner.Text;

        // The template's TextBlock would repeat the name.
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}

public enum StatusKind
{
    /// <summary>Success: active, online, routing.</summary>
    Active,

    /// <summary>Caution: needs attention.</summary>
    Attention,

    /// <summary>Muted: offline.</summary>
    Offline,
}

/// <summary>A 7px dot in Success, Caution or Muted. No glow.</summary>
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
/// Icon chip (DESIGN.md §6): 32px, 6px radius, control fill, with a 16px glyph in the accent when
/// <see cref="IsOn"/> and Muted when off. Feature rows, the folder row and the Tidy card use it.
/// </summary>
public class IconChip : Control
{
    static IconChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IconChip), new FrameworkPropertyMetadata(typeof(IconChip)));
        FocusableProperty.OverrideMetadata(typeof(IconChip), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(IconChip), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(IconChip), new PropertyMetadata(false));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }
}
