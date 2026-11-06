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
using Chippi.Features.Displays.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using static Chippi.Features.Displays.Tests.DisplaysPageTests;

namespace Chippi.Features.Displays.Tests;

/// <summary>The island's Displays page (designs/displays v3) over a fake model: the arrangement, picking, All displays, a display that doesn't answer, and the shots.</summary>
public sealed class DisplaysIslandTests
{
    private static readonly DisplayStatus Omen = new("omen", "OMEN 34c", IsMain: true, Brightness: 54, Contrast: true) { ContrastLevel = 100 };
    private static readonly DisplayStatus Samsung = new("samsung", "LS27D36xG", IsMain: false, Brightness: 60, Contrast: true) { ContrastLevel = 75 };
    private static readonly DisplayStatus Dell = new("dell", "Dell P2419H", IsMain: false, Brightness: null, Contrast: false, Failed: true);

    [Fact]
    public void Opens_on_the_display_under_the_pointer_drawn_where_Windows_puts_it()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeModel([Omen, Samsung]) { Pointer = "samsung" };
            var view = Laid(new DisplaysIslandView(model));
            view.Opened();
            Assert.Equal(1, model.Reads);
            Assert.Equal("samsung", view.Picked);
            var omen = view.Tiles["omen"];
            var samsung = view.Tiles["samsung"];
            Assert.False(omen.IsChecked);
            Assert.True(samsung.IsChecked);
            Assert.True(Canvas.GetLeft(omen) + omen.Width <= Canvas.GetLeft(samsung));
            Assert.True(omen.Width > samsung.Width);
            Assert.Equal("OMEN 34c, Main, 54%", AutomationProperties.GetName(omen));
            Assert.Equal(60, view.BrightnessSlider.Value);
            Assert.Equal(75, view.ContrastSlider.Value);
            Assert.Equal("LS27D36xG brightness", AutomationProperties.GetName(view.BrightnessSlider));
            Assert.Contains("LS27D36xG", Texts(view));
            Assert.DoesNotContain("· Main", Texts(view));

            omen.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("omen", view.Picked);
            Assert.Equal(54, view.BrightnessSlider.Value);
            Assert.Contains("· Main", Texts(view));
        });
    }

    [Fact]
    public void A_slider_sets_its_display_and_holds_its_value_over_a_stale_reading()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeModel([Omen, Samsung]) { Pointer = "omen" };
            long now = 0;
            var view = Laid(new DisplaysIslandView(model) { Clock = () => now });
            view.Opened();
            view.BrightnessSlider.Value = 70;
            view.ContrastSlider.Value = 90;
            Assert.Equal(["omen Brightness 70", "omen Contrast 90"], model.Calls);

            model.Raise(); // The worker hasn't reported the write yet.
            Assert.Equal(70, view.BrightnessSlider.Value);
            Assert.Contains("70%", Texts(view));
            now = 1500;
            model.Raise();
            Assert.Equal(54, view.BrightnessSlider.Value);
        });
    }

    [Fact]
    public void All_displays_shows_the_main_level_and_moves_every_display_together()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeModel([Samsung, Omen]) { Pointer = "samsung" };
            var view = Laid(new DisplaysIslandView(model));
            view.Opened();
            view.AllButton.IsChecked = true;
            view.AllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Null(view.Picked);
            Assert.All(view.Tiles.Values, t => Assert.True(t.IsChecked));
            Assert.Equal(54, view.BrightnessSlider.Value); // The main display's, not the first one's.
            Assert.Equal(100, view.ContrastSlider.Value);
            Assert.Equal("All displays brightness", AutomationProperties.GetName(view.BrightnessSlider));

            // A key or a click on the track: one move, its differences taken then.
            view.BrightnessSlider.Value = 64;
            Assert.Equal(["begin Brightness", "all Brightness 64", "end"], model.Calls);

            // A drag: the differences are taken once, when it starts.
            model.Calls.Clear();
            view.BrightnessSlider.RaiseEvent(new DragStartedEventArgs(0, 0));
            view.BrightnessSlider.Value = 70;
            view.BrightnessSlider.Value = 74;
            view.BrightnessSlider.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            Assert.Equal(["begin Brightness", "all Brightness 70", "all Brightness 74", "end"], model.Calls);

            // Picking a display leaves All displays.
            view.Tiles["samsung"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(view.AllButton.IsChecked);
            Assert.Equal("samsung", view.Picked);
        });
    }

    [Fact]
    public void A_display_that_doesnt_answer_shows_Try_again_in_place_of_its_sliders()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeModel([Omen, Samsung, Dell]) { Pointer = "dell" };
            var view = Laid(new DisplaysIslandView(model));
            view.Opened();
            Assert.Equal("Dell P2419H, Brightness control failed", AutomationProperties.GetName(view.Tiles["dell"]));
            Assert.Equal(Visibility.Visible, view.FailedCard.Visibility);
            Assert.Equal(Visibility.Collapsed, view.BrightnessCard.Visibility);
            Assert.Equal(Visibility.Collapsed, view.ContrastCard.Visibility);
            Assert.Equal("Try again on Dell P2419H", AutomationProperties.GetName(view.TryAgainButton));
            view.TryAgainButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(["try dell"], model.Calls);

            model.Set([Omen, Samsung, Dell with { Retrying = true }]);
            Assert.False(view.TryAgainButton.IsEnabled);
            model.Set([Omen, Samsung, Dell with { Failed = false, Retrying = false, Brightness = 40 }]);
            Assert.Equal(Visibility.Collapsed, view.FailedCard.Visibility);
            Assert.Equal(40, view.BrightnessSlider.Value);
            Assert.Equal(Visibility.Collapsed, view.ContrastCard.Visibility); // It has no contrast.
        });
    }

    [Fact]
    public void One_display_has_no_All_displays_and_none_reads_No_displays_found()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeModel([Omen, Samsung]);
            var view = Laid(new DisplaysIslandView(model));
            view.Opened();
            Assert.Equal(Visibility.Visible, view.AllButton.Visibility);
            model.Set([Samsung]); // The main display unplugged: the one left is picked.
            Assert.Equal(Visibility.Collapsed, view.AllButton.Visibility);
            Assert.Equal("samsung", view.Picked);
            Assert.Single(view.Tiles);
            model.Set([]);
            Assert.Equal(Visibility.Visible, view.NoneMessage.Visibility);
            Assert.Contains(DisplaysIslandView.NoDisplays, Texts(view.NoneMessage));
        });
    }

    /// <summary>The page in the island at 520 × 360, dark or light by CHIPPI_SHOTS_THEME, at 2x.</summary>
    [Fact]
    public void Shots()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var main = new DisplaysIslandView(new FakeModel([Omen, Samsung]) { Pointer = "omen" });
            main.Opened();
            Panel(main, folder, "island-displays");

            var all = new DisplaysIslandView(new FakeModel([Omen, Samsung]) { Pointer = "omen" });
            all.Opened();
            all.AllButton.IsChecked = true;
            all.AllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Panel(all, folder, "island-displays-all");

            var failed = new DisplaysIslandView(new FakeModel([Omen, Samsung, Dell]) { Pointer = "dell" });
            failed.Opened();
            Panel(failed, folder, "island-displays-failed");

            var four = new DisplaysIslandView(new FakeModel(
            [
                Omen, Samsung,
                new("laptop", "Built-in display", IsMain: false, Brightness: 80, Contrast: false),
                new("benq", "BenQ GW2780 with a long model name", IsMain: false, Brightness: 35, Contrast: true) { ContrastLevel = 50 },
            ])
            { Pointer = "laptop" });
            four.Opened();
            Panel(four, folder, "island-displays-four");

            var none = new DisplaysIslandView(new FakeModel([]));
            none.Opened();
            Panel(none, folder, "island-displays-none");
        });
    }

    private static void IslandTheme()
    {
        Theme();
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("IslandStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
    }

    private static DisplaysIslandView Laid(DisplaysIslandView view)
    {
        view.Measure(new Size(IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.Arrange(new Rect(0, 0, IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.UpdateLayout();
        return view;
    }

    /// <summary>The island at 520 × 360: Back, the page's header, and its 496 × 304 body.</summary>
    private static void Panel(DisplaysIslandView view, string folder, string name)
    {
        var glyph = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 16 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var back = new Button { Content = glyph, Margin = new Thickness(-4, 0, 4, 0) };
        back.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        DockPanel.SetDock(back, Dock.Left);
        var header = view.Header;
        var row = new DockPanel { Height = IslandPage.HeaderHeight, Margin = new Thickness(0, 0, 0, IslandPage.Gap), LastChildFill = true, Children = { back, header } };
        var panel = new Border
        {
            Width = IslandPage.Width + 2, // the 1px edge sits outside the 520 × 360
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
    }

    private sealed class FakeModel(IReadOnlyList<DisplayStatus> displays) : IDisplaysPanelModel
    {
        private static readonly Dictionary<string, Rect> Desktop = new()
        {
            ["omen"] = new Rect(0, 0, 3440, 1440),
            ["samsung"] = new Rect(3440, 180, 1920, 1080),
            ["laptop"] = new Rect(-1920, 360, 1920, 1080),
            ["benq"] = new Rect(760, -1080, 1920, 1080),
        };

        private IReadOnlyList<DisplayStatus> _displays = displays;

        public string? Pointer { get; init; }

        public int Reads { get; private set; }

        public List<string> Calls { get; } = [];

        public IReadOnlyList<DisplayStatus> PanelDisplays => _displays;

        public event Action? Changed;

        public void Set(IReadOnlyList<DisplayStatus> list)
        {
            _displays = list;
            Changed?.Invoke();
        }

        public void Raise() => Changed?.Invoke();

        public Rect? Bounds(string displayId) => Desktop.TryGetValue(displayId, out var r) ? r : null;

        public string? UnderPointer() => Pointer;

        public void Read() => Reads++;

        public void Set(string displayId, DisplayControl control, int percent) => Calls.Add($"{displayId} {control} {percent}");

        public void BeginAll(DisplayControl control) => Calls.Add($"begin {control}");

        public void MoveAll(DisplayControl control, int value) => Calls.Add($"all {control} {value}");

        public void EndAll() => Calls.Add("end");

        public void TryAgain(string displayId) => Calls.Add($"try {displayId}");
    }
}
