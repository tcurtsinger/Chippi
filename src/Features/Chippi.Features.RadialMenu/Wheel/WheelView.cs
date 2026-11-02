// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Chippi.Core.Config;
using Chippi.UI.Controls;
using Chippi.UI.Pet;
using static Chippi.Features.RadialMenu.Wheel.WheelGeometry;

namespace Chippi.Features.RadialMenu.Wheel;

/// <summary>What one place on the wheel shows: a slice's glyph or icon, a submenu's chevron, the missing mark, Back, or the editor's +.</summary>
public sealed record WheelSlot(Geometry Glyph, string Name)
{
    /// <summary>An app's or a file's own icon, shown 24 in place of the glyph.</summary>
    public ImageSource? Image { get; init; }

    public bool IsSubmenu { get; init; }

    public bool IsMissing { get; init; }

    public bool IsBack { get; init; }

    /// <summary>The editor's next free place.</summary>
    public bool IsPlus { get; init; }
}

/// <summary>How a slot is drawn: plain, pointed (or selected, in the editor), picked, or faded (the editor's dragged slice).</summary>
public enum SlotLook { Plain, Pointed, Picked, Faded }

/// <summary>
/// One wheel, 248 across (docs/phase-5.5/radial-menu.md, The wheel): the disc on the island's fill, the ring of slices
/// divided by hairlines with a glyph each, and the hub with Chippi and a line. At the pointer it carries the widget's rim
/// and the panel's shadow; Settings draws the same wheel flat. Brushes are the island's, so it follows the app theme.
/// </summary>
public sealed class WheelView : Canvas
{
    private readonly bool _floating;
    private readonly Ellipse _disc = new() { Width = Diameter, Height = Diameter, StrokeThickness = 1 };
    private readonly Ellipse _hub = new() { Width = 2 * HubRadius, Height = 2 * HubRadius, StrokeThickness = 1 };
    private readonly TextBlock _name = new()
    {
        Width = 2 * HubRadius - 12,
        Height = 16,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };
    private readonly List<Slot> _slots = [];

    public WheelView(bool floating, PetColor color = PetColor.Synth)
    {
        _floating = floating;
        Width = Diameter;
        Height = Diameter;
        SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        Paint(_disc, Shape.FillProperty, "IslandBrush");
        Paint(_disc, Shape.StrokeProperty, "IslandWidgetBorderBrush");
        if (floating)
            _disc.SetResourceReference(EffectProperty, "IslandExpandedShadow");
        Paint(_hub, Shape.FillProperty, "IslandBrush");
        Pet = new ChippiPet { Size = 48, Mood = PetMood.Idle, PetColor = color, IsDecorative = true };
        SetLeft(Pet, Middle - HubRadius + 23);
        SetTop(Pet, Middle - HubRadius + 12);
        SetLeft(_hub, Middle - HubRadius);
        SetTop(_hub, Middle - HubRadius);
        SetLeft(_name, Middle - HubRadius + 6);
        SetTop(_name, Middle - HubRadius + 62);
        Show([], 0);
    }

    /// <summary>Chippi in the hub, 48: idle, or wide awake while Keep awake runs; he looks toward the pointed slice.</summary>
    public ChippiPet Pet { get; }

    /// <summary>The slots shown, clockwise from <paramref name="first"/> degrees.</summary>
    public IReadOnlyList<WheelSlot> Slots { get; private set; } = [];

    public double First { get; private set; }

    /// <summary>Draws <paramref name="slots"/> clockwise from <paramref name="first"/>, all plain; none is the hub alone.</summary>
    public void Show(IReadOnlyList<WheelSlot> slots, double first)
    {
        Slots = slots;
        First = first;
        Children.Clear();
        _slots.Clear();
        bool alone = slots.Count == 0;
        if (!alone)
            Children.Add(_disc);
        double span = alone ? 360 : 360.0 / slots.Count;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = new Slot(slots[i], Normalize(first + i * span), span, slots.Count > 1);
            _slots.Add(slot);
            slot.AddTo(this);
        }
        // Alone, the hub carries the rim and the shadow the disc would.
        Paint(_hub, Shape.StrokeProperty, alone ? "IslandWidgetBorderBrush" : "IslandHairBrush");
        if (alone && _floating)
            _hub.SetResourceReference(EffectProperty, "IslandExpandedShadow");
        else
            _hub.Effect = null;
        Children.Add(_hub);
        Children.Add(Pet);
        Children.Add(_name);
    }

    public void SetLook(int index, SlotLook look)
    {
        if (index >= 0 && index < _slots.Count)
            _slots[index].Look(look);
    }

    /// <summary>Every slot plain but <paramref name="index"/>, which looks <paramref name="look"/>.</summary>
    public void SetLooks(int? index, SlotLook look)
    {
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].Look(i == index ? look : SlotLook.Plain);
    }

    public void SetHub(string text, HubTone tone)
    {
        _name.Text = text;
        _name.FontSize = tone == HubTone.Quiet ? 11 : 12;
        _name.FontWeight = tone == HubTone.Quiet ? FontWeights.Normal : FontWeights.SemiBold;
        Paint(_name, TextBlock.ForegroundProperty, tone switch
        {
            HubTone.Quiet => "IslandTextTertiaryBrush",
            HubTone.Caution => "IslandCautionBrush",
            _ => "IslandTextBrush",
        });
    }

    /// <summary>Which slot lies at <paramref name="at"/> (in the view), on the ring only; null in the hub or off the wheel.</summary>
    public int? SlotAt(Point at)
    {
        var offset = at - new Point(Middle, Middle);
        if (_slots.Count == 0 || offset.Length < HubRadius || offset.Length > RingOuter)
            return null;
        return SliceToward(offset, _slots.Count, First);
    }

    internal string HubText => _name.Text;

    internal SlotLook LookOf(int index) => _slots[index].Shown;

    private static void Paint(FrameworkElement element, DependencyProperty property, string brush) => element.SetResourceReference(property, brush);

    /// <summary>One slot's tint, hairline, glyph, chevron and mark.</summary>
    private sealed class Slot
    {
        private readonly WheelSlot _slot;
        private readonly Path _tint;
        private readonly Line? _hairline;
        private readonly FrameworkElement _glyph;
        private readonly Path? _chevron;
        private readonly LucideIcon? _mark;

        public Slot(WheelSlot slot, double angle, double span, bool divided)
        {
            _slot = slot;
            _tint = new Path { Data = Tint(new Point(Middle, Middle), angle, span), Visibility = Visibility.Hidden };
            if (divided)
            {
                var from = At(new Point(Middle, Middle), RingInner, angle - span / 2);
                var to = At(new Point(Middle, Middle), RingOuter, angle - span / 2);
                _hairline = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, StrokeThickness = 1 };
                _hairline.SetResourceReference(Shape.StrokeProperty, "IslandHairBrush");
            }
            var glyphAt = At(new Point(Middle, Middle), GlyphRadius, angle);
            double size = 24;
            if (slot.Image is { } image)
                _glyph = new Image { Source = image, Width = size, Height = size };
            else
            {
                // Glyphs 20, the editor's + 16; an app's or a file's own icon 24.
                size = slot.IsPlus ? 16 : 20;
                var icon = new LucideIcon { Data = slot.Glyph, Size = size };
                // Back's arrow points along its slice, back toward the wheel before.
                if (slot.IsBack)
                    icon.RenderTransform = new RotateTransform(angle - 270, size / 2, size / 2);
                _glyph = icon;
            }
            SetLeft(_glyph, glyphAt.X - size / 2);
            SetTop(_glyph, glyphAt.Y - size / 2);
            if (slot.IsSubmenu)
            {
                var chevronAt = At(new Point(Middle, Middle), ChevronRadius, angle);
                _chevron = new Path
                {
                    Data = Chevron,
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Width = 10,
                    Height = 10,
                    RenderTransform = new RotateTransform(angle, 5, 5),
                };
                SetLeft(_chevron, chevronAt.X - 5);
                SetTop(_chevron, chevronAt.Y - 5);
            }
            if (slot.IsMissing)
            {
                _mark = new LucideIcon { Data = LucideIcons.TriangleAlert, Size = 11 };
                _mark.SetResourceReference(LucideIcon.BrushProperty, "IslandCautionBrush");
                SetLeft(_mark, glyphAt.X + 6);
                SetTop(_mark, glyphAt.Y - 15);
            }
            Look(SlotLook.Plain);
        }

        /// <summary>A small chevron pointing out, in a 10 square, turned to its slice.</summary>
        private static readonly Geometry Chevron = Geometry.Parse("M2,6.5 L5,3.5 L8,6.5");

        public SlotLook Shown { get; private set; }

        public void AddTo(WheelView view)
        {
            view.Children.Add(_tint);
            if (_hairline is not null)
                view.Children.Add(_hairline);
            view.Children.Add(_glyph);
            if (_chevron is not null)
                view.Children.Add(_chevron);
            if (_mark is not null)
                view.Children.Add(_mark);
        }

        public void Look(SlotLook look)
        {
            Shown = look;
            _tint.Visibility = look is SlotLook.Pointed or SlotLook.Picked ? Visibility.Visible : Visibility.Hidden;
            string tint = look == SlotLook.Picked ? "IslandAccentFillBrush" : "IslandAccentTintBrush";
            _tint.SetResourceReference(Shape.FillProperty, tint);
            string glyph = look switch
            {
                SlotLook.Picked => "IslandOnAccentBrush",
                _ when _slot.IsMissing || _slot.IsPlus => "IslandTextTertiaryBrush",
                SlotLook.Pointed => "IslandAccentBrush",
                _ when _slot.IsBack => "IslandTextSecondaryBrush",
                _ => "IslandTextBrush",
            };
            if (_glyph is LucideIcon icon)
                icon.SetResourceReference(LucideIcon.BrushProperty, glyph);
            _glyph.Opacity = look == SlotLook.Faded ? 0.4 : _slot.IsMissing ? 0.55 : 1;
            _chevron?.SetResourceReference(Shape.StrokeProperty, look switch
            {
                SlotLook.Picked => "IslandOnAccentBrush",
                SlotLook.Pointed => "IslandAccentBrush",
                _ => "IslandTextTertiaryBrush",
            });
        }
    }
}

/// <summary>A wheel left behind for a submenu: a 40 hub with the submenu's glyph, which goes back to its wheel when clicked.</summary>
public sealed class WheelCrumb : Border
{
    public WheelCrumb(Geometry glyph)
    {
        Width = CrumbSize;
        Height = CrumbSize;
        CornerRadius = new CornerRadius(CrumbSize / 2);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "IslandBrush");
        SetResourceReference(BorderBrushProperty, "IslandWidgetBorderBrush");
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.16 };
        var icon = new LucideIcon { Data = glyph, Size = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        Child = icon;
    }
}
