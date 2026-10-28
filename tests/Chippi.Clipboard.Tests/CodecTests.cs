// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Buffers.Binary;
using Chippi.Core.Clipboard;

namespace Chippi.Clipboard.Tests;

public sealed class CodecTests
{
    [Fact]
    public void Text_stops_at_the_first_null_and_keeps_tabs_and_line_breaks()
    {
        var data = ClipCodec.EncodeText("a\tb\r\nc é中😀").Concat(new byte[] { 0x41, 0, 0, 0, 0, 0 }).ToArray();
        Assert.Equal("a\tb\r\nc é中😀", ClipCodec.DecodeText(data, out bool tooLarge));
        Assert.False(tooLarge);
    }

    [Fact]
    public void File_lists_round_trip_and_ansi_lists_still_read()
    {
        string[] files = [@"C:\Users\Sam\Desktop\report.pdf", @"D:\Fotos\été.png"];
        Assert.Equal(files, ClipCodec.DecodeFiles(ClipCodec.EncodeFiles(files)));

        var ansi = new byte[20 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(ansi, 20);
        "C:\\a.txt"u8[..6].CopyTo(ansi.AsSpan(20));
        Assert.Equal([@"C:\a.t"], ClipCodec.DecodeFiles(ansi));
        Assert.Null(ClipCodec.DecodeFiles([1, 2, 3]));
    }

    [Fact]
    public void A_32_bit_picture_round_trips_through_a_dib()
    {
        var image = new ClipImage(3, 2, [.. Enumerable.Range(0, 24).Select(i => (byte)(i * 10))]);
        var back = ClipCodec.DecodeDib(ClipCodec.EncodeDib(image))!;
        Assert.Equal((3, 2), (back.Width, back.Height));
        Assert.Equal(image.Bgra, back.Bgra);
    }

    [Fact]
    public void A_24_bit_bottom_up_dib_becomes_opaque_top_down_bgra()
    {
        // 2 x 2, rows padded to 8 bytes; bottom row first.
        var dib = new byte[40 + 16];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        new byte[] { 1, 2, 3, 4, 5, 6 }.CopyTo(dib, 40);   // bottom row
        new byte[] { 7, 8, 9, 10, 11, 12 }.CopyTo(dib, 48); // top row
        var image = ClipCodec.DecodeDib(dib)!;
        Assert.Equal(new byte[] { 7, 8, 9, 255, 10, 11, 12, 255, 1, 2, 3, 255, 4, 5, 6, 255 }, image.Bgra);
    }

    [Fact]
    public void A_32_bit_dib_with_no_alpha_is_opaque()
    {
        var image = ClipCodec.DecodeDib(ClipCodec.EncodeDib(new ClipImage(1, 1, [5, 6, 7, 0])))!;
        Assert.Equal(new byte[] { 5, 6, 7, 255 }, image.Bgra);
    }

    [Fact]
    public void Unsupported_or_short_dibs_are_refused()
    {
        var dib = ClipCodec.EncodeDib(new ClipImage(2, 2, new byte[16]));
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(16), 1); // BI_RLE8
        Assert.Null(ClipCodec.DecodeDib(dib));
        Assert.Null(ClipCodec.DecodeDib(ClipCodec.EncodeDib(new ClipImage(2, 2, new byte[16]))[..50]));
        Assert.False(ClipCodec.TryDibSize([1, 2, 3], out _, out _));
    }

    [Fact]
    public void Only_a_nonzero_dword_allows_history()
    {
        Assert.True(ClipCodec.HistoryAllowed(ClipCodec.Dword(1)));
        Assert.False(ClipCodec.HistoryAllowed(ClipCodec.Dword(0)));
        Assert.False(ClipCodec.HistoryAllowed([1]));
        Assert.False(ClipCodec.HistoryAllowed(null));
    }
}
