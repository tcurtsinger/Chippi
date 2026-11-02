// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.Features.RadialMenu.Slices;
using static Chippi.Features.RadialMenu.Wheel.WheelGeometry;

namespace Chippi.Features.RadialMenu.Wheel;

/// <summary>One wheel: the first, or a submenu's, which has Back first, toward the wheel it opened from.</summary>
public sealed class WheelLevel
{
    public WheelLevel(Point middle, IReadOnlyList<Slice> slices, double? back, Slice? submenu)
    {
        Middle = middle;
        Back = back;
        Submenu = submenu;
        Items = back is null ? [.. slices] : [null, .. slices];
    }

    public Point Middle { get; }

    /// <summary>The direction of the wheel before, where Back sits; null for the first wheel.</summary>
    public double? Back { get; }

    /// <summary>The slice that opened this wheel; null for the first.</summary>
    public Slice? Submenu { get; }

    /// <summary>The wheel's slices clockwise from its first, a submenu's Back (null) first.</summary>
    public IReadOnlyList<Slice?> Items { get; }

    /// <summary>The first slice's angle: the top, or a submenu's Back.</summary>
    public double First => Back ?? 0;

    public double Span => Items.Count == 0 ? 360 : 360.0 / Items.Count;

    public double AngleOf(int index) => Normalize(First + index * Span);
}

/// <summary>What the wheel does after an event.</summary>
public enum WheelOutcome
{
    None,

    /// <summary>Something shown changed: the pointed slice, or a submenu opened or closed.</summary>
    Changed,

    /// <summary>A slice was picked: it runs as the wheel fades.</summary>
    Run,

    /// <summary>The wheel closes without running anything.</summary>
    Close,
}

/// <summary>How the hub's line reads.</summary>
public enum HubTone { Name, Quiet, Caution }

/// <summary>
/// The open wheel (docs/phase-5.5/radial-menu.md, Picking), in device-independent pixels on its screen: which slice is
/// pointed at, the submenus open, and what a move, a let-go or a click does. Held, direction is what counts: anywhere past
/// the hub points at the slice that way. Clicking, only the ring does.
/// </summary>
public sealed class WheelState
{
    private readonly List<WheelLevel> _levels = [];
    private readonly Size _screen;
    private readonly Func<Slice, bool> _missing;

    /// <param name="screen">The screen's size; the wheel is kept whole on it.</param>
    /// <param name="missing">Whether a slice's app, file or Chippi command is gone.</param>
    public WheelState(Size screen, Point at, IReadOnlyList<Slice> slices, bool holding, Func<Slice, bool> missing)
    {
        _screen = screen;
        _missing = missing;
        Holding = holding;
        _levels.Add(new WheelLevel(Keep(at), slices, null, null));
    }

    public IReadOnlyList<WheelLevel> Levels => _levels;

    public WheelLevel Top => _levels[^1];

    /// <summary>The pointed slice on the top wheel, or null.</summary>
    public int? Pointed { get; private set; }

    /// <summary>The picked slice, once one is.</summary>
    public int? Picked { get; private set; }

    /// <summary>The trigger is still held: letting it go picks.</summary>
    public bool Holding { get; private set; }

    public bool IsMissing(Slice? slice) => slice is not null && slice.Kind != SliceKind.Submenu && _missing(slice);

    /// <summary>The hub's line: the pointed slice's name, "Not found", a submenu's name, "No slices", or nothing.</summary>
    public (string Text, HubTone Tone) Hub
    {
        get
        {
            var top = Top;
            if (top.Items.Count == 0)
                return ("No slices", HubTone.Quiet);
            if ((Picked ?? Pointed) is int shown)
            {
                var item = top.Items[shown];
                if (item is null)
                    return ("Back", HubTone.Name);
                if (IsMissing(item))
                    return ("Not found", HubTone.Caution);
                return (NameOf(item), HubTone.Name);
            }
            return top.Submenu is { } submenu ? (NameOf(submenu), HubTone.Quiet) : ("", HubTone.Quiet);
        }
    }

    /// <summary>Where Chippi looks: toward the pointed slice, else ahead.</summary>
    public Vector? Look => (Picked ?? Pointed) is int shown ? Direction(Top.AngleOf(shown)) : null;

    public static string NameOf(Slice slice) => slice.Name.Length > 0 ? slice.Name : SliceCatalog.NameFor(slice);

    public WheelOutcome Move(Point at)
    {
        if (Picked is not null)
            return WheelOutcome.None;
        var (index, distance) = Toward(at, Holding);
        // Held past the ring's edge by 32 on a submenu, it opens, so a flick arrives at its hub; on Back, the wheel before.
        if (Holding && index is int k && distance > SubmenuReach)
        {
            var item = Top.Items[k];
            if (item is null)
                return Leave(_levels.Count - 1);
            if (item.Kind == SliceKind.Submenu)
                return Enter(k);
        }
        if (index == Pointed)
            return WheelOutcome.None;
        Pointed = index;
        return WheelOutcome.Changed;
    }

    /// <summary>The trigger was let go: the slice it points at runs (or opens), or, in the hub, the wheel stays for a click.</summary>
    public WheelOutcome Release(Point at)
    {
        if (!Holding || Picked is not null)
            return WheelOutcome.None;
        Holding = false;
        var (index, _) = Toward(at, holding: true);
        if (index is not int k)
        {
            Pointed = null;
            return WheelOutcome.Changed;
        }
        return Choose(k);
    }

    /// <summary>A click while the wheel waits: on a slice, a crumb (back to its wheel), the hub (nothing), or off the wheel (closes).</summary>
    public WheelOutcome Click(Point at)
    {
        if (Holding || Picked is not null)
            return WheelOutcome.None;
        for (int i = 0; i < _levels.Count - 1; i++)
        {
            if ((at - _levels[i].Middle).Length <= CrumbSize / 2)
                return Leave(i + 1);
        }
        double distance = (at - Top.Middle).Length;
        if (distance < HubRadius)
            return WheelOutcome.None;
        var (index, _) = Toward(at, holding: false);
        return index is int k ? Choose(k) : WheelOutcome.Close;
    }

    /// <summary>The picked slice, once <see cref="WheelOutcome.Run"/> says one was.</summary>
    public Slice? PickedSlice => Picked is int k ? Top.Items[k] : null;

    private WheelOutcome Choose(int index)
    {
        var item = Top.Items[index];
        if (item is null)
            return Leave(_levels.Count - 1);
        if (item.Kind == SliceKind.Submenu)
            return Enter(index);
        Pointed = index;
        Picked = index;
        // A slice whose target is gone runs nothing.
        return IsMissing(item) ? WheelOutcome.Close : WheelOutcome.Run;
    }

    private (int? Index, double Distance) Toward(Point at, bool holding)
    {
        var offset = at - Top.Middle;
        double distance = offset.Length;
        if (Top.Items.Count == 0 || distance < HubRadius || (!holding && distance > RingOuter))
            return (null, distance);
        return (SliceToward(offset, Top.Items.Count, Top.First), distance);
    }

    /// <summary>A submenu's wheel opens just past this one in its slice's direction; this one shrinks to a crumb.</summary>
    private WheelOutcome Enter(int index)
    {
        var submenu = Top.Items[index]!;
        var middle = Keep(At(Top.Middle, SubmenuReach, Top.AngleOf(index)));
        double back = AngleOf(Top.Middle - middle);
        _levels.Add(new WheelLevel(middle, submenu.Slices, back, submenu));
        Pointed = null;
        return WheelOutcome.Changed;
    }

    private WheelOutcome Leave(int depth)
    {
        if (depth < 1 || depth >= _levels.Count)
            return WheelOutcome.None;
        _levels.RemoveRange(depth, _levels.Count - depth);
        Pointed = null;
        return WheelOutcome.Changed;
    }

    /// <summary>Near an edge the wheel shifts in, whole on its screen.</summary>
    private Point Keep(Point at)
    {
        const double inset = RingOuter + EdgeMargin;
        return new Point(
            _screen.Width < 2 * inset ? _screen.Width / 2 : Math.Clamp(at.X, inset, _screen.Width - inset),
            _screen.Height < 2 * inset ? _screen.Height / 2 : Math.Clamp(at.Y, inset, _screen.Height - inset));
    }
}
