// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Island.Media;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.App.Island;

/// <summary>
/// What the Mac-style desktop's island shows (docs/mac-style/brief.md, Island): the widget bar's choice of activity
/// (DESIGN.md §5), told as a lead, a line and a tail, with the page a press on it opens.
/// </summary>
internal static class BarIslandFaces
{
    /// <summary>The face, and what a press does: <c>Key</c>'s page in the spot (null for the home), or <c>Click</c> instead.</summary>
    public static (BarIslandFace Face, string? Key, Action? Click) For(IReadOnlyList<IslandActivity> activities, IReadOnlyList<IslandTileGroup> tiles, MediaTrack? song, PetMood mood)
    {
        // The bar's own icon shows a muted mic; without that icon the island badges it (MenuBarWindow.IslandBadges).
        var shown = activities.Where(a => a.ModuleId != "mic-mute").ToArray();
        if (shown.FirstOrDefault(a => a.Kind == IslandActivityKind.Recording) is { } recording)
            return (new(mood, IslandLead.Recording, null, null, recording.Title, "", IslandTail.None, recording.ModuleId, "Screen recording"),
                recording.TileKey ?? recording.OpensTile, recording.Clicked);
        if (shown.FirstOrDefault(a => a.IsFeedback) is { } feedback)
        {
            var opens = feedback.OpensTile is { } page ? tiles.FirstOrDefault(t => t.Key == page) : null;
            var glyph = feedback.Icon is { } icon ? LucideIcons.Get(icon) : opens is not null ? LucideIcons.Get(opens.Icon) : LucideIcons.ClipboardType;
            return (new(mood, feedback.NeedsAttention ? IslandLead.Attention : IslandLead.Glyph, glyph, null, feedback.Title, "", IslandTail.None, feedback.ModuleId, opens?.Name ?? feedback.Title),
                feedback.OpensTile, feedback.Clicked);
        }
        var top = shown.FirstOrDefault();
        if (top is null)
            return (BarIslandFace.Rest(mood), null, null);
        if (top.Kind == IslandActivityKind.Media)
        {
            if (song is not { } track)
                return (new(mood, top.NeedsAttention ? IslandLead.Attention : IslandLead.Glyph, LucideIcons.Music, null, top.Title, "", IslandTail.None, top.ModuleId, "Now Playing"), IslandPanelWindow.NowPlayingKey, null);
            return (new(track.Playing ? PetMood.Listening : mood, IslandLead.Cover, LucideIcons.Music, track.Artwork, track.Title, track.Artist,
                track.Playing ? IslandTail.Equalizer : IslandTail.Paused, top.ModuleId, "Now Playing"), IslandPanelWindow.NowPlayingKey, null);
        }
        var tile = top.TileKey is { } key ? tiles.FirstOrDefault(t => t.Key == key) : null;
        var mark = tile is not null ? LucideIcons.Get(tile.Toggle?.Icon ?? tile.Icon)
            : top.Icon is { } own ? LucideIcons.Get(own)
            : top.Kind switch
            {
                IslandActivityKind.Download or IslandActivityKind.Stash => LucideIcons.Folder,
                IslandActivityKind.Timer => LucideIcons.Get("timer"),
                IslandActivityKind.Agent => LucideIcons.Get("bot"),
                _ => LucideIcons.Get("circle-dot"),
            };
        string status = tile is null ? "" : tile.Toggle?.Status?.Invoke() ?? tile.Status;
        return (new(mood, top.NeedsAttention ? IslandLead.Attention : IslandLead.Glyph, mark, null, status.Length > 0 ? status : top.Title, "", IslandTail.None, top.ModuleId, tile?.Name ?? top.Title),
            top.TileKey ?? top.OpensTile, top.Clicked);
    }
}
