// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.App.Pet;
using Windows.Win32.Foundation;

namespace Chippi.App.Island;

/// <summary>
/// What a drag shows (DESIGN.md §5 Island): the 44px bubble with Gumdrop that follows the pointer,
/// and the 3px accent line on the target edge. Topmost no-activate windows placed in physical
/// pixels; the tab window keeps the mouse captured, so they never take input.
/// </summary>
internal sealed class IslandOverlay : Window
{
    /// <summary>The bubble's window, in DIPs: the 44px bubble and room for its 0 8px 24px shadow.</summary>
    public const double BubbleWindow = 108;

    private IslandOverlay(UIElement content)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Top = -32000;
        Width = Height = 1;
        Content = content;
    }

    public HWND Handle { get; private set; }

    public Gumdrop? Pet { get; private init; }

    public void Initialize() => Handle = IslandNative.MakeNoActivate(this);

    public void ShowAt(Rect physical)
    {
        if (!IsVisible)
            Show();
        IslandNative.Move(Handle, physical);
    }

    public static IslandOverlay Bubble()
    {
        var shadow = new Ellipse { Width = 44, Height = 44 };
        shadow.SetResourceReference(Shape.FillProperty, "IslandBrush");
        shadow.SetResourceReference(EffectProperty, "IslandShadow");
        var face = new Ellipse { Width = 44, Height = 44, StrokeThickness = 1 };
        face.SetResourceReference(Shape.FillProperty, "IslandBrush");
        face.SetResourceReference(Shape.StrokeProperty, "IslandTabBorderBrush");
        var pet = new Gumdrop { Size = 30, Mood = PetMood.Idle, IsDecorative = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var stage = new Grid { Width = BubbleWindow, Height = BubbleWindow, Children = { shadow, face, pet } };
        // The window can land on a monitor with another DPI before it is resized; the Viewbox keeps the drawing whole.
        return new IslandOverlay(new Viewbox { Child = stage }) { Pet = pet };
    }

    public static IslandOverlay Line()
    {
        var line = new Border { CornerRadius = new CornerRadius(1.5) };
        line.SetResourceReference(Border.BackgroundProperty, "IslandAccentBrush");
        return new IslandOverlay(line);
    }
}
