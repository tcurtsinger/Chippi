// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Chippi.UI.Controls;

/// <summary>
/// The grip on a reorderable row (the rules editor). It is a <see cref="Thumb"/>, so the host list
/// reads DragStarted / DragDelta / DragCompleted for mouse reordering. It also takes keyboard
/// focus: Up and Down raise <see cref="MoveRequested"/> with -1 or +1, so reordering never needs a
/// mouse. Look is provisional until the Phase 2 mockups are approved.
/// </summary>
public class DragHandle : Thumb
{
    static DragHandle()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DragHandle), new FrameworkPropertyMetadata(typeof(DragHandle)));
        FocusableProperty.OverrideMetadata(typeof(DragHandle), new FrameworkPropertyMetadata(true));
    }

    /// <summary>The user asked to move the row up (-1) or down (+1) from the keyboard.</summary>
    public event EventHandler<int>? MoveRequested;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        int delta = e.Key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (delta == 0)
            return;
        e.Handled = true;
        MoveRequested?.Invoke(this, delta);
    }
}
