// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>Which surface a part draws on: the island's brushes and sizes, or Settings'.</summary>
internal sealed record Look(string Text, string Secondary, string Tertiary, string Track, string Fill, string Hair,
    double Hero, double Unit, double Name, double Small)
{
    public static Look Island { get; } = new("IslandTextBrush", "IslandTextSecondaryBrush", "IslandTextTertiaryBrush",
        "IslandTrackBrush", "IslandTextSecondaryBrush", "IslandHairBrush", 24, 14, 12, 11);

    public static Look Settings { get; } = new("TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush",
        "BorderStrongBrush", "AccentBrush", "BorderBrush", 28, 16, 12, 12);

    public const string Caution = "WarningTextBrush";
}

/// <summary>Small builders the System page's widgets and detail pages share.</summary>
internal static class Parts
{
    public static TextBlock Text(string text, double size, string brush, bool semibold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (semibold) block.FontWeight = FontWeights.SemiBold;
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    public static LucideIcon Glyph(Geometry data, double size, string brush)
    {
        var icon = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    /// <summary>An island card (the card's fill and edge, 6 corners), 12 by 10 inside its border.</summary>
    public static Border Card(UIElement? child = null)
    {
        var card = new Border { Child = child, Padding = new Thickness(11, 9, 11, 9) };
        card.SetResourceReference(FrameworkElement.StyleProperty, "IslandCardStyle");
        return card;
    }

    /// <summary>The top line of a widget: glyph, name, and one fact at the right.</summary>
    public static DockPanel TopLine(Geometry glyph, string name, Look look, out TextBlock fact)
    {
        fact = Text("", look.Small, look.Tertiary);
        fact.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(fact, Dock.Right);
        var icon = Glyph(glyph, 14, look.Tertiary);
        icon.Margin = new Thickness(0, 0, 6, 0);
        var label = Text(name, look.Name, look.Tertiary);
        return new DockPanel { Height = 16, LastChildFill = false, Children = { fact, icon, label } };
    }

    /// <summary>"23%" as "23" and "%", "2.4 MiB/s" as "2.4" and "MiB/s"; text that isn't a value has no unit.</summary>
    public static (string Number, string Unit, bool Spaced) Split(string value)
    {
        if (value.EndsWith('%'))
            return (value[..^1], "%", false);
        int space = value.LastIndexOf(' ');
        return space > 0 && char.IsDigit(value[space - 1]) ? (value[..space], value[(space + 1)..], true) : (value, "", false);
    }

    /// <summary>"1 d 22 h", "3 h 12 min", "12 min".</summary>
    public static string UpTime(TimeSpan time) =>
        time.TotalDays >= 1 ? $"{(int)time.TotalDays} d {time.Hours} h"
        : time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min"
        : $"{Math.Max(0, (int)time.TotalMinutes)} min";

    /// <summary>A GPU's name without its maker's marks: "GeForce RTX 4080", "Radeon Graphics", "Arc Graphics".</summary>
    public static string ShortName(string name)
    {
        string s = name.Replace("(R)", "", StringComparison.OrdinalIgnoreCase).Replace("(TM)", "", StringComparison.OrdinalIgnoreCase);
        foreach (string maker in new[] { "NVIDIA ", "AMD ", "Intel " })
            if (s.StartsWith(maker, StringComparison.OrdinalIgnoreCase) && s.Length > maker.Length)
                s = s[maker.Length..];
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly Regex ProcessorSuffix = new(@"\s+(\d+-Core Processor|Processor|CPU|with Radeon Graphics)\s*$", RegexOptions.IgnoreCase);

    /// <summary>A processor's name without its maker's marks and core count: "AMD Ryzen 9 7950X", "Intel Core i7-8700K".</summary>
    public static string ProcessorName(string name)
    {
        string s = name.Replace("(R)", "", StringComparison.OrdinalIgnoreCase).Replace("(TM)", "", StringComparison.OrdinalIgnoreCase);
        int at = s.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0) s = s[..at];
        // Twice, for a name that ends in two of them ("… CPU" after "with Radeon Graphics").
        for (int i = 0; i < 2; i++)
            s = ProcessorSuffix.Replace(s, "");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Names for a list of devices, "#2" after a name that came before.</summary>
    public static IReadOnlyList<string> Distinct(IEnumerable<string> names)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string name in names)
        {
            int count = seen[name] = seen.GetValueOrDefault(name) + 1;
            result.Add(count == 1 ? name : $"{name} #{count}");
        }
        return result;
    }

    /// <summary>Up to two initials for an app without an icon: "GC" for Google Chrome.</summary>
    public static string Initials(string name)
    {
        var words = Regex.Split(name, @"[^\p{L}\p{Nd}]+").Where(w => w.Length > 0).ToList();
        return words.Count switch
        {
            0 => name.Length > 0 ? name[..1].ToUpper(CultureInfo.CurrentCulture) : "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpper(CultureInfo.CurrentCulture),
            _ => string.Concat(words.Take(2).Select(w => char.ToUpper(w[0], CultureInfo.CurrentCulture))),
        };
    }
}

/// <summary>A big number with a smaller unit after it; text that isn't a number shows small and quiet.</summary>
internal sealed class Hero : TextBlock
{
    private readonly Look _look;
    private readonly double _size, _unitSize;
    private readonly Run _number = new(), _unit = new();
    private string _value = "\0";
    private bool _caution;

    public Hero(Look look, double? size = null, double? unitSize = null)
    {
        _look = look;
        _size = size ?? look.Hero;
        _unitSize = unitSize ?? look.Unit;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        TextTrimming = TextTrimming.CharacterEllipsis;
        VerticalAlignment = VerticalAlignment.Center;
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        _unit.FontWeight = FontWeights.SemiBold;
        Inlines.Add(_number);
        Inlines.Add(_unit);
        Set("");
    }

    /// <summary>The value shown, as Format writes it: "23%", "412 GiB".</summary>
    public string Value => _value;

    public void Set(string value, bool caution = false, bool quiet = false)
    {
        if (value == _value && caution == _caution)
            return;
        _value = value;
        _caution = caution;
        var (number, unit, spaced) = quiet ? (value, "", false) : Parts.Split(value);
        _number.Text = number;
        _unit.Text = unit.Length == 0 ? "" : (spaced ? " " : "") + unit;
        bool reading = unit.Length > 0 || value.Length > 0 && (char.IsDigit(value[0]) || value.Length > 1 && value[0] == '+' && char.IsDigit(value[1]));
        FontSize = reading ? _size : Math.Min(_look.Name, _size);
        FontWeight = reading ? FontWeights.SemiBold : FontWeights.Normal;
        _unit.FontSize = _unitSize;
        SetResourceReference(ForegroundProperty, caution ? Look.Caution : reading ? _look.Text : _look.Tertiary);
        _unit.SetResourceReference(TextElement.ForegroundProperty, caution ? Look.Caution : _look.Secondary);
    }
}

/// <summary>A 4 tall meter: the track and the filled share from the left.</summary>
internal sealed class Meter : Grid
{
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(2) };
    private readonly Look _look;
    private double _value = -1;
    private bool _caution;

    public Meter(Look look)
    {
        _look = look;
        Height = 4;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, look.Track);
        SetColumnSpan(track, 2);
        Children.Add(track);
        Children.Add(_fill);
        Set(0);
    }

    /// <summary>The filled share, 0 to 1.</summary>
    public double Value => _value;

    public void Set(double share, bool caution = false)
    {
        share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
        if (share == _value && caution == _caution)
            return;
        _value = share;
        _caution = caution;
        ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
        ColumnDefinitions[1].Width = new GridLength(1 - share, GridUnitType.Star);
        _fill.Visibility = share > 0 ? Visibility.Visible : Visibility.Collapsed;
        _fill.SetResourceReference(Border.BackgroundProperty, caution ? Look.Caution : _look.Fill);
    }
}

/// <summary>A label, a value at the right and a meter under them (a detail page's dedicated or committed memory).</summary>
internal sealed class MeterLine : StackPanel
{
    public MeterLine(string label)
    {
        Label = Parts.Text(label, 11, Look.Island.Tertiary);
        Amount = Parts.Text("", 11, Look.Island.Secondary);
        DockPanel.SetDock(Amount, Dock.Right);
        Children.Add(new DockPanel { Height = 12, Children = { Amount, Label } });
        Meter = new Meter(Look.Island) { Margin = new Thickness(0, 2, 0, 0) };
        Children.Add(Meter);
    }

    public TextBlock Label { get; }

    public TextBlock Amount { get; }

    public Meter Meter { get; }
}
