// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Chippi.UI.Controls;

namespace Chippi.UI.Capture;

/// <summary>A tool in the frozen screen's markup column (Frozen screen v1), with its letter.</summary>
public enum CaptureTool { Select, Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text, Redact, Eraser, Crop }

/// <summary>The ink's width: the colour choice's Thin, Medium and Thick.</summary>
public enum InkWidth { Thin, Medium, Thick }

/// <summary>
/// The frozen screen's bars (Frozen screen v1): the markup column down the selection's side, the finish bar under it, the
/// colour choice, and the small bars a Text mark and Crop show. Screen capture hands the column and the finish bar to
/// <see cref="ICaptureSession.SetBars"/>, which places them. They're drawn in the island's styles and follow the app
/// theme; the frozen screen itself doesn't. They only raise events: the feature does the work.
/// </summary>
public static class CaptureBars
{
    /// <summary>The ink colours, in the colour choice's order: red, yellow, green, blue, violet, black, white, gray.</summary>
    public static IReadOnlyList<(string Name, Color Color)> Inks { get; } =
    [
        ("Red", Color.FromRgb(0xE5, 0x48, 0x4D)),
        ("Yellow", Color.FromRgb(0xFF, 0xC5, 0x3D)),
        ("Green", Color.FromRgb(0x30, 0xA4, 0x6C)),
        ("Blue", Color.FromRgb(0x00, 0x90, 0xFF)),
        ("Violet", Color.FromRgb(0x74, 0x44, 0xF0)),
        ("Black", Color.FromRgb(0x1A, 0x1A, 0x1A)),
        ("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        ("Gray", Color.FromRgb(0x8B, 0x8D, 0x98)),
    ];

    /// <summary>The text sizes a Text mark offers.</summary>
    public static IReadOnlyList<int> TextSizes { get; } = [12, 14, 18, 24, 32, 48];

    /// <summary>The letter that picks a tool on the frozen screen (screen-capture.md, Markup).</summary>
    public static char? Letter(CaptureTool tool) => tool switch
    {
        CaptureTool.Select => 'V',
        CaptureTool.Pen => 'P',
        CaptureTool.Highlighter => 'H',
        CaptureTool.Line => 'L',
        CaptureTool.Arrow => 'A',
        CaptureTool.Rectangle => 'R',
        CaptureTool.Ellipse => 'O',
        CaptureTool.Text => 'T',
        CaptureTool.Redact => 'B',
        CaptureTool.Eraser => 'E',
        CaptureTool.Crop => 'C',
        _ => null,
    };

    internal static Geometry Icon(CaptureTool tool) => tool switch
    {
        CaptureTool.Select => LucideIcons.Pointer,
        CaptureTool.Pen => LucideIcons.Pencil,
        CaptureTool.Highlighter => LucideIcons.Highlighter,
        CaptureTool.Line => LucideIcons.Minus,
        CaptureTool.Arrow => LucideIcons.MoveUpRight,
        CaptureTool.Rectangle => LucideIcons.Square,
        CaptureTool.Ellipse => LucideIcons.Circle,
        CaptureTool.Text => LucideIcons.Type,
        CaptureTool.Redact => LucideIcons.EyeOff,
        CaptureTool.Eraser => LucideIcons.Eraser,
        _ => LucideIcons.Crop,
    };

    /// <summary>A floating bar's surface: the island fill, its 1px edge, 8 corners and the panel's drop shadow.</summary>
    internal static Border Surface(UIElement child, Thickness padding)
    {
        var bar = new Border
        {
            Child = child,
            Padding = padding,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 16, Direction = 270, Opacity = 0.32, Color = Colors.Black },
            SnapsToDevicePixels = true,
        };
        bar.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        bar.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        bar.SetResourceReference(TextElement.ForegroundProperty, "IslandTextBrush");
        return bar;
    }

    internal static LucideIcon Glyph(Geometry icon, string brush = "IslandTextSecondaryBrush", double size = 16)
    {
        var glyph = new LucideIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    internal static Button IconButton(Geometry icon, string name, double width, double height, string? hint = null)
    {
        var button = new Button { Content = Glyph(icon), Width = width, Height = height };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        button.ToolTip = hint ?? name;
        return button;
    }

    internal static Button TextButton(string text, bool accent = false, bool small = false)
    {
        var button = new Button
        {
            Content = text,
            Height = small ? 24 : 28,
            MinWidth = small ? 0 : 64,
            Padding = new Thickness(small ? 8 : 10, 0, small ? 8 : 10, 0),
            FontSize = small ? 11 : 12,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        AutomationProperties.SetName(button, text);
        return button;
    }

    internal static Border Rule(bool vertical)
    {
        var rule = vertical
            ? new Border { Width = 1, Height = 20, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }
            : new Border { Height = 1, Margin = new Thickness(2, 4, 2, 4) };
        rule.SetResourceReference(Border.BackgroundProperty, "IslandHairBrush");
        return rule;
    }

    /// <summary>A round swatch of an ink: a 2px island gap, then a hairline, so white and black read on both themes.</summary>
    internal static Border Swatch(Color color, double size)
    {
        var inner = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(color) };
        var gap = new Border { CornerRadius = new CornerRadius(size / 2 + 2), Padding = new Thickness(2), Child = inner };
        gap.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        var edge = new Border { CornerRadius = new CornerRadius(size / 2 + 3), BorderThickness = new Thickness(1), Child = gap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        edge.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush");
        return edge;
    }
}

/// <summary>A button that's on or off (a tool, Bold): lit with the accent tint, and a toggle to UI Automation.</summary>
internal sealed class ToggleIconButton : Button
{
    private readonly LucideIcon? _glyph;
    private bool _on;

    public ToggleIconButton(Geometry? icon, object? content = null)
    {
        SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        if (icon is not null)
            Content = _glyph = CaptureBars.Glyph(icon);
        else
            Content = content;
    }

    public bool IsOn
    {
        get => _on;
        set
        {
            if (_on == value)
                return;
            _on = value;
            Tag = value ? "Selected" : null;
            _glyph?.SetResourceReference(LucideIcon.BrushProperty, value ? "IslandAccentBrush" : "IslandTextSecondaryBrush");
            if (Content is TextBlock text)
                text.SetResourceReference(TextBlock.ForegroundProperty, value ? "IslandAccentBrush" : "IslandTextSecondaryBrush");
            (UIElementAutomationPeer.FromElement(this) as Peer)?.Changed(!value, value);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ToggleIconButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsOn ? ToggleState.On : ToggleState.Off;

        public void Toggle() => ((IInvokeProvider)this).Invoke();

        public override object GetPattern(PatternInterface pattern) => pattern == PatternInterface.Toggle ? this : base.GetPattern(pattern);

        public void Changed(bool was, bool now) => RaisePropertyChangedEvent(
            TogglePatternIdentifiers.ToggleStateProperty, was ? ToggleState.On : ToggleState.Off, now ? ToggleState.On : ToggleState.Off);
    }
}

/// <summary>
/// The markup column (Frozen screen v1, released-tool-column): Select and the ten tools, the ink's swatch, Undo and Redo,
/// 40 wide. Picking a tool lights it; the feature hears <see cref="ToolPicked"/> and turns <see cref="ICaptureSession.Tooling"/> on.
/// </summary>
public sealed class MarkupTools : Border
{
    private readonly Dictionary<CaptureTool, ToggleIconButton> _tools = [];
    private readonly ContentControl _swatch = new() { IsTabStop = false, Focusable = false };
    private readonly Button _undo, _redo;
    private CaptureTool? _tool;
    private Color _ink;

    public MarkupTools()
    {
        var column = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var tool in Enum.GetValues<CaptureTool>())
        {
            var button = new ToggleIconButton(CaptureBars.Icon(tool)) { Width = 32, Height = 30 };
            string name = ToolName(tool);
            AutomationProperties.SetName(button, name);
            button.ToolTip = CaptureBars.Letter(tool) is { } letter ? $"{name} · {letter}" : name;
            if (CaptureBars.Letter(tool) is { } key)
                AutomationProperties.SetAcceleratorKey(button, key.ToString());
            button.Click += (_, _) =>
            {
                Tool = tool;
                ToolPicked?.Invoke(tool);
            };
            _tools[tool] = button;
            column.Children.Add(button);
        }
        column.Children.Add(CaptureBars.Rule(vertical: false));
        var ink = new Button { Width = 32, Height = 30, Content = _swatch };
        ink.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(ink, "Color and width");
        ink.ToolTip = "Color and width";
        ink.Click += (_, _) => InkAsked?.Invoke(ink);
        InkButton = ink;
        column.Children.Add(ink);
        column.Children.Add(CaptureBars.Rule(vertical: false));
        _undo = CaptureBars.IconButton(LucideIcons.Undo, "Undo", 32, 30, "Undo · Ctrl+Z");
        _redo = CaptureBars.IconButton(LucideIcons.Redo, "Redo", 32, 30, "Redo · Ctrl+Y");
        _undo.Click += (_, _) => UndoAsked?.Invoke();
        _redo.Click += (_, _) => RedoAsked?.Invoke();
        column.Children.Add(_undo);
        column.Children.Add(_redo);

        var surface = CaptureBars.Surface(column, new Thickness(3));
        Child = surface;
        AutomationProperties.SetName(this, "Markup");
        Ink = CaptureBars.Inks[0].Color;
        CanUndo = CanRedo = false;
    }

    /// <summary>The tool lit, or none.</summary>
    public CaptureTool? Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            foreach (var (tool, button) in _tools)
                button.IsOn = tool == value;
        }
    }

    /// <summary>The ink's colour, shown in the swatch.</summary>
    public Color Ink
    {
        get => _ink;
        set
        {
            _ink = value;
            _swatch.Content = CaptureBars.Swatch(value, 14);
        }
    }

    public bool CanUndo { get => _undo.IsEnabled; set => _undo.IsEnabled = value; }

    public bool CanRedo { get => _redo.IsEnabled; set => _redo.IsEnabled = value; }

    /// <summary>The ink's button, for placing the colour choice beside it.</summary>
    public Button InkButton { get; }

    public event Action<CaptureTool>? ToolPicked;

    /// <summary>The ink's button was pressed: show an <see cref="InkChoice"/> beside it.</summary>
    public event Action<Button>? InkAsked;

    public event Action? UndoAsked, RedoAsked;

    internal ToggleIconButton ButtonFor(CaptureTool tool) => _tools[tool];

    public static string ToolName(CaptureTool tool) => tool switch
    {
        CaptureTool.Select => "Select",
        CaptureTool.Pen => "Pen",
        CaptureTool.Highlighter => "Highlighter",
        CaptureTool.Line => "Line",
        CaptureTool.Arrow => "Arrow",
        CaptureTool.Rectangle => "Rectangle",
        CaptureTool.Ellipse => "Ellipse",
        CaptureTool.Text => "Text",
        CaptureTool.Redact => "Redact",
        CaptureTool.Eraser => "Eraser",
        _ => "Crop",
    };
}

/// <summary>What the finish bar's buttons do.</summary>
public enum FinishAction { CopyText, ScanQrCode, SaveAs, Save, Copy, Cancel, Retry }

/// <summary>
/// The finish bar under the selection (Frozen screen v1, released-action-bar): Copy text and Scan QR code (each only when
/// its feature is on), Save as, Save, Copy (the main verb), and Cancel. 40 high. After a failed output the feature sets
/// <see cref="Failure"/>: the line ("Copy failed", "Copied. Save failed") and Retry take the place of Copy text and Scan
/// QR code, and Retry becomes the main verb.
/// </summary>
public sealed class FinishBar : Border
{
    private readonly Button _copyText, _scan, _copy, _retry;
    private readonly Border _lead;
    private readonly StackPanel _failure;
    private readonly TextBlock _failureText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private bool _showCopyText = true, _showScan = true;

    public FinishBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        // The failure line: the island's caution glyph and words, read out when it changes; not a button, so Tab skips it.
        _failureText.SetResourceReference(TextBlock.ForegroundProperty, "IslandCautionBrush");
        AutomationProperties.SetLiveSetting(_failureText, AutomationLiveSetting.Assertive);
        _failure = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Children = { CaptureBars.Glyph(LucideIcons.TriangleAlert, "IslandCautionBrush"), _failureText },
        };
        _retry = CaptureBars.TextButton("Retry", accent: true);
        _retry.Margin = new Thickness(10, 0, 0, 0);
        _retry.Visibility = Visibility.Collapsed;
        _retry.Click += (_, _) => Picked?.Invoke(FinishAction.Retry);
        row.Children.Add(_failure);
        row.Children.Add(_retry);
        _copyText = CaptureBars.IconButton(LucideIcons.ScanText, "Copy text", 32, 28);
        _scan = CaptureBars.IconButton(LucideIcons.QrCode, "Scan QR code", 32, 28);
        _scan.Margin = new Thickness(6, 0, 0, 0);
        _lead = CaptureBars.Rule(vertical: true);
        _lead.Margin = new Thickness(6, 0, 0, 0);
        var saveAs = CaptureBars.TextButton("Save as");
        var save = CaptureBars.TextButton("Save");
        var copy = CaptureBars.TextButton("Copy", accent: true);
        saveAs.Margin = save.Margin = copy.Margin = new Thickness(6, 0, 0, 0);
        saveAs.ToolTip = "Save as · Ctrl+Shift+S";
        save.ToolTip = "Save · Ctrl+S";
        copy.ToolTip = "Copy · Ctrl+C";
        var trail = CaptureBars.Rule(vertical: true);
        trail.Margin = new Thickness(6, 0, 0, 0);
        var cancel = CaptureBars.IconButton(LucideIcons.Close, "Cancel", 28, 28, "Cancel · Esc");
        cancel.Margin = new Thickness(6, 0, 0, 0);
        foreach (var (button, action) in new[] { (_copyText, FinishAction.CopyText), (_scan, FinishAction.ScanQrCode), (saveAs, FinishAction.SaveAs), (save, FinishAction.Save), (copy, FinishAction.Copy), (cancel, FinishAction.Cancel) })
        {
            button.Click += (_, _) => Picked?.Invoke(action);
            row.Children.Add(button);
            if (action == FinishAction.ScanQrCode)
                row.Children.Add(_lead);
            if (action == FinishAction.Copy)
                row.Children.Add(trail);
        }
        Child = CaptureBars.Surface(row, new Thickness(5));
        AutomationProperties.SetName(this, "Finish");
        _copy = copy;
    }

    /// <summary>Copy text shows while Text and QR is on (and no failure is shown).</summary>
    public bool ShowCopyText { get => _showCopyText; set { _showCopyText = value; Lead(); } }

    /// <summary>Scan QR code shows while Text and QR is on (and no failure is shown).</summary>
    public bool ShowScanQrCode { get => _showScan; set { _showScan = value; Lead(); } }

    /// <summary>Copy, the main verb while nothing has failed.</summary>
    public Button Copy => _copy;

    /// <summary>Retry, shown with <see cref="Failure"/>.</summary>
    public Button Retry => _retry;

    /// <summary>The main verb, for Enter's default: Retry while a failure shows, otherwise Copy.</summary>
    public Button Main => Failure is null ? _copy : _retry;

    /// <summary>
    /// The failed output's line ("Copy failed", "Save failed", "Copied. Save failed"), or null. Setting it shows the line
    /// and Retry (accented, focused when the bar is on screen); null puts Copy text, Scan QR code and Copy's accent back.
    /// The feature clears it once Retry, or any other output, succeeds.
    /// </summary>
    public string? Failure
    {
        get => _failure.Visibility == Visibility.Visible ? _failureText.Text : null;
        set
        {
            bool failed = !string.IsNullOrEmpty(value);
            string? was = Failure;
            _failureText.Text = failed ? value : "";
            _failure.Visibility = _retry.Visibility = Show(failed);
            _copy.SetResourceReference(StyleProperty, failed ? "IslandButtonStyle" : "IslandAccentButtonStyle");
            Lead();
            if (failed && was != value)
            {
                UIElementAutomationPeer.FromElement(_failureText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                if (IsVisible)
                    Dispatcher.BeginInvoke(() => _retry.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
    }

    public event Action<FinishAction>? Picked;

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void Lead()
    {
        bool failed = _failure.Visibility == Visibility.Visible;
        _copyText.Visibility = Show(_showCopyText && !failed);
        _scan.Visibility = Show(_showScan && !failed);
        _scan.Margin = new Thickness(_copyText.Visibility == Visibility.Visible ? 6 : 0, 0, 0, 0);
        _lead.Visibility = Show(failed || _copyText.Visibility == Visibility.Visible || _scan.Visibility == Visibility.Visible);
        ((FrameworkElement)Child).InvalidateMeasure();
    }
}

/// <summary>
/// The colour choice (Frozen screen v1, markup-color-choice): eight inks, Outline and Fill (for Rectangle and Ellipse,
/// <see cref="ShowFill"/>), Thin, Medium and Thick, and Pick from screen. The feature shows it beside the column's ink
/// button and closes it on a pick, Esc or a press elsewhere.
/// </summary>
public sealed class InkChoice : Border
{
    private readonly List<(Color Color, ToggleIconButton Button, Border Ring)> _inks = [];
    private readonly ToggleIconButton[] _widths;
    private readonly ToggleIconButton _outline, _fill;
    private readonly Border _shape;
    private Color _ink;
    private InkWidth _stroke;
    private bool _filled;

    public InkChoice()
    {
        var grid = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (name, color) in CaptureBars.Inks)
        {
            var dot = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(color), BorderThickness = new Thickness(1) };
            dot.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
            var ring = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = dot, BorderBrush = Brushes.Transparent };
            var button = new ToggleIconButton(null, ring) { Width = 40, Height = 40, Margin = new Thickness(2), Template = RingTemplate() };
            AutomationProperties.SetName(button, name);
            button.ToolTip = name;
            button.Click += (_, _) =>
            {
                Ink = color;
                InkPicked?.Invoke(color);
            };
            _inks.Add((color, button, ring));
            grid.Children.Add(button);
        }

        // Outline or Fill, for Rectangle and Ellipse only: the same trough as the widths, above them.
        _outline = Segment("Outline", () => Pick(filled: false));
        _fill = Segment("Fill", () => Pick(filled: true));
        _shape = Trough("Shape", [_outline, _fill]);
        _shape.Margin = new Thickness(0, 0, 0, 6);
        _shape.Visibility = Visibility.Collapsed;

        _widths = [.. Enum.GetValues<InkWidth>().Select(width => Segment(width.ToString(), () =>
        {
            Stroke = width;
            WidthPicked?.Invoke(width);
        }))];
        var trough = Trough("Width", _widths);

        var pickLabel = new TextBlock { Text = "Pick from screen", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        pickLabel.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        var pick = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { CaptureBars.Glyph(LucideIcons.Pipette, size: 14), pickLabel } },
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = double.NaN,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 6, 0, 0),
        };
        pick.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        pick.Width = double.NaN;
        AutomationProperties.SetName(pick, "Pick from screen");
        pick.Click += (_, _) => PickAsked?.Invoke();

        var hair = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 10) };
        hair.SetResourceReference(Border.BackgroundProperty, "IslandHairBrush");
        Child = CaptureBars.Surface(new StackPanel { Width = 196, Children = { grid, hair, _shape, trough, pick } }, new Thickness(7));
        AutomationProperties.SetName(this, "Color and width");
        Ink = CaptureBars.Inks[0].Color;
        Stroke = InkWidth.Medium;
        Filled = false;
    }

    /// <summary>Outline and Fill show: the feature sets this while Rectangle or Ellipse is the tool.</summary>
    public bool ShowFill
    {
        get => _shape.Visibility == Visibility.Visible;
        set => _shape.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fill is on (a filled rectangle or ellipse), otherwise Outline.</summary>
    public bool Filled
    {
        get => _filled;
        set
        {
            _filled = value;
            _outline.IsOn = !value;
            _fill.IsOn = value;
        }
    }

    /// <summary>Outline (false) or Fill (true) was picked.</summary>
    public event Action<bool>? FillPicked;

    private void Pick(bool filled)
    {
        Filled = filled;
        FillPicked?.Invoke(filled);
    }

    /// <summary>One choice in a trough (Thin, Outline): a small label that's lit while it's the choice.</summary>
    private static ToggleIconButton Segment(string text, Action picked)
    {
        var label = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        var button = new ToggleIconButton(null, label) { Width = double.NaN, Height = 20, Margin = new Thickness(1) };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => picked();
        return button;
    }

    private static Border Trough(string name, IEnumerable<ToggleIconButton> choices)
    {
        var row = new UniformGrid { Rows = 1, Margin = new Thickness(0) };
        foreach (var choice in choices)
            row.Children.Add(choice);
        var trough = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(1), Child = row, Height = 24 };
        trough.SetResourceReference(Border.BackgroundProperty, "IslandHoverBrush");
        AutomationProperties.SetName(trough, name);
        return trough;
    }

    public Color Ink
    {
        get => _ink;
        set
        {
            _ink = value;
            foreach (var (color, button, ring) in _inks)
            {
                button.IsOn = color == value;
                if (button.IsOn)
                    ring.SetResourceReference(Border.BorderBrushProperty, "IslandAccentBrush");
                else
                    ring.BorderBrush = Brushes.Transparent;
            }
        }
    }

    /// <summary>The ink's width.</summary>
    public InkWidth Stroke
    {
        get => _stroke;
        set
        {
            _stroke = value;
            for (int i = 0; i < _widths.Length; i++)
                _widths[i].IsOn = (InkWidth)i == value;
        }
    }

    public event Action<Color>? InkPicked;

    public event Action<InkWidth>? WidthPicked;

    /// <summary>Pick from screen: the feature picks a pixel off the frozen screen for the ink.</summary>
    public event Action? PickAsked;

    /// <summary>A swatch button: no fill of its own, its ring in the accent while it's the ink.</summary>
    private static ControlTemplate RingTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(BackgroundProperty, Brushes.Transparent);
        root.AppendChild(presenter);
        template.VisualTree = root;
        return template;
    }
}

/// <summary>A Text mark's bar under it (Frozen screen v1, markup-text-size): its size and Bold.</summary>
public sealed class TextMarkBar : Border
{
    private readonly Button _size;
    private readonly TextBlock _sizeText = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _menu = new() { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, VerticalOffset = 4 };
    private readonly ToggleIconButton _bold;
    private int _textSize;

    public TextMarkBar()
    {
        // The size is a menu button ("18 ▾"), like the island's other pickers; its menu lists the sizes.
        var chevron = CaptureBars.Glyph(LucideIcons.ChevronDown, size: 12);
        chevron.Margin = new Thickness(6, 0, 0, 0);
        _size = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _sizeText, chevron } },
            Height = 24,
            Padding = new Thickness(8, 0, 6, 0),
            MinWidth = 0,
        };
        _size.SetResourceReference(StyleProperty, "IslandButtonStyle");
        AutomationProperties.SetName(_size, "Text size");
        var list = new StackPanel { Margin = new Thickness(0) };
        foreach (int size in CaptureBars.TextSizes)
        {
            var item = new Button { Content = size.ToString(CultureInfo.CurrentCulture) };
            item.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
            item.Click += (_, _) =>
            {
                _menu.IsOpen = false;
                TextSize = size;
                SizePicked?.Invoke(size);
            };
            list.Children.Add(item);
        }
        _menu.PlacementTarget = _size;
        _menu.Child = CaptureBars.Surface(list, new Thickness(4));
        ((FrameworkElement)_menu.Child).MinWidth = 72;
        _size.Click += (_, _) => _menu.IsOpen = true;
        var b = new TextBlock { Text = "B", FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        b.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        _bold = new ToggleIconButton(null, b) { Width = 24, Height = 24, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(_bold, "Bold");
        _bold.ToolTip = "Bold · Ctrl+B";
        _bold.Click += (_, _) =>
        {
            _bold.IsOn = !_bold.IsOn;
            BoldChanged?.Invoke(_bold.IsOn);
        };
        // The menu sits in the bar's tree, so it finds the island's styles.
        Child = CaptureBars.Surface(new StackPanel { Orientation = Orientation.Horizontal, Children = { _size, _bold, _menu } }, new Thickness(3));
        AutomationProperties.SetName(this, "Text");
        TextSize = 18;
    }

    public int TextSize
    {
        get => _textSize;
        set
        {
            _textSize = value;
            _sizeText.Text = value.ToString(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>The size menu is open (for shots and tests).</summary>
    public bool MenuOpen { get => _menu.IsOpen; set => _menu.IsOpen = value; }

    public bool Bold { get => _bold.IsOn; set => _bold.IsOn = value; }

    public event Action<int>? SizePicked;

    public event Action<bool>? BoldChanged;
}

/// <summary>Crop's bar under the crop frame (Frozen screen v1, markup-crop-confirm): Cancel and Crop.</summary>
public sealed class CropBar : Border
{
    public CropBar()
    {
        var cancel = CaptureBars.TextButton("Cancel", small: true);
        var crop = CaptureBars.TextButton("Crop", accent: true, small: true);
        crop.Margin = new Thickness(4, 0, 0, 0);
        crop.IsDefault = true;
        cancel.Click += (_, _) => Cancelled?.Invoke();
        crop.Click += (_, _) => Cropped?.Invoke();
        Child = CaptureBars.Surface(new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, crop } }, new Thickness(3));
        AutomationProperties.SetName(this, "Crop");
    }

    public event Action? Cancelled, Cropped;
}
