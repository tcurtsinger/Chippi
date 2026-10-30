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
/// Debug builds only: renders ChippiPet mood sheets to PNG for review (<c>Chippi.exe --debug
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

            foreach (var color in Enum.GetValues<PetColor>())
                foreach (var size in new[] { 24.0, 32, 48, 72 })
                    Render(Row(background, caption, Enum.GetValues<PetMood>().Select(m => (Pet(size, m, color), ChippiPet.Describe(m)))),
                        Path.Combine(directory, $"chippi-{color}-{size}-{suffix}.png"), 1);
            Render(Row(background, caption, Enum.GetValues<PetMood>().Select(m => (Pet(112, m, PetColor.Synth), SentenceCase(ChippiPet.Describe(m))))),
                Path.Combine(directory, $"ws3-pet-moods-112-{suffix}.png"), 1);
            Render(Row(background, caption, Enum.GetValues<PetColor>().Select(c => (Pet(112, PetMood.Idle, c), c.ToString()))),
                Path.Combine(directory, $"ws3-pet-colors-112-{suffix}.png"), 1);
            Render(Row(background, caption, new[] { 16.0, 20, 22, 28, 30 }.Select(s => (Pet(s, PetMood.Idle, PetColor.Synth), $"{s}px"))),
                Path.Combine(directory, $"ws3-pet-small-{suffix}.png"), 1);
            Render(Row(background, caption, new[] { 22.0 }.Select(s => (Pet(s, PetMood.Idle, PetColor.Synth), "22px at 4x"))),
                Path.Combine(directory, $"ws3-pet-small-22-4x-{suffix}.png"), 4);
        }
    }

    /// <summary>
    /// Renders timed frames of a live 112px pet in an off-screen, non-activating window: the
    /// celebrate squish and spark fade, falling asleep, and a blink. File names carry "reduced" when
    /// <see cref="Chippi.UI.Themes.Motion.IsReduced"/> is on (then all frames should match).
    /// </summary>
    public static async Task SaveMotionAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var tokens = new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi.UI;component/Themes/Dark.xaml") };
        var background = (Brush)tokens["WindowBackgroundBrush"];
        var caption = (Brush)tokens["TextTertiaryBrush"];
        var pet = Pet(112, PetMood.Idle, PetColor.Synth);
        var host = new Border { Background = background, Padding = new Thickness(8), Child = pet };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -10000,
            Top = -10000,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = host,
        };
        window.Show();
        await Task.Delay(400);
        string mode = Chippi.UI.Themes.Motion.IsReduced ? "reduced" : "full";

        pet.Mood = PetMood.Celebrating;
        await SaveStripAsync(host, caption, [0, 60, 110, 180, 260, 400, 700, 1300], Path.Combine(directory, $"ws3-pet-motion-celebrate-{mode}.png"));

        pet.Mood = PetMood.Idle;
        await Task.Delay(500);
        pet.Mood = PetMood.Sleepy;
        await SaveStripAsync(host, caption, [0, 200, 400, 600, 680, 800, 1000, 1300], Path.Combine(directory, $"ws3-pet-motion-asleep-{mode}.png"));

        pet.Mood = PetMood.Idle;
        await Task.Delay(500);
        pet.BlinkNow();
        await SaveStripAsync(host, caption, [0, 25, 50, 60, 70, 95, 120, 200], Path.Combine(directory, $"ws3-pet-motion-blink-{mode}.png"));

        window.Close();
    }

    private static async Task SaveStripAsync(FrameworkElement host, Brush caption, int[] times, string path)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var frames = new List<(BitmapSource Image, long Ms)>();
        foreach (int t in times)
        {
            long wait = t - clock.ElapsedMilliseconds;
            if (wait > 0)
                await Task.Delay((int)wait);
            await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            var frame = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            frame.Render(host);
            frames.Add((frame, clock.ElapsedMilliseconds));
        }

        double w = host.ActualWidth, h = host.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w * frames.Count, h + 22));
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            for (int i = 0; i < frames.Count; i++)
            {
                dc.DrawImage(frames[i].Image, new Rect(i * w, 0, w, h));
                var label = new FormattedText($"{frames[i].Ms} ms", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, caption, 1.0);
                dc.DrawText(label, new Point(i * w + (w - label.Width) / 2, h + 4));
            }
        }
        var strip = new RenderTargetBitmap((int)(w * frames.Count), (int)(h + 22), 96, 96, PixelFormats.Pbgra32);
        strip.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(strip));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string SentenceCase(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static ChippiPet Pet(double size, PetMood mood, PetColor color) => new() { Size = size, Mood = mood, PetColor = color };

    private static FrameworkElement Row(Brush background, Brush caption, IEnumerable<(ChippiPet Pet, string Label)> cells)
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
