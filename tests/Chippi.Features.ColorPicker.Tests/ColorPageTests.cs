// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Chippi.Features.ColorPicker.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.ColorPicker.Tests;

/// <summary>The island's Color page (Capture results v3) over a fake model: the band, the formats, the palette, Clear and the shots.</summary>
public sealed class ColorPageTests
{
    private static readonly PickedColor Violet = new("#7C5CFF",
    [
        new("RGB", "rgb(124, 92, 255)"), new("HSL", "hsl(252, 100%, 68%)"),
        new("HSV", "hsv(252, 64%, 100%)"), new("CMYK", "cmyk(51%, 64%, 0%, 0%)"),
    ]);

    private static readonly PickedColor Butter = new("#FFC53D",
    [
        new("RGB", "rgb(255, 197, 61)"), new("HSL", "hsl(42, 100%, 62%)"),
        new("HSV", "hsv(42, 76%, 100%)"), new("CMYK", "cmyk(0%, 23%, 76%, 0%)"),
    ]);

    private static readonly string[] Palette = ["#7C5CFF", "#1B2838", "#E5484D", "#F5F5F5", "#2C3E57", "#FFC53D", "#30A46C", "#0090FF", "#8B8D98", "#7444F0"];

    [Fact]
    public void Shows_the_pick_and_copies_each_format_on_its_own()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeColor { Color = Violet, CopiedAs = "HEX", Recent = Palette };
            var view = Laid(new ColorResultView(model));
            Assert.Equal("#7C5CFF", view.HexText);
            Assert.Equal("Copied as HEX", view.Status);
            Assert.Equal(["RGB", "rgb(124, 92, 255)", "HSL", "hsl(252, 100%, 68%)", "HSV", "hsv(252, 64%, 100%)", "CMYK", "cmyk(51%, 64%, 0%, 0%)"], view.FormatTexts);
            Assert.Equal(["Copy RGB", "Copy HSL", "Copy HSV", "Copy CMYK"], view.FormatButtons.Select(AutomationProperties.GetName));
            Assert.Empty(model.Copies); // The pick copied; the page didn't.

            Click(view.FormatButtons[1]);
            Assert.Equal(["HSL hsl(252, 100%, 68%)"], model.Copies);
            Assert.Equal("Copied as HSL", view.Status);
            Click(view.CopyHexButton);
            Assert.Equal("HEX #7C5CFF", model.Copies[^1]);

            model.Refuse = true;
            Click(view.FormatButtons[0]);
            Assert.Equal(ColorResultView.CopyFailed, view.Status);
        });
    }

    [Fact]
    public void Pick_and_review_copies_nothing_and_a_recent_color_only_shows()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeColor { Color = Violet, Recent = Palette };
            var view = Laid(new ColorResultView(model));
            Assert.Equal("", view.Status);
            Assert.Equal(10, view.Swatches.Count);
            Assert.Equal("#7C5CFF, on the page", AutomationProperties.GetName(view.Swatches[0]));
            Assert.Equal("#FFC53D", AutomationProperties.GetName(view.Swatches[5]));

            Click(view.Swatches[5]);
            Assert.Equal(["#FFC53D"], model.Shown);
            model.Set(Butter);
            Assert.Equal("#FFC53D", view.HexText);
            Assert.Empty(model.Copies);
            Assert.Equal("", view.Status);
            Assert.Equal(Colors.Black, ((SolidColorBrush)view.BandInk).Color); // Dark ink on a light color.

            Click(view.PickAgainButton);
            Assert.Equal(1, model.Picks);
        });
    }

    [Fact]
    public void Clear_asks_first_and_an_empty_palette_says_so()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeColor { Color = Violet, Recent = Palette };
            var view = Laid(new ColorResultView(model));
            Click(view.ClearButton);
            Assert.True(view.AskingToClear);
            Click(view.CancelButton);
            Assert.False(view.AskingToClear);
            Assert.Equal(0, model.Clears);

            Click(view.ClearButton);
            Click(view.ConfirmClearButton);
            Assert.Equal(1, model.Clears);
            model.Recent = [];
            model.Raise();
            Assert.Empty(view.Swatches);
            Assert.Equal(Visibility.Visible, view.NoRecent.Visibility);
            Assert.Equal(Visibility.Collapsed, view.ClearButton.Visibility);
        });
    }

    [Fact]
    public void Ink_is_whichever_reads_better_on_the_color()
    {
        Assert.Equal(Colors.Black, ColorResultView.Ink(Color.FromRgb(0x7C, 0x5C, 0xFF))); // 4.8:1 against 4.4:1 for white
        Assert.Equal(Colors.White, ColorResultView.Ink(Color.FromRgb(0x1B, 0x28, 0x38)));
        Assert.Equal(Colors.Black, ColorResultView.Ink(Color.FromRgb(0xFF, 0xC5, 0x3D)));
        Assert.Equal(Colors.White, ColorResultView.Ink(Colors.Black));
        Assert.Equal(Colors.Black, ColorResultView.Ink(Colors.White));
        Assert.Null(ColorResultView.Parse("violet"));
    }

    [Fact]
    public void Shots()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 }) return;
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var picked = new ColorResultView(new FakeColor { Color = Violet, CopiedAs = "HEX", Recent = Palette });
            Panel(Laid(picked), picked.Header, "island-color");
            var butter = new ColorResultView(new FakeColor { Color = Butter, Recent = ["#FFC53D", "#7C5CFF", "#30A46C"] });
            Panel(Laid(butter), butter.Header, "island-color-light-pick");
            var asking = Laid(new ColorResultView(new FakeColor { Color = Violet, Recent = Palette }));
            Click(asking.ClearButton);
            Panel(asking, asking.Header, "island-color-clear");
            var empty = new ColorResultView(new FakeColor { Color = Violet, Recent = [] });
            Panel(Laid(empty), empty.Header, "island-color-no-recent");
        });
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void IslandTheme()
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        if (!resources.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        if (!resources.Any(d => d.Source?.OriginalString.EndsWith("IslandStyles.xaml", StringComparison.Ordinal) == true))
            resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
    }

    private static ColorResultView Laid(ColorResultView view)
    {
        view.Measure(new Size(IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.Arrange(new Rect(0, 0, IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.UpdateLayout();
        return view;
    }

    /// <summary>The island at 520 × 360: Back, the page's header and its 496 × 304 body, on the window's background, at 2x.</summary>
    private static void Panel(ColorResultView view, DockPanel header, string name)
    {
        string folder = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS")!;
        var glyph = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 16 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var back = new Button { Content = glyph, Margin = new Thickness(-4, 0, 4, 0) };
        back.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        DockPanel.SetDock(back, Dock.Left);
        var row = new DockPanel { Height = IslandPage.HeaderHeight, Margin = new Thickness(0, 0, 0, IslandPage.Gap), LastChildFill = true, Children = { back, header } };
        var panel = new Border
        {
            Width = IslandPage.Width + 2,
            Height = IslandPage.Height + 2,
            Padding = new Thickness(IslandPage.Inset),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel { Children = { row, view } },
        };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        panel.SetResourceReference(TextElement.ForegroundProperty, "IslandTextBrush");
        var backdrop = new Border { Padding = new Thickness(16), Child = panel };
        backdrop.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        backdrop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        backdrop.Arrange(new Rect(backdrop.DesiredSize));
        backdrop.UpdateLayout();
        Assert.InRange(view.ActualHeight, 0, IslandPage.BodyHeight);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(backdrop.ActualWidth * 2), (int)Math.Ceiling(backdrop.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
        row.Children.Clear();
        panel.Child = null;
    }

    private sealed class FakeColor : IColorPageModel
    {
        public PickedColor? Color { get; set; }

        public string? CopiedAs { get; set; }

        public IReadOnlyList<string> Recent { get; set; } = [];

        public bool Refuse { get; set; }

        public List<string> Copies { get; } = [];

        public List<string> Shown { get; } = [];

        public int Clears { get; private set; }

        public int Picks { get; private set; }

        public event Action? Changed;

        public void Set(PickedColor color)
        {
            Color = color;
            CopiedAs = null;
            Changed?.Invoke();
        }

        public void Raise() => Changed?.Invoke();

        public bool Copy(ColorFormat format)
        {
            if (Refuse) return false;
            Copies.Add($"{format.Name} {format.Value}");
            return true;
        }

        public void Show(string hex) => Shown.Add(hex);

        public void ClearRecent() => Clears++;

        public void PickAgain() => Picks++;
    }
}
