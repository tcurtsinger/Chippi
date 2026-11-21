// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Chippi.Capture;
using Chippi.UI.Controls;

namespace Chippi.UI.Capture;

/// <summary>What the scrolling bar says (docs/phase-7/scrolling-capture.md, Copy). <see cref="None"/> says nothing.</summary>
public enum ScrollingLine { Scroll, None, Scrolling, TooFast, EndReached, LimitReached }

/// <summary>Everything the scrolling bar draws: the line, the result's size and Auto.</summary>
/// <param name="Width">The result's width in physical pixels.</param>
/// <param name="Height">The result's height in physical pixels.</param>
/// <param name="AutoAvailable">Auto is offered; off, it's dimmed with "Auto-scroll not available".</param>
/// <param name="AutoActive">Auto is running: Stop takes Auto's place.</param>
public sealed record ScrollingBarState(ScrollingLine Line, int Width, int Height, bool AutoAvailable, bool AutoActive);

/// <summary>
/// The scrolling capture's bar (scrolling-capture.html, boards 3 to 9): the line and the result's size, Auto (Stop while it
/// runs), Done (the main verb) and Cancel, 40 high. It only raises events; the session does the work.
/// </summary>
public sealed class ScrollingBar : Border
{
    internal const string AutoOff = "Auto-scroll not available";

    private readonly LucideIcon _lineGlyph = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _lineText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly StackPanel _line;
    private readonly Border _lineRule = CaptureBars.Rule(vertical: true);
    private readonly TextBlock _size = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly Button _auto, _done, _cancel;
    private readonly LucideIcon _autoGlyph = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _autoText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private bool _active;

    public ScrollingBar()
    {
        _lineText.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        _size.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        _autoText.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        AutomationProperties.SetLiveSetting(_lineText, AutomationLiveSetting.Polite);
        _line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), Children = { _lineGlyph, _lineText } };

        _auto = CaptureBars.TextButton("Auto");
        _auto.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _autoGlyph, _autoText } };
        _auto.MinWidth = 0;
        ToolTipService.SetShowOnDisabled(_auto, true);
        _done = CaptureBars.TextButton("Done", accent: true);
        _done.Margin = new Thickness(6, 0, 0, 0);
        _done.ToolTip = "Done · Enter";
        _cancel = CaptureBars.IconButton(LucideIcons.Close, "Cancel", 28, 28, "Cancel · Esc");
        _cancel.Margin = new Thickness(6, 0, 0, 0);
        var trail = CaptureBars.Rule(vertical: true);
        trail.Margin = new Thickness(6, 0, 0, 0);
        var lead = CaptureBars.Rule(vertical: true);
        _auto.Click += (_, _) => (_active ? StopAsked : AutoAsked)?.Invoke();
        _done.Click += (_, _) => DoneAsked?.Invoke();
        _cancel.Click += (_, _) => CancelAsked?.Invoke();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_line);
        row.Children.Add(_lineRule);
        row.Children.Add(_size);
        row.Children.Add(lead);
        row.Children.Add(_auto);
        row.Children.Add(_done);
        row.Children.Add(trail);
        row.Children.Add(_cancel);
        Child = CaptureBars.Surface(row, new Thickness(5));
        AutomationProperties.SetName(this, "Scrolling capture");
        Apply(new ScrollingBarState(ScrollingLine.Scroll, 0, 0, false, false));
    }

    /// <summary>Auto was pressed (not while it runs).</summary>
    public event Action? AutoAsked;

    /// <summary>Stop was pressed while Auto runs: it keeps what was stitched and returns to scrolling by hand.</summary>
    public event Action? StopAsked;

    public event Action? DoneAsked, CancelAsked;

    /// <summary>The line's words, or null when it says nothing.</summary>
    public string? LineText => _line.Visibility == Visibility.Visible ? _lineText.Text : null;

    /// <summary>The result's size as shown: "700 × 2,180".</summary>
    public string SizeText => _size.Text;

    public Button AutoButton => _auto;

    public Button DoneButton => _done;

    public Button CancelButton => _cancel;

    /// <summary>Auto's or Stop's words.</summary>
    public string AutoText => _autoText.Text;

    public static string Size(int width, int height) => string.Create(CultureInfo.CurrentCulture, $"{width:N0} × {height:N0}");

    public static string? Words(ScrollingLine line) => line switch
    {
        ScrollingLine.Scroll => "Scroll",
        ScrollingLine.Scrolling => "Scrolling",
        ScrollingLine.TooFast => "Too fast",
        ScrollingLine.EndReached => "End reached",
        ScrollingLine.LimitReached => "Limit reached",
        _ => null,
    };

    public void Apply(ScrollingBarState state)
    {
        string? words = Words(state.Line);
        string? was = LineText;
        bool caution = state.Line is ScrollingLine.TooFast or ScrollingLine.LimitReached;
        _line.Visibility = _lineRule.Visibility = words is null ? Visibility.Collapsed : Visibility.Visible;
        _lineText.Text = words ?? "";
        string ink = caution ? "IslandCautionBrush" : "IslandTextSecondaryBrush";
        _lineText.SetResourceReference(TextBlock.ForegroundProperty, ink);
        _lineGlyph.Data = state.Line switch
        {
            ScrollingLine.Scrolling => LucideIcons.ChevronsDown,
            ScrollingLine.EndReached => LucideIcons.CircleCheck,
            ScrollingLine.TooFast or ScrollingLine.LimitReached => LucideIcons.TriangleAlert,
            _ => LucideIcons.Mouse,
        };
        _lineGlyph.SetResourceReference(LucideIcon.BrushProperty, ink);
        _size.Text = Size(state.Width, state.Height);

        _active = state.AutoActive;
        bool over = state.Line is ScrollingLine.EndReached or ScrollingLine.LimitReached;
        _autoGlyph.Data = _active ? LucideIcons.SquareStop : LucideIcons.ChevronsDown;
        _autoGlyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        _autoText.Text = _active ? "Stop" : "Auto";
        _auto.IsEnabled = _active || (state.AutoAvailable && !over);
        _auto.ToolTip = !_auto.IsEnabled && !over ? AutoOff : null;
        AutomationProperties.SetName(_auto, _autoText.Text);

        if (words is not null && words != was)
            UIElementAutomationPeer.FromElement(_lineText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        InvalidateMeasure();
    }
}

/// <summary>
/// The scrolling capture's preview strip (scrolling-capture.html, boards 3 to 9): the result so far, 96 wide and growing
/// downward, with the part the area shows now framed, dashed seams where each strip was joined and a Caution line where
/// "Too fast" left a gap. Taller than the area, it scrolls with the newest part in view. It never takes the pointer.
/// </summary>
public sealed class ScrollingPreview : Border
{
    private static readonly Brush PaperBrush = Frozen(Color.FromRgb(0xF7, 0xF7, 0xF7));
    private static readonly Brush SeamBrush = Frozen(Color.FromArgb(0x99, 0x74, 0x44, 0xF0));

    private readonly Canvas _strip = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Border _frame;
    private readonly Border _box;

    public ScrollingPreview()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _frame = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2), Width = ScrollingPreviewLayout.StripWidth };
        _frame.SetResourceReference(BorderBrushProperty, "IslandAccentBrush");
        _box = new Border { Width = ScrollingPreviewLayout.StripWidth, Height = 72, CornerRadius = new CornerRadius(4), Background = PaperBrush, ClipToBounds = true, Child = _strip };
        Child = CaptureBars.Surface(_box, new Thickness(6));
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Preview");
    }

    /// <summary>The strip's visible height in DIPs after the last <see cref="Apply"/>.</summary>
    public double StripHeight => _box.Height;

    /// <summary>The dashed seams drawn, for tests.</summary>
    public int SeamCount { get; private set; }

    /// <summary>The gap line is drawn, for tests.</summary>
    public bool HasGap { get; private set; }

    /// <summary>Draws <paramref name="layout"/> over <paramref name="picture"/> (null: blank paper).</summary>
    public void Apply(ScrollingPreviewLayout layout, ImageSource? picture)
    {
        _box.Height = layout.Height;
        _strip.Children.Clear();
        _image.Source = picture;
        _image.Width = layout.Width;
        _image.Height = layout.StripHeight;
        Canvas.SetTop(_image, -layout.Offset);
        _strip.Children.Add(_image);
        SeamCount = 0;
        foreach (double y in layout.Seams)
        {
            var seam = new Line { X1 = 0, X2 = layout.Width, Y1 = y, Y2 = y, Stroke = SeamBrush, StrokeThickness = 1, StrokeDashArray = [4, 3] };
            _strip.Children.Add(seam);
            SeamCount++;
        }
        _frame.Height = Math.Max(4, layout.ViewportHeight);
        Canvas.SetTop(_frame, layout.ViewportTop);
        _strip.Children.Add(_frame);
        // The gap sits over the frame, whose bottom edge it usually meets, and stays inside the strip.
        HasGap = layout.Gap is not null;
        if (layout.Gap is { } gap)
        {
            double y = Math.Clamp(gap, 1.5, Math.Max(1.5, layout.Height - 1.5));
            var line = new Line { X1 = 0, X2 = layout.Width, Y1 = y, Y2 = y, StrokeThickness = 3, StrokeDashArray = [2, 1.34] };
            line.SetResourceReference(Shape.StrokeProperty, "IslandCautionBrush");
            _strip.Children.Add(line);
        }
    }

    /// <summary>The strip's pixels as an image: BGRA, straight alpha.</summary>
    public static BitmapSource Picture(CapturedImage strip)
    {
        var bitmap = BitmapSource.Create(strip.Width, strip.Height, 96, 96, PixelFormats.Bgra32, null, strip.Pixels, strip.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
