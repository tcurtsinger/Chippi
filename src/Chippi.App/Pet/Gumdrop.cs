// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Chippi.Core.Config;

namespace Chippi.App.Pet;

public enum PetMood
{
    Idle,
    Celebrating,
    Sleepy,
    WideAwake,
}

/// <summary>
/// Gumdrop, the pet (DESIGN.md §7). STUB seeded by the lead so WS1 and WS2 can place the pet
/// before WS3 lands: the public API (<see cref="Mood"/>, <see cref="Size"/>, <see cref="PetColor"/>)
/// is the contract, the gradient circle below is a placeholder. WS3 owns this folder and replaces
/// the body with the real geometry, moods and motion without changing the API.
/// </summary>
public class Gumdrop : UserControl
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(PetMood), typeof(Gumdrop), new PropertyMetadata(PetMood.Idle));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Gumdrop), new PropertyMetadata(28.0, OnSizeChanged));

    public static readonly DependencyProperty PetColorProperty = DependencyProperty.Register(
        nameof(PetColor), typeof(PetColor), typeof(Gumdrop), new PropertyMetadata(PetColor.Synth));

    public Gumdrop()
    {
        var body = new Ellipse();
        body.SetResourceReference(Shape.FillProperty, "AccentGradientBrush");
        Content = body;
        Width = Height = Size;
    }

    public PetMood Mood
    {
        get => (PetMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>Rendered edge in DIPs. At 22 and below the small form is drawn (DESIGN.md §7, Sizes).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public PetColor PetColor
    {
        get => (PetColor)GetValue(PetColorProperty);
        set => SetValue(PetColorProperty, value);
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pet = (Gumdrop)d;
        pet.Width = pet.Height = (double)e.NewValue;
    }
}
