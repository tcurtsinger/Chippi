// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Config;

namespace Chippi.App.Pet;

/// <summary>
/// Debug builds only: renders Gumdrop mood sheets to PNG for review (<c>Chippi.exe --debug
/// --pet-sheet &lt;dir&gt;</c>). Each sheet is drawn on the dark and the light window color, at 1x.
/// </summary>
internal static class PetSheet
{
    public static void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var tokens = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") };
            var background = (Brush)tokens["WindowBackgroundBrush"];
            var caption = (Brush)tokens["TextTertiaryBrush"];
            string suffix = theme.ToLowerInvariant();

            Render(Row(background, caption, Enum.GetValues<PetMood>().Select(m => (Pet(112, m, PetColor.Synth), Gumdrop.Describe(m)))),
                Path.Combine(directory, $"ws3-pet-moods-112-{suffix}.png"), 1);
            Render(Row(background, caption, Enum.GetValues<PetColor>().Select(c => (Pet(112, PetMood.Idle, c), c.ToString()))),
                Path.Combine(directory, $"ws3-pet-colors-112-{suffix}.png"), 1);
            Render(Row(background, caption, new[] { 16.0, 20, 22, 28, 30 }.Select(s => (Pet(s, PetMood.Idle, PetColor.Synth), $"{s}px"))),
                Path.Combine(directory, $"ws3-pet-small-{suffix}.png"), 1);
            Render(Row(background, caption, new[] { 22.0 }.Select(s => (Pet(s, PetMood.Idle, PetColor.Synth), "22px at 4x"))),
                Path.Combine(directory, $"ws3-pet-small-22-4x-{suffix}.png"), 4);
        }
    }

    private static Gumdrop Pet(double size, PetMood mood, PetColor color) => new() { Size = size, Mood = mood, PetColor = color };

    private static FrameworkElement Row(Brush background, Brush caption, IEnumerable<(Gumdrop Pet, string Label)> cells)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        foreach (var (pet, label) in cells)
        {
            var cell = new StackPanel { Margin = new Thickness(8), MinWidth = 64 };
            pet.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(pet);
            var text = new TextBlock { Text = label, Foreground = caption, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            text.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTextStyle");
            text.Foreground = caption;
            cell.Children.Add(text);
            row.Children.Add(cell);
        }
        return new Border { Background = background, Child = row };
    }

    private static void Render(FrameworkElement element, string path, double scale)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
#endif
