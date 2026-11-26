// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>Where the menu bar's two groups go, and what gives way when they don't both fit.</summary>
/// <param name="LeftStart">The left group's left edge, DIPs from the bar's left.</param>
/// <param name="MenusShown">How many of the app's menus show; the rest go into ».</param>
/// <param name="RightEnd">The right group's right edge, DIPs from the bar's left.</param>
/// <param name="RightDropped">How many of the right group's items that can go are left out, counted in the order they go.</param>
/// <param name="NameTrim">How much narrower the app's name is drawn, DIPs; it ends in an ellipsis then.</param>
public readonly record struct BarArrangement(double LeftStart, int MenusShown, double RightEnd, int RightDropped = 0, double NameTrim = 0)
{
    public bool Overflows(int menus) => MenusShown < menus;
}

/// <summary>
/// The menu bar's items keep clear of the notch and of each other (docs/mac-style/brief.md, Placement): the app's menus
/// collapse into » before they'd touch the notch; a notch over the right group moves that group to its left; a notch at
/// the far left starts the left group after it. On a bar too narrow for all of it, the right group's lesser items go one
/// at a time, then the app's name is cut short; nothing is drawn over anything else. Pure, so it's tested without
/// windows. DIPs throughout.
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
    /// <param name="right">The right group's width, all of it.</param>
    /// <param name="notch">The notch's left and right edges on this bar, or null when it isn't on it.</param>
    /// <param name="rightOptional">The widths of the right group's items that can go, in the order they go; the clock never does.</param>
    /// <param name="nameSlack">How much of the app's name can be cut, within <paramref name="fixedLeft"/>.</param>
    public static BarArrangement Arrange(double width, double fixedLeft, IReadOnlyList<double> menus, double overflow, double right,
        (double Left, double Right)? notch, IReadOnlyList<double>? rightOptional = null, double nameSlack = 0)
    {
        var optional = rightOptional ?? [];
        double leftStart = Pad, rightEnd = width - Pad, limit = 0, kept = right;
        int dropped = 0;
        // The least the left group is with the app's name whole: its fixed titles, and » standing in for the menus. The
        // right group's items go before the name is cut.
        double least = fixedLeft + (menus.Count > 0 ? overflow : 0);

        void DropUntil(Func<bool> fits)
        {
            while (!fits() && dropped < optional.Count)
                kept -= optional[dropped++];
        }

        void ShareOneStretch()
        {
            DropUntil(() => leftStart + least + GroupGap + kept <= rightEnd);
            limit = rightEnd - kept - GroupGap;
        }

        if (notch is { } n)
        {
            double before = n.Left - NotchClearance, after = n.Right + NotchClearance;
            if (before < Pad + LeftRoom)
            {
                // At the far left: both groups share the stretch after it.
                leftStart = after;
                ShareOneStretch();
            }
            else if (after <= rightEnd - right)
                limit = before; // clear of the right group where it is: the left group ends before the notch
            else if (leftStart + least + GroupGap + right <= before)
            {
                // Over the right group, with room for all of it on the notch's left.
                rightEnd = before;
                limit = rightEnd - right - GroupGap;
            }
            else
            {
                // A narrow bar: the right group stays on the right and gives up items until it's clear of the notch.
                DropUntil(() => after <= rightEnd - kept);
                if (after <= rightEnd - kept)
                    limit = before;
                else
                {
                    // Not even the clock fits there (the notch is at the far right): everything shares the notch's left.
                    (dropped, kept, rightEnd) = (0, right, before);
                    ShareOneStretch();
                }
            }
        }
        else
            ShareOneStretch();

        int shown = menus.Count;
        while (shown > 0 && leftStart + LeftWidth(fixedLeft, menus, overflow, shown) > limit)
            shown--;
        double over = leftStart + LeftWidth(fixedLeft, menus, overflow, shown) - limit;
        return new BarArrangement(leftStart, shown, rightEnd, dropped, Math.Clamp(over, 0, Math.Max(0, nameSlack)));
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
