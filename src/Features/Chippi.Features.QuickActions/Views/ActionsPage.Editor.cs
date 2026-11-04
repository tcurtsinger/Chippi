// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

internal sealed partial class ActionsPage
{
    private const int SlotCount = 20;
    private const double DragThreshold = 5;
    private readonly HashSet<QuickActionId> _picking = [];
    private readonly List<ToggleButton> _picks = [];
    private readonly List<Border> _slots = [];
    private readonly TextBlock _pickedText = Ui.Muted("", 11);
    private List<string?> _draft = [];
    private Grid? _editorGrid;
    private Canvas? _dragLayer;
    private FrameworkElement? _ghost;
    private Border? _targetMark;
    private TranslateTransform? _ghostAt;
    private (int Slot, Point At)? _gesture;
    private int _target = -1;
    private bool _editing, _choosing, _updatingPicks, _dragging;

    internal IReadOnlyList<string?> EditorLayout => _draft;
    internal IReadOnlyCollection<QuickActionId> EditorSelection => _picking;
    internal bool IsEditing => _editing;
    internal bool IsDragging => _dragging;

    /// <summary>Pencil opens a private draft. Changing selection or position never acts on Windows.</summary>
    internal void BeginEdit()
    {
        CloseConfirm();
        _list = null;
        StartEdit(first: _module.Settings.Shown is null || _module.Settings.Shown.Count == 0);
        Render();
        _picks.FirstOrDefault()?.Focus();
    }

    private void StartEdit(bool first)
    {
        CancelEditorDrag();
        _editing = true;
        _choosing = first;
        _draft = [.. _module.Layout];
        _picking.Clear();
        foreach (var action in QuickAction.All.Where(a => _module.Settings.Shows(a.Id))) _picking.Add(action.Id);
        _built = "";
    }

    private void DiscardEdit()
    {
        CancelEditorDrag();
        _editing = _choosing = false;
        _draft.Clear();
        _picking.Clear();
        _built = "";
    }

    internal void FinishEdit()
    {
        if (!_editing || !_module.Offered.Any(a => _picking.Contains(a.Id))) return;
        var selected = _picking.ToArray();
        var layout = _draft.ToArray();
        DiscardEdit();
        _module.SaveLayout(selected, layout);
        Render();
        _edit.Focus();
    }

    private void RenderPicker()
    {
        var offered = _module.Offered.ToDictionary(a => a.Key);
        string built = "picker:" + string.Join(",", _draft) + ":" + string.Join(",", offered.Keys);
        if (_built != built)
        {
            CancelEditorDrag();
            _built = built;
            _picks.Clear();
            _slots.Clear();
            _body.Children.Clear();
            if (_pickedText.Parent is Panel prior) prior.Children.Remove(_pickedText);
            var editor = new Grid { ClipToBounds = true };
            editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            editor.RowDefinitions.Add(new RowDefinition());
            editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            var hint = Ui.Muted("Click to show or hide \u00b7 Drag to move", 11);
            hint.VerticalAlignment = VerticalAlignment.Top;
            editor.Children.Add(hint);
            _editorGrid = SlotGrid(4, 6);
            _editorGrid.Background = Brushes.Transparent;
            Grid.SetRow(_editorGrid, 1);
            editor.Children.Add(_editorGrid);
            for (int i = 0; i < SlotCount; i++)
            {
                int slotIndex = i;
                var slot = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = Brushes.Transparent };
                slot.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush");
                if (_draft[i] is { } key && offered.TryGetValue(key, out var action))
                {
                    slot.BorderThickness = new Thickness(0);
                    var pick = PickCell(action, slotIndex);
                    slot.Child = pick;
                    _picks.Add(pick);
                }
                else
                {
                    AutomationProperties.SetName(slot, $"Empty position, row {i / Columns + 1}, column {i % Columns + 1}");
                }
                Put(_editorGrid, slot, i, 6);
                _slots.Add(slot);
            }
            _dragLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            Grid.SetRow(_dragLayer, 1);
            editor.Children.Add(_dragLayer);
            _pickedText.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(_pickedText, 2);
            AutomationProperties.SetLiveSetting(_pickedText, AutomationLiveSetting.Polite);
            editor.Children.Add(_pickedText);
            _editorGrid.PreviewMouseLeftButtonDown += (_, e) =>
            {
                int slot = SlotAt(e.GetPosition(_editorGrid));
                if (!EditorPointerDown(slot, e.GetPosition(_editorGrid))) return;
                if (!_editorGrid.CaptureMouse()) CancelEditorDrag();
                e.Handled = true;
            };
            _editorGrid.PreviewMouseMove += (_, e) =>
            {
                if (_gesture is null) return;
                if (e.LeftButton != MouseButtonState.Pressed) { CancelEditorDrag(); return; }
                EditorPointerMove(e.GetPosition(_editorGrid));
                e.Handled = true;
            };
            _editorGrid.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (_gesture is null) return;
                EditorPointerUp(e.GetPosition(_editorGrid));
                e.Handled = true;
            };
            _editorGrid.LostMouseCapture += (_, _) => CancelEditorDrag();
            _body.Children.Add(editor);
        }
        _updatingPicks = true;
        try
        {
            foreach (var pick in _picks) pick.IsChecked = _picking.Contains((QuickActionId)pick.Tag);
        }
        finally { _updatingPicks = false; }
        int count = _module.Offered.Count(a => _picking.Contains(a.Id));
        _pickedText.Text = $"{count} shown";
        _done.IsEnabled = count > 0;
    }

    private ToggleButton PickCell(QuickAction action, int slot)
    {
        var button = new ToggleButton { Tag = action.Id };
        button.SetResourceReference(StyleProperty, "ActionEditorTileStyle");
        var content = PickContents(action, _picking.Contains(action.Id));
        button.Content = content.Root;
        AutomationProperties.SetName(button, action.Name);
        AutomationProperties.SetHelpText(button, "Show or hide in Actions. Drag to move, or use Alt and an arrow key.");
        AutomationProperties.SetItemStatus(button, $"Row {slot / Columns + 1}, column {slot % Columns + 1}");
        button.ToolTip = action.Name;
        void Changed()
        {
            bool selected = button.IsChecked == true;
            content.Ring.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
            content.Fill.Visibility = content.Check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (_updatingPicks) return;
            if (selected) _picking.Add(action.Id); else _picking.Remove(action.Id);
            RenderPicker();
        }
        button.Checked += (_, _) => Changed();
        button.Unchecked += (_, _) => Changed();
        button.PreviewKeyDown += (_, e) =>
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0 || key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            MoveEditorByKey(slot, key);
            e.Handled = true;
        };
        return button;
    }

    private static (Grid Root, Ellipse Ring, Ellipse Fill, LucideIcon Check) PickContents(QuickAction action, bool selected)
    {
        var glyph = Ui.Glyph(action.Icon, 17);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var name = Ui.Text(action.Label, 11, "IslandTextSecondaryBrush");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(2, 4, 2, 0);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { glyph, name } };
        var mark = new Grid { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
        var ring = new Ellipse { StrokeThickness = 1, Visibility = selected ? Visibility.Collapsed : Visibility.Visible };
        ring.SetResourceReference(Shape.StrokeProperty, "IslandTextTertiaryBrush");
        var fill = new Ellipse { Visibility = selected ? Visibility.Visible : Visibility.Collapsed };
        fill.SetResourceReference(Shape.FillProperty, "IslandAccentBrush");
        var check = Ui.Glyph(LucideIcons.Check, 9, "IslandOnAccentBrush");
        check.HorizontalAlignment = HorizontalAlignment.Center;
        check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        mark.Children.Add(ring);
        mark.Children.Add(fill);
        mark.Children.Add(check);
        return (new Grid { Children = { stack, mark } }, ring, fill, check);
    }

    /// <summary>Pointer handlers share this path with the fixture probe; no native input or clipboard is needed.</summary>
    internal bool EditorPointerDown(int slot, Point point)
    {
        if (!_editing || slot < 0 || slot >= _draft.Count || _draft[slot] is not { } key || !_module.Offered.Any(a => a.Key == key)) return false;
        CancelEditorDrag();
        (_slots[slot].Child as ToggleButton)?.Focus();
        _gesture = (slot, point);
        return true;
    }

    internal void EditorPointerMove(Point point)
    {
        if (_gesture is not { } gesture || _editorGrid is null) return;
        if (point.X < 0 || point.Y < 0 || point.X >= _editorGrid.ActualWidth - 6 || point.Y >= _editorGrid.ActualHeight - 6)
        {
            CancelEditorDrag();
            return;
        }
        if (!_dragging && (point - gesture.At).Length >= DragThreshold)
        {
            _dragging = true;
            var source = _slots[gesture.Slot];
            var action = QuickAction.FromKey(_draft[gesture.Slot]!)!;
            var visual = new Border
            {
                Width = source.ActualWidth, Height = source.ActualHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                Child = new Border { CornerRadius = new CornerRadius(5), Child = PickContents(action, _picking.Contains(action.Id)).Root },
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.24, BlurRadius = 12, ShadowDepth = 4 },
            };
            // The ghost must cover the destination, even though the panel and its tint normally compose with the desktop.
            var surface = (TryFindResource("IslandBrush") as SolidColorBrush)?.Color ?? Colors.Black;
            visual.Background = new SolidColorBrush(Color.FromRgb(surface.R, surface.G, surface.B));
            ((Border)visual.Child).SetResourceReference(Border.BackgroundProperty, _picking.Contains(action.Id) ? "IslandAccentTintBrush" : "IslandGalleryFaceBrush");
            visual.SetResourceReference(Border.BorderBrushProperty, "IslandAccentBrush");
            _ghostAt = new TranslateTransform();
            visual.RenderTransform = new TransformGroup { Children = { new ScaleTransform(0.94, 0.94, visual.Width / 2, visual.Height / 2), _ghostAt } };
            _ghost = visual;
            _dragLayer!.Children.Add(visual);
            source.Opacity = 0.32;
        }
        if (!_dragging) return;
        int target = SlotAt(point);
        MarkTarget(target == gesture.Slot ? -1 : target);
        if (_ghostAt is not null && _ghost is not null)
        {
            _ghostAt.X = point.X - _ghost.Width / 2;
            _ghostAt.Y = point.Y - _ghost.Height / 2;
        }
    }

    internal void EditorPointerUp(Point point)
    {
        if (_gesture is not { } gesture) return;
        int target = SlotAt(point);
        bool dragged = _dragging;
        CancelEditorDrag();
        if (dragged)
        {
            if (target >= 0 && target != gesture.Slot) MoveEditor(gesture.Slot, target);
        }
        else if (target == gesture.Slot && _slots[target].Child is ToggleButton button)
            button.IsChecked = button.IsChecked != true;
    }

    private void MarkTarget(int target)
    {
        if (_target == target) return;
        if (_targetMark is not null) _dragLayer?.Children.Remove(_targetMark);
        _targetMark = null;
        _target = target;
        if (target < 0 || _dragLayer is null) return;
        // Overlay the destination so highlighting never changes its content's size or moves a label under the pointer.
        var bounds = EditorSlotBounds(target);
        _targetMark = new Border { Width = bounds.Width, Height = bounds.Height, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2) };
        _targetMark.SetResourceReference(Border.BorderBrushProperty, "IslandAccentBrush");
        Canvas.SetLeft(_targetMark, bounds.Left);
        Canvas.SetTop(_targetMark, bounds.Top);
        Panel.SetZIndex(_targetMark, -1);
        _dragLayer.Children.Add(_targetMark);
    }
    private void CancelEditorDrag()
    {
        _gesture = null;
        _dragging = false;
        MarkTarget(-1);
        foreach (var slot in _slots) slot.Opacity = 1;
        _dragLayer?.Children.Clear();
        _ghost = null;
        _ghostAt = null;
        if (_editorGrid?.IsMouseCaptured == true) _editorGrid.ReleaseMouseCapture();
    }

    internal Rect EditorSlotBounds(int index) => _editorGrid is not null && index >= 0 && index < _slots.Count
        ? new Rect(_slots[index].TranslatePoint(new Point(), _editorGrid), _slots[index].RenderSize) : Rect.Empty;

    private int SlotAt(Point point)
    {
        if (_editorGrid is null) return -1;
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var origin = slot.TranslatePoint(new Point(), _editorGrid);
            if (new Rect(origin, slot.RenderSize).Contains(point)) return i;
        }
        return -1;
    }

    internal void MoveEditorByKey(int slot, Key key)
    {
        int next = key switch
        {
            Key.Left when slot % Columns > 0 => slot - 1,
            Key.Right when slot % Columns < Columns - 1 => slot + 1,
            Key.Up when slot >= Columns => slot - Columns,
            Key.Down when slot < SlotCount - Columns => slot + Columns,
            _ => slot,
        };
        if (next != slot) MoveEditor(slot, next);
    }

    internal void MoveEditor(int from, int to)
    {
        if (!_editing || from < 0 || to < 0 || from >= SlotCount || to >= SlotCount || from == to || _draft[from] is not { } moved) return;
        CancelEditorDrag();
        (_draft[from], _draft[to]) = (_draft[to], _draft[from]);
        RenderPicker();
        if (QuickAction.FromKey(moved) is { } action)
        {
            _picks.FirstOrDefault(b => Equals(b.Tag, action.Id))?.Focus();
            _pickedText.Text = $"{action.Label} moved to row {to / Columns + 1}, column {to % Columns + 1}";
            UIElementAutomationPeer.FromElement(_pickedText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
