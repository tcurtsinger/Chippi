// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Frozen;
using System.Reflection;
using System.Windows.Media;

namespace Chippi.UI.Controls;

/// <summary>
/// Lucide-style stroke icons (Lucide is ISC-licensed) as frozen geometries on a 24x24 grid.
/// XAML uses them through <c>{x:Static c:LucideIcons.Name}</c>; module descriptors name them by
/// key, resolved with <see cref="Get"/>. Ported from Hopper and extended for Chippi.
/// </summary>
public static class LucideIcons
{
    private static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    // Devices (from Hopper).
    public static Geometry Monitor { get; } = G("M4,3 H20 A2,2 0 0 1 22,5 V15 A2,2 0 0 1 20,17 H4 A2,2 0 0 1 2,15 V5 A2,2 0 0 1 4,3 Z M8,21 H16 M12,17 V21");
    public static Geometry Headphones { get; } = G("M3,14 h3 a2,2 0 0 1 2,2 v3 a2,2 0 0 1 -2,2 H5 a2,2 0 0 1 -2,-2 v-5 a9,9 0 0 1 18,0 v5 a2,2 0 0 1 -2,2 h-1 a2,2 0 0 1 -2,-2 v-3 a2,2 0 0 1 2,-2 h3");
    public static Geometry Earbuds { get; } = G("M5,8 a3,3 0 1 0 6,0 a3,3 0 1 0 -6,0 Z M8,11 v5 a1.5,1.5 0 0 0 1.5,1.5 M13,8 a3,3 0 1 0 6,0 a3,3 0 1 0 -6,0 Z M16,11 v5 a1.5,1.5 0 0 1 -1.5,1.5");
    public static Geometry Mic { get; } = G("M12,2 a3,3 0 0 0 -3,3 v7 a3,3 0 0 0 6,0 V5 a3,3 0 0 0 -3,-3 Z M19,10 v2 a7,7 0 0 1 -14,0 v-2 M12,19 v3 M8,22 h8");
    public static Geometry Video { get; } = G("M22,8 L16,12 L22,16 Z M4,6 H14 A2,2 0 0 1 16,8 V16 A2,2 0 0 1 14,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z");
    public static Geometry Speaker { get; } = G("M11,5 L6,9 L2,9 L2,15 L6,15 L11,19 Z M15.5,8.5 a5,5 0 0 1 0,7 M19,5 a10,10 0 0 1 0,14");

    // Shell and controls (from Hopper).
    public static Geometry Sliders { get; } = G("M21,4 H14 M10,4 H3 M21,12 H12 M8,12 H3 M21,20 H16 M12,20 H3 M14,2 V6 M8,10 V14 M16,18 V22");
    public static Geometry Settings { get; } = G("M12.22,2 h-.44 a2,2 0 0 0 -2,2 v.18 a2,2 0 0 1 -1,1.73 l-.43.25 a2,2 0 0 1 -2,0 l-.15-.08 a2,2 0 0 0 -2.73.73 l-.22.38 a2,2 0 0 0 .73,2.73 l.15.1 a2,2 0 0 1 1,1.72 v.51 a2,2 0 0 1 -1,1.74 l-.15.09 a2,2 0 0 0 -.73,2.73 l.22.38 a2,2 0 0 0 2.73.73 l.15-.08 a2,2 0 0 1 2,0 l.43.25 a2,2 0 0 1 1,1.73 V20 a2,2 0 0 0 2,2 h.44 a2,2 0 0 0 2,-2 v-.18 a2,2 0 0 1 1,-1.73 l.43-.25 a2,2 0 0 1 2,0 l.15.08 a2,2 0 0 0 2.73-.73 l.22-.39 a2,2 0 0 0 -.73,-2.73 l-.15-.08 a2,2 0 0 1 -1,-1.74 v-.5 a2,2 0 0 1 1,-1.74 l.15-.09 a2,2 0 0 0 .73,-2.73 l-.22-.38 a2,2 0 0 0 -2.73,-.73 l-.15.08 a2,2 0 0 1 -2,0 l-.43-.25 a2,2 0 0 1 -1,-1.73 V4 a2,2 0 0 0 -2,-2 Z M12,9 a3,3 0 1 0 0,6 a3,3 0 1 0 0,-6 Z");
    public static Geometry ChevronLeft { get; } = G("M15,18 L9,12 L15,6");
    public static Geometry ChevronRight { get; } = G("M9,18 L15,12 L9,6");
    public static Geometry ChevronDown { get; } = G("M6,9 L12,15 L18,9");
    public static Geometry Up { get; } = G("M18,15 L12,9 L6,15");
    public static Geometry Down { get; } = G("M6,9 L12,15 L18,9");
    public static Geometry Trash { get; } = G("M3,6 H21 M19,6 V20 a2,2 0 0 1 -2,2 H7 a2,2 0 0 1 -2,-2 V6 M8,6 V4 a2,2 0 0 1 2,-2 h4 a2,2 0 0 1 2,2 v2");
    public static Geometry Plus { get; } = G("M12,5 V19 M5,12 H19");
    public static Geometry Power { get; } = G("M12,2 v10 M18.4,6.6 a9,9 0 1 1 -12.77,0.04");
    public static Geometry Moon { get; } = G("M12,3 a6,6 0 0 0 9,9 a9,9 0 1 1 -9,-9 Z");
    public static Geometry Sun { get; } = G("M16,12 a4,4 0 1 0 -8,0 a4,4 0 1 0 8,0 Z M12,2 v2 M12,20 v2 M4.93,4.93 l1.41,1.41 M17.66,17.66 l1.41,1.41 M2,12 h2 M20,12 h2 M6.34,17.66 l-1.41,1.41 M19.07,4.93 l-1.41,1.41");
    public static Geometry Minus { get; } = G("M5,12 H19");
    public static Geometry Square { get; } = G("M6,4 H18 a2,2 0 0 1 2,2 V18 a2,2 0 0 1 -2,2 H6 a2,2 0 0 1 -2,-2 V6 a2,2 0 0 1 2,-2 Z");
    public static Geometry Expand { get; } = G("M7,9 L12,4 L17,9 M7,15 L12,20 L17,15");
    public static Geometry Close { get; } = G("M18,6 L6,18 M6,6 L18,18");
    public static Geometry Info { get; } = G("M12,2 a10,10 0 1 0 0,20 a10,10 0 1 0 0,-20 Z M12,16 V12 M12,8 h0.01");

    // Added for Chippi.
    public static Geometry Search { get; } = G("M11,3 a8,8 0 1 0 0,16 a8,8 0 1 0 0,-16 Z M21,21 L16.65,16.65");
    public static Geometry Check { get; } = G("M20,6 L9,17 L4,12");
    public static Geometry Volume { get; } = G("M11,5 L6,9 H2 V15 H6 L11,19 Z M15.54,8.46 a5,5 0 0 1 0,7.07 M19.07,4.93 a10,10 0 0 1 0,14.14");
    public static Geometry VolumeMuted { get; } = G("M11,5 L6,9 H2 V15 H6 L11,19 Z M22,9 L16,15 M16,9 L22,15");
    public static Geometry Undo { get; } = G("M9,14 L4,9 L9,4 M4,9 H14.5 a5.5,5.5 0 0 1 5.5,5.5 a5.5,5.5 0 0 1 -5.5,5.5 H11");
    public static Geometry Coffee { get; } = G("M10,2 v2 M14,2 v2 M6,2 v2 M16,8 a1,1 0 0 1 1,1 v8 a4,4 0 0 1 -4,4 H7 a4,4 0 0 1 -4,-4 V9 a1,1 0 0 1 1,-1 h14 a4,4 0 1 1 0,8 h-1");
    public static Geometry Zap { get; } = G("M13,2 L3,14 H12 L11,22 L21,10 H12 Z");
    public static Geometry Cpu { get; } = G("M6,4 H18 a2,2 0 0 1 2,2 V18 a2,2 0 0 1 -2,2 H6 a2,2 0 0 1 -2,-2 V6 a2,2 0 0 1 2,-2 Z M9,9 H15 V15 H9 Z M15,2 v2 M15,20 v2 M2,15 h2 M2,9 h2 M20,15 h2 M20,9 h2 M9,2 v2 M9,20 v2");
    public static Geometry Activity { get; } = G("M22,12 H18 L15,21 L9,3 L6,12 H2");
    public static Geometry Folder { get; } = G("M20,20 a2,2 0 0 0 2,-2 V8 a2,2 0 0 0 -2,-2 H12.1 a2,2 0 0 1 -1.69,-0.9 L9.6,3.9 A2,2 0 0 0 7.93,3 H4 a2,2 0 0 0 -2,2 V18 a2,2 0 0 0 2,2 Z");
    public static Geometry Download { get; } = G("M21,15 v4 a2,2 0 0 1 -2,2 H5 a2,2 0 0 1 -2,-2 v-4 M7,10 L12,15 L17,10 M12,15 V3");
    public static Geometry Clipboard { get; } = G("M9,2 H15 a1,1 0 0 1 1,1 V5 a1,1 0 0 1 -1,1 H9 a1,1 0 0 1 -1,-1 V3 a1,1 0 0 1 1,-1 Z M16,4 h2 a2,2 0 0 1 2,2 v14 a2,2 0 0 1 -2,2 H6 a2,2 0 0 1 -2,-2 V6 a2,2 0 0 1 2,-2 h2");
    public static Geometry Camera { get; } = G("M14.5,4 h-5 L7,7 H4 a2,2 0 0 0 -2,2 v9 a2,2 0 0 0 2,2 h16 a2,2 0 0 0 2,-2 V9 a2,2 0 0 0 -2,-2 h-3 Z M12,10 a3,3 0 1 0 0,6 a3,3 0 1 0 0,-6 Z");
    public static Geometry AppWindow { get; } = G("M4,4 H20 a2,2 0 0 1 2,2 V18 a2,2 0 0 1 -2,2 H4 a2,2 0 0 1 -2,-2 V6 a2,2 0 0 1 2,-2 Z M2,8 H22 M6,4 V8 M10,4 V8");
    public static Geometry Ellipsis { get; } = G("M5,11 a1,1 0 1 0 0,2 a1,1 0 1 0 0,-2 Z M12,11 a1,1 0 1 0 0,2 a1,1 0 1 0 0,-2 Z M19,11 a1,1 0 1 0 0,2 a1,1 0 1 0 0,-2 Z");
    public static Geometry LayoutGrid { get; } = G("M4,3 H9 a1,1 0 0 1 1,1 V9 a1,1 0 0 1 -1,1 H4 a1,1 0 0 1 -1,-1 V4 a1,1 0 0 1 1,-1 Z M15,3 H20 a1,1 0 0 1 1,1 V9 a1,1 0 0 1 -1,1 H15 a1,1 0 0 1 -1,-1 V4 a1,1 0 0 1 1,-1 Z M15,14 H20 a1,1 0 0 1 1,1 V20 a1,1 0 0 1 -1,1 H15 a1,1 0 0 1 -1,-1 V15 a1,1 0 0 1 1,-1 Z M4,14 H9 a1,1 0 0 1 1,1 V20 a1,1 0 0 1 -1,1 H4 a1,1 0 0 1 -1,-1 V15 a1,1 0 0 1 1,-1 Z");
    public static Geometry Wrench { get; } = G("M14.7,6.3 a1,1 0 0 0 0,1.4 l1.6,1.6 a1,1 0 0 0 1.4,0 l3.77,-3.77 a6,6 0 0 1 -7.94,7.94 l-6.91,6.91 a2.12,2.12 0 0 1 -3,-3 l6.91,-6.91 a6,6 0 0 1 7.94,-7.94 Z");
    public static Geometry Bell { get; } = G("M6,8 a6,6 0 0 1 12,0 c0,7 3,9 3,9 H3 s3,-2 3,-9 M10.3,21 a1.94,1.94 0 0 0 3.4,0");

    /// <summary>A screen with a small pill at the top: the island as a notification place (drawn for Chippi, not a Lucide original).</summary>
    public static Geometry IslandScreen { get; } = G("M4,4 H20 A2,2 0 0 1 22,6 V18 A2,2 0 0 1 20,20 H4 A2,2 0 0 1 2,18 V6 A2,2 0 0 1 4,4 Z M9.5,8 H14.5");

    public static Geometry Clock { get; } = G("M12,2 a10,10 0 1 0 0,20 a10,10 0 1 0 0,-20 Z M12,6 V12 L16,14");
    public static Geometry Keyboard { get; } = G("M4,4 H20 a2,2 0 0 1 2,2 V18 a2,2 0 0 1 -2,2 H4 a2,2 0 0 1 -2,-2 V6 a2,2 0 0 1 2,-2 Z M6,8 h.01 M10,8 h.01 M14,8 h.01 M18,8 h.01 M8,12 h.01 M12,12 h.01 M16,12 h.01 M7,16 h10");
    public static Geometry Shield { get; } = G("M20,13 c0,5 -3.5,7.5 -7.66,8.95 a1,1 0 0 1 -.67,-.01 C7.5,20.5 4,18 4,13 V6 a1,1 0 0 1 1,-1 c2,0 4.5,-1.2 6.24,-2.72 a1.17,1.17 0 0 1 1.52,0 C14.51,3.81 17,5 19,5 a1,1 0 0 1 1,1 Z");
    public static Geometry Droplet { get; } = G("M12,22 a7,7 0 0 0 7,-7 c0,-2 -1,-3.9 -3,-5.5 s-3.5,-4 -4,-6.5 c-.5,2.5 -2,4.9 -4,6.5 C6,11.1 5,13 5,15 a7,7 0 0 0 7,7 Z");
    public static Geometry Eye { get; } = G("M2,12 s3,-7 10,-7 s10,7 10,7 s-3,7 -10,7 s-10,-7 -10,-7 Z M12,9 a3,3 0 1 0 0,6 a3,3 0 1 0 0,-6 Z");
    public static Geometry Play { get; } = G("M6,3 L20,12 L6,21 Z");
    public static Geometry Pause { get; } = G("M6,4 H10 V20 H6 Z M14,4 H18 V20 H14 Z");
    public static Geometry SkipBack { get; } = G("M19,20 L9,12 L19,4 Z M5,19 V5");
    public static Geometry SkipForward { get; } = G("M5,4 L15,12 L5,20 Z M19,5 V19");

    private static readonly FrozenDictionary<string, Geometry> ByKey = typeof(LucideIcons)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(Geometry))
        .ToFrozenDictionary(p => p.Name, p => (Geometry)p.GetValue(null)!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves an icon key (a property name, case-insensitive) from a module descriptor. Unknown keys get <see cref="LayoutGrid"/>.</summary>
    public static Geometry Get(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var geometry) ? geometry : LayoutGrid;

    /// <summary>Hopper's device-kind lookup, kept for the Phase 3 audio views.</summary>
    public static Geometry ForKind(string? kind) => kind switch
    {
        "monitor" => Monitor,
        "headphones" => Headphones,
        "earbuds" => Earbuds,
        "mic" => Mic,
        "video" => Video,
        _ => Speaker,
    };
}
