// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Chippi.UI.Pet;
using Chippi.UI.Themes;

namespace Chippi.Features.CleaningMode.Screens;

/// <summary>
/// The main screen's middle (docs/phase-5.5/cleaning-mode.md, Screens), top to bottom: Chippi at 200, idle; 32 below him
/// "Press Esc 3 times to unlock" in 16 Muted; 16 below that three 10 dots, 12 apart, each an outline until its press fills
/// it with the island's violet; and 32 below, Unlock. The screen is black in either theme, so the colors are fixed.
/// </summary>
internal sealed class CleaningFace : UserControl
{
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x9D, 0x9D, 0x9D));
    private static readonly Brush DotEdge = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Violet = Frozen(Color.FromRgb(0xB7, 0x9C, 0xFF)); // the island's accent on dark

    private readonly Ellipse[] _dots = new Ellipse[UnlockCount.Needed];
    private readonly StackPanel _dotRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TranslateTransform _rise = new();

    public CleaningFace()
    {
        // His face and anything themed draw as on dark, on the black.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi.UI;component/Themes/Dark.xaml") });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi.Features.CleaningMode;component/Screens/Cleaning.xaml") });
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Pet = new ChippiPet { Size = PetSize.Cleaning, Mood = PetMood.Idle, IsDecorative = true, HorizontalAlignment = HorizontalAlignment.Center, RenderTransform = _rise };
        var hint = new TextBlock { Text = "Press Esc 3 times to unlock", FontSize = 16, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 32, 0, 0) };
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i] = new Ellipse { Width = 10, Height = 10, StrokeThickness = 1.5, Stroke = DotEdge, Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0) };
            _dotRow.Children.Add(_dots[i]);
        }
        Unlock = new Button { Content = "Unlock", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 32, 0, 0) };
        Unlock.SetResourceReference(StyleProperty, "CleaningUnlockStyle");
        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Pet, hint, _dotRow, Unlock },
        };
        Count(0, squish: false);
    }

    public ChippiPet Pet { get; }

    public Button Unlock { get; }

    /// <summary>The dots filled so far, read out as "2 of 3".</summary>
    public int Filled { get; private set; }

    /// <summary>Fills <paramref name="presses"/> dots; <paramref name="squish"/>: Chippi squishes with the grin, as a boop does.</summary>
    public void Count(int presses, bool squish)
    {
        Filled = presses;
        for (int i = 0; i < _dots.Length; i++)
        {
            bool on = i < presses;
            _dots[i].Fill = on ? Violet : Brushes.Transparent;
            _dots[i].Stroke = on ? Violet : DotEdge;
        }
        AutomationProperties.SetName(_dotRow, $"{presses} of {_dots.Length}");
        if (squish && !Motion.IsReduced)
            Pet.Boop();
    }

    /// <summary>Unlocked: the dots full, and Chippi rises 16 and celebrates, sparks and all. Under reduced motion only the dots.</summary>
    public void Celebrate()
    {
        Count(_dots.Length, squish: false);
        if (Motion.IsReduced)
            return;
        _rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-16, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Pet.Celebrate();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
