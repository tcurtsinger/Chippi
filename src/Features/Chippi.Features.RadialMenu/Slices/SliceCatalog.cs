// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Core.Actions;
using Chippi.Features.CommandBar.Search;
using Chippi.UI.Controls;

namespace Chippi.Features.RadialMenu.Slices;

/// <summary>A slice's glyph and name: its action's (<see cref="ActionCatalog"/>), or a submenu's.</summary>
public static class SliceCatalog
{
    /// <summary>The Kind list's own kind, after the shared ones.</summary>
    public const string SubmenuName = "Submenu";

    /// <summary>
    /// A slice's glyph: the one picked with Change, else its action's or its kind's. Apps and files have their own icons
    /// (the module reads them); this is what shows until one is read, or when there's none.
    /// </summary>
    public static Geometry GlyphOf(Slice slice, Func<string, string?>? commandIcon = null) => slice.Kind == SliceKind.Submenu
        ? slice.Glyph is { } picked ? LucideIcons.Get(picked) : LucideIcons.LayoutGrid
        : LucideIcons.Get(ActionCatalog.GlyphOf(slice.ToAction(), commandIcon));

    /// <summary>
    /// The name a slice gets from what it does (<see cref="ActionCatalog.NameFor"/>). <paramref name="known"/> names an
    /// app or a Chippi command; a Settings page's name comes from the Command bar's list.
    /// </summary>
    public static string NameFor(Slice slice, Func<string, string?>? known = null) => slice.Kind switch
    {
        SliceKind.Submenu => SubmenuName,
        SliceKind.SettingsPage => ActionCatalog.NameFor(slice.ToAction(), SettingsPageName),
        _ => ActionCatalog.NameFor(slice.ToAction(), known),
    };

    /// <summary>A Windows Settings page's name, by its ms-settings: address.</summary>
    public static string? SettingsPageName(string uri) => SettingsPages.All.FirstOrDefault(p => p.Uri == uri)?.Name;
}
