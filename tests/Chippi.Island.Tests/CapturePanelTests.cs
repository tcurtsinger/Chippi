// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.Core.Modules;
using Chippi.UI.Capture;
using Chippi.UI.Controls;
using static Chippi.Island.Tests.IslandPanelTests;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Island.Tests;

/// <summary>
/// The Capture panel (designs/capture-panel v1) over a fake model: what each card does, what shows for each set of
/// installed features, and Delay. With CHIPPI_ISLAND_SHOTS, its states in the real island panel window (capture-panel-*);
/// with CHIPPI_CAPTURE_PANEL_TIMINGS, the page's open from the home with 50 recent captures.
/// </summary>
public sealed class CapturePanelTests
{
    [Fact]
    public void Each_mode_card_starts_its_mode_and_says_its_verb_and_keys() => WpfTestThread.Run(() =>
    {
        var model = new Model { RegionKeys = "Print Screen" };
        var view = new CapturePanelView(model);
        Assert.Equal(["Region", "Window", "Screen", "All displays", "Freeform", "Last region"], CapturePanelView.Modes.Select(m => m.Name));
        foreach (var (mode, card) in view.ModeCards)
            card.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(CapturePanelView.Modes.Select(m => m.Mode), model.Started);
        Assert.Equal("Capture region · Print Screen", view.ModeCards[CaptureMode.Region].ToolTip);
        Assert.Equal("Capture region", AutomationProperties.GetName(view.ModeCards[CaptureMode.Region]));
        Assert.Equal("Capture window", view.ModeCards[CaptureMode.Window].ToolTip);

        // Windows' setting is back on: Chippi doesn't hold the key, so the tooltip shows none.
        model.RegionKeys = null;
        model.Raise();
        Assert.Equal("Capture region", view.ModeCards[CaptureMode.Region].ToolTip);
    });

    [Fact]
    public void Only_installed_tools_show_in_their_order_and_six_fit() => WpfTestThread.Run(() =>
    {
        var model = new Model();
        var view = new CapturePanelView(model);
        Assert.Equal(Visibility.Collapsed, view.ToolsRow.Visibility);

        model.Tools = [.. Wave1()];
        model.Raise();
        Assert.Equal(["Extract text", "Scan QR", "Pick color"], view.ToolCards.Select(AutomationProperties.GetName));
        view.ToolCards.Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(["color"], model.ToolsStarted);

        // Color picker removed: its card goes, the others keep their places.
        model.Tools = [.. Wave1().Take(2)];
        model.Raise();
        Assert.Equal(["Extract text", "Scan QR"], view.ToolCards.Select(AutomationProperties.GetName));

        model.Tools = [.. Wave1(), .. Wave2(), new CapturePanelTool("extra", "Extra", "Extra", LucideIcons.Image, () => { })];
        model.Raise();
        Assert.Equal(CapturePanelView.MaxTools, view.ToolCards.Count());
    });

    [Fact]
    public void Recent_shows_the_newest_four_and_first_use_shows_none_without_Open_folder() => WpfTestThread.Run(() =>
    {
        var model = new Model();
        var view = new CapturePanelView(model);
        Assert.Equal(Visibility.Visible, view.EmptyCard.Visibility);
        Assert.Equal(Visibility.Collapsed, view.OpenFolderButton.Visibility);
        Assert.Contains(Find<TextBlock>(view.EmptyCard), t => t.Text == "No captures yet");

        model.Recent = [.. Fifty()];
        model.Raise();
        Assert.Equal(Visibility.Collapsed, view.EmptyCard.Visibility);
        Assert.Equal(Visibility.Visible, view.OpenFolderButton.Visibility);
        Assert.Equal(["Region · 1280 × 720, 2 min ago", "Window · Terminal, 9 min ago", "Screen · 3440 × 1440, Yesterday", "Freeform · 612 × 488, Yesterday"],
            view.RecentCards.Select(AutomationProperties.GetName));
        view.RecentCards.ElementAt(1).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        view.OpenFolderButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(["recent-1"], model.Opened);
        Assert.Equal(1, model.Folders);
    });

    [Fact]
    public void Delay_lists_its_choices_marks_the_kept_one_and_sets_the_next_mode() => WpfTestThread.Run(() =>
    {
        var model = new Model { Delay = 3 };
        var view = new CapturePanelView(model);
        Assert.Equal("Delay: 3 seconds", AutomationProperties.GetName(view.DelayButton));
        Assert.Equal(["No delay", "3 seconds", "5 seconds", "10 seconds"], view.DelayItems.Select(AutomationProperties.GetName));
        Assert.Equal(["", "Selected", "", ""], view.DelayItems.Select(AutomationProperties.GetItemStatus));

        view.DelayItems.Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(10, model.Delay);
        Assert.Equal("Delay: 10 seconds", AutomationProperties.GetName(view.DelayButton));
        Assert.Equal(["", "", "", "Selected"], view.DelayItems.Select(AutomationProperties.GetItemStatus));
    });

    [Fact]
    public void Without_Screen_capture_only_the_tools_show() => WpfTestThread.Run(() =>
    {
        var model = new Model { CanCapture = false, Tools = [.. Wave1()], Recent = [.. Fifty()] };
        var view = new CapturePanelView(model);
        Assert.Equal(Visibility.Collapsed, view.ModesRow.Visibility);
        Assert.Equal(Visibility.Collapsed, view.DelayButton.Visibility);
        Assert.Equal(Visibility.Collapsed, view.RecentHeading.Visibility);
        Assert.Equal(Visibility.Collapsed, view.EmptyCard.Visibility);
        Assert.Empty(view.RecentCards);
        Assert.Equal(Visibility.Visible, view.ToolsRow.Visibility);
    });

    [Fact]
    public void Every_state_fits_the_body_without_scrolling() => WpfTestThread.Run(() =>
    {
        foreach (var model in new[]
        {
            new Model { Tools = [.. Wave1()], Recent = [.. Fifty()] },
            new Model { Tools = [.. Wave1(), .. Wave2()], Recent = [.. Fifty()] },
            new Model { Tools = [.. Wave1()] },
        })
        {
            var view = new CapturePanelView(model);
            var body = (FrameworkElement)view.Content;
            body.Measure(new Size(IslandPage.BodyWidth, double.PositiveInfinity));
            double used = ((Panel)body).Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).Sum(c => c.DesiredSize.Height);
            Assert.True(used <= IslandPage.BodyHeight, $"{used} > {IslandPage.BodyHeight}");
        }
    });

#if DEBUG
    private static readonly MonitorArea Area = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        string theme = WpfTestThread.ShotsTheme.ToLowerInvariant();
        WpfTestThread.Run(() =>
        {
            Shoot(new Model { RegionKeys = "Print Screen", Tools = [.. Wave1()], Recent = [.. Fifty()] }, Path.Combine(folder, $"capture-panel-{theme}.png"));
            Shoot(new Model { Tools = [.. Wave1()] }, Path.Combine(folder, $"capture-panel-first-use-{theme}.png"));
            Shoot(new Model { Delay = 3, Tools = [.. Wave1()], Recent = [.. Fifty()] }, Path.Combine(folder, $"capture-panel-delay-{theme}.png"), view => view.DelayMenuOpen = true);
            Shoot(new Model { Tools = [.. Wave1(), .. Wave2()], Recent = [.. Fifty()] }, Path.Combine(folder, $"capture-panel-wave-2-{theme}.png"));
            Shoot(new Model { CanCapture = false, Tools = [.. Wave1()] }, Path.Combine(folder, $"capture-panel-tools-only-{theme}.png"));
        });
    }

    [Fact]
    public void Time()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_CAPTURE_PANEL_TIMINGS") is not { Length: > 0 } report)
            return;
        var lines = new List<string>();
        WpfTestThread.Run(() =>
        {
            var model = new Model { RegionKeys = "Print Screen", Tools = [.. Wave1()], Recent = [.. Fifty()] };
            var (tiles, _) = Tile(model);
            var panel = new IslandPanelWindow();
            var frames = new List<TimeSpan>();
            EventHandler record = (_, e) =>
            {
                var at = ((RenderingEventArgs)e).RenderingTime;
                if (frames.Count == 0 || frames[^1] != at) frames.Add(at);
            };
            CompositionTarget.Rendering += record;
            try
            {
                panel.Initialize();
                panel.SetMedia(new FakeMedia());
                panel.SetTiles(tiles);
                panel.SetLastPage(null);
                panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
                Pump(TimeSpan.FromMilliseconds(600));
                var warm = new List<double>();
                lines.Add("open\tShowPage to laid out ms\tfirst frame after ms\tlongest gap in the slide ms");
                for (int i = 0; i < 21; i++)
                {
                    int before = frames.Count;
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    panel.ShowPage("capture", remember: false);
                    panel.UpdateLayout();
                    double call = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var asked = frames[^1];
                    Pump(TimeSpan.FromMilliseconds(400));
                    var after = frames.Skip(before).Where(t => t <= asked + TimeSpan.FromMilliseconds(260)).Prepend(asked).ToList();
                    double first = after.Count > 1 ? (after[1] - asked).TotalMilliseconds : double.NaN;
                    double longest = after.Zip(after.Skip(1), (a, b) => (b - a).TotalMilliseconds).DefaultIfEmpty(0).Max();
                    lines.Add($"{(i == 0 ? "cold" : "warm")}\t{call:0.0}\t{first:0.0}\t{longest:0.0}");
                    if (i > 0) warm.Add(call);
                    panel.ShowPage(null, remember: false);
                    Pump(TimeSpan.FromMilliseconds(400));
                }
                warm.Sort();
                lines.Insert(0, $"50 recent captures (4 shown, thumbnails prepared), 3 tools. Warm p50 {warm[warm.Count / 2]:0.0} ms, p95 {warm[(int)Math.Ceiling(warm.Count * 0.95) - 1]:0.0} ms, max {warm[^1]:0.0} ms (budget: warm p95 <= 100, cold <= 200)");
            }
            finally
            {
                CompositionTarget.Rendering -= record;
                panel.Close();
            }
        });
        File.WriteAllLines(report, lines);
    }

    /// <summary>The Capture tile as the host will make it: the view as its section, its header as the page's.</summary>
    private static (IReadOnlyList<IslandTileGroup> Tiles, Func<CapturePanelView> View) Tile(Model model)
    {
        CapturePanelView? view = null;
        CapturePanelView View() => view ??= new CapturePanelView(model);
        var spec = new IslandTileSpec("capture", "Capture", nameof(LucideIcons.SquareDashed), Order: 75, SectionOrder: 0, () => View())
        {
            CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus,
        };
        return (IslandTileContribution.Group([new IslandTileContribution("screen-capture", spec)]), View);
    }

    private static void Shoot(Model model, string path, Action<CapturePanelView>? arrange = null)
    {
        var (tiles, view) = Tile(model);
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(tiles);
            panel.SetLastPage(null);
            panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
            Pump(TimeSpan.FromMilliseconds(400));
            panel.ShowPage("capture");
            Pump(TimeSpan.FromMilliseconds(400));
            arrange?.Invoke(view());
            Pump(TimeSpan.FromMilliseconds(300));
            Save(panel, view(), path);
        }
        finally
        {
            panel.ShowPage(null, remember: false);
            panel.Close();
        }
    }

    /// <summary>The panel at 2x over a dark desktop, with the Delay menu drawn where its popup sits.</summary>
    private static void Save(IslandPanelWindow panel, CapturePanelView view, string path)
    {
        const double scale = 2;
        var root = panel.Visual;
        var panelPixels = Pixels(root, scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, panelPixels.PixelWidth, panelPixels.PixelHeight));
            dc.DrawImage(panelPixels, new Rect(0, 0, panelPixels.PixelWidth, panelPixels.PixelHeight));
            if (view.DelayMenuOpen && view.DelayItems.First().Parent is FrameworkElement list && list.Parent is FrameworkElement menu)
            {
                var anchor = view.DelayButton;
                var at = anchor.TranslatePoint(new Point(anchor.ActualWidth - menu.ActualWidth, anchor.ActualHeight + 4), root);
                var menuPixels = Pixels(menu, scale);
                dc.DrawImage(menuPixels, new Rect(at.X * scale, at.Y * scale, menuPixels.PixelWidth, menuPixels.PixelHeight));
            }
        }
        var image = new RenderTargetBitmap(panelPixels.PixelWidth, panelPixels.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static RenderTargetBitmap Pixels(FrameworkElement element, double scale)
    {
        var pixels = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        pixels.Render(element);
        return pixels;
    }
#endif

    private static IEnumerable<CapturePanelTool> Wave1() =>
    [
        new("text", "Text", "Extract text", LucideIcons.ScanText, () => { }),
        new("qr", "QR code", "Scan QR", LucideIcons.QrCode, () => { }),
        new("color", "Color", "Pick color", LucideIcons.Pipette, () => { }),
    ];

    private static IEnumerable<CapturePanelTool> Wave2() =>
    [
        new("record", "Record", "Record region", LucideIcons.Video, () => { }),
        new("gif", "GIF", "Record GIF", LucideIcons.Video, () => { }),
        new("convert", "Convert", "Convert media", LucideIcons.Image, () => { }),
    ];

    /// <summary>Fifty finished captures, newest first, with prepared thumbnails like the history index keeps.</summary>
    private static IEnumerable<CapturePanelRecent> Fifty()
    {
        (string Name, string When, int Width, int Height, Action<DrawingContext, Rect> Draw)[] first =
        [
            ("Region · 1280 × 720", "2 min ago", 1280, 720, Document),
            ("Window · Terminal", "9 min ago", 1100, 700, Terminal),
            ("Screen · 3440 × 1440", "Yesterday", 3440, 1440, Desktop),
            ("Freeform · 612 × 488", "Yesterday", 612, 488, Site),
        ];
        for (int i = 0; i < 50; i++)
        {
            var (name, when, width, height, draw) = i < first.Length ? first[i] : ($"Region · {400 + i * 10} × {300 + i * 4}", $"{i / 4 + 2} days ago", 400 + i * 10, 300 + i * 4, (Action<DrawingContext, Rect>)Document);
            yield return new CapturePanelRecent($"recent-{i}", name, when, Thumbnail(width, height, draw));
        }
    }

    /// <summary>A thumbnail at most 236 × 132 pixels (the card's 118 × 66 at 2x), the capture's own shape.</summary>
    private static BitmapSource Thumbnail(int width, int height, Action<DrawingContext, Rect> draw)
    {
        double fit = Math.Min(236.0 / width, 132.0 / height);
        var size = new Size(Math.Round(width * fit), Math.Round(height * fit));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc, new Rect(size));
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush B(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private static void Document(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(B(0xFFF7F7F8), null, r);
        dc.DrawRectangle(B(0xFFE8E8EC), null, new Rect(0, 0, r.Width, r.Height * 0.16));
        for (int line = 0; line < 6; line++)
            dc.DrawRectangle(B(0xFFC9CAD1), null, new Rect(r.Width * 0.1, r.Height * (0.28 + line * 0.11), r.Width * (line % 3 == 2 ? 0.5 : 0.8), r.Height * 0.04));
    }

    private static void Terminal(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(B(0xFF0C0C0C), null, r);
        dc.DrawRectangle(B(0xFF2B2B2B), null, new Rect(0, 0, r.Width, r.Height * 0.12));
        for (int line = 0; line < 7; line++)
            dc.DrawRectangle(B(line % 2 == 0 ? 0xFF16C60Cu : 0xFFCCCCCCu), null, new Rect(r.Width * 0.05, r.Height * (0.2 + line * 0.1), r.Width * (0.3 + line % 3 * 0.15), r.Height * 0.04));
    }

    private static void Desktop(DrawingContext dc, Rect r)
    {
        var wall = new LinearGradientBrush(Color.FromRgb(0x2C, 0x3E, 0x57), Color.FromRgb(0x1B, 0x28, 0x38), 45);
        wall.Freeze();
        dc.DrawRectangle(wall, null, r);
        dc.DrawRectangle(B(0xFFF3F3F3), null, new Rect(r.Width * 0.12, r.Height * 0.14, r.Width * 0.42, r.Height * 0.62));
        dc.DrawRectangle(B(0xFF1E1E1E), null, new Rect(r.Width * 0.5, r.Height * 0.24, r.Width * 0.36, r.Height * 0.56));
        dc.DrawRectangle(B(0xE0202020), null, new Rect(0, r.Height * 0.92, r.Width, r.Height * 0.08));
    }

    private static void Site(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(B(0xFFFFFFFF), null, r);
        dc.DrawRectangle(B(0xFF7444F0), null, new Rect(0, 0, r.Width, r.Height * 0.4));
        dc.DrawRectangle(B(0xFFFFFFFF), null, new Rect(r.Width * 0.1, r.Height * 0.14, r.Width * 0.5, r.Height * 0.07));
        dc.DrawRectangle(B(0xFFE4E4EA), null, new Rect(r.Width * 0.1, r.Height * 0.52, r.Width * 0.35, r.Height * 0.3));
        dc.DrawRectangle(B(0xFFE4E4EA), null, new Rect(r.Width * 0.55, r.Height * 0.52, r.Width * 0.35, r.Height * 0.3));
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }

    private sealed class Model : ICapturePanelModel
    {
        public bool CanCapture { get; set; } = true;
        public int Delay { get; set; }
        public string? RegionKeys { get; set; }
        public IReadOnlyList<CapturePanelTool> Tools { get; set; } = [];
        public IReadOnlyList<CapturePanelRecent> Recent { get; set; } = [];
        public List<CaptureMode> Started { get; } = [];
        public List<string> ToolsStarted { get; } = [];
        public List<string> Opened { get; } = [];
        public int Folders { get; private set; }

        public event Action? Changed;

        public string? Keys(CaptureMode mode) => mode == CaptureMode.Region ? RegionKeys : null;

        public void Start(CaptureMode mode) => Started.Add(mode);

        public void OpenRecent(string id) => Opened.Add(id);

        public void OpenFolder() => Folders++;

        public void Raise()
        {
            // Tools record whether they were started through their own Start.
            Tools = [.. Tools.Select(t => t with { Start = () => ToolsStarted.Add(t.Id) })];
            Changed?.Invoke();
        }
    }
}
