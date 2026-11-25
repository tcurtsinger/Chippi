// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Actions;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>The dock's geometry (docs/mac-style/brief.md, Dock), in DIPs. Pure, so it's tested without windows.</summary>
public static class DockLayout
{
    public const double Icon = 48, Gap = 6, Shelf = 64, Padding = 8, Corner = 16, EdgeGap = 6;

    /// <summary>The divider's length along the shelf: its 1 line and 4 each side.</summary>
    public const double Divider = 9;

    /// <summary>The shelf's edge line. Its tiles sit inside it, in a slot this much shallower than the shelf on each side.</summary>
    public const double Edge = 1;

    /// <summary>A tile's depth across the shelf: the shelf inside its edge line.</summary>
    public const double Slot = Shelf - 2 * Edge;

    /// <summary>The magnification reaches this many icons each side of the pointer.</summary>
    public const double Reach = 3;

    /// <summary>The strip the dock keeps for itself along its edge: the shelf and the gap under it.</summary>
    public const double Reserved = Shelf + EdgeGap;

    /// <summary>
    /// An icon's size with the pointer <paramref name="distance"/> icons from its center: <paramref name="max"/> under the
    /// pointer, falling off along a cosine to 48 three icons away.
    /// </summary>
    public static double Magnified(double distance, double max, double rest = Icon, double reach = Reach)
    {
        distance = Math.Abs(distance);
        if (!double.IsFinite(distance) || distance >= reach || max <= rest)
            return rest;
        return rest + (max - rest) * (Math.Cos(Math.PI * distance / reach) + 1) / 2;
    }

    /// <summary>
    /// Each icon's size along the shelf with the pointer at <paramref name="pointer"/> (DIPs from the first icon's rest
    /// position's start), or every icon at rest for null. Distances are measured against where the icons sit at rest, so the
    /// row doesn't chase the pointer.
    /// </summary>
    public static double[] Sizes(int count, double? pointer, double max)
    {
        var sizes = new double[count];
        for (int i = 0; i < count; i++)
        {
            double center = i * (Icon + Gap) + Icon / 2;
            sizes[i] = pointer is { } p ? Magnified((p - center) / (Icon + Gap), max) : Icon;
        }
        return sizes;
    }

    /// <summary>
    /// Where each tile goes for one frame of magnification. Sizes are exact, so an icon grows and moves by fractions of a
    /// pixel; a tile's slot in the row is whole pixels, as layout rounds it. Rounding each slot on its own made every icon's
    /// size and place step a pixel at a different moment, which read as a stutter under a slow pointer.
    /// </summary>
    /// <remarks>
    /// The row grows about its middle, an even number of pixels at a time, a pixel to each side. Each icon is drawn at its
    /// exact size and place inside its slot. Once nothing has moved for a moment (<paramref name="still"/>), the icons at
    /// rest on both sides are put on whole pixels, where they're sharp; the grown ones between them stay exact.
    /// </remarks>
    /// <param name="sizes">Each tile's size along the shelf now, in DIPs.</param>
    /// <param name="rests">Each tile's size at rest, in DIPs: 48 for an icon, 9 for the divider.</param>
    /// <param name="pixels">Device pixels per DIP.</param>
    /// <param name="still">Whether the icons have stopped moving: the ones at rest then sit on whole pixels.</param>
    /// <param name="grown">How much longer the row is than at rest, in DIPs.</param>
    public static DockSlot[] Slots(IReadOnlyList<double> sizes, IReadOnlyList<double> rests, double pixels, bool still, out double grown)
    {
        if (!(pixels > 0) || !double.IsFinite(pixels))
            pixels = 1;
        int count = sizes.Count;
        var slots = new DockSlot[count];
        // Growth before each tile, in pixels: before[i] is what the tiles ahead of it have grown by.
        var before = new double[count + 1];
        for (int i = 0; i < count; i++)
        {
            double more = (sizes[i] - rests[i]) * pixels;
            before[i + 1] = before[i] + (double.IsFinite(more) && more > 0 ? more : 0);
        }
        double total = before[count];
        for (int i = 0; i < count; i++)
        {
            double rest = Math.Max(1, Math.Round(rests[i] * pixels)); // as layout rounds a size
            double more = before[i + 1] - before[i];
            // The slot's edges, from the row's middle: whole pixels either side of it.
            double start = before[i] - total / 2, edge = Whole(start);
            double extent = rest + Whole(before[i + 1] - total / 2) - edge;
            // Still, a tile at rest with nothing grown on one side of it sits on its pixel; one among the grown ones is exact.
            double among = still ? Math.Clamp(Math.Min(before[i + 1], total - before[i]), 0, 1) : 1;
            double shift = among * (start - edge) + more / 2;
            slots[i] = new DockSlot(extent / pixels, shift / pixels, (rest + more) / rest);
        }
        grown = 2 * Whole(total / 2) / pixels;
        return slots;
    }

    /// <summary>
    /// The room the shelf keeps each side of itself to grow into, in DIPs: half of what its icons grow by at
    /// <paramref name="max"/>, in whole pixels. The shelf's margins are this less half of what it has grown, so its length
    /// with them stays the same and its middle stays put.
    /// </summary>
    public static double Room(double max, double pixels)
    {
        if (!(pixels > 0) || !double.IsFinite(pixels))
            pixels = 1;
        return (Math.Ceiling(Growth(max) * pixels / 2) + 1) / pixels;
    }

    /// <summary>
    /// The nearest whole number, the same for a value and its negative, with a half rounded toward zero from just short of
    /// it: sizes that sum to exactly a half land a hair either side of it, and must not flip the row a pixel.
    /// </summary>
    private static double Whole(double value) => Math.Sign(value) * Math.Floor(Math.Abs(value) + 0.5 - 1e-6);

    /// <summary>The shelf's length for icons of these sizes, with <paramref name="dividers"/> dividers of 9 (1 and 4 each side).</summary>
    public static double Length(IReadOnlyList<double> sizes, int dividers) =>
        2 * Padding + sizes.Sum() + Gap * Math.Max(0, sizes.Count - 1) + dividers * (9 + Gap);

    /// <summary>How far a magnified icon reaches past the shelf's outer edge line.</summary>
    public static double Overhang(double size) => Math.Max(0, size - (Slot - (Padding - Edge)));

    /// <summary>
    /// The room the icons take beyond their resting length while the pointer is among them: the icon under it and three
    /// each side, along the cosine.
    /// </summary>
    public static double Growth(double max)
    {
        double growth = 0;
        for (int i = -(int)Reach; i <= (int)Reach; i++)
            growth += Magnified(i, max) - Icon;
        return growth;
    }

    /// <summary>
    /// The scale that fits a shelf <paramref name="length"/> long into <paramref name="available"/>: 1 while it fits, then
    /// smaller, so a dock with more apps than the display is wide shrinks instead of running off both ends.
    /// </summary>
    public static double Fit(double length, double available) =>
        !(length > 0) || !(available > 0) || length <= available ? 1 : Math.Max(0.4, available / length);
}

/// <summary>
/// A tile's place for one frame (DockLayout.Slots), in DIPs: its slot's length along the shelf, in whole pixels; how far
/// its icon's middle is drawn from where the slot puts it at rest; and the icon's size over its size at rest.
/// </summary>
public readonly record struct DockSlot(double Extent, double Shift, double Scale);

/// <summary>What a click on an app's icon does.</summary>
public enum DockClick { Launch, Minimize, Show }

/// <summary>One app in the dock: pinned, running, or both. Its windows are in z-order, the front one first.</summary>
public sealed record DockApp(string Key, string Launch, string Path, string Name, bool Pinned, IReadOnlyList<ListedWindow> Windows)
{
    public bool Running => Windows.Count > 0;
}

/// <summary>
/// A pin as the dock matches windows to it: what launches it, the program it runs (null when a shortcut has no file target,
/// as a Store app's or a web app's doesn't), its name and its AppUserModelID (null when it sets none).
/// </summary>
public sealed record DockPin(string Launch, string? Program, string Name, string? AppId = null);

/// <summary>Builds the dock's apps from its pins and the windows open now.</summary>
public static class DockApps
{
    /// <summary>
    /// The pins in their order, each with its windows, then every other app with a window, in the order it first appears in
    /// <paramref name="windows"/> (front to back). A window goes to the pin with its AppUserModelID first (<paramref
    /// name="appIdOf"/>; Store apps, web apps and apps that set one), else to a pin that runs its program, unless both have
    /// an ID and they differ (an Edge web app's windows aren't Edge's). Windows no pin claims are grouped by ID, else program.
    /// </summary>
    public static IReadOnlyList<DockApp> Compose(IReadOnlyList<DockPin> pins, IReadOnlyList<ListedWindow> windows, Func<ListedWindow, string?>? appIdOf = null)
    {
        var ids = windows.Select(w => appIdOf?.Invoke(w) is { Length: > 0 } id ? id : null).ToArray();
        var owner = new int[windows.Count];
        Array.Fill(owner, -1);
        for (int i = 0; i < windows.Count; i++)
        {
            if (ids[i] is not { } id)
                continue;
            owner[i] = IndexOf(pins, p => string.Equals(p.AppId, id, StringComparison.OrdinalIgnoreCase));
        }
        for (int i = 0; i < windows.Count; i++)
        {
            if (owner[i] >= 0 || windows[i].Executable.Length == 0)
                continue;
            owner[i] = IndexOf(pins, p => p.Program is not null && SameProgram(p.Program, windows[i].Executable)
                && (p.AppId is null || ids[i] is null));
        }

        var apps = new List<DockApp>();
        for (int p = 0; p < pins.Count; p++)
        {
            var pin = pins[p];
            var own = windows.Where((_, i) => owner[i] == p).ToArray();
            apps.Add(new DockApp(Key(pin.Launch), pin.Launch, pin.Program ?? pin.Launch, pin.Name, true, own));
        }
        var groups = new List<(string Key, List<ListedWindow> Windows, string? Id)>();
        for (int i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            if (owner[i] >= 0 || window.Executable.Length == 0)
                continue;
            string key = ids[i] is { } id ? "AUMID:" + id.ToUpperInvariant() : Key(window.Executable);
            int at = groups.FindIndex(g => g.Key == key);
            if (at < 0)
                groups.Add((key, [window], ids[i]));
            else
                groups[at].Windows.Add(window);
        }
        foreach (var (key, own, id) in groups)
        {
            var front = own[0];
            // A Store app opens again through its ID; a desktop app through its program.
            string launch = id is not null && IsPackaged(front.Executable) ? @"shell:AppsFolder\" + id : front.Executable;
            apps.Add(new DockApp(key, launch, front.Executable, front.App, false, own));
        }
        return apps;
    }

    /// <summary>
    /// What a click does, with <paramref name="front"/> the window in front: an app that isn't running opens; its window in
    /// front minimizes, as on the taskbar; otherwise its front window is shown, minimized or not.
    /// </summary>
    public static (DockClick Action, nint Window) OnClick(DockApp app, nint front)
    {
        if (!app.Running)
            return (DockClick.Launch, 0);
        foreach (var window in app.Windows)
            if (window.Handle == front)
                return (window.Minimized ? DockClick.Show : DockClick.Minimize, front); // minimized, it can still hold the keyboard
        return (DockClick.Show, app.Windows[0].Handle);
    }

    /// <summary>
    /// Whether a window's program is the one a pin runs: the same file, or a Squirrel app's versioned copy of the launcher
    /// the pin targets (Discord's and Claude's shortcuts run <c>App\x.exe</c>, their windows <c>App\app-1.2.3\x.exe</c>).
    /// </summary>
    public static bool SameProgram(string pinned, string running)
    {
        if (string.Equals(pinned, running, StringComparison.OrdinalIgnoreCase))
            return true;
        string? folder = Path.GetDirectoryName(running);
        return folder is not null
            && Path.GetFileName(folder).StartsWith("app-", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(pinned), Path.GetFileName(running), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(folder), Path.GetDirectoryName(pinned), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The apps in the order the dock shows them: one row, kept in <paramref name="order"/> (their keys, updated) for as
    /// long as the dock runs. An app keeps the place it has, so bringing one to the front moves nothing. A running app
    /// that's new goes at the end, in the order it opened; a new pin goes after the pin before it. The places pins hold
    /// are filled in the pins' own order, which is the order that's saved. At a fresh start that's the pins, then the
    /// running apps.
    /// </summary>
    public static IReadOnlyList<DockApp> Arrange(IReadOnlyList<DockApp> apps, List<string> order)
    {
        var byKey = new Dictionary<string, DockApp>();
        foreach (var app in apps)
            byKey.TryAdd(app.Key, app);
        order.RemoveAll(key => !byKey.ContainsKey(key));
        string? before = null; // the pin before this one, in the pins' own order
        foreach (var app in apps)
        {
            if (!order.Contains(app.Key))
            {
                if (app.Pinned)
                    order.Insert(before is null ? 0 : order.IndexOf(before) + 1, app.Key);
                else
                    order.Add(app.Key);
            }
            if (app.Pinned)
                before = app.Key;
        }
        var pins = apps.Where(a => a.Pinned).Select(a => a.Key).Distinct().ToArray();
        var pinned = pins.ToHashSet();
        for (int i = 0, next = 0; i < order.Count && next < pins.Length; i++)
            if (pinned.Contains(order[i]))
                order[i] = pins[next++];
        return order.Select(key => byKey[key]).ToArray();
    }

    /// <summary>
    /// The row after <paramref name="moved"/> is dropped at <paramref name="index"/> among the apps: the keys in their new
    /// order (for <see cref="Arrange"/>) and the pins to save. Any app can go anywhere in the row. One that wasn't pinned
    /// is kept from then on, as on a Mac; the others stay as they were, so a pin can sit to the right of a running app.
    /// </summary>
    public static (IReadOnlyList<string> Order, IReadOnlyList<string> Pins) Place(IReadOnlyList<DockApp> apps, DockApp moved, int index)
    {
        var row = apps.Where(a => a.Key != moved.Key).ToList();
        row.Insert(Math.Clamp(index, 0, row.Count), moved);
        // Pinned, it's known by what launches it.
        return (row.Select(a => a.Key == moved.Key ? Key(moved.Launch) : a.Key).ToArray(),
            row.Where(a => a.Pinned || a.Key == moved.Key).Select(a => a.Launch).ToArray());
    }

    private static int IndexOf(IReadOnlyList<DockPin> pins, Func<DockPin, bool> match)
    {
        for (int i = 0; i < pins.Count; i++)
            if (match(pins[i]))
                return i;
        return -1;
    }

    /// <summary>A Store (packaged) app's program, or the frame Windows hosts them in.</summary>
    public static bool IsPackaged(string executable) =>
        executable.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        || executable.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
        || executable.Contains(@"\SystemApps\", StringComparison.OrdinalIgnoreCase);

    public static string Key(string path) => path.ToUpperInvariant();

    /// <summary>The pins with <paramref name="moved"/> placed at <paramref name="index"/> among them.</summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<string> pins, string moved, int index)
    {
        var list = pins.Where(p => !string.Equals(p, moved, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), moved);
        return list;
    }

    /// <summary>
    /// Where among the pins a tile dropped at <paramref name="at"/> goes, given each pin's center along the shelf in order.
    /// <paramref name="dragged"/> is the dragged pin's own index among them, or -1 for an app that isn't pinned: it isn't
    /// counted, so the index is in the list without it, as <see cref="Move"/> takes it.
    /// </summary>
    public static int InsertIndex(IReadOnlyList<double> centers, int dragged, double at)
    {
        int index = 0;
        for (int i = 0; i < centers.Count; i++)
            if (i != dragged && at > centers[i])
                index++;
        return index;
    }

    /// <summary>
    /// The place among <paramref name="count"/> pins that a dragged pin's middle is over: places are
    /// <paramref name="pitch"/> apart from the first one's middle at <paramref name="first"/>. The index is the pin's own
    /// place once it lands, as <see cref="Move"/> takes it; past either end it's the end.
    /// </summary>
    public static int SlotAt(double center, double first, double pitch, int count) =>
        count <= 0 || !(pitch > 0) ? 0 : Math.Clamp((int)Math.Round((center - first) / pitch, MidpointRounding.AwayFromZero), 0, count - 1);

    /// <summary>A name for <paramref name="name"/> that's free in <paramref name="exists"/>: "name (2).ext", as Explorer does.</summary>
    public static string FreeName(string name, Func<string, bool> exists)
    {
        if (!exists(name))
            return name;
        string stem = System.IO.Path.GetFileNameWithoutExtension(name), extension = System.IO.Path.GetExtension(name);
        for (int n = 2; ; n++)
        {
            string candidate = $"{stem} ({n}){extension}";
            if (!exists(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// The taskbar's pinned shortcuts in the taskbar's own order: as their names appear in its Taskband Favorites value
    /// (UTF-16), then any it doesn't name, File Explorer first and the rest by name.
    /// </summary>
    public static IReadOnlyList<string> TaskbarOrder(IReadOnlyList<string> links, byte[]? favorites)
    {
        string text = favorites is { Length: > 1 } ? System.Text.Encoding.Unicode.GetString(favorites) : "";
        string shifted = favorites is { Length: > 2 } ? System.Text.Encoding.Unicode.GetString(favorites, 1, favorites.Length - 1) : "";
        int Place(string link)
        {
            string file = System.IO.Path.GetFileName(link);
            int a = text.IndexOf(file, StringComparison.OrdinalIgnoreCase), b = shifted.IndexOf(file, StringComparison.OrdinalIgnoreCase);
            return a >= 0 && (b < 0 || a <= b) ? a : b >= 0 ? b : int.MaxValue;
        }
        return links.OrderBy(Place)
            .ThenBy(p => System.IO.Path.GetFileNameWithoutExtension(p).Contains("Explorer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => System.IO.Path.GetFileNameWithoutExtension(p), StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>A file's age as the stack shows it: "Now" under a minute, then minutes, hours, days, weeks.</summary>
    public static string Age(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "Now",
        < 60 => $"{(int)age.TotalMinutes}m",
        < 60 * 24 => $"{(int)age.TotalHours}h",
        < 60 * 24 * 7 => $"{(int)age.TotalDays}d",
        _ => $"{(int)(age.TotalDays / 7)}w",
    };

    /// <summary>The newest files in a folder, newest first; folders and hidden files left out.</summary>
    public static IReadOnlyList<FileInfo> Newest(string folder, int count)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !f.Name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(count).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
