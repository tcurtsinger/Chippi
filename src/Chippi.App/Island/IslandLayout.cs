// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.Sound;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>A gallery destination. The two independently installed audio pages share one entry.</summary>
internal sealed record IslandTool(string Key, IslandTileGroup Source, bool Visible)
{
    public const string Audio = "audio";
    public string Name => Key == Audio ? "Audio" : Source.Name;
    public string Icon => Key == Audio ? nameof(LucideIcons.Speaker) : Source.Icon;
    public static bool IsAudioPage(string? key) => key is SoundTile.Key or AppVolumeModule.TileKey;
    public static string GalleryKey(string key) => IsAudioPage(key) ? Audio : key;
}

/// <summary>
/// What Settings › Island &amp; pet says about the island's content: which page it opens on, the
/// tiles' order and which are left off the grid.
/// </summary>
internal sealed record IslandLayout(IslandOpensOn OpensOn, IReadOnlyList<string> Order, IReadOnlySet<string> Hidden)
{
    public static IslandLayout Default { get; } = new(IslandOpensOn.LastPage, [], new HashSet<string>());

    public static IslandLayout From(AppSettings settings) => new(
        settings.IslandOpensOn,
        settings.IslandTileOrder ?? [],
        new HashSet<string>(settings.IslandHiddenTiles ?? [], StringComparer.Ordinal));

    /// <summary><paramref name="keys"/> (in their own order) put in the chosen order; keys it doesn't list follow in theirs.</summary>
    public IReadOnlyList<string> Sorted(IEnumerable<string> keys)
    {
        var all = keys.ToList();
        var order = Order.ToList();
        // OrderBy is stable: unlisted keys keep the feature registry's order.
        return [.. all.OrderBy(k =>
        {
            int at = order.IndexOf(IslandTool.GalleryKey(k));
            if (at < 0) at = order.IndexOf(k);
            return at < 0 ? int.MaxValue : at;
        })];
    }

    /// <summary>The grid's tiles: <see cref="Sorted"/>, without the hidden ones.</summary>
    public IReadOnlyList<string> Shown(IEnumerable<string> keys) => [.. Sorted(keys).Where(k => !Hidden.Contains(k))];

    /// <summary>Use the earliest visible legacy audio entry's position. Keep the device controls when both pages exist.</summary>
    public IReadOnlyList<IslandTool> Tools(IReadOnlyList<IslandTileGroup> groups)
    {
        var byKey = groups.Where(g => g.Key != IslandPanelWindow.NowPlayingKey).ToDictionary(g => g.Key);
        var order = Sorted(byKey.Keys);
        var audio = order.Where(IslandTool.IsAudioPage).ToList();
        string? audioSlot = audio.FirstOrDefault(k => !Hidden.Contains(k)) ?? audio.FirstOrDefault();
        var tools = new List<IslandTool>();
        foreach (string key in order)
        {
            if (!IslandTool.IsAudioPage(key))
                tools.Add(new IslandTool(key, byKey[key], !Hidden.Contains(key)));
            else if (key == audioSlot)
                tools.Add(new IslandTool(IslandTool.Audio, byKey.GetValueOrDefault(SoundTile.Key) ?? byKey[key],
                    !Hidden.Contains(IslandTool.Audio) && !Hidden.Contains(key)));
        }
        return tools;
    }

    /// <summary>Changes the visibility of one tool and preserves saved choices for uninstalled tools.</summary>
    public static List<string> Switched(IEnumerable<string> hidden, string key, bool show)
    {
        var next = hidden.Where(k => IslandTool.GalleryKey(k) != key).ToList();
        if (!show)
            next.Add(key);
        return next;
    }
}
