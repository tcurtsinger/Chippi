// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Chippi.Features.WindowLayouts.Views;

/// <summary>A layout's zones as a small picture (the Layouts list): each zone a filled block with a hairline between.</summary>
internal sealed class LayoutPicture : FrameworkElement
{
    private readonly IReadOnlyList<SnapZone> _zones;

    public LayoutPicture(ZoneNode root, double width = 56, double height = 32)
    {
        _zones = root.Flatten().Zones;
        Width = width;
        Height = height;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var frame = (Brush)FindResource("BorderStrongBrush");
        var fill = (Brush)FindResource("TextTertiaryBrush");
        dc.DrawRoundedRectangle(null, new Pen(frame, 1), new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1), 4, 4);
        double inset = 3, w = ActualWidth - 2 * inset, h = ActualHeight - 2 * inset;
        foreach (var z in _zones)
        {
            var r = new Rect(inset + z.X * w + 1, inset + z.Y * h + 1, Math.Max(1, z.Width * w - 2), Math.Max(1, z.Height * h - 2));
            dc.PushOpacity(0.55);
            dc.DrawRoundedRectangle(fill, null, r, 1.5, 1.5);
            dc.Pop();
        }
    }
}

/// <summary>
/// The zone editor's canvas (wave-3.html, sheet-snap-editor): the layout at its display's shape, a button per zone (Tab goes
/// through them, then the dividers) and a handle per divider that drags, or moves 1% per arrow key, with its split as a tip.
/// </summary>
internal sealed class ZoneCanvas : Panel
{
    private const double Gap = 6, HandleSize = 14;
    private readonly List<(ZoneTile Tile, SnapZone Zone)> _tiles = [];
    private readonly List<(DividerHandle Handle, ZoneDivider Divider)> _handles = [];
    private readonly Border _tip;
    private readonly TextBlock _tipText = new() { FontSize = 12 };
    private ZoneNode _root = new();
    private double _aspect = 16.0 / 9;

    public ZoneCanvas()
    {
        _tip = new Border { Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _tipText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _tip.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        _tip.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _tipText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Panel.SetZIndex(_tip, 10);
        ClipToBounds = true;
    }

    public string? Selected { get; private set; }

    /// <summary>A zone was pressed (or Enter/Space on it).</summary>
    public event Action<string>? ZoneSelected;

    /// <summary>A divider moved to a new ratio, already within 5% to 95%.</summary>
    public event Action<string, double>? DividerMoved;

    /// <summary>A divider drag started or ended, for the caller to hold off rebuilding while the mouse is down.</summary>
    public bool Dragging { get; private set; }

    public void Show(ZoneNode root, string? selected, double aspect)
    {
        _root = root;
        _aspect = aspect > 0 ? aspect : 16.0 / 9;
        Selected = selected;
        string? focused = Children.OfType<ZoneTile>().FirstOrDefault(t => t.IsKeyboardFocused)?.ZoneId;
        string? focusedHandle = Children.OfType<DividerHandle>().FirstOrDefault(h => h.IsKeyboardFocused)?.DividerId;
        Children.Clear();
        _tiles.Clear();
        _handles.Clear();
        var (zones, dividers) = root.Flatten();
        // Tab order: zones in reading order, then the dividers.
        foreach (var zone in zones.OrderBy(z => Math.Round(z.Y, 4)).ThenBy(z => z.X))
        {
            var tile = new ZoneTile(zone.Id, zone.Name, zone.Id == selected);
            AutomationProperties.SetName(tile, zone.Name);
            AutomationProperties.SetItemStatus(tile, zone.Id == selected ? "Selected" : "");
            tile.Click += (_, _) => ZoneSelected?.Invoke(zone.Id);
            _tiles.Add((tile, zone));
            Children.Add(tile);
        }
        foreach (var divider in dividers)
        {
            var handle = new DividerHandle(divider);
            AutomationProperties.SetName(handle, divider.Direction == SplitDirection.SideBySide ? "Divider between columns" : "Divider between rows");
            AutomationProperties.SetHelpText(handle, Split(divider.Ratio));
            handle.DragStarted += (_, _) => { Dragging = true; ShowTip(handle, divider.Ratio); };
            handle.DragDelta += (_, e) =>
            {
                double length = (divider.Direction == SplitDirection.SideBySide ? ActualWidth : ActualHeight) * divider.AxisLength;
                double delta = divider.Direction == SplitDirection.SideBySide ? e.HorizontalChange : e.VerticalChange;
                if (length <= 0) return;
                double ratio = Clamp(handle.DragRatio + delta / length);
                handle.DragRatio = ratio;
                ShowTip(handle, ratio);
                DividerMoved?.Invoke(divider.Id, ratio);
            };
            handle.DragCompleted += (_, _) => { Dragging = false; _tip.Visibility = Visibility.Collapsed; };
            handle.Step += step => Nudge(divider.Id, step);
            _handles.Add((handle, divider));
            Children.Add(handle);
        }
        Children.Add(_tip);
        if (focused is not null)
            _tiles.FirstOrDefault(t => t.Zone.Id == focused).Tile?.Focus();
        if (focusedHandle is not null)
            _handles.FirstOrDefault(h => h.Divider.Id == focusedHandle).Handle?.Focus();
        InvalidateMeasure();
    }

    /// <summary>A drag keeps its handle: the caller redraws with the new ratio, and the handle under the mouse stays the same object.</summary>
    public void Resize(ZoneNode root)
    {
        var (zones, dividers) = root.Flatten();
        _root = root;
        for (int i = 0; i < _tiles.Count; i++)
            if (zones.FirstOrDefault(z => z.Id == _tiles[i].Zone.Id) is { } z)
                _tiles[i] = (_tiles[i].Tile, z);
        for (int i = 0; i < _handles.Count; i++)
            if (dividers.FirstOrDefault(d => d.Id == _handles[i].Divider.Id) is { } d)
            {
                _handles[i] = (_handles[i].Handle, d);
                AutomationProperties.SetHelpText(_handles[i].Handle, Split(d.Ratio));
            }
        InvalidateArrange();
    }

    /// <summary>Moves a divider by whole percents, as its arrow keys do.</summary>
    internal void Nudge(string dividerId, int step)
    {
        var divider = _handles.First(h => h.Divider.Id == dividerId).Divider;
        DividerMoved?.Invoke(dividerId, Clamp(Math.Round(divider.Ratio * 100 + step) / 100));
    }

    /// <summary>Shows a divider's tip as a drag does, for the editor's shot of a drag.</summary>
    internal void ShowTipFor(string dividerId)
    {
        var (handle, divider) = _handles.First(h => h.Divider.Id == dividerId);
        ShowTip(handle, divider.Ratio);
    }

    internal static string Split(double ratio)
    {
        int first = (int)Math.Round(ratio * 100);
        return $"{first}% · {100 - first}%";
    }

    private static double Clamp(double ratio) => Math.Clamp(ratio, .05, .95);

    private void ShowTip(DividerHandle handle, double ratio)
    {
        _tipText.Text = Split(ratio);
        _tip.Visibility = Visibility.Visible;
        _tip.Tag = handle;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 520 : available.Width;
        var size = new Size(width, width / _aspect);
        foreach (UIElement child in InternalChildren)
            child.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size final)
    {
        double w = final.Width, h = final.Height;
        foreach (var (tile, z) in _tiles)
            tile.Arrange(new Rect(z.X * w + Gap / 2, z.Y * h + Gap / 2, Math.Max(0, z.Width * w - Gap), Math.Max(0, z.Height * h - Gap)));
        foreach (var (handle, d) in _handles)
        {
            handle.DragRatio = Dragging && handle.IsDragging ? handle.DragRatio : d.Ratio;
            var rect = d.Direction == SplitDirection.SideBySide
                ? new Rect(d.X * w - HandleSize / 2, d.Y * h, HandleSize, d.Length * h)
                : new Rect(d.X * w, d.Y * h - HandleSize / 2, d.Length * w, HandleSize);
            handle.Arrange(rect);
        }
        if (_tip.Visibility == Visibility.Visible && _tip.Tag is DividerHandle shown)
        {
            var at = _handles.First(x => x.Handle == shown).Divider;
            var tip = _tip.DesiredSize;
            double cx = at.Direction == SplitDirection.SideBySide ? at.X * w : (at.X + at.Length / 2) * w;
            double cy = at.Direction == SplitDirection.SideBySide ? (at.Y + at.Length / 2) * h : at.Y * h;
            double x = Math.Clamp(cx - tip.Width / 2, 0, Math.Max(0, w - tip.Width));
            double y = Math.Clamp(cy - tip.Height - 14, 0, Math.Max(0, h - tip.Height));
            _tip.Arrange(new Rect(x, y, tip.Width, tip.Height));
        }
        else
            _tip.Arrange(new Rect());
        return final;
    }

    /// <summary>A zone: its name centred; the selected one has the accent's outline and a tint of it.</summary>
    internal sealed class ZoneTile : Button
    {
        public ZoneTile(string id, string name, bool selected)
        {
            ZoneId = id;
            ZoneName = name;
            IsSelected = selected;
            Template = new ControlTemplate(typeof(ZoneTile));
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
        }

        public string ZoneId { get; }

        public string ZoneName { get; }

        public bool IsSelected { get; }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusedChanged(e);
            InvalidateVisual();
        }

        protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }

        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            var accent = (Brush)FindResource("AccentBrush");
            var fill = IsSelected ? Tint(accent, 0.22) : (Brush)FindResource(IsMouseOver ? "ButtonHoverBrush" : "ChipBrush");
            var stroke = IsSelected ? new Pen(accent, 2) : new Pen((Brush)FindResource("BorderBrush"), 1);
            double inset = stroke.Thickness / 2;
            dc.DrawRoundedRectangle(fill, stroke, new Rect(inset, inset, Math.Max(0, rect.Width - 2 * inset), Math.Max(0, rect.Height - 2 * inset)), 4, 4);
            if (IsKeyboardFocused)
                dc.DrawRoundedRectangle(null, new Pen((Brush)FindResource("TextPrimaryBrush"), 2) { DashStyle = DashStyles.Solid }, new Rect(-3, -3, rect.Width + 6, rect.Height + 6), 6, 6);
            var text = new FormattedText(ZoneName, CultureInfo.CurrentUICulture, FlowDirection, new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, IsSelected ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                13, (Brush)FindResource(IsSelected ? "TextPrimaryBrush" : "TextSecondaryBrush"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, rect.Width - 12), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
            dc.DrawText(text, new Point(6, (rect.Height - text.Height) / 2));
        }

        private static Brush Tint(Brush accent, double opacity)
        {
            var color = accent is SolidColorBrush solid ? solid.Color : Colors.MediumPurple;
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        }
    }

    /// <summary>A divider: a thin grip on the line between two zones. Arrows along its axis move it by 1%.</summary>
    internal sealed class DividerHandle : Thumb
    {
        private readonly ZoneDivider _divider;

        public DividerHandle(ZoneDivider divider)
        {
            _divider = divider;
            DividerId = divider.Id;
            DragRatio = divider.Ratio;
            Template = new ControlTemplate(typeof(DividerHandle));
            Focusable = true;
            FocusVisualStyle = null;
            Cursor = divider.Direction == SplitDirection.SideBySide ? Cursors.SizeWE : Cursors.SizeNS;
            Background = Brushes.Transparent;
        }

        public string DividerId { get; }

        public double DragRatio { get; set; }

        public event Action<int>? Step;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool side = _divider.Direction == SplitDirection.SideBySide;
            int step = (e.Key, side) switch
            {
                (Key.Left, true) or (Key.Up, false) => -1,
                (Key.Right, true) or (Key.Down, false) => 1,
                _ => 0,
            };
            if (step != 0)
            {
                Step?.Invoke(step);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            base.OnMouseLeftButtonDown(e);
        }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusedChanged(e);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            bool side = _divider.Direction == SplitDirection.SideBySide;
            bool hot = IsDragging || IsKeyboardFocused || IsMouseOver;
            var brush = (Brush)FindResource(hot ? "AccentBrush" : "TextSecondaryBrush");
            double length = Math.Min(28, (side ? ActualHeight : ActualWidth) * 0.5);
            var grip = side
                ? new Rect(ActualWidth / 2 - 2, ActualHeight / 2 - length / 2, 4, length)
                : new Rect(ActualWidth / 2 - length / 2, ActualHeight / 2 - 2, length, 4);
            dc.DrawRoundedRectangle(brush, null, grip, 2, 2);
            if (IsKeyboardFocused)
                dc.DrawRoundedRectangle(null, new Pen((Brush)FindResource("TextPrimaryBrush"), 1.5), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 4, 4);
        }
    }
}
