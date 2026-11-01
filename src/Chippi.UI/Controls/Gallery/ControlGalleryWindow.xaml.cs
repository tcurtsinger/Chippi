// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.UI.Themes;
using Chippi.Core.Config;

namespace Chippi.UI.Controls.Gallery;

/// <summary>Debug-only window that shows every token and control state for review against DESIGN.md.</summary>
public partial class ControlGalleryWindow : Window
{
    private static readonly string[] ThemedTokens =
    [
        "WindowBackgroundBrush", "SurfaceBrush", "FlyoutBrush", "FlyoutFooterBrush", "PanelBrush", "ButtonBackgroundBrush",
        "ButtonHoverBrush", "ButtonPressedBrush", "HoverBrush", "SurfaceSelectedBrush", "SegmentTroughBrush", "InputBrush",
        "BorderBrush", "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "TextMutedBrush",
        "AccentBrush", "OnAccentBrush", "WarningBrush", "SuccessBrush", "DangerBrush",
    ];

    private static readonly AccentChoice[] Accents = [AccentChoice.Blue, AccentChoice.Violet, AccentChoice.Green, AccentChoice.Pink];

    public ControlGalleryWindow()
    {
        Resources.Add("InverseBool", new InverseBoolConverter());
        InitializeComponent();

        ThemePicker.SelectedIndex = ThemeManager.Mode switch { ThemePreference.Light => 1, ThemePreference.System => 2, _ => 0 };
        AddTokens();
        foreach (var color in Enum.GetValues<PetColor>())
        {
            var swatch = new RadioButton { Style = (Style)FindResource("SwatchStyle"), GroupName = "Pet", IsChecked = color == PetColor.Synth, Background = new SolidColorBrush(Pet.ChippiPet.ColorOf(color)) };
            System.Windows.Automation.AutomationProperties.SetName(swatch, color.ToString());
            PetSwatches.Children.Add(swatch);
        }

        Loaded += (_, _) => FontReport.Text = DescribeFonts();
    }

    /// <summary>
    /// Renders the whole gallery to PNGs in <paramref name="directory"/>, dark and light with the Blue and
    /// Violet accents, then closes. For reviews and reports.
    /// </summary>
    public async Task SnapshotAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var (theme, themeName) in new[] { (0, "dark"), (1, "light") })
        {
            foreach (var (accent, accentName) in new[] { (0, "blue"), (1, "violet") })
            {
                ThemePicker.SelectedIndex = theme;
                AccentPicker.SelectedIndex = accent;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Root.Measure(new Size(Root.ActualWidth, double.PositiveInfinity));
                Root.Arrange(new Rect(new Point(0, 0), new Size(Root.ActualWidth, Root.DesiredSize.Height)));
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(Root.ActualWidth * scale), (int)Math.Ceiling(Root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                // Through a brush, so the scroll position and layout offset never crop the picture.
                var page = new DrawingVisual();
                using (var dc = page.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(Root), null, new Rect(Root.RenderSize));
                bitmap.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"r1-gallery-{themeName}-{accentName}.png"));
                encoder.Save(file);
            }
        }
        Close();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e) =>
        ThemeManager.Apply(ThemePicker.SelectedIndex switch { 1 => ThemePreference.Light, 2 => ThemePreference.System, _ => ThemePreference.Dark });

    private void OnAccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccentPicker.SelectedIndex >= 0 && ThemeManager.NamedAccent(Accents[AccentPicker.SelectedIndex]) is { } shades)
            ThemeManager.SetAccent(shades.Dark, shades.Light);
    }

    private void AddTokens()
    {
        foreach (var key in ThemedTokens)
        {
            var swatch = new Border { Style = (Style)FindResource("Token") };
            swatch.SetResourceReference(Border.BackgroundProperty, key);
            var label = new TextBlock { Text = key.Replace("Brush", "", StringComparison.Ordinal), Margin = new Thickness(0, 0, 0, 8) };
            label.SetResourceReference(StyleProperty, "CaptionTextStyle");
            Swatches.Children.Add(new StackPanel { Children = { swatch, label } });
        }
    }

    /// <summary>Which face WPF resolved for each role, to prove the variable font's named weights load without simulation.</summary>
    private string DescribeFonts()
    {
        var lines = new List<string>();
        void Probe(string resource, FontWeight weight)
        {
            var family = (FontFamily)FindResource(resource);
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            lines.Add(typeface.TryGetGlyphTypeface(out var glyph)
                ? $"{resource} {weight.ToOpenTypeWeight()} → {Path.GetFileName(glyph.FontUri.LocalPath)} {glyph.FaceNames.Values.FirstOrDefault()} (sim: {glyph.StyleSimulations})"
                : $"{resource} {weight.ToOpenTypeWeight()} → NOT RESOLVED");
        }
        Probe("UiFont", FontWeights.Normal);
        Probe("UiFont", FontWeights.SemiBold);
        Probe("DisplayFont", FontWeights.SemiBold);
        Probe("CodeFont", FontWeights.Normal);
        return string.Join("   ·   ", lines);
    }

    private sealed class InverseBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not true;
    }
}
