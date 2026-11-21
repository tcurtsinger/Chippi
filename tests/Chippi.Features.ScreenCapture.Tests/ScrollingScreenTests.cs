// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Capture;
using Chippi.Island.Tests;
using Chippi.UI.Capture;

namespace Chippi.Features.ScreenCapture.Tests;

/// <summary>
/// The scrolling capture's screens (docs/phase-7/scrolling-capture.md, scrolling-capture.html): the finish bar's Scroll, the
/// scrolling bar's states, the preview strip and the result's bar. Built and rendered offscreen: no window is ever shown and
/// nothing is captured. With <c>CHIPPI_ISLAND_SHOTS</c> the boards are saved as PNGs (<c>CHIPPI_SHOTS_THEME=light</c> for light).
/// </summary>
public sealed class ScrollingScreenTests
{
    private static ResourceDictionary IslandStyles() => new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") };

    /// <summary>The element in a host that carries the island's styles, measured and arranged at its own size.</summary>
    private static Border Laid(FrameworkElement element, Brush? background = null)
    {
        var host = new Border { Child = element, Background = background, HorizontalAlignment = HorizontalAlignment.Left };
        host.Resources.MergedDictionaries.Add(IslandStyles());
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        return host;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void WithCulture(string name, Action test)
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        try { test(); }
        finally { CultureInfo.CurrentCulture = old; }
    }

    // ---- the finish bar ----

    [Fact]
    public void The_finish_bar_leaves_Scroll_out_until_a_feature_turns_it_on() => WpfTestThread.Run(() =>
    {
        var bar = new FinishBar();
        Laid(bar);
        Assert.False(bar.ShowScroll);
        Assert.Equal(Visibility.Collapsed, bar.Scroll.Visibility);
        bar.ShowScroll = true;
        Assert.Equal(Visibility.Visible, bar.Scroll.Visibility);
    });

    [Fact]
    public void Scroll_joins_the_lead_group_with_its_tooltips_and_raises_Scroll() => WpfTestThread.Run(() =>
    {
        var bar = new FinishBar { ShowCopyText = true, ShowScanQrCode = true, ShowScroll = true };
        Laid(bar);
        Assert.True(bar.Scroll.IsEnabled);
        Assert.Equal("Capture scrolling area", bar.Scroll.ToolTip);
        bar.ScrollKeys = "Ctrl+Shift+D";
        Assert.Equal("Capture scrolling area · Ctrl+Shift+D", bar.Scroll.ToolTip);
        bar.ScrollKeys = null;
        Assert.Equal(6, bar.Scroll.Margin.Left); // 6 from Scan QR code, in the lead group

        var picked = new List<FinishAction>();
        bar.Picked += picked.Add;
        Click(bar.Scroll);
        Assert.Equal([FinishAction.Scroll], picked);

        bar.ScrollEnabled = false;
        Assert.False(bar.Scroll.IsEnabled);
        Assert.Equal("One display, rectangle", bar.Scroll.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(bar.Scroll));
        bar.ScrollKeys = "Ctrl+Shift+D"; // the keys only belong to the enabled tooltip
        Assert.Equal("One display, rectangle", bar.Scroll.ToolTip);
    });

    [Fact]
    public void Scroll_alone_sits_flush_and_a_failure_takes_the_lead_group_away() => WpfTestThread.Run(() =>
    {
        var bar = new FinishBar { ShowCopyText = false, ShowScanQrCode = false, ShowScroll = true };
        Laid(bar);
        Assert.Equal(0, bar.Scroll.Margin.Left);
        bar.Failure = "Copy failed";
        Assert.Equal(Visibility.Collapsed, bar.Scroll.Visibility);
        bar.Failure = null;
        Assert.Equal(Visibility.Visible, bar.Scroll.Visibility);
    });

    // ---- the scrolling bar ----

    [Fact]
    public void The_bar_says_each_state_in_the_briefs_words_and_the_size_with_separators() => WpfTestThread.Run(() => WithCulture("en-US", () =>
    {
        var bar = new ScrollingBar();
        Laid(bar);
        Assert.Equal("Scroll", bar.LineText);
        void Check(ScrollingLine line, string? words, int w = 700, int h = 2180)
        {
            bar.Apply(new ScrollingBarState(line, w, h, true, false));
            Assert.Equal(words, bar.LineText);
        }
        Check(ScrollingLine.Scroll, "Scroll");
        Check(ScrollingLine.None, null); // the status is empty while all is well
        Check(ScrollingLine.Scrolling, "Scrolling");
        Check(ScrollingLine.TooFast, "Too fast");
        Check(ScrollingLine.EndReached, "End reached");
        Check(ScrollingLine.LimitReached, "Limit reached", 700, 30000);
        Assert.Equal("700 × 30,000", bar.SizeText);
        Check(ScrollingLine.None, null, 700, 508);
        Assert.Equal("700 × 508", bar.SizeText);
        Assert.Equal("700 × 2,180", ScrollingBar.Size(700, 2180));
    }));

    [Fact]
    public void Auto_is_dimmed_with_its_tooltip_where_it_is_off_and_Stop_replaces_it_while_it_runs() => WpfTestThread.Run(() =>
    {
        var bar = new ScrollingBar();
        Laid(bar);
        var asked = new List<string>();
        bar.AutoAsked += () => asked.Add("auto");
        bar.StopAsked += () => asked.Add("stop");
        bar.DoneAsked += () => asked.Add("done");
        bar.CancelAsked += () => asked.Add("cancel");

        bar.Apply(new ScrollingBarState(ScrollingLine.Scroll, 700, 508, AutoAvailable: false, AutoActive: false));
        Assert.False(bar.AutoButton.IsEnabled);
        Assert.Equal("Auto-scroll not available", bar.AutoButton.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(bar.AutoButton));

        bar.Apply(new ScrollingBarState(ScrollingLine.Scroll, 700, 508, true, false));
        Assert.True(bar.AutoButton.IsEnabled);
        Assert.Null(bar.AutoButton.ToolTip);
        Assert.Equal("Auto", bar.AutoText);
        Click(bar.AutoButton);

        bar.Apply(new ScrollingBarState(ScrollingLine.Scrolling, 700, 3640, true, true));
        Assert.True(bar.AutoButton.IsEnabled);
        Assert.Equal("Stop", bar.AutoText);
        Click(bar.AutoButton);

        // At the end, and at the limit, Auto is spent: dimmed without a tooltip about availability.
        foreach (var line in new[] { ScrollingLine.EndReached, ScrollingLine.LimitReached })
        {
            bar.Apply(new ScrollingBarState(line, 700, 4920, true, false));
            Assert.False(bar.AutoButton.IsEnabled);
            Assert.Null(bar.AutoButton.ToolTip);
        }

        Click(bar.DoneButton);
        Click(bar.CancelButton);
        Assert.Equal(["auto", "stop", "done", "cancel"], asked);
        Assert.Equal("Done · Enter", bar.DoneButton.ToolTip);
        Assert.Equal("Cancel · Esc", bar.CancelButton.ToolTip);
    });

    // ---- the preview strip ----

    [Fact]
    public void The_preview_draws_its_seams_and_marks_a_gap() => WpfTestThread.Run(() =>
    {
        var preview = new ScrollingPreview();
        Laid(preview);
        var layout = ScrollingPreviewLayout.Compute(700, 2180, 508, [508, 1000, 1500], 1672, 2000, gap: true, 508);
        preview.Apply(layout, null);
        Assert.Equal(3, preview.SeamCount);
        Assert.True(preview.HasGap);
        Assert.Equal(layout.Height, preview.StripHeight, 6);
        preview.Apply(ScrollingPreviewLayout.Compute(700, 508, 508, [], 0, 0, false, 508), null);
        Assert.Equal(0, preview.SeamCount);
        Assert.False(preview.HasGap);
    });

    // ---- Recent ----

    [Fact]
    public void A_scrolling_capture_reads_Scrolling_with_its_size_and_the_others_read_as_before() => WithCulture("en-US", () =>
    {
        Assert.Equal("Scrolling · 700 × 4,920", RecentKinds.Label(RecentKinds.Scrolling, 700, 4920));
        Assert.Equal("700 × 4,920", RecentKinds.Label(RecentKinds.Scrolling, 700, 4920, withKind: false));
        Assert.Equal("1040 × 800", RecentKinds.Label(null, 1040, 800));
        var entry = new RecentCapture(Guid.NewGuid(), "x.png", DateTimeOffset.Now, 700, 4920, null, null, RecentKinds.Scrolling);
        Assert.True(entry.IsScrolling);
        Assert.Equal("Scrolling · 700 × 4,920", entry.Label(withKind: true));
        Assert.False(new RecentCapture(Guid.NewGuid(), "x.png", DateTimeOffset.Now, 1, 1, null).IsScrolling);
    });

    [Fact]
    public void An_entry_from_before_kinds_reads_with_no_kind_and_a_scrolling_one_keeps_its_kind() => WpfTestThread.Run(() =>
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<RecentCapture>(
            $$"""{"Id":"{{Guid.NewGuid()}}","Image":"a.png","Time":"2026-11-01T10:00:00+00:00","Width":10,"Height":20,"SavedFile":null,"SavedIdentity":null}""")!;
        Assert.Null(old.Kind);
        Assert.False(old.IsScrolling);

        string folder = Path.Combine(Path.GetTempPath(), "chippi-kind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var pixels = BitmapSource.Create(40, 160, 96, 96, PixelFormats.Bgra32, null, new byte[40 * 160 * 4], 160);
            var plain = Guid.NewGuid();
            var tall = Guid.NewGuid();
            using (var store = new RecentCaptureStore(folder))
            {
                store.Put(plain, pixels, DateTimeOffset.Now, null);
                store.Put(tall, pixels, DateTimeOffset.Now, null, RecentKinds.Scrolling);
                // Saving the same capture again keeps its kind.
                store.Put(tall, pixels, DateTimeOffset.Now, Path.Combine(folder, "saved.png"));
            }
            using (var store = new RecentCaptureStore(folder))
            {
                var entries = store.Read();
                Assert.Null(entries.Single(x => x.Id == plain).Kind);
                Assert.Equal(RecentKinds.Scrolling, entries.Single(x => x.Id == tall).Kind);
            }
        }
        finally { Directory.Delete(folder, true); }
    });

    // ---- the shots ----

    private static string? ShotsFolder => Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is { Length: > 0 } folder ? folder : null;

    private static void Save(FrameworkElement view, string folder, string name, double scale = 2)
    {
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        view.Arrange(new Rect(view.DesiredSize));
        view.UpdateLayout();
        int w = (int)Math.Ceiling(view.ActualWidth * scale), h = (int)Math.Ceiling(view.ActualHeight * scale);
        var bitmap = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }

    private static bool Light => WpfTestThread.ShotsTheme == "Light";

    /// <summary>The desk behind a bar, as the mockup draws it.</summary>
    private static Brush Desk => new SolidColorBrush(Light ? Color.FromRgb(0xAE, 0xB6, 0xC4) : Color.FromRgb(0x3A, 0x3F, 0x4B));

    private static StackPanel Column(params FrameworkElement[] items)
    {
        var column = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        foreach (var item in items)
        {
            item.Margin = new Thickness(0, 0, 0, 10);
            column.Children.Add(item);
        }
        return column;
    }

    private static Border Sheet(FrameworkElement content)
    {
        var host = new Border { Child = content, Background = Desk };
        host.Resources.MergedDictionaries.Add(IslandStyles());
        return host;
    }

    /// <summary>A stand-in for the result so far: paper, a header, a sidebar and lines, as the mockup's mini page.</summary>
    private static CapturedImage Paper(int width, int height)
    {
        var px = new byte[width * height * 4];
        void Fill(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(height, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                {
                    int i = (y * width + x) * 4;
                    px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
                }
        }
        Fill(0, 0, width, height, 0xF7, 0xF7, 0xF7);
        Fill(0, 0, width * 19 / 100, height, 0xEF, 0xEF, 0xF2);
        Fill(0, 0, width, 5, 0xFF, 0xFF, 0xFF);
        for (int y = 9, k = 0; y < height - 2; y += 3, k++)
        {
            if (k % 9 == 4) { Fill(width * 24 / 100, y, width * 94 / 100, y + 9, 0xC8, 0xB8, 0xF6); y += 8; }
            else if (k % 9 == 0) Fill(width * 24 / 100, y, width * 24 / 100 + width * 46 / 100 * 7 / 10, y + 2, 0x9A, 0x9A, 0xA6);
            else Fill(width * 24 / 100, y, width * 24 / 100 + (width * 70 / 100 * (60 + (k * 7) % 38)) / 100, y + 1, 0xC6, 0xC6, 0xCC);
        }
        return new CapturedImage(width, height, px);
    }

    private static ScrollingPreview Strip(int resultWidth, int resultHeight, int[] seams, int viewportTop, int gapRow = 0, bool gap = false, int area = 508)
    {
        var preview = new ScrollingPreview();
        var layout = ScrollingPreviewLayout.Compute(resultWidth, resultHeight, area, seams, viewportTop, gapRow, gap, area);
        preview.Apply(layout, ScrollingPreview.Picture(Paper(96, Math.Max(1, (int)Math.Ceiling(layout.StripHeight)))));
        return preview;
    }

    [Fact]
    public void Shots()
    {
        if (ShotsFolder is not { } folder) return;
        WpfTestThread.Run(() =>
        {
            // Board 1: the finish bar with Scroll, and with it dimmed for a freeform or two-display selection.
            var enabled = new FinishBar { ShowCopyText = true, ShowScanQrCode = true, ShowScroll = true };
            var disabled = new FinishBar { ShowCopyText = true, ShowScanQrCode = true, ShowScroll = true, ScrollEnabled = false };
            Save(Sheet(Column(enabled, disabled)), folder, "scrolling-finish");

            // Boards 3 to 9: the scrolling bar in each state.
            var states = new (ScrollingLine Line, int W, int H, bool Avail, bool Active)[]
            {
                (ScrollingLine.Scroll, 700, 508, true, false),
                (ScrollingLine.None, 700, 2180, true, false),
                (ScrollingLine.TooFast, 700, 2180, true, false),
                (ScrollingLine.Scrolling, 700, 3640, true, true),
                (ScrollingLine.EndReached, 700, 4920, true, false),
                (ScrollingLine.LimitReached, 700, 30000, true, false),
                (ScrollingLine.Scroll, 700, 508, false, false),
            };
            var bars = states.Select(s =>
            {
                var bar = new ScrollingBar();
                bar.Apply(new ScrollingBarState(s.Line, s.W, s.H, s.Avail, s.Active));
                bar.HorizontalAlignment = HorizontalAlignment.Left;
                return (FrameworkElement)bar;
            }).ToArray();
            Save(Sheet(Column(bars)), folder, "scrolling-bars");

            // The preview strip: the start, by hand with seams, Too fast with its gap, Auto, the end, the limit.
            var previews = new[]
            {
                Strip(700, 508, [], 0),
                Strip(700, 2180, [508, 1000, 1500], 1672),
                Strip(700, 2180, [508, 1000, 1500], 1672, gapRow: 2180, gap: true),
                Strip(700, 3640, [508, 1000, 1500, 2000, 2500], 3132),
                Strip(700, 4920, [508, 1000, 1500, 2000, 2500, 3000, 3300], 4412),
                Strip(700, 30000, [], 29492),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 16) };
            foreach (var preview in previews)
            {
                preview.VerticalAlignment = VerticalAlignment.Top;
                preview.Margin = new Thickness(0, 0, 16, 0);
                row.Children.Add(preview);
            }
            Save(Sheet(row), folder, "scrolling-previews");

            // Board 10: the result's bar.
            var result = new FinishBar { ShowCopyText = false, ShowScanQrCode = false, ShowScroll = false };
            Save(Sheet(Column(result)), folder, "scrolling-result-bar");

            Board(folder, "scrolling-board-start", ScrollingLine.Scroll, 700, 508, true, false, 0, [], 0);
            Board(folder, "scrolling-board-manual", ScrollingLine.None, 700, 2180, true, false, 0, [508, 1000, 1500], 1672);
            Board(folder, "scrolling-board-toofast", ScrollingLine.TooFast, 700, 2180, true, false, 2180, [508, 1000, 1500], 1672);
            Board(folder, "scrolling-board-auto", ScrollingLine.Scrolling, 700, 3640, true, true, 0, [508, 1000, 1500, 2000, 2500], 3132);
            Board(folder, "scrolling-board-end", ScrollingLine.EndReached, 700, 4920, true, false, 0, [508, 1000, 1500, 2000, 2500, 3000, 3300], 4412);
            Board(folder, "scrolling-board-unavailable", ScrollingLine.Scroll, 700, 508, false, false, 0, [], 0);
            Board(folder, "scrolling-board-limit", ScrollingLine.LimitReached, 700, 30000, true, false, 0, [], 29492);
        });
    }

    /// <summary>
    /// A board of the mockup at real size: a 1100 by 640 display with a page in a window, the frame and dim around the area,
    /// and the bar and preview where <see cref="ScrollingPlacement"/> puts them. The page is invented.
    /// </summary>
    private static void Board(string folder, string name, ScrollingLine line, int w, int h, bool avail, bool active, int gapRow, int[] seams, int viewportTop)
    {
        var display = new PixelRect(0, 0, 1100, 640);
        var area = new PixelRect(60, 56, 700, 508);
        var screen = new Canvas { Width = 1100, Height = 640, Background = new LinearGradientBrush(Color.FromRgb(0x2C, 0x3E, 0x57), Color.FromRgb(0x1B, 0x28, 0x38), 70) };
        Rectangle(screen, 60, 24, 700, 540, 0xF3);
        Rectangle(screen, 60, 56, 700, 40, 0xFF);   // the sticky header
        Rectangle(screen, 60, 96, 150, 468, 0xEF);  // the fixed sidebar
        for (int y = 110, k = 0; y < 550; y += 20, k++)
            Rectangle(screen, 236, y, k % 9 == 0 ? 320 : 380 + (k * 37) % 120, 8, (byte)(k % 9 == 0 ? 0x9A : 0xD3));
        Rectangle(screen, 780, 70, 290, 380, 0x1E);  // another window, left alone
        screen.Children.Add(ScrollingStage.Backdrop(display, area, 96));

        var bar = new ScrollingBar();
        bar.Apply(new ScrollingBarState(line, w, h, avail, active));
        bar.Margin = new Thickness(0);
        var preview = Strip(w, h, seams, viewportTop, gapRow, gap: line == ScrollingLine.TooFast, area: area.Height);
        var root = new Grid { Width = 1100, Height = 640 };
        root.Resources.MergedDictionaries.Add(IslandStyles());
        root.Children.Add(screen);
        var host = new Canvas { Width = 1100, Height = 640 };
        root.Children.Add(host);
        host.Children.Add(bar);
        host.Children.Add(preview);
        root.Measure(new Size(1100, 640));
        root.Arrange(new Rect(0, 0, 1100, 640));
        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // The surfaces' shadows aren't part of their layout, so these are the visible sizes.
        var barRect = ScrollingPlacement.Bar(area, display, new PixelSize((int)Math.Ceiling(bar.DesiredSize.Width), (int)Math.Ceiling(bar.DesiredSize.Height)));
        var previewRect = ScrollingPlacement.Preview(area, display, new PixelSize((int)Math.Ceiling(preview.DesiredSize.Width), (int)Math.Ceiling(preview.DesiredSize.Height)));
        Canvas.SetLeft(bar, barRect.X); Canvas.SetTop(bar, barRect.Y);
        Canvas.SetLeft(preview, previewRect.X); Canvas.SetTop(preview, previewRect.Y);
        root.Measure(new Size(1100, 640));
        root.Arrange(new Rect(0, 0, 1100, 640));
        root.UpdateLayout();
        Save(root, folder, name, scale: 1);
    }

    private static void Rectangle(Canvas canvas, double x, double y, double w, double h, byte gray)
    {
        var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromRgb(gray, gray, gray)) };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        canvas.Children.Add(rect);
    }
}
