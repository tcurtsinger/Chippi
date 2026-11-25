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

/// <summary>Builds the dock's apps from its pins and the windows open now.</summary>
public static class DockApps
{
    /// <summary>
    /// The pins in their order, each with its windows, then every other app with a window, in the order it first appears in
    /// <paramref name="windows"/> (front to back). <paramref name="pins"/> are (what launches it, the program it runs).
    /// </summary>
    public static IReadOnlyList<DockApp> Compose(IReadOnlyList<(string Launch, string? Program, string Name)> pins, IReadOnlyList<ListedWindow> windows)
    {
        var byProgram = windows.Where(w => w.Executable.Length > 0)
            .GroupBy(w => w.Executable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ListedWindow>)g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var apps = new List<DockApp>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (launch, program, name) in pins)
        {
            var own = program is not null && byProgram.TryGetValue(program, out var found) ? found : [];
            if (program is not null)
                claimed.Add(program);
            apps.Add(new DockApp(Key(launch), launch, program ?? launch, name, true, own));
        }
        foreach (var window in windows)
        {
            if (window.Executable.Length == 0 || !claimed.Add(window.Executable))
                continue;
            apps.Add(new DockApp(Key(window.Executable), window.Executable, window.Executable, window.App, false, byProgram[window.Executable]));
        }
        return apps;
    }

    public static string Key(string path) => path.ToUpperInvariant();

    /// <summary>The pins with <paramref name="moved"/> placed at <paramref name="index"/> among them.</summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<string> pins, string moved, int index)
    {
        var list = pins.Where(p => !string.Equals(p, moved, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), moved);
        return list;
    }

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
