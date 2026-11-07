// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media.Gif;

/// <summary>
/// One GIF frame's change: the smallest rectangle holding every pixel that changed, its pixels (BGRA, top-down),
/// and which of them changed. The rest are written transparent, so the frame below shows through.
/// </summary>
public sealed record GifPatch(int X, int Y, int Width, int Height, byte[] Pixels, bool[] Changed);

/// <summary>
/// Frame differencing for the GIF path (architecture.md section 3): each frame is compared with what the GIF
/// already shows, and only the change is written. Frames decoded from H.264 carry noise, so a pixel counts as
/// changed when a channel moves by more than <see cref="Tolerance"/>. It's compared with what's shown, not the last
/// frame, so slow drift still gets written once it adds up.
/// </summary>
public sealed class GifFrameDiff(int width, int height, int tolerance = 6)
{
    private byte[]? _shown;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public int Tolerance { get; } = tolerance;

    /// <summary>The change <paramref name="bgra"/> makes, or null when it changes nothing (the last frame holds).</summary>
    public GifPatch? Next(ReadOnlySpan<byte> bgra)
    {
        var stride = Width * 4;
        if (bgra.Length < stride * Height) throw new ArgumentException("Frame is smaller than the GIF.", nameof(bgra));
        if (_shown is null)
        {
            _shown = bgra[..(stride * Height)].ToArray();
            var all = new bool[Width * Height];
            Array.Fill(all, true);
            return new GifPatch(0, 0, Width, Height, (byte[])_shown.Clone(), all);
        }

        int left = Width, top = Height, right = -1, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < Width; x++)
            {
                if (!Differs(bgra, row + (x * 4))) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                bottom = y;
            }
        }
        if (right < 0) return null;

        var w = right - left + 1;
        var h = bottom - top + 1;
        var pixels = new byte[w * h * 4];
        var changed = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var source = ((top + y) * stride) + ((left + x) * 4);
                var target = ((y * w) + x) * 4;
                bgra.Slice(source, 4).CopyTo(pixels.AsSpan(target, 4));
                if (!Differs(bgra, source)) continue;
                changed[(y * w) + x] = true;
                bgra.Slice(source, 4).CopyTo(_shown.AsSpan(source, 4));
            }
        }
        return new GifPatch(left, top, w, h, pixels, changed);
    }

    private bool Differs(ReadOnlySpan<byte> bgra, int at) =>
        Math.Abs(bgra[at] - _shown![at]) > Tolerance
        || Math.Abs(bgra[at + 1] - _shown[at + 1]) > Tolerance
        || Math.Abs(bgra[at + 2] - _shown[at + 2]) > Tolerance;
}
