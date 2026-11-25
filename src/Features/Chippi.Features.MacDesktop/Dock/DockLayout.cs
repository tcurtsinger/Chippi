// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Actions;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>The dock's geometry (docs/mac-style/brief.md, Dock), in DIPs. Pure, so it's tested without windows.</summary>
public static class DockLayout
{
    public const double Icon = 48, Gap = 6, Shelf = 64, Padding = 8, Corner = 16, EdgeGap = 6;

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

    /// <summary>The shelf's length for icons of these sizes, with <paramref name="dividers"/> dividers of 9 (1 and 4 each side).</summary>
    public static double Length(IReadOnlyList<double> sizes, int dividers) =>
        2 * Padding + sizes.Sum() + Gap * Math.Max(0, sizes.Count - 1) + dividers * (9 + Gap);
}

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
            owner[i] = IndexOf(pins, p => p.Program is not null && string.Equals(p.Program, windows[i].Executable, StringComparison.OrdinalIgnoreCase)
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
