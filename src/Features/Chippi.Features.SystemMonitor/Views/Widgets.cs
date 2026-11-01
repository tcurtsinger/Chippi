// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>What a widget opens, and what each detail page shows.</summary>
internal enum Detail { Cpu, Gpu, Memory, Network, Drives, Battery, Temperature }

/// <summary>
/// One reading's widget: glyph and name with one fact at the right, a big value, a line of context,
/// then the reading's graph out to the card's edges, or a meter for a level. On the island it's a
/// button that opens its detail page; in Settings, a card.
/// </summary>
internal class Widget
{
    private readonly TextBlock _fact;

    public Widget(Detail kind, Geometry glyph, string name, Look look, Action<Detail>? open)
    {
        Kind = kind;
        Name = name;
        Look = look;
        Body = new DockPanel { LastChildFill = false };
        Add(Parts.TopLine(glyph, name, look, out _fact));
        Root = Host(Body, look, open is null ? null : () => open(kind));
        AutomationProperties.SetName(Root, name);
    }

    public Detail Kind { get; }

    public string Name { get; }

    public Look Look { get; }

    /// <summary>The widget's content: lines from the top, a meter or graph at the bottom, filling the card.</summary>
    public DockPanel Body { get; }

    protected void Add(UIElement element, Dock dock = Dock.Top)
    {
        DockPanel.SetDock(element, dock);
        Body.Children.Add(element);
    }

    /// <summary>A graph that fills the rest of the card, past its padding to its sides and bottom.</summary>
    protected HistoryGraph AddGraph()
    {
        var graph = new HistoryGraph(Look)
        {
            Midline = false, Baseline = false, BottomRadius = Look.InnerRadius,
            Margin = new Thickness(-Look.Padding.Left, 0, -Look.Padding.Right, -Look.Padding.Bottom),
        };
        Body.LastChildFill = true;
        Body.Children.Add(graph);
        return graph;
    }

    /// <summary>The island's card button, or Settings' card.</summary>
    public FrameworkElement Root { get; }

    public string Fact => _fact.Text;

    public void SetFact(string fact, bool caution = false)
    {
        _fact.Text = fact;
        _fact.ToolTip = fact.Length > 0 ? fact : null;
        _fact.SetResourceReference(TextBlock.ForegroundProperty, caution ? Look.Caution : Look.Tertiary);
    }

    /// <summary>Names the widget for UI Automation with what it shows ("CPU, 23%, 4.18 GHz").</summary>
    public void Describe(params string[] parts) =>
        AutomationProperties.SetName(Root, string.Join(", ", new[] { Name }.Concat(parts.Where(p => p.Length > 0))));

    private static FrameworkElement Host(FrameworkElement body, Look look, Action? open)
    {
        if (look == Look.Island)
        {
            var button = new Button { Content = body, Padding = look.Padding, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.SetResourceReference(FrameworkElement.StyleProperty, "IslandCardButtonStyle");
            // The card button centers its content; the body fills it instead, so a row's widgets line up
            // top and bottom (12 by 10 inside the 1 border).
            button.SizeChanged += (_, e) => body.Height = Math.Max(0, e.NewSize.Height - 20);
            if (open is not null)
                button.Click += (_, _) => open();
            return button;
        }
        var card = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = look.Padding };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }
}

/// <summary>
/// A widget with one value: CPU, GPU, Memory and Temperature with their graphs, and Battery, a level,
/// with a meter (4 minutes of a battery would be a flat line).
/// </summary>
internal sealed class ReadingWidget : Widget
{
    public ReadingWidget(Detail kind, Geometry glyph, string name, Look look, Action<Detail>? open) : base(kind, glyph, name, look, open)
    {
        Hero = new Hero(look) { Margin = new Thickness(0, 4, 0, 0) };
        Context = Parts.Text("", look.Small, look.Tertiary);
        Context.Height = 16;
        Add(Hero);
        Add(Context);
        if (kind == Detail.Battery)
        {
            Meter = new Meter(look) { Margin = new Thickness(0, 4, 0, 0) };
            Add(Meter, Dock.Bottom);
        }
        else
            Graph = AddGraph();
    }

    public Hero Hero { get; }

    public TextBlock Context { get; }

    public Meter? Meter { get; }

    public HistoryGraph? Graph { get; }

    public void Set(string value, string context, double? share, bool caution = false, bool quiet = false)
    {
        Hero.Set(value, caution, quiet);
        Context.Text = context;
        Context.ToolTip = context.Length > 0 ? context : null;
        Meter?.Set(share ?? 0, caution);
        Describe(value, context);
    }
}

/// <summary>
/// Download and upload with their graph. Alone in its row: the two side by side on the left and the
/// graph across the rest, its oldest readings fading in. Beside other widgets: download over upload,
/// the graph under them.
/// </summary>
internal sealed class NetworkWidget : Widget
{
    private readonly bool _wide;
    private readonly Hero _down, _up;
    private readonly TextBlock _upLine;
    private readonly FrameworkElement _upArrow, _downArrow;

    public NetworkWidget(Look look, Action<Detail>? open, bool wide) : base(Detail.Network, Icons.Network, "Network", look, open)
    {
        _wide = wide;
        _down = new Hero(look);
        _up = new Hero(look);
        _downArrow = Arrow(LucideIcons.ArrowDown, 16);
        _upArrow = Arrow(LucideIcons.ArrowUp, wide ? 16 : 11);
        var down = new StackPanel { Orientation = Orientation.Horizontal, Children = { _downArrow, _down } };
        _upLine = Parts.Text("", look.Small, look.Tertiary);
        if (wide)
        {
            // The values' column ends where the graph starts: 245 into the island's 496, 345 into Settings' 760.
            var row = new Grid { Margin = new Thickness(0, look == Look.Island ? 4 : 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(look == Look.Island ? 233 : 329) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var up = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), Children = { _upArrow, _up } };
            row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { down, up } });
            Graph = new HistoryGraph(look)
            {
                Midline = false, Baseline = false, BottomRadius = look.InnerRadius, RoundLeft = false, FadeIn = 40,
                Margin = new Thickness(0, 0, -look.Padding.Right, -look.Padding.Bottom),
            };
            Grid.SetColumn(Graph, 1);
            row.Children.Add(Graph);
            Body.LastChildFill = true;
            Body.Children.Add(row);
        }
        else
        {
            down.Margin = new Thickness(0, 4, 0, 0);
            Add(down);
            _upArrow.Margin = new Thickness(0, 0, 3, 0);
            Add(new StackPanel { Orientation = Orientation.Horizontal, Height = 16, Children = { _upArrow, _upLine } });
            Graph = AddGraph();
        }

        FrameworkElement Arrow(Geometry data, double size)
        {
            var glyph = Parts.Glyph(data, size, look.Tertiary);
            glyph.Margin = new Thickness(0, 0, size > 12 ? 4 : 3, 0);
            return glyph;
        }
    }

    public string Down => _down.Value;

    public string Up => _wide ? _up.Value : _upLine.Text;

    public bool Wide => _wide;

    public HistoryGraph Graph { get; }

    public void Set(string adapter, string? down, string? up, string status = "")
    {
        SetFact(adapter);
        bool rates = down is not null && up is not null;
        _downArrow.Visibility = _upArrow.Visibility = rates ? Visibility.Visible : Visibility.Collapsed;
        _down.Set(rates ? down! : status, quiet: !rates);
        if (_wide) _up.Set(rates ? up! : "");
        else _upLine.Text = rates ? up! : "";
        Describe(rates ? $"down {down}, up {up}" : status, adapter);
    }
}

/// <summary>
/// Free space in rows of 20, two drives to a row: the letter, a meter of what's used and what's free.
/// <see cref="Rows"/> rows hold every drive, or the fullest (an alerting one first) and "+N more" in the
/// last place. A single drive is one row with its size.
/// </summary>
internal sealed class DrivesWidget : Widget
{
    public const double RowHeight = 20;
    private readonly Grid _cells = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _status;
    private readonly Func<string, bool> _alerting;
    private string _key = "";

    public DrivesWidget(Look look, Action<Detail>? open, Func<string, bool> alerting) : base(Detail.Drives, Icons.HardDrive, "Drives", look, open)
    {
        _alerting = alerting;
        _status = Parts.Text("", look.Name, look.Tertiary);
        _status.Margin = new Thickness(0, 8, 0, 0);
        Add(_status);
        Add(_cells);
    }

    /// <summary>How many rows of drives the overview gives the widget, 1 to 3.</summary>
    public int Rows { get; set; } = 3;

    /// <summary>The widget's height for <paramref name="rows"/> rows: its padding and border, the top line, 4, and the rows.</summary>
    public static double Height(int rows) => 20 + 16 + 4 + rows * RowHeight;

    /// <summary>The drives shown, in order (tests read them).</summary>
    internal IReadOnlyList<string> Shown { get; private set; } = [];

    internal string More { get; private set; } = "";

    /// <summary>
    /// Which volumes show in <paramref name="places"/>, in letter order: all that fit; past that the
    /// fullest, alerting ones first, leaving the last place for "+N more" when <paramref name="morePlace"/>.
    /// </summary>
    internal static (IReadOnlyList<VolumeReading> Shown, int More) Choose(IReadOnlyList<VolumeReading> volumes, Func<string, bool> alerting, int places, bool morePlace = true)
    {
        if (volumes.Count <= places)
            return (volumes, 0);
        var keep = volumes.OrderByDescending(v => alerting(v.Id)).ThenBy(v => v.FreePercent).Take(Math.Max(0, morePlace ? places - 1 : places)).ToHashSet();
        return ([.. volumes.Where(keep.Contains)], volumes.Count - keep.Count);
    }

    public void Set(SystemSnapshot snapshot)
    {
        if (snapshot.Disks.Status == ReadingStatus.Unavailable || snapshot.Disks.Value is null)
        {
            _status.Text = snapshot.Disks.Status == ReadingStatus.Unavailable ? "Unavailable" : "";
            _status.Visibility = Visibility.Visible;
            _cells.Visibility = Visibility.Collapsed;
            SetFact("");
            Shown = [];
            Describe(_status.Text);
            return;
        }
        _status.Visibility = Visibility.Collapsed;
        _cells.Visibility = Visibility.Visible;
        var volumes = Volumes(snapshot.Disks.Value);
        bool solo = volumes.Count == 1;
        var (shown, more) = Choose(volumes, _alerting, solo ? 1 : Rows * 2);
        SetFact(volumes.Count > 1 ? $"{volumes.Count} drives" : "");
        string key = string.Join("|", shown.Select(v => v.Id)) + "+" + more + "/" + Rows;
        if (key != _key)
        {
            _key = key;
            Build(shown.Count, more, solo);
        }
        for (int i = 0; i < shown.Count; i++)
            ((DriveCell)_cells.Children[i]).Set(shown[i], _alerting(shown[i].Id), solo);
        Shown = [.. shown.Select(Letter)];
        More = more > 0 ? $"+{more}" : "";
        Describe(string.Join(", ", shown.Select(v => $"{Letter(v)} {Format.Size(v.FreeBytes)} free")), More.Length > 0 ? $"{more} more" : "");
    }

    // Two to a row, 24 apart, filling across then down; a single drive across the whole width.
    private void Build(int count, int more, bool solo)
    {
        _cells.Children.Clear();
        _cells.ColumnDefinitions.Clear();
        _cells.RowDefinitions.Clear();
        int across = solo ? 1 : 2;
        for (int c = 0; c < across; c++)
        {
            if (c > 0) _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            _cells.ColumnDefinitions.Add(new ColumnDefinition());
        }
        int places = count + (more > 0 ? 1 : 0);
        for (int r = 0; r < (places + across - 1) / across; r++)
            _cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        for (int i = 0; i < places; i++)
        {
            FrameworkElement cell = i < count ? new DriveCell(Look) : Parts.Text($"+{more} more", Look.Name, Look.Secondary);
            Grid.SetRow(cell, i / across);
            Grid.SetColumn(cell, i % across * 2);
            _cells.Children.Add(cell);
        }
    }

    internal static IReadOnlyList<VolumeReading> Volumes(IReadOnlyList<DiskReading> disks) =>
        [.. disks.SelectMany(d => d.Volumes).DistinctBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v.Letter.Length == 0).ThenBy(v => v.Letter, StringComparer.OrdinalIgnoreCase)];

    internal static string Letter(VolumeReading volume) => volume.Letter.Length > 0 ? volume.Letter : "Volume";

    /// <summary>One drive's row: its letter, a meter of what's used, and "704 GiB free" (of its size when alone).</summary>
    private sealed class DriveCell : Grid
    {
        private readonly TextBlock _letter, _text;
        private readonly Run _free = new() { FontWeight = FontWeights.SemiBold }, _rest = new();
        private readonly Meter _meter;
        private readonly Look _look;

        public DriveCell(Look look)
        {
            _look = look;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 76 });
            _letter = Parts.Text("", look.Name, look.Secondary);
            _meter = new Meter(look) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            _text = Parts.Text("", look.Small, look.Tertiary);
            _text.TextAlignment = TextAlignment.Right;
            _free.FontSize = look.Name;
            _text.Inlines.Add(_free);
            _text.Inlines.Add(_rest);
            SetColumn(_meter, 1);
            SetColumn(_text, 2);
            Children.Add(_letter);
            Children.Add(_meter);
            Children.Add(_text);
        }

        public void Set(VolumeReading volume, bool caution, bool solo)
        {
            _letter.Text = Letter(volume);
            _free.Text = Format.Size(volume.FreeBytes);
            _free.SetResourceReference(TextElement.ForegroundProperty, caution ? Look.Caution : _look.Text);
            _rest.Text = solo ? " free of " + Format.Size(volume.TotalBytes) : " free";
            _meter.Set(volume.TotalBytes == 0 ? 0 : 1 - (double)volume.FreeBytes / volume.TotalBytes, caution);
            ToolTip = $"{Letter(volume)} {Format.Size(volume.FreeBytes)} free of {Format.Size(volume.TotalBytes)}";
        }
    }
}

/// <summary>The glyphs the System page adds to Lucide's (Lucide is ISC-licensed; GPU is drawn in the same stroke).</summary>
internal static class Icons
{
    private static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    public static Geometry Gpu { get; } = G("M2,6 H19 a3,3 0 0 1 3,3 V15 a3,3 0 0 1 -3,3 H6 M2,4 V20 M10.5,9 a3,3 0 1 0 0,6 a3,3 0 1 0 0,-6 Z M17,10 V14");
    public static Geometry MemoryStick { get; } = G("M6,19 v-3 M10,19 v-3 M14,19 v-3 M18,19 v-3 M8,11 V9 M16,11 V9 M12,11 V9 M2,15 h20 M2,7 a2,2 0 0 1 2,-2 h16 a2,2 0 0 1 2,2 v1.1 a2,2 0 0 0 0,3.837 V17 a2,2 0 0 1 -2,2 H4 a2,2 0 0 1 -2,-2 v-5.1 a2,2 0 0 0 0,-3.837 Z");
    public static Geometry Network { get; } = G("M17,16 H21 a1,1 0 0 1 1,1 V21 a1,1 0 0 1 -1,1 H17 a1,1 0 0 1 -1,-1 V17 a1,1 0 0 1 1,-1 Z M3,16 H7 a1,1 0 0 1 1,1 V21 a1,1 0 0 1 -1,1 H3 a1,1 0 0 1 -1,-1 V17 a1,1 0 0 1 1,-1 Z M10,2 H14 a1,1 0 0 1 1,1 V7 a1,1 0 0 1 -1,1 H10 a1,1 0 0 1 -1,-1 V3 a1,1 0 0 1 1,-1 Z M5,16 v-3 a1,1 0 0 1 1,-1 h12 a1,1 0 0 1 1,1 v3 M12,12 V8");
    public static Geometry HardDrive { get; } = G("M22,12 H2 M5.45,5.11 L2,12 v6 a2,2 0 0 0 2,2 h16 a2,2 0 0 0 2,-2 v-6 l-3.45,-6.89 A2,2 0 0 0 16.76,4 H7.24 a2,2 0 0 0 -1.79,1.11 Z M6,16 h0.01 M10,16 h0.01");
    public static Geometry Battery { get; } = G("M4,7 H16 a2,2 0 0 1 2,2 V15 a2,2 0 0 1 -2,2 H4 a2,2 0 0 1 -2,-2 V9 a2,2 0 0 1 2,-2 Z M22,11 v2 M6,11 v2 M10,11 v2");
    public static Geometry Thermometer { get; } = G("M14,4 v10.54 a4,4 0 1 1 -4,0 V4 a2,2 0 0 1 4,0 Z");
}
