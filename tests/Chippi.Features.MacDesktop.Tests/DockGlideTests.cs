// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Features.MacDesktop.Dock;

namespace Chippi.Features.MacDesktop.Tests;

/// <summary>
/// Magnification under a slow pointer: every icon grows and moves by fractions of a pixel, while the slots layout gives
/// them, the shelf and the icons at rest either side stay on whole pixels.
/// </summary>
public sealed class DockSlotTests
{
    private const double Pitch = DockLayout.Icon + DockLayout.Gap;

    /// <summary>A dock like the owner's: Start and eleven apps, the divider, Downloads and the Recycle Bin.</summary>
    private static double[] Rests(int apps = 12) =>
        [.. Enumerable.Repeat(DockLayout.Icon, apps), DockLayout.Divider, DockLayout.Icon, DockLayout.Icon];

    /// <summary>Each tile's size with the pointer <paramref name="pointer"/> DIPs along the row at rest, as the dock sets it.</summary>
    private static double[] Sizes(double[] rests, double? pointer, double max)
    {
        var sizes = new double[rests.Length];
        double offset = 0;
        for (int i = 0; i < rests.Length; i++)
        {
            double center = offset + rests[i] / 2;
            offset += rests[i] + DockLayout.Gap;
            sizes[i] = rests[i] != DockLayout.Icon || pointer is not { } p ? rests[i] : DockLayout.Magnified((p - center) / Pitch, max);
        }
        return sizes;
    }

    /// <summary>Where each icon is drawn along the shelf, in pixels from the shelf's middle: its left edge and its size.</summary>
    private static (double Left, double Size)[] Drawn(DockSlot[] slots, double[] rests, double pixels)
    {
        double gap = Math.Round(DockLayout.Gap * pixels), length = slots.Sum(s => s.Extent * pixels + gap);
        var drawn = new (double, double)[slots.Length];
        double left = -length / 2;
        for (int i = 0; i < slots.Length; i++)
        {
            double rest = Math.Round(rests[i] * pixels), size = rest * slots[i].Scale;
            drawn[i] = (left + rest / 2 + slots[i].Shift * pixels - size / 2, size);
            left += slots[i].Extent * pixels + gap;
        }
        return drawn;
    }

    /// <summary>Where each icon would be with nothing rounded at all.</summary>
    private static (double Left, double Size)[] Exact(double[] sizes, double[] rests, double pixels)
    {
        double gap = Math.Round(DockLayout.Gap * pixels);
        var px = sizes.Select((s, i) => Math.Round(rests[i] * pixels) + (s - rests[i]) * pixels).ToArray();
        var exact = new (double, double)[sizes.Length];
        double left = -px.Sum(s => s + gap) / 2;
        for (int i = 0; i < px.Length; i++)
        {
            exact[i] = (left, px[i]);
            left += px[i] + gap;
        }
        return exact;
    }

    public static TheoryData<double> Scales => [1, 1.25, 1.5, 1.75, 2];

    [Theory]
    [MemberData(nameof(Scales))]
    public void At_rest_every_tile_has_its_own_pixels_and_nothing_is_moved(double pixels)
    {
        var rests = Rests();
        foreach (bool still in new[] { true, false })
        {
            var slots = DockLayout.Slots(rests, rests, pixels, still, out double grown);
            Assert.Equal(0, grown);
            for (int i = 0; i < slots.Length; i++)
                Assert.Equal(new DockSlot(Math.Round(rests[i] * pixels) / pixels, 0, 1), slots[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Slots_are_whole_pixels_and_the_row_grows_evenly_about_its_middle(double pixels)
    {
        var rests = Rests();
        for (double pointer = -40; pointer <= 15 * Pitch; pointer += 0.37)
        {
            var slots = DockLayout.Slots(Sizes(rests, pointer, 72), rests, pixels, still: false, out double grown);
            Assert.All(slots, s => Assert.Equal(Math.Round(s.Extent * pixels), s.Extent * pixels, 6));
            Assert.Equal(Math.Round(grown * pixels / 2) * 2, grown * pixels, 6);
            Assert.Equal(grown * pixels, slots.Select((s, i) => s.Extent * pixels - Math.Round(rests[i] * pixels)).Sum(), 6);
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Each_icon_is_drawn_exactly_where_and_as_large_as_it_is(double pixels)
    {
        var rests = Rests();
        for (double pointer = -20; pointer <= 15 * Pitch; pointer += 0.25)
        {
            var sizes = Sizes(rests, pointer, 72);
            var exact = Exact(sizes, rests, pixels);
            // Moving, every icon is exact.
            var moving = Drawn(DockLayout.Slots(sizes, rests, pixels, still: false, out _), rests, pixels);
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.Equal(exact[i].Size, moving[i].Size, 6);
                Assert.Equal(exact[i].Left, moving[i].Left, 6);
            }
            // Still, the ones at rest either side are on whole pixels, the grown ones between them exact.
            var slots = DockLayout.Slots(sizes, rests, pixels, still: true, out _);
            var drawn = Drawn(slots, rests, pixels);
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.Equal(exact[i].Size, drawn[i].Size, 6);
                // Within half a pixel everywhere; exact once a pixel of growth lies on both sides of it.
                Assert.InRange(drawn[i].Left - exact[i].Left, -0.5 - 1e-6, 0.5 + 1e-6);
                double before = sizes.Take(i + 1).Select((s, n) => (s - rests[n]) * pixels).Sum();
                double after = sizes.Skip(i).Select((s, n) => (s - rests[i + n]) * pixels).Sum();
                if (before >= 1 && after >= 1)
                    Assert.Equal(exact[i].Left, drawn[i].Left, 6);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Icons_at_rest_either_side_stay_on_their_pixels_while_the_pointer_moves_among_the_others(double pixels)
    {
        var rests = Rests();
        // From the sixth icon's middle to the seventh's: the first two icons and the last three tiles are out of reach.
        (double Left, double Size)[]? first = null;
        double? length = null;
        for (double pointer = 5 * Pitch + 24; pointer <= 6 * Pitch + 24; pointer += 0.25)
        {
            var slots = DockLayout.Slots(Sizes(rests, pointer, 72), rests, pixels, still: true, out double grown);
            var drawn = Drawn(slots, rests, pixels);
            first ??= drawn;
            length ??= grown;
            Assert.Equal(length.Value, grown, 9);
            foreach (int far in new[] { 0, 1, 12, 13, 14 })
            {
                Assert.Equal(first[far].Left, drawn[far].Left, 6);
                Assert.Equal(Math.Round(drawn[far].Left * 2) / 2, drawn[far].Left, 6); // a whole pixel from the middle, or a half
                Assert.Equal(0, slots[far].Shift);
                Assert.Equal(1, slots[far].Scale);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void A_slow_pointer_moves_no_icon_by_a_whole_pixel_at_once(double pixels)
    {
        var rests = Rests();
        foreach (double max in new[] { 56.0, 72, 81, 96 })
        {
            double glide = 0, stepped = 0;
            (double Left, double Size)[]? last = null;
            double[]? lastRounded = null;
            // A quarter of a DIP at a time, from off the row's start to off its end.
            for (double pointer = -30; pointer <= 15 * Pitch + 30; pointer += 0.25)
            {
                var sizes = Sizes(rests, pointer, max);
                var drawn = Drawn(DockLayout.Slots(sizes, rests, pixels, still: false, out _), rests, pixels);
                // As it was: each icon's size rounded to pixels on its own.
                var rounded = sizes.Select(s => Math.Round(s * pixels)).ToArray();
                if (last is not null && lastRounded is not null)
                    for (int i = 0; i < drawn.Length; i++)
                    {
                        glide = Math.Max(glide, Math.Max(Math.Abs(drawn[i].Left - last[i].Left), Math.Abs(drawn[i].Size - last[i].Size)));
                        stepped = Math.Max(stepped, Math.Abs(rounded[i] - lastRounded[i]));
                    }
                (last, lastRounded) = (drawn, rounded);
            }
            Assert.True(glide < 0.35 * pixels, $"an icon jumped {glide:0.###} px at magnified size {max}");
            Assert.True(stepped >= 1); // the stutter this replaces: a whole pixel at once
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_row_grows_a_pixel_each_side_at_a_time_as_the_icons_ease_in(double pixels)
    {
        var rests = Rests();
        var full = Sizes(rests, 6 * Pitch + 11, 72);
        double last = 0;
        (double Left, double Size)[]? lastDrawn = null;
        for (int step = 0; step <= 400; step++)
        {
            var sizes = full.Select((s, i) => rests[i] + (s - rests[i]) * step / 400).ToArray();
            var drawn = Drawn(DockLayout.Slots(sizes, rests, pixels, still: true, out double grown), rests, pixels);
            Assert.InRange((grown - last) * pixels, -1e-6, 2 + 1e-6);
            if (lastDrawn is not null)
                for (int i = 0; i < drawn.Length; i++)
                    Assert.InRange(Math.Abs(drawn[i].Left - lastDrawn[i].Left), 0, 1 + 1e-6); // the icons at rest: one pixel, with the shelf
            (last, lastDrawn) = (grown, drawn);
        }
    }

    [Fact]
    public void Growth_of_exactly_an_odd_pixel_does_not_flip_the_row_between_two_lengths()
    {
        // Magnified 81 at 100%: the icons grow 99 pixels in all, to half a pixel each side. The sum lands a hair either
        // side of it as the pointer moves.
        var rests = Rests();
        var lengths = new HashSet<double>();
        for (double pointer = 5 * Pitch + 24; pointer <= 6 * Pitch + 24; pointer += 0.01)
        {
            DockLayout.Slots(Sizes(rests, pointer, 81), rests, 1, still: false, out double grown);
            lengths.Add(grown);
        }
        Assert.Equal([98.0], lengths);
    }

    [Fact]
    public void The_shelfs_room_covers_its_growth_in_whole_pixels()
    {
        Assert.Equal(37, DockLayout.Room(72, 1));
        Assert.Equal(1, DockLayout.Room(DockLayout.Icon, 1));
        Assert.Equal(Math.Round(DockLayout.Room(72, 1.25) * 1.25), DockLayout.Room(72, 1.25) * 1.25, 9);
    }
}

/// <summary>
/// The row as the dock builds it (its tiles in a row in the shelf, layout rounding on), measured offscreen: where each
/// icon is drawn as the pointer creeps along. With CHIPPI_ISLAND_SHOTS naming a folder it also saves
/// <c>dock-glide.png</c> (one icon's edge per pointer step, as it was and as it is, 6 times) and <c>dock-glide.txt</c>.
/// Shows nothing.
/// </summary>
public sealed class DockGlideTests
{
    private const double Pitch = DockLayout.Icon + DockLayout.Gap;
    private const int Icons = 11, StripWidth = 56;

    [Fact]
    public void Icons_follow_a_slow_pointer_between_pixels() => OnSta(() =>
    {
        var row = new Row(magnified: 72);
        double pixels = row.Pixels;
        var rest = row.Read(null, glide: true);
        double worstGlide = 0, worstStepped = 0;
        var report = new List<string> { "pointer\tas it was: left,size\tnow: left,size  (icon 6, pixels)" };
        var frames = new List<(BitmapSource Was, BitmapSource Now)>();
        string? output = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        bool shots = !string.IsNullOrWhiteSpace(output);
        Rect[]? lastNow = null, lastWas = null;
        for (double pointer = 4 * Pitch + 24; pointer <= 6 * Pitch + 24; pointer += 0.25)
        {
            var was = row.Read(pointer, glide: false);
            var wasShot = shots ? row.Shot(5) : null;
            var now = row.Read(pointer, glide: true);
            var nowShot = shots ? row.Shot(5) : null;
            // Now, each icon is where the exact sizes put it, about the row's middle.
            var sizes = row.Sizes(pointer);
            double before = 0, total = sizes.Sum(s => s - DockLayout.Icon);
            for (int i = 0; i < Icons; i++)
            {
                double center = rest[i].Left + rest[i].Width / 2 + (before + (sizes[i] - DockLayout.Icon) / 2 - total / 2) * pixels;
                before += sizes[i] - DockLayout.Icon;
                Assert.Equal(center, now[i].Left + now[i].Width / 2, 6);
                Assert.Equal(sizes[i] * pixels, now[i].Width, 6);
                Assert.Equal(sizes[i] * pixels, now[i].Height, 6);
                Assert.Equal(rest[i].Bottom, now[i].Bottom, 6); // grown up out of the shelf, its foot where it was
            }
            if (lastNow is not null && lastWas is not null)
                for (int i = 0; i < Icons; i++)
                {
                    worstGlide = Math.Max(worstGlide, Math.Max(Math.Abs(now[i].Left - lastNow[i].Left), Math.Abs(now[i].Width - lastNow[i].Width)));
                    worstStepped = Math.Max(worstStepped, Math.Max(Math.Abs(was[i].Left - lastWas[i].Left), Math.Abs(was[i].Width - lastWas[i].Width)));
                }
            (lastNow, lastWas) = (now, was);
            report.Add(string.Create(CultureInfo.InvariantCulture, $"{pointer:0.00}\t{was[5].Left:0.00},{was[5].Width:0.00}\t{now[5].Left:0.00},{now[5].Width:0.00}"));
            if (wasShot is not null && nowShot is not null)
                frames.Add((wasShot, nowShot));
        }
        Assert.True(worstGlide < 0.35 * pixels, $"an icon jumped {worstGlide:0.###} px");
        Assert.True(worstStepped >= 1, "the row as it was no longer steps: the comparison measures nothing");
        // Back at rest, every icon is on the pixels it started on.
        Assert.Equal(rest, row.Read(null, glide: true));

        if (!shots)
            return;
        Directory.CreateDirectory(output!);
        report.Insert(0, string.Create(CultureInfo.InvariantCulture, $"largest move of any icon's edge or size per 0.25 DIP of pointer: as it was {worstStepped:0.###} px, now {worstGlide:0.###} px ({pixels:0.##} px per DIP)"));
        File.WriteAllLines(Path.Combine(output!, "dock-glide.txt"), report);
        Save(Strip(frames), Path.Combine(output!, "dock-glide.png"));
    });

    /// <summary>The dock's row without its window: the shelf centered in a root with layout rounding on, as DockWindow has it.</summary>
    private sealed class Row
    {
        private readonly Grid _root = new() { UseLayoutRounding = true, Width = 1201, Height = 150, Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2E)) };
        private readonly Border _shelf = new() { BorderThickness = new Thickness(1), Height = DockLayout.Shelf, Padding = new Thickness(DockLayout.Padding, 0, DockLayout.Padding, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        private readonly List<DockTile> _tiles = [];
        private readonly double _max;

        public Row(double magnified)
        {
            _max = magnified;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var picture = Picture(72);
            for (int i = 0; i < Icons; i++)
            {
                var tile = DockTile.Still("icon" + i, picture);
                tile.Orient(DockEdge.Bottom);
                _tiles.Add(tile);
                row.Children.Add(tile);
            }
            _shelf.Child = row;
            _root.Children.Add(_shelf);
            Pixels = VisualTreeHelper.GetDpi(_root).DpiScaleX;
        }

        public double Pixels { get; }

        public double[] Sizes(double? pointer) =>
            [.. _tiles.Select((_, i) => pointer is { } p ? DockLayout.Magnified((p - (i * Pitch + DockLayout.Icon / 2)) / Pitch, _max) : DockLayout.Icon)];

        /// <summary>
        /// Lays the row out for the pointer and returns each icon's drawn bounds in pixels. Not gliding, each slot and icon
        /// is its size rounded to pixels on its own, as the row was laid out before.
        /// </summary>
        public Rect[] Read(double? pointer, bool glide)
        {
            var sizes = Sizes(pointer);
            var rests = Enumerable.Repeat(DockLayout.Icon, Icons).ToArray();
            var slots = DockLayout.Slots(sizes, rests, Pixels, still: false, out double grown);
            for (int i = 0; i < Icons; i++)
            {
                _tiles[i].SetSize(sizes[i]);
                double rounded = Math.Round(sizes[i] * Pixels) / Pixels;
                _tiles[i].Place(glide ? slots[i] : new DockSlot(rounded, (rounded - DockLayout.Icon) / 2, rounded / DockLayout.Icon));
            }
            double room = glide ? DockLayout.Room(_max, Pixels) - grown / 2 : 0;
            _shelf.Margin = new Thickness(room, 0, room, DockLayout.EdgeGap);
            _root.Measure(new Size(_root.Width, _root.Height));
            _root.Arrange(new Rect(0, 0, _root.Width, _root.Height));
            _root.UpdateLayout();
            return [.. _tiles.Select(t =>
            {
                var bounds = t.Art.TransformToAncestor(_root).TransformBounds(new Rect(t.Art.RenderSize));
                return new Rect(bounds.X * Pixels, bounds.Y * Pixels, bounds.Width * Pixels, bounds.Height * Pixels);
            })];
        }

        /// <summary>The row as drawn now, cropped to one line across one icon's left edge.</summary>
        public BitmapSource Shot(int icon)
        {
            var target = new RenderTargetBitmap((int)Math.Round(_root.Width * Pixels), (int)Math.Round(_root.Height * Pixels), 96 * Pixels, 96 * Pixels, PixelFormats.Pbgra32);
            target.Render(_root);
            var art = _tiles[icon].Art.TransformToAncestor(_root).TransformBounds(new Rect(_tiles[icon].Art.RenderSize));
            int rest = (int)Math.Round((_root.Width - Icons * Pitch) / 2 * Pixels) + (int)Math.Round(icon * Pitch * Pixels);
            var crop = new CroppedBitmap(target, new Int32Rect(rest - 36, (int)Math.Round((art.Y + art.Height / 2) * Pixels), StripWidth, 1));
            crop.Freeze();
            return crop;
        }

        /// <summary>A flat bright square with a dark edge, so its edge reads on the shelf at any size.</summary>
        private static BitmapSource Picture(int size)
        {
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < 3 || y < 3 || x >= size - 3 || y >= size - 3;
                    int at = (y * size + x) * 4;
                    (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = edge ? ((byte)0x20, (byte)0x60, (byte)0xFF, (byte)0xFF) : ((byte)0xF0, (byte)0xF0, (byte)0xF0, (byte)0xFF);
                }
            var picture = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
            picture.Freeze();
            return picture;
        }
    }

    /// <summary>One row per pointer step, shown 6 times: the icon's edge as it was on the left, as it is on the right.</summary>
    private static BitmapSource Strip(List<(BitmapSource Was, BitmapSource Now)> frames)
    {
        const int zoom = 6, width = StripWidth, height = 3, gap = 4;
        int outWidth = (width * 2 + gap) * zoom, outHeight = frames.Count * height;
        var big = new byte[outWidth * outHeight * 4];
        var line = new byte[width * 4];
        for (int f = 0; f < frames.Count; f++)
            for (int side = 0; side < 2; side++)
            {
                var source = side == 0 ? frames[f].Was : frames[f].Now;
                source.CopyPixels(new Int32Rect(0, 0, width, 1), line, width * 4, 0);
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width * zoom; x++)
                        Array.Copy(line, x / zoom * 4, big, ((f * height + y) * outWidth + side * (width + gap) * zoom + x) * 4, 4);
            }
        var strip = BitmapSource.Create(outWidth, outHeight, 96, 96, PixelFormats.Pbgra32, null, big, outWidth * 4);
        strip.Freeze();
        return strip;
    }

    private static void Save(BitmapSource picture, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void OnSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
