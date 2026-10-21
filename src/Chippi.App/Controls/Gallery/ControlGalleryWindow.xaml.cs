// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Themes;
using Chippi.Core.Config;
using Chippi.Core.Modules;

namespace Chippi.App.Controls.Gallery;

/// <summary>Debug-only window that shows every token and control state for review against DESIGN.md.</summary>
public partial class ControlGalleryWindow : Window
{
    private static readonly string[] ThemedTokens =
    [
        "BackdropBrush", "WindowBackgroundBrush", "TitlebarBrush", "PanelBrush", "SurfaceBrush", "SurfaceSelectedBrush",
        "ChipBrush", "InputBrush", "TrackBrush", "TrackRemainderBrush", "SwitchOffBrush", "ButtonBackgroundBrush",
        "ButtonHoverBrush", "TextPrimaryBrush", "TextStrongBrush", "TextControlBrush", "TextSecondaryBrush",
        "TextTertiaryBrush", "TextMutedBrush", "FocusRingBrush", "AccentGradientBrush", "AccentGradientDeepBrush",
        "SuccessBrush", "WarningBrush", "DangerBrush", "DangerDeepBrush",
    ];

    private static readonly string[] PetTokens =
    [
        "IslandBrush", "PetSynthBrush", "PetIrisBrush", "PetAzureBrush", "PetSignalBrush", "PetEmberBrush", "PetFrostBrush",
        "PetInkBrush", "PetShineBrush", "PetBlushBrush", "PetSparkBrush", "PetShadowBrush", "PetZBrush",
    ];

    public ControlGalleryWindow()
    {
        Resources.Add("InverseBool", new InverseBoolConverter());
        InitializeComponent();

        ThemePicker.SelectedIndex = ThemeManager.Mode switch { ThemePreference.Light => 1, ThemePreference.System => 2, _ => 0 };
        AddSwatches(Swatches, ThemedTokens);
        AddSwatches(PetSwatches, PetTokens);

        CpuTile.Samples = new double[] { 12, 18, 15, 30, 22, 26, 41, 23, 19, 28, 23 };
        MemTile.Samples = new double[] { 55, 56, 58, 57, 60, 61, 61, 62, 60, 61 };
        RoutingCard.Badges = [FeatureBadge.Source("From Hopper"), FeatureBadge.Needs("Needs VB-CABLE")];
        KeepAwakeCard.Badges = [FeatureBadge.Overlap("Overlaps PowerToys")];
        ClipboardCard.Badges = [FeatureBadge.Overlap("Overlaps Win+V")];

        Loaded += (_, _) => FontReport.Text = DescribeFonts();
    }

    /// <summary>Renders the whole gallery to dark and light PNGs in <paramref name="directory"/>, then closes. For reviews and reports.</summary>
    public async Task SnapshotAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var (index, name) in new[] { (0, "dark"), (1, "light") })
        {
            ThemePicker.SelectedIndex = index;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Root.Measure(new Size(Root.ActualWidth, double.PositiveInfinity));
            Root.Arrange(new Rect(new Point(0, 0), new Size(Root.ActualWidth, Root.DesiredSize.Height)));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(Root.ActualWidth * scale), (int)Math.Ceiling(Root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
                dc.DrawRectangle((Brush)FindResource("BackdropBrush"), null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            bitmap.Render(backdrop);
            bitmap.Render(Root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"ws0-gallery-{name}.png"));
            encoder.Save(file);
        }
        Close();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e) =>
        ThemeManager.Apply(ThemePicker.SelectedIndex switch { 1 => ThemePreference.Light, 2 => ThemePreference.System, _ => ThemePreference.Dark });

    private void AddSwatches(Panel panel, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            var swatch = new Border { Style = (Style)FindResource("Swatch") };
            swatch.SetResourceReference(Border.BackgroundProperty, key);
            var label = new TextBlock { Text = key.Replace("Brush", "", StringComparison.Ordinal), FontSize = 10.5, Margin = new Thickness(2, 0, 0, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            panel.Children.Add(new StackPanel { Children = { swatch, label }, Margin = new Thickness(0, 0, 0, 6) });
        }
    }

    /// <summary>Which font file WPF actually resolved for each weight, to prove the static TTFs load.</summary>
    private string DescribeFonts()
    {
        var lines = new List<string>();
        void Probe(string resource, FontWeight weight)
        {
            var family = (FontFamily)FindResource(resource);
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            lines.Add(typeface.TryGetGlyphTypeface(out var glyph)
                ? $"{resource} {weight.ToOpenTypeWeight()} -> {Path.GetFileName(glyph.FontUri.LocalPath)} (sim: {glyph.StyleSimulations})"
                : $"{resource} {weight.ToOpenTypeWeight()} -> NOT RESOLVED");
        }
        foreach (var w in new[] { FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold, FontWeights.ExtraBold })
            Probe("UiFont", w);
        foreach (var w in new[] { (FontWeight)FindResource("MonoMedium"), (FontWeight)FindResource("MonoSemiBold") })
            Probe("MonoFont", w);
        return string.Join("   ", lines);
    }

    private sealed class InverseBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not true;
    }
}
