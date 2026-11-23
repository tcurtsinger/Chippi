// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

/// <summary>The CPU side of <see cref="WgcAreaFrames"/>: row copies out of a mapped staging texture, and the checks on its sizes.</summary>
internal static class AreaReadback
{
    public const int BytesPerPixel = 4;

    /// <summary>Whether the area fits in a frame of the given size.</summary>
    public static bool Fits(int left, int top, int width, int height, int frameWidth, int frameHeight) =>
        width > 0 && height > 0 && left >= 0 && top >= 0 && left + width <= frameWidth && top + height <= frameHeight;

    /// <summary>
    /// Copies <paramref name="height"/> rows of <paramref name="width"/> pixels from a mapped texture (rows
    /// <paramref name="rowPitch"/> bytes apart) into tight rows.
    /// </summary>
    public static void CopyRows(ReadOnlySpan<byte> mapped, int rowPitch, int width, int height, byte[] destination)
    {
        int row = width * BytesPerPixel;
        if (rowPitch < row) throw new ArgumentOutOfRangeException(nameof(rowPitch));
        if (destination.Length < row * height) throw new ArgumentException("Destination too small", nameof(destination));
        if (height > 0 && mapped.Length < (height - 1) * rowPitch + row) throw new ArgumentException("Source too small", nameof(mapped));
        for (int y = 0; y < height; y++)
            mapped.Slice(y * rowPitch, row).CopyTo(destination.AsSpan(y * row, row));
    }

    /// <summary>Sets every pixel's alpha to 255: a captured screen is opaque, and a stitched result is then known to be.</summary>
    public static void ForceOpaque(byte[] bgra)
    {
        var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan(0, bgra.Length / 4 * 4));
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] |= 0xFF000000u;
    }

    /// <summary>The same copy straight from a mapped pointer.</summary>
    public static unsafe void CopyRows(byte* mapped, int rowPitch, int width, int height, byte[] destination)
    {
        if (height <= 0) return;
        CopyRows(new ReadOnlySpan<byte>(mapped, (height - 1) * rowPitch + width * BytesPerPixel), rowPitch, width, height, destination);
    }
}
