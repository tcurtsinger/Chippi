// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Chippi.Features.Recording;

/// <summary>Two independently draggable, keyboard-adjustable and accessible trim handles.</summary>
internal sealed class RecordingRange : Canvas
{
    private readonly Border _track = new() { Height = 4, CornerRadius = new(2) };
    private readonly Border _selected = new() { Height = 4, CornerRadius = new(2) };
    private readonly Handle _start, _end;
    public double Duration { get; }
    public double Start { get; private set; }
    public double End { get; private set; }
    public event Action? Changed;
    public RecordingRange(double duration)
    {
        Duration = End = duration; Height = 28;
        _track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush"); _selected.SetResourceReference(Border.BackgroundProperty, "IslandAccentBrush");
        Children.Add(_track); Children.Add(_selected);
        _start = new(this, false); _end = new(this, true); Children.Add(_start); Children.Add(_end);
        SizeChanged += (_, _) => Draw(); Loaded += (_, _) => Draw();
    }
    public void Set(double start, double end)
    {
        double gap = Math.Min(.05, Duration);
        Start = Math.Clamp(start, 0, Math.Max(0, Duration - gap)); End = Math.Clamp(end, Start + gap, Duration);
        Draw(); Changed?.Invoke();
    }
    private void Draw()
    {
        double width = Math.Max(0, ActualWidth - 12);
        _track.Width = width; SetLeft(_track, 6); SetTop(_track, 12);
        _selected.Width = (End - Start) / Duration * width; SetLeft(_selected, 6 + Start / Duration * width); SetTop(_selected, 12);
        SetLeft(_start, Start / Duration * width); SetLeft(_end, End / Duration * width); SetTop(_start, 2); SetTop(_end, 2);
    }
    private sealed class Handle : Thumb
    {
        private readonly RecordingRange _range;
        private readonly bool _end;
        internal double Value => _end ? _range.End : _range.Start;
        internal double Minimum => _end ? _range.Start + Math.Min(.05, _range.Duration) : 0;
        internal double Maximum => _end ? _range.Duration : _range.End - Math.Min(.05, _range.Duration);
        public Handle(RecordingRange range, bool end)
        {
            _range = range; _end = end; Width = 12; Height = 24; Focusable = true;
            AutomationProperties.SetName(this, end ? "Trim end in seconds" : "Trim start in seconds");
            var chrome = new FrameworkElementFactory(typeof(Border)); chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); chrome.SetResourceReference(Border.BackgroundProperty, "IslandAccentBrush");
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = chrome };
            DragDelta += (_, e) => Move(Value + e.HorizontalChange / Math.Max(1, range.ActualWidth - 12) * range.Duration);
            PreviewMouseLeftButtonDown += (_, _) => Focus();
            KeyDown += (_, e) =>
            {
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : .1;
                double? value = e.Key switch { Key.Left or Key.Down => Value - step, Key.Right or Key.Up => Value + step, Key.Home => Minimum, Key.End => Maximum, _ => null };
                if (value != null) { Move(value.Value); e.Handled = true; }
            };
        }
        internal void Move(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            value = Math.Clamp(value, Minimum, Maximum);
            _range.Set(_end ? _range.Start : value, _end ? value : _range.End);
        }
        protected override AutomationPeer OnCreateAutomationPeer() => new HandlePeer(this);
    }
    private sealed class HandlePeer(Handle owner) : ThumbAutomationPeer(owner), IRangeValueProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 1;
        public double SmallChange => .1;
        public double Maximum => owner.Maximum;
        public double Minimum => owner.Minimum;
        public double Value => owner.Value;
        public void SetValue(double value) => owner.Dispatcher.Invoke(() => owner.Move(value));
    }
}
