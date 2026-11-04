// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Chippi.Features.Sound.Soundboard.Views;

/// <summary>A real imported waveform and pointer trim handles; the paired Start/End fields provide keyboard access.</summary>
internal sealed class SoundWaveform : FrameworkElement
{
    private float[] _samples = [];
    private double _duration = 1, _start, _end = 1;
    private bool? _dragStart;
    internal event Action<double, double>? RangeChanged;

    internal SoundWaveform()
    {
        Height = 48; Cursor = Cursors.SizeWE;
        AutomationProperties.SetName(this, "Sound trim waveform");
        AutomationProperties.SetHelpText(this, "Drag either edge to trim, or edit the Start and End fields.");
        MouseLeftButtonDown += (_, e) =>
        {
            if (!CaptureMouse()) return;
            BeginDrag(e.GetPosition(this).X); e.Handled = true;
        };
        MouseMove += (_, e) => MoveDrag(e.GetPosition(this).X, e.LeftButton == MouseButtonState.Pressed);
        MouseLeftButtonUp += (_, e) => { if (_dragStart is not null) { EndDrag(); e.Handled = true; } };
        LostMouseCapture += (_, _) => EndDrag();
        Unloaded += (_, _) => EndDrag();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) EndDrag(); };
    }

    internal void Set(float[] samples, double duration, double start, double end)
    {
        _samples = samples;
        _duration = Math.Max(.01, double.IsFinite(duration) ? duration : 1);
        _start = Math.Clamp(double.IsFinite(start) ? start : 0, 0, _duration);
        _end = Math.Clamp(double.IsFinite(end) ? end : _duration, _start, _duration);
        InvalidateVisual();
    }

    // Kept independent from desktop capture so interruption behavior can be exercised unhosted.
    internal void BeginDrag(double x)
    {
        if (_dragStart is not null || !IsEnabled) return;
        var seconds = Seconds(x);
        _dragStart = Math.Abs(seconds - _start) <= Math.Abs(seconds - _end);
        Update(seconds);
    }

    internal void MoveDrag(double x, bool pressed)
    {
        if (!pressed) { EndDrag(); return; }
        if (_dragStart is not null) Update(Seconds(x));
    }

    internal void EndDrag()
    {
        _dragStart = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private double Seconds(double x) => Math.Clamp((x - 4) / Math.Max(1, ActualWidth - 8), 0, 1) * _duration;
    private double X(double seconds) => 4 + seconds / _duration * Math.Max(1, ActualWidth - 8);

    private void Update(double seconds)
    {
        double minimum = Math.Min(.05, _duration);
        if (_dragStart == true) _start = Math.Clamp(seconds, 0, Math.Max(0, _end - minimum));
        else _end = Math.Clamp(seconds, Math.Min(_duration, _start + minimum), _duration);
        InvalidateVisual(); RangeChanged?.Invoke(_start, _end);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var accent = TryFindResource("IslandAccentBrush") as Brush ?? Brushes.MediumPurple;
        var quiet = TryFindResource("IslandTextTertiaryBrush") as Brush ?? Brushes.Gray;
        var tint = TryFindResource("IslandAccentTintBrush") as Brush ?? Brushes.Transparent;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double left = X(_start), right = X(_end), center = ActualHeight / 2;
        drawing.DrawRoundedRectangle(tint, null, new Rect(left, 2, Math.Max(0, right - left), Math.Max(0, ActualHeight - 4)), 3, 3);
        int count = Math.Max(1, (int)(ActualWidth / 4));
        for (int i = 0; i < count; i++)
        {
            int index = _samples.Length == 0 ? -1 : Math.Min(_samples.Length - 1, i * _samples.Length / count);
            double value = index < 0 || !float.IsFinite(_samples[index]) ? 0 : Math.Clamp(Math.Abs(_samples[index]), 0, 1);
            double x = 4 + i * Math.Max(1, ActualWidth - 8) / count, half = Math.Max(1, value * (center - 5));
            drawing.DrawLine(new Pen(x >= left && x <= right ? accent : quiet, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                new Point(x, center - half), new Point(x, center + half));
        }
        foreach (var x in new[] { left, right })
            drawing.DrawRoundedRectangle(accent, null, new Rect(x - 2, 1, 4, Math.Max(0, ActualHeight - 2)), 2, 2);
    }
}
