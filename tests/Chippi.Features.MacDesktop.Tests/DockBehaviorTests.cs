// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Actions;
using Chippi.Features.MacDesktop.Dock;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Controls;

namespace Chippi.Features.MacDesktop.Tests;

/// <summary>What a click on a dock icon does, and how a crowded shelf fits its display.</summary>
public sealed class DockClickTests
{
    private static ListedWindow Window(nint handle, bool minimized = false) =>
        new(handle, 1, "App", "Title", @"C:\App\app.exe", minimized, 0);

    private static DockApp App(params ListedWindow[] windows) => new("APP", @"C:\App\app.exe", @"C:\App\app.exe", "App", true, windows);

    [Fact]
    public void An_app_that_is_not_running_opens() =>
        Assert.Equal((DockClick.Launch, (nint)0), DockApps.OnClick(App(), front: 5));

    [Fact]
    public void An_app_behind_another_shows_its_front_window() =>
        Assert.Equal((DockClick.Show, (nint)7), DockApps.OnClick(App(Window(7), Window(8)), front: 5));

    [Fact]
    public void A_minimized_app_is_shown_not_left_minimized() =>
        Assert.Equal((DockClick.Show, (nint)7), DockApps.OnClick(App(Window(7, minimized: true)), front: 5));

    [Fact]
    public void The_window_in_front_minimizes() =>
        Assert.Equal((DockClick.Minimize, (nint)8), DockApps.OnClick(App(Window(7), Window(8)), front: 8));

    /// <summary>A minimized window can still hold the keyboard; a click must bring it back, never minimize it again.</summary>
    [Fact]
    public void A_minimized_window_that_holds_the_keyboard_is_shown() =>
        Assert.Equal((DockClick.Show, (nint)7), DockApps.OnClick(App(Window(7, minimized: true)), front: 7));

    /// <summary>Four pins 54 apart, the first one's middle at 100.</summary>
    [Theory]
    [InlineData(100, 0)]
    [InlineData(126, 0)]  // not yet half a place over
    [InlineData(128, 1)]  // half a place in: the neighbor gives way
    [InlineData(154, 1)]
    [InlineData(180, 1)]
    [InlineData(182, 2)]
    [InlineData(262, 3)]
    [InlineData(900, 3)]  // past the last pin: the last place
    [InlineData(-50, 0)]
    public void A_dragged_pin_lands_in_the_place_its_middle_is_over(double middle, int place) =>
        Assert.Equal(place, DockApps.SlotAt(middle, first: 100, pitch: 54, count: 4));

    [Fact]
    public void A_pin_lands_where_it_is_dropped_from_either_side()
    {
        string[] pins = ["a", "b", "c", "d"];
        // "a" dragged one place right, then "d" dragged one place left: each moves exactly one place.
        Assert.Equal(["b", "a", "c", "d"], DockApps.Move(pins, "a", DockApps.SlotAt(154, 100, 54, 4)));
        Assert.Equal(["a", "b", "d", "c"], DockApps.Move(pins, "d", DockApps.SlotAt(208, 100, 54, 4)));
        // Dropped back on its own place, nothing moves.
        Assert.Equal(pins, DockApps.Move(pins, "b", DockApps.SlotAt(160, 100, 54, 4)));
        // Three places over lands three places over.
        Assert.Equal(["b", "c", "d", "a"], DockApps.Move(pins, "a", DockApps.SlotAt(262, 100, 54, 4)));
    }

    [Fact]
    public void A_shelf_that_fits_is_not_scaled()
    {
        Assert.Equal(1, DockLayout.Fit(800, 1920));
        Assert.Equal(1, DockLayout.Fit(800, 800));
    }

    [Fact]
    public void A_shelf_longer_than_its_edge_shrinks_to_it()
    {
        Assert.Equal(0.5, DockLayout.Fit(2000, 1000), 6);
        Assert.Equal(0.4, DockLayout.Fit(10000, 1000), 6); // never below 40%
        Assert.Equal(1, DockLayout.Fit(0, 1000));
        Assert.Equal(1, DockLayout.Fit(1000, 0));
    }

    [Fact]
    public void The_room_magnified_icons_take_is_the_sum_along_the_cosine()
    {
        // 72 under the pointer: +24 there, +18 one icon away each side, +6 two away, nothing three away.
        Assert.Equal(24 + 2 * 18 + 2 * 6, DockLayout.Growth(72), 6);
        Assert.Equal(0, DockLayout.Growth(DockLayout.Icon), 6);
    }

    [Fact]
    public void A_magnified_icon_reaches_past_the_shelf()
    {
        Assert.Equal(0, DockLayout.Overhang(DockLayout.Icon));
        Assert.Equal(17, DockLayout.Overhang(72));
        Assert.Equal(41, DockLayout.Overhang(MacDesktopSettings.MaxMagnified));
    }
}

/// <summary>
/// The apps are one row: pinned or only running, any of them can be dropped anywhere in it. With pins kept first, a pin
/// couldn't go right of the last pin and a running app couldn't be moved at all, so drags to the right did nothing.
/// </summary>
public sealed class DockRowTests
{
    private static ListedWindow Window(string name, nint handle) => new(handle, 1, name, name, $@"C:\apps\{name}.exe", false, 0);

    private static DockPin Pin(string name) => new($@"C:\apps\{name}.exe", $@"C:\apps\{name}.exe", name);

    private static readonly ListedWindow[] Running = [Window("x", 1), Window("y", 2)];

    private static string[] Names(IEnumerable<DockApp> apps) => apps.Select(a => a.Name).ToArray();

    /// <summary>The row as the dock builds it: the saved pins and the open windows, in the order kept so far.</summary>
    private static IReadOnlyList<DockApp> Row(IEnumerable<string> pins, List<string> order, params ListedWindow[] windows) =>
        DockApps.Arrange(DockApps.Compose(pins.Select(Pin).ToArray(), windows), order);

    /// <summary>Drops an app at a place in the row, then builds the row again from what was saved.</summary>
    private static IReadOnlyList<DockApp> Drop(IReadOnlyList<DockApp> row, string name, int index, List<string> order)
    {
        var (keys, pins) = DockApps.Place(row, row.Single(a => a.Name == name), index);
        order.Clear();
        order.AddRange(keys);
        return DockApps.Arrange(DockApps.Compose(pins.Select(p => Pin(Path.GetFileNameWithoutExtension(p))).ToArray(), Running), order);
    }

    [Fact]
    public void A_fresh_start_shows_the_pins_then_the_running_apps()
    {
        Assert.Equal(["a", "b", "x", "y"], Names(Row(["a", "b"], [], Running)));
    }

    [Fact]
    public void A_pin_dragged_right_of_a_running_app_stays_there()
    {
        var order = new List<string>();
        var row = Drop(Row(["a", "b"], order, Running), "a", 2, order);
        Assert.Equal(["b", "x", "a", "y"], Names(row));
        Assert.Equal([true, false, true, false], row.Select(a => a.Pinned));
        // ...to the very end too.
        Assert.Equal(["b", "x", "y", "a"], Names(Drop(row, "a", 3, order)));
    }

    [Fact]
    public void A_running_app_can_be_moved_and_is_kept_from_then_on()
    {
        var order = new List<string>();
        var row = Drop(Row(["a", "b"], order, Running), "x", 3, order); // right, past another running app
        Assert.Equal(["a", "b", "y", "x"], Names(row));
        Assert.Equal([true, true, false, true], row.Select(a => a.Pinned));

        row = Drop(row, "y", 0, order); // left, to the front
        Assert.Equal(["y", "a", "b", "x"], Names(row));
        Assert.All(row, app => Assert.True(app.Pinned));
    }

    [Fact]
    public void Apps_keep_their_places_as_others_open_and_close()
    {
        var order = new List<string>();
        var row = Drop(Row(["a", "b"], order, Running), "a", 2, order);
        Assert.Equal(["b", "x", "a", "y"], Names(row));
        // A new app opens at the end; one that closes leaves the rest where they are.
        Assert.Equal(["b", "x", "a", "y", "z"], Names(Row(["b", "a"], order, Window("x", 1), Window("y", 2), Window("z", 3))));
        Assert.Equal(["b", "a", "y", "z"], Names(Row(["b", "a"], order, Window("y", 2), Window("z", 3))));
    }

    [Fact]
    public void The_places_pins_hold_follow_the_saved_order()
    {
        var order = new List<string>();
        Assert.Equal(["a", "b", "x", "y"], Names(Row(["a", "b"], order, Running)));
        // The pins are put the other way round from elsewhere: they swap, in the places pins hold.
        Assert.Equal(["b", "a", "x", "y"], Names(Row(["b", "a"], order, Running)));
        // A new pin goes after the pin before it.
        Assert.Equal(["b", "c", "a", "x", "y"], Names(Row(["b", "c", "a"], order, Running)));
    }
}

/// <summary>
/// Whose the keyboard is while a dock menu or question is open. A menu reports that it closed only after it has faded
/// out, by when the question its item opened holds the keyboard: the menu letting go then closed the question.
/// </summary>
public sealed class FocusLeaseTests
{
    [Fact]
    public void A_menu_closing_late_does_not_let_go_of_the_question_it_opened()
    {
        var lease = new FocusLease();
        object menu = new(), question = new();
        lease.Take(menu);
        lease.Take(question); // the item ran while the menu was still fading out

        Assert.False(lease.Leave(menu));
        Assert.True(lease.Held);
        Assert.True(lease.Leave(question));
        Assert.False(lease.Held);
    }

    [Fact]
    public void Its_holder_lets_go_once()
    {
        var lease = new FocusLease();
        object menu = new();
        lease.Take(menu);

        Assert.True(lease.Leave(menu));
        Assert.False(lease.Leave(menu));
    }
}

/// <summary>
/// The arithmetic on icon pixels. Windows hands app icons over with straight alpha; drawn as premultiplied their soft
/// edges come out brighter than the icon, which is what made the dock's icons jagged.
/// </summary>
public sealed class IconPixelTests
{
    [Fact]
    public void A_color_above_its_alpha_is_straight()
    {
        Assert.True(IconPixels.IsStraight([200, 100, 50, 128]));
        Assert.True(IconPixels.IsStraight([0, 0, 255, 0])); // color left in a clear pixel
        Assert.False(IconPixels.IsStraight([100, 50, 25, 128]));
        Assert.False(IconPixels.IsStraight([255, 255, 255, 255, 0, 0, 0, 0]));
    }

    [Fact]
    public void Premultiplying_scales_each_color_by_its_alpha()
    {
        byte[] pixels = [200, 100, 50, 128, 10, 20, 30, 255, 90, 90, 90, 0];
        IconPixels.Premultiply(pixels);
        Assert.Equal([100, 50, 25, 128, 10, 20, 30, 255, 0, 0, 0, 0], pixels);
        Assert.False(IconPixels.IsStraight(pixels));
    }

    [Fact]
    public void The_art_box_holds_every_pixel_that_is_not_clear()
    {
        var pixels = new byte[8 * 6 * 4];
        Set(pixels, 8, x: 2, y: 1);
        Set(pixels, 8, x: 5, y: 4);
        var box = IconPixels.ArtBox(pixels, 8, 6);
        Assert.Equal((2, 1, 4, 4), (box.X, box.Y, box.Width, box.Height));
        Assert.True(IconPixels.ArtBox(new byte[4 * 4 * 4], 4, 4).IsEmpty);
    }

    [Fact]
    public void Fitting_trims_the_clear_margins_and_fills_the_size()
    {
        // 6 x 3 of opaque art in the middle of a clear 16 x 16.
        var pixels = new byte[16 * 16 * 4];
        for (int y = 6; y < 9; y++)
            for (int x = 5; x < 11; x++)
                Set(pixels, 16, x, y);
        var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Pbgra32, null, pixels, 16 * 4);

        var fitted = IconPixels.Fit(source, 12)!;

        Assert.Equal((12, 12), (fitted.PixelWidth, fitted.PixelHeight));
        Assert.True(fitted.IsFrozen);
        var result = new byte[12 * 12 * 4];
        fitted.CopyPixels(result, 12 * 4, 0);
        var box = IconPixels.ArtBox(result, 12, 12);
        // The art's long side now spans the picture; its short side stays in the middle (the scaler softens it by a row).
        Assert.Equal((0, 12), (box.X, box.Width));
        Assert.InRange(box.Height, 5, 8);
        Assert.InRange(box.Y, 1, 4);
        Assert.InRange(box.Y + box.Height, 8, 11);
    }

    [Fact]
    public void A_clear_picture_fits_to_nothing() =>
        Assert.Null(IconPixels.Fit(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Pbgra32, null, new byte[64], 16), 8));

    private static void Set(byte[] pixels, int width, int x, int y)
    {
        int at = (y * width + x) * 4;
        pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 255;
    }
}

/// <summary>
/// With CHIPPI_ISLAND_SHOTS naming a folder, saves the dock's icons as it draws them at rest (48 pixels, shown 6 times
/// over the shelf's color): Windows' own icons, read through the dock's path. Shows nothing.
/// </summary>
public sealed class DockIconShots
{
    [Fact]
    public async Task Icons_at_rest()
    {
        string? output = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var items = new List<string>
        {
            Path.Combine(windows, "explorer.exe"),
            "::{645FF040-5081-101B-9F08-00AA002F954E}", // Recycle Bin
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Path.Combine(windows, "System32", "notepad.exe"),
        };
        // Extra items to draw (CHIPPI_DOCK_SHOT_ITEMS, separated by |): shortcuts, programs or shell names.
        if (Environment.GetEnvironmentVariable("CHIPPI_DOCK_SHOT_ITEMS") is { Length: > 0 } extra)
            items.InsertRange(0, extra.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        const int size = 48, zoom = 6, gap = 6, pad = 8;
        int width = pad * 2 + items.Count * size + (items.Count - 1) * gap, height = size + pad * 2;
        var shelf = new byte[width * height * 4];
        for (int i = 0; i < shelf.Length; i += 4)
            (shelf[i], shelf[i + 1], shelf[i + 2], shelf[i + 3]) = (0x2E, 0x28, 0x28, 0xFF);
        for (int n = 0; n < items.Count; n++)
        {
            var icon = await AppIcon.LoadFittedAsync(items[n], size);
            if (icon is null)
                continue;
            var pixels = new byte[size * size * 4];
            icon.CopyPixels(pixels, size * 4, 0);
            int left = pad + n * (size + gap);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int from = (y * size + x) * 4, to = ((pad + y) * width + left + x) * 4;
                    int keep = 255 - pixels[from + 3];
                    for (int c = 0; c < 3; c++) // premultiplied over the shelf
                        shelf[to + c] = (byte)Math.Min(255, pixels[from + c] + shelf[to + c] * keep / 255);
                }
        }
        var big = new byte[width * zoom * height * zoom * 4];
        for (int y = 0; y < height * zoom; y++)
            for (int x = 0; x < width * zoom; x++)
                Array.Copy(shelf, (y / zoom * width + x / zoom) * 4, big, (y * width * zoom + x) * 4, 4);
        var picture = BitmapSource.Create(width * zoom, height * zoom, 96, 96, PixelFormats.Bgra32, null, big, width * zoom * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var file = File.Create(Path.Combine(output, "dock-icons-rest.png"));
        encoder.Save(file);
    }
}
