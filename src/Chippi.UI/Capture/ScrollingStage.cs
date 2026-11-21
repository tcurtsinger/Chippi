// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Chippi.Capture;

namespace Chippi.UI.Capture;

/// <summary>The preview strip's contents: the result so far, box-sampled to <see cref="IScrollingStage.PreviewWidth"/> pixels wide.</summary>
/// <param name="Strip">The result so far at the preview's pixel width, or null before the first frame.</param>
/// <param name="Width">The result's width in physical pixels.</param>
/// <param name="Height">The result's height in physical pixels.</param>
/// <param name="Seams">The result rows where each appended strip starts.</param>
/// <param name="ViewportTop">The result row of the area's top.</param>
/// <param name="GapRow">The result row of the gap "Too fast" left.</param>
/// <param name="TooFast">The gap is drawn.</param>
public sealed record ScrollingPreviewState(CapturedImage? Strip, int Width, int Height, IReadOnlyList<int> Seams, int ViewportTop, int GapRow, bool TooFast);

/// <summary>Where a scrolling capture's windows go: the area on a display, in physical virtual-screen pixels, and that display's DPI.</summary>
/// <param name="Styles">The island's styles (<see cref="CaptureLayer.BarStyles"/>), merged into each window as the overlays get them.</param>
public sealed record ScrollingStageSetup(PixelRect Area, PixelRect Display, int Dpi, Uri? Styles);

/// <summary>
/// The scrolling capture's three windows (docs/phase-7/scrolling-capture.md, Starting): the frame and dim around the area, the
/// bar and the preview. None ever takes the foreground and every one is out of every screen read, so the page under the area
/// keeps the wheel, trackpad and keys. Call on the UI thread.
/// </summary>
public interface IScrollingStage : IDisposable
{
    /// <summary>The pixel width to compose the preview strip at (96 DIPs at the display's scale).</summary>
    int PreviewWidth { get; }

    /// <summary>Shows the frame, bar and preview.</summary>
    void Show();

    void SetBar(ScrollingBarState state);

    void SetPreview(ScrollingPreviewState state);

    /// <summary>
    /// Gives the bar the keyboard (End reached: "Done then takes focus", so Enter finishes). It's the only time a window of
    /// the stage takes the foreground, and a refusal is ignored.
    /// </summary>
    void TakeKeyboard();

    event Action? AutoAsked;
    event Action? StopAsked;
    event Action? DoneAsked;
    event Action? CancelAsked;
}

/// <summary>The real stage: layered, click-through where it should be, never activated, excluded from capture.</summary>
public sealed class ScrollingStage : IScrollingStage
{
    private readonly ScrollingStageSetup _setup;
    private readonly double _scale;
    private readonly ScrollingBar _bar = new();
    private readonly ScrollingPreview _preview = new();
    private ScrollingFrameWindow? _frame;
    private ScrollingToolWindow? _barWindow, _previewWindow;
    private ScrollingBarState _state = new(ScrollingLine.Scroll, 0, 0, false, false);
    private ScrollingPreviewState _previewState = new(null, 0, 0, [], 0, 0, false);
    private bool _shown, _disposed;

    public ScrollingStage(ScrollingStageSetup setup)
    {
        _setup = setup;
        _scale = Math.Max(1, setup.Dpi) / 96.0;
        _bar.AutoAsked += () => AutoAsked?.Invoke();
        _bar.StopAsked += () => StopAsked?.Invoke();
        _bar.DoneAsked += () => DoneAsked?.Invoke();
        _bar.CancelAsked += () => CancelAsked?.Invoke();
    }

    /// <summary>The frame and dim around <paramref name="area"/> as the stage's window draws them, in DIPs of a display at <paramref name="dpi"/> (for the offscreen shots).</summary>
    public static FrameworkElement Backdrop(PixelRect display, PixelRect area, int dpi) => ScrollingFrameWindow.Draw(display, area, dpi);

    public int PreviewWidth => Math.Max(1, (int)Math.Round(ScrollingPreviewLayout.StripWidth * _scale));

    public event Action? AutoAsked, StopAsked, DoneAsked, CancelAsked;

    public void Show()
    {
        if (_shown || _disposed)
            return;
        _shown = true;
        _frame = new ScrollingFrameWindow(_setup.Styles);
        _frame.Show(_setup.Display, _setup.Area, _setup.Dpi);
        _barWindow = new ScrollingToolWindow(_setup.Styles, _bar, keys: true);
        _barWindow.KeyAsked += key =>
        {
            if (key == Key.Escape)
                CancelAsked?.Invoke();
            else
                DoneAsked?.Invoke();
        };
        _previewWindow = new ScrollingToolWindow(_setup.Styles, _preview, keys: false);
        SetPreview(_previewState);
        SetBar(_state);
    }

    public void SetBar(ScrollingBarState state)
    {
        _state = state;
        _bar.Apply(state);
        _barWindow?.Reposition(size => ScrollingPlacement.Bar(_setup.Area, _setup.Display, size, Gap), _setup.Dpi);
    }

    public void SetPreview(ScrollingPreviewState state)
    {
        _previewState = state;
        int width = state.Width > 0 ? state.Width : _setup.Area.Width;
        int height = state.Height > 0 ? state.Height : _setup.Area.Height;
        var layout = ScrollingPreviewLayout.Compute(width, height, _setup.Area.Height, state.Seams, state.ViewportTop, state.GapRow, state.TooFast, _setup.Area.Height / _scale);
        _preview.Apply(layout, state.Strip is { Width: > 0, Height: > 0 } strip ? ScrollingPreview.Picture(strip) : null);
        _previewWindow?.Reposition(size => ScrollingPlacement.Preview(_setup.Area, _setup.Display, size, Gap), _setup.Dpi);
    }

    public void TakeKeyboard() => _barWindow?.TakeKeyboard(_bar.DoneButton);

    private int Gap => (int)Math.Round(BarPlacement.Gap * _scale);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _frame?.Dispose();
        _barWindow?.Dispose();
        _previewWindow?.Dispose();
        _frame = null;
        _barWindow = _previewWindow = null;
    }
}

/// <summary>A window of the stage: layered, never activated by a click, a tool window, and out of every screen read.</summary>
internal abstract partial class ScrollingWindow : Window
{
    private bool _disposed;

    protected ScrollingWindow(Uri? styles, bool clickThrough)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Chippi scrolling capture";
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
        if (styles is not null)
            Resources.MergedDictionaries.Add(SharedStyles(styles));
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            nint style = WsExNoActivate | WsExToolWindow | (clickThrough ? WsExTransparent : 0);
            SetWindowLongPtr(Handle, GwlExStyle, GetWindowLongPtr(Handle, GwlExStyle) | style);
            // Out of the frames the capture reads (the Sandbox check, scrolling-capture.md): the area never holds the stage.
            SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
        };
    }

    // The island's styles are parsed once and shared by the stage's windows (each parse costs tens of milliseconds, cold). UI thread only.
    private static ResourceDictionary? _styles;
    private static Uri? _stylesSource;

    private static ResourceDictionary SharedStyles(Uri source)
    {
        if (_styles is null || _stylesSource != source)
        {
            _styles = new ResourceDictionary { Source = source };
            _stylesSource = source;
        }
        return _styles;
    }

    public nint Handle { get; private set; }

    /// <summary>On screen at <paramref name="bounds"/> (physical pixels) without taking the foreground.</summary>
    protected void PlaceAt(PixelRect bounds, int dpi)
    {
        _ = dpi;
        if (Handle == 0)
            new WindowInteropHelper(this).EnsureHandle();
        if (!IsVisible)
            Show();
        // Twice: moving onto a display at another scale makes WPF resize for it (WM_DPICHANGED) after the first.
        for (int i = 0; i < 2; i++)
            SetWindowPos(Handle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoActivate);
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_disposed)
            e.Cancel = true; // Alt+F4 is the bar's Cancel, not a close of one window.
    }

    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x80, WsExTransparent = 0x20, WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010, WdaExcludeFromCapture = 0x11;
    private static readonly nint HwndTopmost = -1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}

/// <summary>
/// The frame and the dim: one click-through window over the area's display. A 2 px accent frame sits just outside the area
/// with a hairline of shadow, and everything else on the display is dimmed 30%, so the page under the area takes the wheel.
/// </summary>
internal sealed class ScrollingFrameWindow : ScrollingWindow
{
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x4D, 0, 0, 0));
    private static readonly Brush Edge = Frozen(Color.FromArgb(0x59, 0, 0, 0));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0xB7, 0x9C, 0xFF));

    public ScrollingFrameWindow(Uri? styles) : base(styles, clickThrough: true)
    {
        IsHitTestVisible = false;
    }

    public void Show(PixelRect display, PixelRect area, int dpi)
    {
        Content = Draw(display, area, dpi);
        PlaceAt(display, dpi);
    }

    /// <summary>The dim and the frame in DIPs of a display at <paramref name="dpi"/>. Public to the assembly for the offscreen shots.</summary>
    internal static Canvas Draw(PixelRect display, PixelRect area, int dpi)
    {
        double s = Math.Max(1, dpi) / 96.0;
        // The frame is 2 DIPs, whole physical pixels; its hairline one pixel out.
        double frame = Math.Max(2, Math.Round(2 * s)) / s, hair = 1 / s;
        var screen = new Rect(0, 0, display.Width / s, display.Height / s);
        var hole = new Rect((area.X - display.X) / s, (area.Y - display.Y) / s, area.Width / s, area.Height / s);
        var outer = Rect.Inflate(hole, frame, frame);
        var shadow = Rect.Inflate(outer, hair, hair);
        var canvas = new Canvas { Width = screen.Width, Height = screen.Height, SnapsToDevicePixels = true, UseLayoutRounding = true, IsHitTestVisible = false };
        canvas.Children.Add(Ring(screen, shadow, Dim));
        canvas.Children.Add(Ring(shadow, outer, Edge));
        canvas.Children.Add(Ring(outer, hole, Accent));
        return canvas;
    }

    private static System.Windows.Shapes.Path Ring(Rect outside, Rect inside, Brush fill) => new()
    {
        Fill = fill,
        IsHitTestVisible = false,
        Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outside), new RectangleGeometry(inside)),
    };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The bar's or the preview's window: sized to its control, placed in physical pixels, never activated by a click. A margin of
/// transparent pixels around the control holds its shadow; transparent pixels of a layered window pass the pointer through.
/// </summary>
internal sealed class ScrollingToolWindow : ScrollingWindow
{
    private const double Room = 24;
    private readonly FrameworkElement _control;

    public ScrollingToolWindow(Uri? styles, FrameworkElement control, bool keys) : base(styles, clickThrough: false)
    {
        _control = control;
        // A lighter shadow than the frozen screen's bars: it has to fit the margin.
        if (control is Border { Child: Border surface })
            surface.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black };
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        control.Margin = new Thickness(Room);
        Content = new Grid { Children = { control } };
        if (keys)
            PreviewKeyDown += (_, e) =>
            {
                // A focused button takes Enter itself.
                if (e.Key == Key.Escape || e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
                {
                    e.Handled = true;
                    KeyAsked?.Invoke(e.Key);
                }
            };
    }

    /// <summary>Esc or Enter reached the window (it only gets keys once it holds the keyboard).</summary>
    public event Action<Key>? KeyAsked;

    /// <summary>
    /// Sizes the window to its control and places it where <paramref name="place"/> says, given the control's size in physical
    /// pixels.
    /// </summary>
    public void Reposition(Func<PixelSize, PixelRect> place, int dpi)
    {
        double s = Math.Max(1, dpi) / 96.0;
        _control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wanted = new Size(_control.DesiredSize.Width - 2 * Room, _control.DesiredSize.Height - 2 * Room);
        var visible = place(new PixelSize((int)Math.Ceiling(wanted.Width * s), (int)Math.Ceiling(wanted.Height * s)));
        int pad = (int)Math.Round(Room * s);
        PlaceAt(new PixelRect(visible.X - pad, visible.Y - pad, visible.Width + 2 * pad, visible.Height + 2 * pad), dpi);
    }

    /// <summary>Takes the foreground (a window with no-activate still can, on purpose) and focuses <paramref name="target"/>.</summary>
    public void TakeKeyboard(IInputElement target)
    {
        try
        {
            if (!IsVisible)
                return;
            Activate();
            target.Focus();
        }
        catch (InvalidOperationException)
        {
        }
    }
}
