// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.RadialMenu.Wheel;
using Chippi.UI.Controls;
using static Chippi.Features.RadialMenu.Wheel.WheelGeometry;

namespace Chippi.Features.RadialMenu.Views;

/// <summary>What a place on the editor's wheel holds.</summary>
internal readonly record struct EditorPlace(int? Slice, bool IsBack, bool IsPlus);

/// <summary>
/// The wheel as Settings › Radial menu edits it (docs/phase-5.5/radial-menu.md, Settings): drawn at its real size, a
/// submenu's with Back first. Click a slice to select it, drag it onto another to swap them, + adds one in the next place.
/// </summary>
internal sealed class WheelEditor : UserControl
{
    private readonly List<EditorPlace> _places = [];
    private int? _selected, _dragFrom, _dragOver;

    public WheelEditor(Core.Config.PetColor color)
    {
        View = new WheelView(floating: false, color);
        Content = View;
        Width = Diameter;
        Height = Diameter;
        Background = System.Windows.Media.Brushes.Transparent;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => EndDrag();
    }

    public WheelView View { get; }

    /// <summary>A slice was clicked.</summary>
    public event Action<int>? Selected;

    /// <summary>A slice was dragged onto another: swap them.</summary>
    public event Action<int, int>? Swapped;

    /// <summary>+ was clicked.</summary>
    public event Action? Added;

    /// <summary>Back was clicked, in a submenu.</summary>
    public event Action? WentBack;

    /// <summary>
    /// Draws <paramref name="slices"/>, Back first in a submenu (its slice pointing at <paramref name="back"/>), and + when
    /// there's room for <paramref name="max"/>; <paramref name="selected"/> takes the tint.
    /// </summary>
    public void Show(IReadOnlyList<WheelSlot> slices, double? back, int max, int? selected, string hub, HubTone tone)
    {
        _places.Clear();
        var slots = new List<WheelSlot>();
        if (back is not null)
        {
            _places.Add(new EditorPlace(null, true, false));
            slots.Add(new WheelSlot(LucideIcons.ArrowLeft, "Back") { IsBack = true });
        }
        for (int i = 0; i < slices.Count; i++)
        {
            _places.Add(new EditorPlace(i, false, false));
            slots.Add(slices[i]);
        }
        if (slices.Count < max)
        {
            _places.Add(new EditorPlace(null, false, true));
            slots.Add(new WheelSlot(LucideIcons.Plus, "Add") { IsPlus = true });
        }
        _selected = selected;
        // An empty wheel shows its ring with + at its top, so there's somewhere to start.
        View.Show(slots, back ?? 0);
        View.SetHub(hub, tone);
        Paint();
    }

    private int PlaceOf(int slice) => _places.FindIndex(p => p.Slice == slice);

    private void Paint()
    {
        for (int k = 0; k < _places.Count; k++)
        {
            var place = _places[k];
            var look = SlotLook.Plain;
            if (place.Slice is int i)
            {
                if (_dragFrom is int from && _dragOver is int over && over != from)
                    look = i == over ? SlotLook.Pointed : i == from ? SlotLook.Faded : SlotLook.Plain;
                else if (i == _selected)
                    look = SlotLook.Pointed;
            }
            View.SetLook(k, look);
        }
        int? shown = _selected is int s ? PlaceOf(s) : null;
        View.Pet.Look(shown is int k2 && k2 >= 0 ? Direction(View.First + k2 * (360.0 / _places.Count)) : null);
    }

    private EditorPlace? At(MouseEventArgs e) => View.SlotAt(e.GetPosition(View)) is int k && k < _places.Count ? _places[k] : null;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (At(e) is not { } place)
            return;
        e.Handled = true;
        if (place.IsBack)
            WentBack?.Invoke();
        else if (place.IsPlus)
            Added?.Invoke();
        else if (place.Slice is int i)
        {
            _dragFrom = _dragOver = i;
            CaptureMouse();
            Selected?.Invoke(i);
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is null)
            return;
        int? over = At(e)?.Slice;
        if (over == _dragOver)
            return;
        _dragOver = over;
        Paint();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFrom is int from && _dragOver is int to && from != to)
        {
            _dragFrom = _dragOver = null;
            ReleaseMouseCapture();
            Swapped?.Invoke(from, to);
            return;
        }
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragFrom is null)
            return;
        _dragFrom = _dragOver = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Paint();
    }
}
