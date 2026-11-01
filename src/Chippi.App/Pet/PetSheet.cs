// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Config;
using Chippi.UI.Pet;

namespace Chippi.App.Pet;

/// <summary>
/// Debug builds only: renders ChippiPet mood sheets to PNG for review (<c>Chippi.exe --debug
/// --pet-sheet &lt;dir&gt;</c>). Each sheet is drawn on the dark and the light window color, at 1x,
/// with that theme's resources (the z's take its secondary island text).
/// </summary>
internal static class PetSheet
{
    /// <summary>The moods in the order the approved mock shows them.</summary>
    private static readonly PetMood[] Moods = [PetMood.Sleepy, PetMood.Idle, PetMood.Listening, PetMood.WideAwake, PetMood.Celebrating];

    public static void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var tokens = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") };
            var background = (Brush)tokens["WindowBackgroundBrush"];
            var caption = (Brush)tokens["TextTertiaryBrush"];
            string suffix = theme.ToLowerInvariant();

            // Every mood at the small form (20), the puck form (40), and above it (48, 72), per color.
            foreach (var color in Enum.GetValues<PetColor>())
                Render(Themed(theme, Sheet(background, caption, new[] { 20.0, 40, 48, 72 }.Select(size =>
                        ($"{size}", Moods.Select(m => ((FrameworkElement)Pet(size, m, color), SentenceCase(ChippiPet.Describe(m)))))))),
                    Path.Combine(directory, $"chippi-{color}-{suffix}.png"), 1);

            // The pet at 40 on the 50 puck, at 1x and 2x, on the mock's gray wallpaper.
            foreach (var scale in new[] { 1.0, 2.0 })
                Render(Themed(theme, Sheet(Wallpaper(), Brushes.White, [("", Moods.Select(m => (Puck(PetColor.Synth, m), SentenceCase(ChippiPet.Describe(m)))))])),
                    Path.Combine(directory, $"chippi-puck-{scale}x-{suffix}.png"), scale);

            Render(Themed(theme, Row(background, caption, Moods.Select(m => ((FrameworkElement)Pet(112, m, PetColor.Synth), SentenceCase(ChippiPet.Describe(m)))))),
                Path.Combine(directory, $"ws3-pet-moods-112-{suffix}.png"), 1);
            Render(Themed(theme, Row(background, caption, Enum.GetValues<PetColor>().Select(c => ((FrameworkElement)Pet(112, PetMood.Idle, c), c.ToString())))),
                Path.Combine(directory, $"ws3-pet-colors-112-{suffix}.png"), 1);
            Render(Themed(theme, Row(background, caption, new[] { 16.0, 20, 22, 28, 30 }.Select(s => ((FrameworkElement)Pet(s, PetMood.Idle, PetColor.Synth), $"{s}px")))),
                Path.Combine(directory, $"ws3-pet-small-{suffix}.png"), 1);
            Render(Themed(theme, Row(background, caption, new[] { 22.0 }.Select(s => ((FrameworkElement)Pet(s, PetMood.Idle, PetColor.Synth), "22px at 4x")))),
                Path.Combine(directory, $"ws3-pet-small-22-4x-{suffix}.png"), 4);
        }
    }

    /// <summary>The 50 puck as the widget draws it (island fill, hairline rim) with the pet at 40.</summary>
    private static FrameworkElement Puck(PetColor color, PetMood mood)
    {
        var puck = new Grid { Width = 50, Height = 50, HorizontalAlignment = HorizontalAlignment.Center };
        var disc = new System.Windows.Shapes.Ellipse { StrokeThickness = 1 };
        disc.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandBrush");
        disc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "IslandBorderBrush");
        puck.Children.Add(disc);
        var pet = Pet(40, mood, color);
        pet.VerticalAlignment = VerticalAlignment.Center;
        puck.Children.Add(pet);
        return puck;
    }

    private static Brush Wallpaper()
    {
        var brush = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0x7A, 0x76, 0x72), 0), new GradientStop(Color.FromRgb(0x5D, 0x5B, 0x59), 0.48), new GradientStop(Color.FromRgb(0x4B, 0x4A, 0x49), 1)],
            new Point(0, 0), new Point(0.35, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>Gives a sheet its theme's resources, so resource references in it resolve to that theme, not the app's.</summary>
    private static FrameworkElement Themed(string theme, FrameworkElement sheet)
    {
        sheet.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") });
        return sheet;
    }

    /// <summary>Rows of cells, each row led by its label (a size).</summary>
    private static FrameworkElement Sheet(Brush background, Brush caption, IEnumerable<(string Label, IEnumerable<(FrameworkElement Item, string Label)> Cells)> rows)
    {
        var stack = new StackPanel { Margin = new Thickness(8) };
        foreach (var (label, cells) in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (label.Length > 0)
                line.Children.Add(new TextBlock { Text = label, Foreground = caption, Width = 28, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var (item, text) in cells)
                line.Children.Add(Cell(caption, item, text));
            stack.Children.Add(line);
        }
        return new Border { Background = background, Child = stack };
    }

    /// <summary>
    /// Renders timed frames of a live 112px pet in an off-screen, non-activating window: the celebrate hop, spark
    /// burst and fade, falling asleep, a breath while asleep, waking, a blink, perking up for Keep awake and the yawn
    /// after it, a glance, a boop, landing after he's held, and a beat. File names carry "reduced" when
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
        // The hop: take-off squish, stretched rising, the top, touch down, the landing squish, settled; then the sparks' hold.
        await SaveStripAsync(host, caption, [0, 60, 100, 160, 200, 260, 320, 360, 380, 500, 1300, 2450], Path.Combine(directory, $"ws3-pet-motion-celebrate-{mode}.png"));

        pet.Mood = PetMood.Idle;
        await Task.Delay(500);
        pet.Mood = PetMood.Sleepy;
        await SaveStripAsync(host, caption, [0, 200, 400, 600, 680, 800, 1000, 1300], Path.Combine(directory, $"ws3-pet-motion-asleep-{mode}.png"));
        // Still asleep: one 4.2s breath, the z's drifting with it.
        await SaveStripAsync(host, caption, [0, 525, 1050, 1575, 2100, 2625, 3150, 3675, 4200], Path.Combine(directory, $"pet-motion-breathe-{mode}.png"));

        pet.Mood = PetMood.Idle;
        await SaveStripAsync(host, caption, [0, 40, 90, 140, 180, 230, 280, 400], Path.Combine(directory, $"pet-motion-wake-{mode}.png"));
        await Task.Delay(500);
        pet.BlinkNow();
        await SaveStripAsync(host, caption, [0, 25, 50, 60, 70, 95, 120, 200], Path.Combine(directory, $"ws3-pet-motion-blink-{mode}.png"));

        // Keep awake: he perks up as it starts, and when it ends the coffee wears off: a yawn, then asleep.
        await Task.Delay(300);
        pet.Mood = PetMood.WideAwake;
        await SaveStripAsync(host, caption, [0, 60, 120, 190, 260, 400], Path.Combine(directory, $"pet-motion-perk-{mode}.png"));
        await Task.Delay(300);
        pet.Mood = PetMood.Sleepy;
        await SaveStripAsync(host, caption, [0, 120, 250, 450, 700, 950, 1100, 1250, 1700], Path.Combine(directory, $"pet-motion-yawn-{mode}.png"));

        // Awake again: a glance to his left (the bar), a boop, picked up and set down.
        pet.Mood = PetMood.Idle;
        await Task.Delay(600);
        pet.Glance(-1);
        await SaveStripAsync(host, caption, [0, 150, 250, 700, 1200, 1350], Path.Combine(directory, $"pet-motion-glance-{mode}.png"));
        pet.Boop();
        await SaveStripAsync(host, caption, [0, 60, 110, 240, 400, 650], Path.Combine(directory, $"pet-motion-boop-{mode}.png"));
        pet.IsHeld = true;
        await Task.Delay(300);
        pet.IsHeld = false;
        await SaveStripAsync(host, caption, [0, 40, 80, 160, 260], Path.Combine(directory, $"pet-motion-land-{mode}.png"));

        // One face at a time: booped mid-yawn, the grin takes the yawn's place; picked up as he dozes off, the round eyes alone.
        await Task.Delay(400);
        pet.Yawn();
        await Task.Delay(300);
        pet.Boop();
        await SaveStripAsync(host, caption, [0, 60, 110, 240, 400, 650], Path.Combine(directory, $"pet-motion-boop-yawn-{mode}.png"));
        pet.Mood = PetMood.Sleepy;
        await Task.Delay(200);
        pet.IsHeld = true;
        await SaveStripAsync(host, caption, [0, 100, 300, 500, 700], Path.Combine(directory, $"pet-motion-held-dozing-{mode}.png"));
        pet.IsHeld = false;

        // Listening: a strong beat.
        pet.Mood = PetMood.Listening;
        await Task.Delay(500);
        pet.Beat(1);
        await SaveStripAsync(host, caption, [0, 30, 60, 120, 220], Path.Combine(directory, $"pet-motion-beat-{mode}.png"));

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

    private static FrameworkElement Row(Brush background, Brush caption, IEnumerable<(FrameworkElement Item, string Label)> cells)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        foreach (var (item, label) in cells)
            row.Children.Add(Cell(caption, item, label));
        return new Border { Background = background, Child = row };
    }

    private static FrameworkElement Cell(Brush caption, FrameworkElement item, string label)
    {
        var cell = new StackPanel { Margin = new Thickness(8), MinWidth = 72, VerticalAlignment = VerticalAlignment.Bottom };
        item.HorizontalAlignment = HorizontalAlignment.Center;
        cell.Children.Add(item);
        var text = new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        text.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTextStyle");
        text.Foreground = caption;
        cell.Children.Add(text);
        return cell;
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
