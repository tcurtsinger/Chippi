// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>What a widget opens, and what each detail page shows.</summary>
internal enum Detail { Cpu, Gpu, Memory, Network, Drives, Battery, Temperature }

/// <summary>
/// One reading's widget: glyph and name with one fact at the right, a big value, a line of context
/// and a meter. On the island it's a button that opens its detail page; in Settings, a card.
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

    /// <summary>The widget's content: lines from the top, a meter at the bottom, filling the card.</summary>
    public DockPanel Body { get; }

    protected void Add(UIElement element, Dock dock = Dock.Top)
    {
        DockPanel.SetDock(element, dock);
        Body.Children.Add(element);
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
            var button = new Button { Content = body, Padding = new Thickness(11, 9, 11, 9), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.SetResourceReference(FrameworkElement.StyleProperty, "IslandCardButtonStyle");
            // The card button centers its content; the body fills it instead, so a row's widgets line up
            // top and bottom (12 by 10 inside the 1 border).
            button.SizeChanged += (_, e) => body.Height = Math.Max(0, e.NewSize.Height - 20);
            if (open is not null)
                button.Click += (_, _) => open();
            return button;
        }
        var card = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(15, 11, 15, 11) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }
}

/// <summary>A widget with one value: CPU, GPU, Memory, Battery, Temperature.</summary>
internal sealed class ReadingWidget : Widget
{
    public ReadingWidget(Detail kind, Geometry glyph, string name, Look look, Action<Detail>? open, bool meter) : base(kind, glyph, name, look, open)
    {
        Hero = new Hero(look) { Margin = new Thickness(0, look == Look.Island ? 6 : 4, 0, 0) };
        Context = Parts.Text("", look.Small, look.Tertiary);
        Context.Height = 16;
        Add(Hero);
        Add(Context);
        if (meter)
        {
            Meter = new Meter(look) { Margin = new Thickness(0, 4, 0, 0) };
            Add(Meter, Dock.Bottom);
        }
    }

    public Hero Hero { get; }

    public TextBlock Context { get; }

    public Meter? Meter { get; }

    public void Set(string value, string context, double? share, bool caution = false, bool quiet = false)
    {
        Hero.Set(value, caution, quiet);
        Context.Text = context;
        Context.ToolTip = context.Length > 0 ? context : null;
        Meter?.Set(share ?? 0, caution);
        Describe(value, context);
    }
}

/// <summary>Download and upload: side by side across the whole width, or download over upload in a narrow one.</summary>
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
            var twin = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            twin.ColumnDefinitions.Add(new ColumnDefinition());
            twin.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            twin.ColumnDefinitions.Add(new ColumnDefinition());
            var up = new StackPanel { Orientation = Orientation.Horizontal, Children = { _upArrow, _up } };
            Grid.SetColumn(up, 2);
            twin.Children.Add(down);
            twin.Children.Add(up);
            Add(twin);
        }
        else
        {
            down.Margin = new Thickness(0, look == Look.Island ? 6 : 4, 0, 0);
            Add(down);
            _upArrow.Margin = new Thickness(0, 0, 3, 0);
            Add(new StackPanel { Orientation = Orientation.Horizontal, Height = 16, Children = { _upArrow, _upLine } });
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
/// Free space: one column per drive for up to 4, or the 3 fullest (an alerting one first) and "+N";
/// a single drive across the whole width.
/// </summary>
internal sealed class DrivesWidget : Widget
{
    public const int Columns = 4;
    private readonly Grid _columns = new();
    private readonly TextBlock _status;
    private readonly Func<string, bool> _alerting;
    private string _key = "";

    public DrivesWidget(Look look, Action<Detail>? open, Func<string, bool> alerting) : base(Detail.Drives, Icons.HardDrive, "Drives", look, open)
    {
        _alerting = alerting;
        _status = Parts.Text("", look.Name, look.Tertiary);
        _status.Margin = new Thickness(0, 8, 0, 0);
        _columns.Margin = new Thickness(0, 8, 0, 0);
        Add(_status);
        Body.LastChildFill = true; // the columns fill the rest, their meters at the bottom
        Body.Children.Add(_columns);
    }

    /// <summary>The drives shown, in order (tests read them).</summary>
    internal IReadOnlyList<string> Shown { get; private set; } = [];

    internal string More { get; private set; } = "";

    /// <summary>Which volumes show, in letter order: all up to 4; past that the 3 fullest, alerting ones first.</summary>
    internal static (IReadOnlyList<VolumeReading> Shown, int More) Choose(IReadOnlyList<VolumeReading> volumes, Func<string, bool> alerting)
    {
        if (volumes.Count <= Columns)
            return (volumes, 0);
        var keep = volumes.OrderByDescending(v => alerting(v.Id)).ThenBy(v => v.FreePercent).Take(Columns - 1).ToHashSet();
        return ([.. volumes.Where(keep.Contains)], volumes.Count - keep.Count);
    }

    public void Set(SystemSnapshot snapshot)
    {
        if (snapshot.Disks.Status == ReadingStatus.Unavailable || snapshot.Disks.Value is null)
        {
            _status.Text = snapshot.Disks.Status == ReadingStatus.Unavailable ? "Unavailable" : "";
            _status.Visibility = Visibility.Visible;
            _columns.Visibility = Visibility.Collapsed;
            SetFact("");
            Shown = [];
            Describe(_status.Text);
            return;
        }
        _status.Visibility = Visibility.Collapsed;
        _columns.Visibility = Visibility.Visible;
        var volumes = Volumes(snapshot.Disks.Value);
        var (shown, more) = Choose(volumes, _alerting);
        SetFact(volumes.Count == 1 ? Letter(volumes[0]) : volumes.Count > 1 ? $"{volumes.Count} drives" : "");
        string key = string.Join("|", shown.Select(v => v.Id)) + "+" + more;
        if (key != _key)
        {
            _key = key;
            Build(shown, more);
        }
        for (int i = 0; i < shown.Count; i++)
            ((DriveColumn)_columns.Children[i]).Set(shown[i], _alerting(shown[i].Id));
        Shown = [.. shown.Select(Letter)];
        More = more > 0 ? $"+{more}" : "";
        Describe(string.Join(", ", shown.Select(v => $"{Letter(v)} {Format.Size(v.FreeBytes)} free")), More.Length > 0 ? $"{more} more" : "");
    }

    private void Build(IReadOnlyList<VolumeReading> shown, int more)
    {
        _columns.Children.Clear();
        _columns.ColumnDefinitions.Clear();
        int count = shown.Count + (more > 0 ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            _columns.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (int i = 0; i < shown.Count; i++)
        {
            var column = new DriveColumn(Look, solo: count == 1);
            Grid.SetColumn(column, i * 2);
            _columns.Children.Add(column);
        }
        if (more > 0)
        {
            var hero = new Hero(Look, Look == Look.Island ? 18 : 20, 12);
            hero.Set($"+{more}");
            hero.SetResourceReference(TextBlock.ForegroundProperty, Look.Secondary);
            var label = Parts.Text("more drives", Look.Small, Look.Tertiary);
            var cell = new StackPanel { Children = { new Border { Height = 16 }, hero, label } };
            hero.Margin = new Thickness(0, 2, 0, 0);
            Grid.SetColumn(cell, (count - 1) * 2);
            _columns.Children.Add(cell);
        }
    }

    internal static IReadOnlyList<VolumeReading> Volumes(IReadOnlyList<DiskReading> disks) =>
        [.. disks.SelectMany(d => d.Volumes).DistinctBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v.Letter.Length == 0).ThenBy(v => v.Letter, StringComparer.OrdinalIgnoreCase)];

    internal static string Letter(VolumeReading volume) => volume.Letter.Length > 0 ? volume.Letter : "Volume";

    /// <summary>One drive: its letter and size, its free space, and a meter of what's used at the bottom.</summary>
    private sealed class DriveColumn : DockPanel
    {
        private readonly TextBlock _letter, _total, _of;
        private readonly Hero _free;
        private readonly Meter _meter;
        private readonly bool _solo;

        public DriveColumn(Look look, bool solo)
        {
            _solo = solo;
            _letter = Parts.Text("", look.Name, look.Tertiary);
            _total = Parts.Text("", look.Small, look.Tertiary);
            DockPanel.SetDock(_total, Dock.Right);
            _free = new Hero(look, solo ? look.Hero : look == Look.Island ? 18 : 20, 12);
            var free = Parts.Text(" free", look.Small, look.Tertiary);
            free.VerticalAlignment = VerticalAlignment.Bottom;
            free.Margin = new Thickness(0, 0, 0, solo ? 5 : 3);
            _of = Parts.Text("", look.Small, look.Tertiary);
            DockPanel.SetDock(_of, Dock.Right);
            _of.VerticalAlignment = VerticalAlignment.Bottom;
            _of.Margin = free.Margin;
            _meter = new Meter(look) { Margin = new Thickness(0, 6, 0, 0) };
            LastChildFill = false;
            if (!solo)
            {
                var top = new DockPanel { Height = 16, Children = { _total, _letter } };
                SetDock(top, Dock.Top);
                Children.Add(top);
            }
            var heroLine = new DockPanel { LastChildFill = false };
            if (solo) heroLine.Children.Add(_of);
            heroLine.Children.Add(_free);
            heroLine.Children.Add(free);
            SetDock(heroLine, Dock.Top);
            SetDock(_meter, Dock.Bottom);
            Children.Add(heroLine);
            Children.Add(_meter);
        }

        public void Set(VolumeReading volume, bool caution)
        {
            _letter.Text = Letter(volume);
            _total.Text = Format.Size(volume.TotalBytes);
            _of.Text = _solo ? "of " + Format.Size(volume.TotalBytes) : "";
            _free.Set(Format.Size(volume.FreeBytes), caution);
            _meter.Set(volume.TotalBytes == 0 ? 0 : 1 - (double)volume.FreeBytes / volume.TotalBytes, caution);
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
