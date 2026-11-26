// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Chippi.Core.Config;
using Chippi.UI.Controls;
using Chippi.UI.Pet;
using Chippi.UI.Themes;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>What leads the island's line: nothing, a glyph in the bar's ink, a song's cover, a recording's red dot, or a glyph on the amber disc of something waiting on the user.</summary>
public enum IslandLead { None, Glyph, Cover, Recording, Attention }

/// <summary>What ends it: nothing, the equalizer of a song playing, or the mark of one paused.</summary>
public enum IslandTail { None, Equalizer, Paused }

/// <summary>What rides beside it: a state the bar has no icon for.</summary>
public enum IslandBadge { MicMuted, Awake, Agent }

/// <summary>
/// What the island shows (docs/mac-style/brief.md, Island): Chippi, then the highest activity as a lead, a line and a
/// tail. <paramref name="Module"/> is the feature it stands for, so that feature gets no badge beside it.
/// </summary>
public sealed record BarIslandFace(PetMood Mood, IslandLead Lead, Geometry? Glyph, ImageSource? Cover, string Text, string Detail, IslandTail Tail, string? Module, string Name)
{
    /// <summary>Nothing going on: Chippi alone.</summary>
    public static BarIslandFace Rest(PetMood mood) => new(mood, IslandLead.None, null, null, "", "", IslandTail.None, null, "Chippi");

    public bool IsRest => Lead == IslandLead.None && Text.Length == 0;
}

/// <summary>
/// The island: what's live, in the middle of the main display's menu bar. 22 tall inside the 28 bar, with a 1px edge; it
/// only widens. Its text, glyphs and edge are the bar's ink, taken from the wallpaper under it; Chippi, a cover, the
/// amber disc and the red dot keep their own colours. A press opens its page in the spot.
/// </summary>
internal sealed class BarIsland : Border
{
    public const double Tall = 22, Top = 3;
    private const double Gap = 6, MaxLine = 240;

    private static readonly Brush Critical = Frozen(new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)));
    private static readonly Brush Amber = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x87, 0x5B)));

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ChippiPet _pet = new() { Size = 18, IsDecorative = true, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<UIElement> _inked = [];
    private Run? _detail;
    private Brush _hover = Brushes.Transparent;
    private BarIslandFace? _face;
    private string _badges = "";

    public event Action? Pressed;
    public event Action? MenuAsked;

    public BarIsland()
    {
        Height = Tall;
        CornerRadius = new CornerRadius(Tall / 2);
        BorderThickness = new Thickness(1);
        Background = Brushes.Transparent; // takes the press where nothing is drawn
        SnapsToDevicePixels = true;
        Child = _row;
        ToolTipService.SetInitialShowDelay(this, 700);
        ToolTipService.SetPlacement(this, PlacementMode.Bottom);
        MouseEnter += (_, _) => Background = _hover;
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonDown += (_, e) => { e.Handled = true; Pressed?.Invoke(); };
        MouseRightButtonUp += (_, e) => { e.Handled = true; MenuAsked?.Invoke(); };
    }

    /// <summary>Shows <paramref name="face"/> with <paramref name="badges"/> beside it. Built again only when what shows changes.</summary>
    public void Show(BarIslandFace face, IReadOnlyList<IslandBadge> badges, PetColor color)
    {
        _pet.PetColor = color;
        _pet.ShowMood(face.Mood);
        string key = string.Join(',', badges);
        if (face with { Mood = PetMood.Idle } == (_face is { } now ? now with { Mood = PetMood.Idle } : null) && key == _badges)
            return;
        (_face, _badges) = (face, key);
        _row.Children.Clear();
        _inked.Clear();
        _detail = null;
        _row.Children.Add(_pet);
        if (Lead(face) is { } lead)
            Add(lead);
        if (face.Text.Length > 0)
        {
            var line = new TextBlock { FontSize = 12, MaxWidth = MaxLine, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            line.FontFeatures();
            line.Inlines.Add(new Run(face.Text) { FontWeight = FontWeights.SemiBold });
            if (face.Detail.Length > 0)
                line.Inlines.Add(_detail = new Run(" · " + face.Detail));
            Add(Inked(line));
        }
        if (face.Tail == IslandTail.Equalizer)
            Add(Bars([(5, 12, 0.42), (5, 12, 0.36), (5, 12, 0.5)], 2, animate: true));
        else if (face.Tail == IslandTail.Paused)
            Add(Bars([(10, 10, 0), (10, 10, 0)], 3, animate: false));
        foreach (var badge in badges)
            Add(Badge(badge));
        bool alone = _row.Children.Count == 1;
        Padding = new Thickness(4, 0, alone ? 4 : 8, 0);
        AutomationProperties.SetName(this, face.Name);
        ToolTip = face.Name;
    }

    private void Add(UIElement element)
    {
        if (element is FrameworkElement framework)
            framework.Margin = new Thickness(Gap, 0, 0, 0);
        _row.Children.Add(element);
    }

    private T Inked<T>(T element) where T : UIElement
    {
        _inked.Add(element);
        return element;
    }

    private UIElement? Lead(BarIslandFace face) => face.Lead switch
    {
        IslandLead.Glyph when face.Glyph is { } glyph => Inked(new LucideIcon { Data = glyph, Size = 14, VerticalAlignment = VerticalAlignment.Center }),
        IslandLead.Cover when face.Cover is { } art => new Ellipse { Width = 16, Height = 16, Fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill }, VerticalAlignment = VerticalAlignment.Center },
        IslandLead.Cover => Inked(new LucideIcon { Data = LucideIcons.Music, Size = 14, VerticalAlignment = VerticalAlignment.Center }),
        IslandLead.Recording => new Ellipse { Width = 10, Height = 10, Fill = Critical, VerticalAlignment = VerticalAlignment.Center },
        IslandLead.Attention => Disc(face.Glyph ?? LucideIcons.Gauge, Amber),
        _ => null,
    };

    private UIElement Badge(IslandBadge badge) => badge switch
    {
        IslandBadge.MicMuted => Disc(LucideIcons.MicOff, Critical),
        IslandBadge.Agent => Disc(LucideIcons.Gauge, Amber),
        _ => Inked(new LucideIcon { Data = LucideIcons.Coffee, Size = 13, VerticalAlignment = VerticalAlignment.Center }),
    };

    /// <summary>What needs the eye keeps its colour, whatever the ink: a white glyph on a 16 disc.</summary>
    private static Border Disc(Geometry glyph, Brush fill) => new()
    {
        Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = fill, VerticalAlignment = VerticalAlignment.Center,
        Child = new LucideIcon { Data = glyph, Size = 10, Brush = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>3-wide bars in the ink, from the bottom: the equalizer's move, the pause mark's don't.</summary>
    private StackPanel Bars(IReadOnlyList<(double From, double To, double Seconds)> bars, double gap, bool animate)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < bars.Count; i++)
        {
            var (from, to, seconds) = bars[i];
            var bar = new Rectangle { Width = 3, Height = animate && Motion.IsReduced ? 8 : to, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(i == 0 ? 0 : gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            if (animate && !Motion.IsReduced)
                bar.BeginAnimation(HeightProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            panel.Children.Add(Inked(bar));
        }
        return panel;
    }

    /// <summary>The bar's ink for this part of the wallpaper: the text, the glyphs, the bars and the edge.</summary>
    public void SetInk(BarInk ink)
    {
        bool light = ink == BarInk.Light;
        var text = Frozen(new SolidColorBrush(light ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A)));
        var second = Frozen(new SolidColorBrush(light ? Color.FromRgb(0xD5, 0xD5, 0xD5) : Color.FromRgb(0x3A, 0x3A, 0x3A)));
        var shadow = light
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.45 }
            : new DropShadowEffect { Color = Colors.White, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.45 };
        shadow.Freeze();
        _hover = Frozen(new SolidColorBrush(light ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0)));
        if (IsMouseOver)
            Background = _hover;
        BorderBrush = Frozen(new SolidColorBrush(light ? Color.FromArgb(0x57, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x57, 0x1A, 0x1A, 0x1A)));
        foreach (var element in _inked)
        {
            switch (element)
            {
                case TextBlock block:
                    block.Foreground = text;
                    block.Effect = shadow;
                    break;
                case LucideIcon icon:
                    icon.Brush = text;
                    icon.Effect = shadow;
                    break;
                case Rectangle bar:
                    bar.Fill = text;
                    break;
            }
        }
        if (_detail is not null)
            _detail.Foreground = second;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
