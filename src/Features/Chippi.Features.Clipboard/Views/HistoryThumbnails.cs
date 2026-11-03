// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media.Imaging;
using Chippi.Features.Clipboard.History;

namespace Chippi.Features.Clipboard.Views;

/// <summary>One open history view's decoded thumbnails. Full pictures stay in the store.</summary>
internal sealed class HistoryThumbnails
{
    private readonly Dictionary<long, (byte[] Png, BitmapSource? Image)> _items = [];

    public BitmapSource? Get(HistoryEntry entry)
    {
        if (entry.Thumbnail is not { Length: > 0 } png)
            return null;
        // IDs can be reused after an entry is deleted. A changed PNG must replace the old image.
        if (_items.TryGetValue(entry.Id, out var cached) && cached.Png.AsSpan().SequenceEqual(png))
            return cached.Image;
        BitmapSource? image = null;
        try
        {
            image = Pictures.Load(png);
        }
        catch (Exception)
        {
            // A damaged thumbnail falls back to its picture glyph.
        }
        _items[entry.Id] = (png, image);
        return image;
    }

    public void Retain(IReadOnlyList<HistoryEntry> entries)
    {
        if (_items.Count == 0)
            return;
        var retained = entries.Where(e => e.Thumbnail is { Length: > 0 }).Select(e => e.Id).ToHashSet();
        foreach (long removed in _items.Keys.Where(id => !retained.Contains(id)).ToArray())
            _items.Remove(removed);
    }

    public void Clear() => _items.Clear();
}
