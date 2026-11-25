// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>Where the menu bar's two groups go, and how many of the app's menus fit before the » menu takes the rest.</summary>
/// <param name="LeftStart">The left group's left edge, DIPs from the bar's left.</param>
/// <param name="MenusShown">How many of the app's menus show; the rest go into ».</param>
/// <param name="RightEnd">The right group's right edge, DIPs from the bar's left.</param>
public readonly record struct BarArrangement(double LeftStart, int MenusShown, double RightEnd)
{
    public bool Overflows(int menus) => MenusShown < menus;
}

/// <summary>
/// The menu bar's items keep clear of the notch (docs/mac-style/brief.md, Placement): the app's menus collapse into » before
/// they'd touch it; a notch over the right group moves that group to its left; a notch at the far left starts the left group
/// after it. Pure, so it's tested without windows. DIPs throughout.
/// </summary>
public static class MenuBarLayout
{
    /// <summary>The bar's padding at each end.</summary>
    public const double Pad = 8;

    /// <summary>The room kept on each side of the notch.</summary>
    public const double NotchClearance = 10;

    /// <summary>The space kept between the left group and the right one when they meet.</summary>
    public const double GroupGap = 12;

    /// <summary>A notch closer than this to the left edge has the left group start after it.</summary>
    public const double LeftRoom = 140;

    /// <param name="width">The bar's width.</param>
    /// <param name="fixedLeft">The left group's width without the app's menus and »: the mark, the app's name and Window.</param>
    /// <param name="menus">Each of the app's menu titles' width, in order.</param>
    /// <param name="overflow">The » title's width.</param>
    /// <param name="right">The right group's width.</param>
    /// <param name="notch">The notch's left and right edges on this bar, or null when it isn't on it.</param>
    public static BarArrangement Arrange(double width, double fixedLeft, IReadOnlyList<double> menus, double overflow, double right,
        (double Left, double Right)? notch)
    {
        double leftStart = Pad, rightEnd = width - Pad, limit;
        if (notch is { } n)
        {
            double before = n.Left - NotchClearance, after = n.Right + NotchClearance;
            if (after > width - Pad - right && n.Left > Pad + LeftRoom)
            {
                // Over the right group: it moves to the notch's left, and the left group ends before it.
                rightEnd = before;
                limit = rightEnd - right - GroupGap;
            }
            else if (before < Pad + LeftRoom)
            {
                leftStart = after;
                limit = width - Pad - right - GroupGap;
            }
            else
                limit = before;
        }
        else
            limit = width - Pad - right - GroupGap;

        int shown = menus.Count;
        while (shown > 0 && leftStart + LeftWidth(fixedLeft, menus, overflow, shown) > limit)
            shown--;
        return new BarArrangement(leftStart, shown, rightEnd);
    }

    /// <summary>The left group's width with the first <paramref name="shown"/> menus, and » when any are left out.</summary>
    public static double LeftWidth(double fixedLeft, IReadOnlyList<double> menus, double overflow, int shown)
    {
        double total = fixedLeft;
        for (int i = 0; i < shown; i++)
            total += menus[i];
        return shown < menus.Count ? total + overflow : total;
    }
}
