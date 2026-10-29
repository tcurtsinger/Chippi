// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Shelf;

/// <summary>What a Shelf item is: its glyph and its caption (design notes #37).</summary>
public enum ShelfKind
{
    Text = 0,
    Link = 1,
    Picture = 2,
    Files = 3,
}

/// <summary>
/// One Shelf item, decrypted into memory. A file item is only a reference: its paths, never a copy
/// (architecture §6). A picture's full PNG stays in the store until it's copied or dragged out.
/// </summary>
public sealed record ShelfItem(
    long Id,
    ShelfKind Kind,
    DateTimeOffset Added,
    bool Pinned,
    string? Text,
    IReadOnlyList<string>? Files,
    int Width,
    int Height,
    byte[]? Thumbnail,
    long Bytes);

/// <summary>One drop to keep (notes #40): its files together, or a text, link or picture.</summary>
public sealed record ShelfDrop(ShelfKind Kind, string? Text, IReadOnlyList<string>? Files, int Width, int Height, byte[]? Thumbnail, byte[]? Png)
{
    public static ShelfDrop FromText(string text) => new(IsLink(text) ? ShelfKind.Link : ShelfKind.Text, text, null, 0, 0, null, null);

    public static ShelfDrop FromFiles(IReadOnlyList<string> files) => new(ShelfKind.Files, null, files, 0, 0, null, null);

    public static ShelfDrop FromPicture(int width, int height, byte[] thumbnail, byte[] png) => new(ShelfKind.Picture, null, null, width, height, thumbnail, png);

    /// <summary>One http or https link with nothing around it.</summary>
    public static bool IsLink(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length > 0
            && !trimmed.Any(char.IsWhiteSpace)
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0;
    }
}

/// <summary>Why the last drop wasn't kept (notes #44, #45), shown beside the actions until the next drop.</summary>
public enum ShelfLimit
{
    None,

    /// <summary>"Over 100 files".</summary>
    Files,

    /// <summary>"Picture over 16 MiB".</summary>
    Picture,

    /// <summary>"Shelf full · 128 MiB".</summary>
    Full,

    /// <summary>"Shelf full": <see cref="ShelfService.MaxItems"/> items, all the page shows.</summary>
    Items,
}
