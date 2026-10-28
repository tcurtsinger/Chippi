// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Buffers.Binary;
using System.Text;
using Chippi.Core.Clipboard;

namespace Chippi.Clipboard;

/// <summary>Clipboard bytes to content and back: CF_UNICODETEXT, CF_HDROP and 24/32-bit DIBs. Pure.</summary>
public static class ClipCodec
{
    /// <summary>
    /// CF_UNICODETEXT: UTF-16 up to the first null. Null when the text is over
    /// <see cref="ClipLimits.TextUtf8Bytes"/> once counted as UTF-8 (the raw allocation is checked by the caller).
    /// </summary>
    public static string? DecodeText(ReadOnlySpan<byte> data, out bool tooLarge)
    {
        var chars = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(data[..(data.Length & ~1)]);
        int end = chars.IndexOf('\0');
        string text = new(end < 0 ? chars : chars[..end]);
        tooLarge = Encoding.UTF8.GetByteCount(text) > ClipLimits.TextUtf8Bytes;
        return tooLarge ? null : text;
    }

    public static byte[] EncodeText(string text) => Encoding.Unicode.GetBytes(text + "\0");

    /// <summary>CF_HDROP (a DROPFILES block): the paths, or null when malformed.</summary>
    public static IReadOnlyList<string>? DecodeFiles(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20)
            return null;
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        bool wide = BinaryPrimitives.ReadInt32LittleEndian(data[16..]) != 0;
        if (offset < 20 || offset > data.Length)
            return null;
        var rest = data[offset..];
        var files = new List<string>();
        if (wide)
        {
            var chars = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(rest[..(rest.Length & ~1)]);
            while (true)
            {
                int end = chars.IndexOf('\0');
                if (end <= 0)
                    break;
                files.Add(new string(chars[..end]));
                chars = chars[(end + 1)..];
            }
        }
        else
        {
            while (true)
            {
                int end = rest.IndexOf((byte)0);
                if (end <= 0)
                    break;
                files.Add(Encoding.Default.GetString(rest[..end]));
                rest = rest[(end + 1)..];
            }
        }
        return files;
    }

    public static byte[] EncodeFiles(IReadOnlyList<string> files)
    {
        var list = Encoding.Unicode.GetBytes(string.Concat(files.Select(f => f + "\0")) + "\0");
        var data = new byte[20 + list.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 20);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 1);
        list.CopyTo(data, 20);
        return data;
    }

    /// <summary>DROPEFFECT_COPY for "Preferred DropEffect", so pasting a file list copies and never moves.</summary>
    public static byte[] CopyEffect { get; } = [1, 0, 0, 0];

    /// <summary>Reads a DIB header's size without touching the pixels. False for anything not a plain DIB.</summary>
    public static bool TryDibSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 40)
            return false;
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (headerSize < 40 || headerSize > data.Length)
            return false;
        width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[8..]));
        return width > 0 && height > 0;
    }

    /// <summary>
    /// CF_DIB or CF_DIBV5 to top-down BGRA: 32-bit (BI_RGB, or BI_BITFIELDS with the standard masks) and
    /// 24-bit BI_RGB. Null for other layouts or when the pixel data is short. Check the size first with
    /// <see cref="TryDibSize"/>: the pixel cap applies before this allocates.
    /// </summary>
    public static ClipImage? DecodeDib(ReadOnlySpan<byte> data)
    {
        if (!TryDibSize(data, out int width, out int height))
            return null;
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
        bool bottomUp = BinaryPrimitives.ReadInt32LittleEndian(data[8..]) > 0;
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        int colors = BinaryPrimitives.ReadInt32LittleEndian(data[32..]);
        const int BiRgb = 0, BiBitfields = 3;
        int pixelsAt = headerSize;
        if (compression == BiBitfields)
        {
            if (bits != 32)
                return null;
            // A V4/V5 header carries the masks inside it; a plain 40-byte header has them after it.
            int masksAt = headerSize >= 52 ? 40 : headerSize;
            if (data.Length < masksAt + 12)
                return null;
            uint r = BinaryPrimitives.ReadUInt32LittleEndian(data[masksAt..]);
            uint g = BinaryPrimitives.ReadUInt32LittleEndian(data[(masksAt + 4)..]);
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(data[(masksAt + 8)..]);
            if (r != 0x00FF0000 || g != 0x0000FF00 || b != 0x000000FF)
                return null;
            if (headerSize == 40)
                pixelsAt += 12;
        }
        else if (compression != BiRgb || bits is not (24 or 32))
        {
            return null;
        }
        pixelsAt += colors * 4;
        int stride = (width * bits + 31) / 32 * 4;
        if ((long)stride * height > data.Length - pixelsAt)
            return null;
        var bgra = new byte[(long)width * height * 4];
        bool anyAlpha = false;
        for (int y = 0; y < height; y++)
        {
            var row = data.Slice(pixelsAt + (bottomUp ? height - 1 - y : y) * stride, stride);
            var to = bgra.AsSpan(y * width * 4, width * 4);
            if (bits == 32)
            {
                row[..(width * 4)].CopyTo(to);
                for (int x = 3; x < to.Length && !anyAlpha; x += 4)
                    anyAlpha = to[x] != 0;
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    to[x * 4] = row[x * 3];
                    to[x * 4 + 1] = row[x * 3 + 1];
                    to[x * 4 + 2] = row[x * 3 + 2];
                    to[x * 4 + 3] = 255;
                }
            }
        }
        // Most apps leave a 32-bit BI_RGB alpha at 0; treat that as opaque.
        if (bits == 32 && !anyAlpha)
            for (int i = 3; i < bgra.Length; i += 4)
                bgra[i] = 255;
        return new ClipImage(width, height, bgra);
    }

    /// <summary>Top-down BGRA to a bottom-up 32-bit BI_RGB CF_DIB, the layout every app reads.</summary>
    public static byte[] EncodeDib(ClipImage image)
    {
        int stride = image.Width * 4;
        var data = new byte[40 + stride * image.Height];
        BinaryPrimitives.WriteInt32LittleEndian(data, 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 32);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), stride * image.Height);
        for (int y = 0; y < image.Height; y++)
            image.Bgra.AsSpan(y * stride, stride).CopyTo(data.AsSpan(40 + (image.Height - 1 - y) * stride));
        return data;
    }

    /// <summary>A CanIncludeInClipboardHistory value: anything but a 4-byte nonzero DWORD keeps the copy out (fail closed).</summary>
    public static bool HistoryAllowed(byte[]? value) => value is { Length: 4 } && BinaryPrimitives.ReadUInt32LittleEndian(value) != 0;

    public static byte[] Dword(uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return data;
    }
}
