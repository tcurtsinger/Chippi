// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Chippi.UI.Themes;

namespace Chippi.App.Island;

/// <summary>
/// Something new arriving in a place that keeps its size (DESIGN.md §5 Island, Motion): a new track on the bar, the
/// home's card and Now Playing, or the bar showing something else. A picture of what was there fades out in its
/// place over 90ms (<see cref="Leave"/>, before the change); the new content, already in place, fades in from 50ms to
/// 210ms and its text rises 4 (<see cref="Enter"/>, after it). The change itself happens at once, so anything reading
/// the content sees the new one. Under reduced motion nothing rises. A changed glyph (play and pause, the bars and the
/// pause mark) scales in instead (<see cref="Pop"/>).
/// </summary>
internal static class Arrival
{
    private static readonly TimeSpan LeaveTime = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan EnterDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan EnterTime = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan PopTime = TimeSpan.FromMilliseconds(120);
    private const double Rise = 4;
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Whether <paramref name="part"/> is on screen with a size, so a picture of it means something.</summary>
    internal static bool Showing(FrameworkElement part) => part.IsVisible && part.ActualWidth > 0 && part.ActualHeight > 0;

    /// <summary>
    /// Lays a picture of <paramref name="part"/> as it looks now over it, in <paramref name="ghost"/> (an Image in the
    /// same panel and cell), and fades it out.
    /// </summary>
    internal static void Leave(FrameworkElement part, Image ghost)
    {
        if (!Showing(part))
            return;
        ghost.BeginAnimation(UIElement.OpacityProperty, null);
        ghost.Source = Picture(part);
        ghost.Stretch = Stretch.Fill;
        ghost.IsHitTestVisible = false;
        ghost.Width = part.ActualWidth;
        ghost.Height = part.ActualHeight;
        ghost.Margin = part.Margin;
        // Where the part sits in its cell: a stretched part fills it, unless it has a size of its own, which WPF centers.
        ghost.HorizontalAlignment = part.HorizontalAlignment != HorizontalAlignment.Stretch ? part.HorizontalAlignment
            : double.IsNaN(part.Width) ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        ghost.VerticalAlignment = part.VerticalAlignment != VerticalAlignment.Stretch ? part.VerticalAlignment
            : double.IsNaN(part.Height) ? VerticalAlignment.Top : VerticalAlignment.Center;
        Grid.SetColumn(ghost, Grid.GetColumn(part));
        Grid.SetRow(ghost, Grid.GetRow(part));
        var source = ghost.Source;
        var fade = new DoubleAnimation(1, 0, LeaveTime);
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(ghost.Source, source))
                ghost.Source = null;
        };
        ghost.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary><paramref name="part"/>'s new content fades in as the old goes; text (<paramref name="rise"/>) also rises 4 into place.</summary>
    internal static void Enter(UIElement part, bool rise)
    {
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(EnterDelay)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(EnterDelay + EnterTime)));
        part.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        if (!rise || Motion.IsReduced)
            return;
        if (part.RenderTransform is not TranslateTransform move || move.IsFrozen)
            part.RenderTransform = move = new TranslateTransform();
        var up = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        up.KeyFrames.Add(new DiscreteDoubleKeyFrame(Rise, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        up.KeyFrames.Add(new DiscreteDoubleKeyFrame(Rise, KeyTime.FromTimeSpan(EnterDelay)));
        up.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(EnterDelay + EnterTime), EaseOut));
        move.BeginAnimation(TranslateTransform.YProperty, up, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>A glyph that just changed (play and pause, the bars and the pause mark) scales in from <paramref name="from"/> and fades in over 120ms; under reduced motion it only fades.</summary>
    internal static void Pop(UIElement glyph, double from = 0.8, TimeSpan? time = null)
    {
        var length = time ?? PopTime;
        glyph.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, length) { FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        if (Motion.IsReduced)
            return;
        glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        if (glyph.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
            glyph.RenderTransform = scale = new ScaleTransform();
        var grow = new DoubleAnimation(from, 1, length) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow, HandoffBehavior.SnapshotAndReplace);
    }

    // The part exactly as laid out, at the screen's scale: its layout box, not its ink's bounds. A VisualBrush draws
    // the part where its parent has it (its offset and transform included), so the box is taken in the parent's terms.
    private static BitmapSource Picture(FrameworkElement part)
    {
        var dpi = VisualTreeHelper.GetDpi(part);
        var box = new Rect(0, 0, part.ActualWidth, part.ActualHeight);
        var seen = VisualTreeHelper.GetParent(part) is Visual parent ? part.TransformToVisual(parent).TransformBounds(box) : box;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(part) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = seen, ViewportUnits = BrushMappingMode.Absolute, Viewport = box }, null, box);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(box.Width * dpi.DpiScaleX), (int)Math.Ceiling(box.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
