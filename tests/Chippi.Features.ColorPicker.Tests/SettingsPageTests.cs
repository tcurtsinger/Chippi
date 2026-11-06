// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Shortcuts;
using Chippi.Features.ColorPicker.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.ColorPicker.Tests;

/// <summary>Settings › Color picker over a fake model: the palette, Copy as, an empty palette, Edit, and the shots.</summary>
public sealed class SettingsPageTests
{
    [Fact]
    public void The_palette_shows_every_recent_color_newest_first()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ColorPickerSettingsPage(model);
            Assert.Equal(model.Recent, page.Swatches.Select(s => AutomationProperties.GetName(s)));
            Assert.Equal(Visibility.Visible, page.Clear.Visibility);

            model.SetRecent([]);
            Assert.Empty(page.Swatches);
            Assert.Equal(Visibility.Collapsed, page.Clear.Visibility);
            Assert.Contains("No recent colors", Texts(page));
        });
    }

    [Fact]
    public void Copy_as_writes_each_format_out_and_saves_through_the_model()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ColorPickerSettingsPage(model);
            Assert.Equal(["HEX · #7C5CFF", "RGB · rgb(124, 92, 255)", "HSL · hsl(252, 100%, 68%)", "HSV · hsv(252, 64%, 100%)", "CMYK · cmyk(51%, 64%, 0%, 0%)"], page.CopyAs.Items.Cast<string>());
            Assert.Equal("HEX · #7C5CFF", page.CopyAs.SelectedItem);
            page.CopyAs.SelectedIndex = 2;
            Assert.Equal("HSL", model.CopyAs);

            model.Refuse = true;
            page.CopyAs.SelectedIndex = 1;
            Assert.Equal("Save failed", page.Error.Text);
            Assert.Equal("HSL · hsl(252, 100%, 68%)", page.CopyAs.SelectedItem); // Back to what's saved.
        });
    }

    [Fact]
    public void Shortcuts_are_read_only_and_Edit_opens_Settings_Shortcuts()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ColorPickerSettingsPage(model);
            Assert.Equal("Not set", page.ShortcutOf(ColorPickerShortcuts.PickColor).KeysText);
            var edit = page.ShortcutOf(ColorPickerShortcuts.PickAndReview).EditButton;
            Assert.Equal("Edit Pick and review color shortcut", AutomationProperties.GetName(edit));
            edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal([ColorPickerShortcuts.PickAndReview], model.Edited);
        });
    }

    [Fact]
    public void Shots()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        WpfTestThread.Run(() =>
        {
            Theme();
            Shot(new ColorPickerSettingsPage(new FakeModel()), folder, "color-picker", 768);
            Shot(new ColorPickerSettingsPage(new FakeModel()), folder, "color-picker-narrow", 508);
            var empty = new FakeModel();
            empty.SetRecent([]);
            Shot(new ColorPickerSettingsPage(empty), folder, "color-picker-empty", 508);
        });
    }

    private static List<string> Texts(DependencyObject root) => [.. Find<TextBlock>(root).Select(t => t.Text)];

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private static void Theme()
    {
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    /// <summary>The page under its title on the window's background, at 2x.</summary>
    private static void Shot(FrameworkElement page, string folder, string name, double width)
    {
        var heading = new TextBlock { Text = "Color picker", Margin = new Thickness(0, 0, 0, 12) };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "PageTitleStyle");
        var view = new Border { Padding = new Thickness(40, 24, 40, 32), Child = new StackPanel { Children = { heading, page } } };
        view.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        Layout(view, width + 80);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * 2), (int)Math.Ceiling(view.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"settings-{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }

    private sealed class FakeModel : IColorPickerSettingsModel
    {
        private IReadOnlyList<string> _recent = ["#7C5CFF", "#1E1F26", "#F4F1EA", "#3A6EA5", "#E5484D", "#30A46C", "#F5D90A", "#FFFFFF", "#000000", "#8E4EC6"];

        public IReadOnlyList<string> Recent => _recent;

        public IReadOnlyList<ColorFormat> Formats { get; } =
        [
            new("HEX", "#7C5CFF"), new("RGB", "rgb(124, 92, 255)"), new("HSL", "hsl(252, 100%, 68%)"),
            new("HSV", "hsv(252, 64%, 100%)"), new("CMYK", "cmyk(51%, 64%, 0%, 0%)"),
        ];

        public string CopyAs { get; private set; } = "HEX";

        public string? SettingsError => null;

        public bool Refuse { get; set; }

        public List<string> Edited { get; } = [];

        public event Action? Changed;

        public void SetRecent(IReadOnlyList<string> recent)
        {
            _recent = recent;
            Changed?.Invoke();
        }

        public (KeyShortcut? Keys, string? Error) Shortcut(string name) => (null, null);

        public bool SetCopyAs(string format)
        {
            if (Refuse) return false;
            CopyAs = format;
            return true;
        }

        public void ClearRecent() => SetRecent([]);

        public void EditShortcut(string name) => Edited.Add(name);
    }
}
