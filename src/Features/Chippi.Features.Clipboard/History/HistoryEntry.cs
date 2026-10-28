// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Clipboard.History;

/// <summary>What a saved copy is: its glyph and its details (design notes #8, #9).</summary>
public enum EntryKind
{
    Text = 0,
    Link = 1,
    Picture = 2,
    Files = 3,
}

/// <summary>
/// One saved copy, decrypted into memory. A picture's full PNG stays in the store until it's pasted or
/// copied; the entry holds its size and a thumbnail. Rich copies are kept as their plain text (notes #16).
/// </summary>
public sealed record HistoryEntry(
    long Id,
    EntryKind Kind,
    DateTimeOffset Recent,
    DateTimeOffset? PinnedAt,
    string? Text,
    IReadOnlyList<string>? Files,
    int Width,
    int Height,
    byte[]? Thumbnail,
    long Bytes)
{
    public bool Pinned => PinnedAt is not null;

    /// <summary>Whether the search finds it: its text, or its files' names. Ordinal, ignoring case.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || Text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || Files?.Any(f => System.IO.Path.GetFileName(f).Contains(search, StringComparison.OrdinalIgnoreCase)) == true;
}

/// <summary>A copy to save. <see cref="Png"/> is a picture's full image; the rest goes in the entry.</summary>
public sealed record HistoryCapture(EntryKind Kind, string? Text, IReadOnlyList<string>? Files, int Width, int Height, byte[]? Thumbnail, byte[]? Png)
{
    public static HistoryCapture FromText(string text) =>
        new(UrlCleaning.UrlCleaner.Clean(text) is not null ? EntryKind.Link : EntryKind.Text, text, null, 0, 0, null, null);

    public static HistoryCapture FromFiles(IReadOnlyList<string> files) => new(EntryKind.Files, null, files, 0, 0, null, null);

    public static HistoryCapture FromPicture(int width, int height, byte[] thumbnail, byte[] png) =>
        new(EntryKind.Picture, null, null, width, height, thumbnail, png);
}
