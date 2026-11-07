// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.Core.Modules;
using Chippi.UI.Capture;
using Chippi.UI.Controls;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// A Recent capture's page (screen-capture.md, Recent) over a fake model: Copy, Save as, Annotate and Open folder, what
/// each says back, File missing and a capture that left Recent. With CHIPPI_ISLAND_SHOTS, its states in the real island
/// panel window (capture-recent-*); with CHIPPI_CAPTURE_PANEL_TIMINGS beside it, the page's open.
/// </summary>
public sealed class CaptureRecentTests
{
    private const string Saved = @"C:\Users\Test\Pictures\Screenshots\Screenshot-2026-11-06-142233-118.png";

    [Fact]
    public void Each_verb_asks_the_model_and_says_how_it_went() => WpfTestThread.Run(() =>
    {
        var model = new Model { Result = Capture() with { SavedFile = Saved } };
        var view = new CaptureRecentView(model);
        Assert.Equal("Screenshot-2026-11-06-142233-118.png | 1280 × 720 · 2 min ago", view.Info);
        Assert.Equal(["Open folder", "Annotate", "Save as", "Copy"], new[] { view.OpenFolderButton, view.AnnotateButton, view.SaveAsButton, view.CopyButton }.Select(AutomationProperties.GetName));
        Assert.Same(view.CopyButton, view.InitialFocus);

        Press(view.CopyButton);
        Assert.Equal("Copied", view.Status);
        model.CopyWorks = false;
        Press(view.CopyButton);
        Assert.Equal("Copy failed", view.Status);

        Press(view.SaveAsButton);
        Assert.Equal(@"Saved · Screenshot copy.png", view.Status);
        model.Save = new CaptureSave(CaptureSaveOutcome.Cancelled);
        Press(view.SaveAsButton);
        Assert.Equal(@"Saved · Screenshot copy.png", view.Status); // the dialog's Cancel leaves the page as it was
        model.Save = new CaptureSave(CaptureSaveOutcome.Failed);
        Press(view.SaveAsButton);
        Assert.Equal("Save failed", view.Status);

        Press(view.OpenFolderButton);
        Assert.Equal("", view.Status);
        model.FolderOpens = false;
        Press(view.OpenFolderButton);
        Assert.Equal("Open folder failed", view.Status);

        Press(view.AnnotateButton);
        Assert.Equal("", view.Status);
        model.AnnotateWorks = false;
        Press(view.AnnotateButton);
        Assert.Equal("Annotate failed", view.Status);
        Assert.Equal((2, 3, 2, 2), (model.Copies, model.Saves, model.Annotates, model.Folders));
    });

    [Fact]
    public void A_deleted_file_reads_File_missing_and_every_verb_still_works() => WpfTestThread.Run(() =>
    {
        var model = new Model { Result = Capture() with { SavedFile = Saved } };
        var view = new CaptureRecentView(model);
        model.Result = model.Result with { FileMissing = true };
        model.Raise();
        Assert.Equal("File missing | Screenshot-2026-11-06-142233-118.png | 1280 × 720 · 2 min ago", view.Info);
        Assert.All(new[] { view.OpenFolderButton, view.AnnotateButton, view.SaveAsButton, view.CopyButton }, b => Assert.True(b.IsEnabled));
        Press(view.CopyButton);
        Assert.Equal("Copied", view.Status);
    });

    [Fact]
    public void A_copy_only_capture_shows_its_size_and_the_full_image_replaces_the_thumbnail_in_place() => WpfTestThread.Run(() =>
    {
        var model = new Model { Result = Capture() };
        var view = new CaptureRecentView(model);
        Assert.Equal("1280 × 720 · 2 min ago", view.Info);
        Assert.Same(model.Result.Thumbnail, view.Shown);
        Measure(view);
        var before = view.ShownBounds;
        Assert.True(before.Width > 300, $"{before}");

        model.Result = model.Result with { Image = Picture(1280, 720) };
        model.Raise();
        Assert.Same(model.Result.Image, view.Shown);
        Measure(view);
        Assert.Equal(before, view.ShownBounds);
    });

    [Fact]
    public void A_capture_that_left_Recent_says_so_and_offers_nothing() => WpfTestThread.Run(() =>
    {
        var model = new Model { Result = Capture() };
        var view = new CaptureRecentView(model);
        model.Result = null;
        model.Raise();
        Assert.Equal("Capture removed", AutomationProperties.GetName(view.Picture));
        Assert.Null(view.Shown);
        Assert.Equal(Visibility.Hidden, ((FrameworkElement)view.CopyButton.Parent).Visibility);
        Press(view.CopyButton);
        Assert.Equal(0, model.Copies);
    });

    [Fact]
    public void Verbs_wait_while_a_copy_runs() => WpfTestThread.Run(() =>
    {
        var pending = new TaskCompletionSource<bool>();
        var model = new Model { Result = Capture(), PendingCopy = pending.Task };
        var view = new CaptureRecentView(model);
        Press(view.CopyButton);
        Assert.False(view.CopyButton.IsEnabled);
        Assert.False(view.SaveAsButton.IsEnabled);
        Press(view.CopyButton);
        Assert.Equal(1, model.Copies);
        pending.SetResult(true);
        Pump(TimeSpan.FromMilliseconds(50));
        Assert.True(view.CopyButton.IsEnabled);
        Assert.Equal("Copied", view.Status);
    });

    [Fact]
    public void Every_state_fits_the_body() => WpfTestThread.Run(() =>
    {
        foreach (var result in new[] { Capture() with { SavedFile = Saved, FileMissing = true }, Capture(), Capture(400, 1400), null })
        {
            var view = new CaptureRecentView(new Model { Result = result });
            Measure(view);
            var body = (FrameworkElement)view.Content;
            Assert.True(body.DesiredSize.Height <= IslandPage.BodyHeight, $"{body.DesiredSize.Height} > {IslandPage.BodyHeight}");
            Assert.True(view.Picture.RenderSize.Height >= 200, $"picture {view.Picture.RenderSize.Height}");
        }
    });

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Measure(CaptureRecentView view)
    {
        view.Measure(new Size(IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.Arrange(new Rect(0, 0, IslandPage.BodyWidth, IslandPage.BodyHeight));
    }

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
            var full = Capture() with { Image = Picture(1280, 720) };
            Shoot(new Model { Result = full with { SavedFile = Saved } }, Path.Combine(folder, $"capture-recent-saved-{theme}.png"));
            Shoot(new Model { Result = full with { SavedFile = Saved, FileMissing = true } }, Path.Combine(folder, $"capture-recent-missing-{theme}.png"));
            Shoot(new Model { Result = full }, Path.Combine(folder, $"capture-recent-copied-{theme}.png"), view => Press(view.CopyButton));
            Shoot(new Model { Result = full, CopyWorks = false }, Path.Combine(folder, $"capture-recent-copy-failed-{theme}.png"), view => Press(view.CopyButton));
            Shoot(new Model { Result = Capture(400, 1400) with { Image = Picture(400, 1400), SavedFile = Saved } }, Path.Combine(folder, $"capture-recent-tall-{theme}.png"));
            Shoot(new Model { Result = Capture(240, 120) with { Image = Picture(240, 120) } }, Path.Combine(folder, $"capture-recent-small-{theme}.png"));
            Shoot(new Model(), Path.Combine(folder, $"capture-recent-removed-{theme}.png"));
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
            var (tiles, _) = Tile(new Model { Result = Capture() with { Image = Picture(3440, 1440), SavedFile = Saved } });
            var panel = new IslandPanelWindow();
            try
            {
                panel.Initialize();
                panel.SetMedia(new FakeMedia());
                panel.SetTiles(tiles);
                panel.SetLastPage(null);
                panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
                Pump(TimeSpan.FromMilliseconds(600));
                var warm = new List<double>();
                for (int i = 0; i < 21; i++)
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    panel.ShowPage("capture-recent", remember: false);
                    panel.UpdateLayout();
                    double call = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    lines.Add($"{(i == 0 ? "cold" : "warm")}\t{call:0.0}");
                    if (i > 0) warm.Add(call);
                    Pump(TimeSpan.FromMilliseconds(300));
                    panel.ShowPage(null, remember: false);
                    Pump(TimeSpan.FromMilliseconds(300));
                }
                warm.Sort();
                lines.Insert(0, $"Recent capture page, 3440 × 1440 retained image already decoded. ShowPage to laid out: warm p50 {warm[warm.Count / 2]:0.0} ms, p95 {warm[(int)Math.Ceiling(warm.Count * 0.95) - 1]:0.0} ms, max {warm[^1]:0.0} ms (budget: warm p95 <= 100, cold <= 200)");
            }
            finally
            {
                panel.Close();
            }
        });
        File.AppendAllLines(report, lines);
    }

    /// <summary>The page as the host will add it: the view as its section, its header as the page's.</summary>
    private static (IReadOnlyList<IslandTileGroup> Tiles, Func<CaptureRecentView> View) Tile(Model model)
    {
        CaptureRecentView? view = null;
        CaptureRecentView View() => view ??= new CaptureRecentView(model);
        var spec = new IslandTileSpec("capture-recent", "Recent", nameof(LucideIcons.Image), Order: 76, SectionOrder: 0, () => View())
        {
            CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus,
        };
        return (IslandTileContribution.Group([new IslandTileContribution("screen-capture", spec)]), View);
    }

    private static void Shoot(Model model, string path, Action<CaptureRecentView>? arrange = null)
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
            panel.ShowPage("capture-recent");
            Pump(TimeSpan.FromMilliseconds(400));
            arrange?.Invoke(view());
            Pump(TimeSpan.FromMilliseconds(300));
            var root = panel.Visual;
            var pixels = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            pixels.Render(root);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, pixels.PixelWidth, pixels.PixelHeight));
                dc.DrawImage(pixels, new Rect(0, 0, pixels.PixelWidth, pixels.PixelHeight));
            }
            var image = new RenderTargetBitmap(pixels.PixelWidth, pixels.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path);
            encoder.Save(file);
        }
        finally
        {
            panel.ShowPage(null, remember: false);
            panel.Close();
        }
    }
#endif

    private static CaptureRecentResult Capture(int width = 1280, int height = 720) =>
        new("recent-0", width, height, "2 min ago", Picture(Math.Max(1, (int)(width * Math.Min(236.0 / width, 132.0 / height))), Math.Max(1, (int)(height * Math.Min(236.0 / width, 132.0 / height)))));

    /// <summary>A synthetic document: a title bar and lines of text as grey bars, frozen.</summary>
    private static BitmapSource Picture(int width, int height)
    {
        var r = new Rect(0, 0, width, height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush(0xFFF7F7F8), null, r);
            dc.DrawRectangle(Brush(0xFF7444F0), null, new Rect(0, 0, width, height * 0.14));
            for (int line = 0; line < 8; line++)
                dc.DrawRectangle(Brush(0xFFC9CAD1), null, new Rect(width * 0.08, height * (0.24 + line * 0.09), width * (line % 3 == 2 ? 0.45 : 0.8), height * 0.035));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private sealed class Model : ICaptureRecentModel
    {
        public CaptureRecentResult? Result { get; set; }
        public bool CopyWorks { get; set; } = true;
        public Task<bool>? PendingCopy { get; set; }
        public CaptureSave Save { get; set; } = new(CaptureSaveOutcome.Saved, @"D:\Shared\Screenshot copy.png");
        public bool FolderOpens { get; set; } = true;
        public int Copies { get; private set; }
        public int Saves { get; private set; }
        public int Annotates { get; private set; }
        public int Folders { get; private set; }

        public event Action? Changed;

        public Task<bool> CopyAsync()
        {
            Copies++;
            return PendingCopy ?? Task.FromResult(CopyWorks);
        }

        public Task<CaptureSave> SaveAsAsync()
        {
            Saves++;
            return Task.FromResult(Save);
        }

        public bool AnnotateWorks { get; set; } = true;

        public bool Annotate()
        {
            Annotates++;
            return AnnotateWorks;
        }

        public bool OpenFolder()
        {
            Folders++;
            return FolderOpens;
        }

        public void Raise() => Changed?.Invoke();
    }
}
