// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace Chippi.Media.Gif;

/// <summary>
/// Writes an animated GIF through WIC's GIF encoder (architecture.md section 3): looping forever (the NETSCAPE2.0
/// block), each frame a <see cref="GifPatch"/> placed at its offset over the last, with unchanged pixels transparent.
/// Each frame's palette is WIC's own, built from that frame and dithered to. One thread; nothing is written past
/// <see cref="Finish"/>.
/// </summary>
public sealed class GifWriter : IDisposable
{
    private readonly IWICImagingFactory _factory;
    private readonly IWICStream _stream;
    private readonly IWICBitmapEncoder _encoder;
    private bool _finished;

    public unsafe GifWriter(string path, int width, int height)
    {
        Width = width;
        Height = height;
        var clsid = MediaPInvoke.CLSID_WICImagingFactory;
        MediaPInvoke.CoCreateInstance(clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory factory).ThrowOnFailure();
        _factory = factory;
        _factory.CreateStream(out _stream);
        _stream.InitializeFromFilename(path, unchecked((uint)GENERIC_WRITE));
        var gif = MediaPInvoke.GUID_ContainerFormatGif;
        _encoder = _factory.CreateEncoder(&gif, null);
        _encoder.Initialize(_stream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
        _encoder.GetMetadataQueryWriter(out var global);
        try
        {
            SetUShort(global, "/logscrdesc/Width", (ushort)width);
            SetUShort(global, "/logscrdesc/Height", (ushort)height);
            SetBytes(global, "/appext/Application", "NETSCAPE2.0"u8);
            // Sub-block 1, loop count 0: forever.
            SetBytes(global, "/appext/Data", [3, 1, 0, 0, 0]);
        }
        finally
        {
            Marshal.ReleaseComObject(global);
        }
    }

    private const int GENERIC_WRITE = 0x4000_0000;

    public int Width { get; }

    public int Height { get; }

    public int Frames { get; private set; }

    /// <summary>Adds <paramref name="patch"/>, shown for <paramref name="delay"/> hundredths of a second.</summary>
    public unsafe void Add(GifPatch patch, int delay)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        var transparent = Array.IndexOf(patch.Changed, false) >= 0;
        var pixels = (byte[])patch.Pixels.Clone();
        for (var i = 0; i < patch.Changed.Length; i++)
            pixels[(i * 4) + 3] = patch.Changed[i] ? (byte)255 : (byte)0;

        var bgra = MediaPInvoke.GUID_WICPixelFormat32bppBGRA;
        var indexed = MediaPInvoke.GUID_WICPixelFormat8bppIndexed;
        IWICBitmap? bitmap = null;
        IWICPalette? palette = null;
        IWICFormatConverter? converter = null;
        IWICBitmapFrameEncode? frame = null;
        IWICMetadataQueryWriter? metadata = null;
        try
        {
            _factory.CreateBitmapFromMemory((uint)patch.Width, (uint)patch.Height, &bgra, (uint)(patch.Width * 4), (uint)pixels.Length, pixels, out bitmap);
            _factory.CreatePalette(out palette);
            palette.InitializeFromBitmap(bitmap, 256, transparent);
            var index = transparent ? TransparentIndex(palette) : -1;
            _factory.CreateFormatConverter(out converter);
            converter.Initialize(bitmap, &indexed, WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion, palette, 50,
                WICBitmapPaletteType.WICBitmapPaletteTypeCustom);

            IPropertyBag2? options = null;
            _encoder.CreateNewFrame(out frame, ref options!);
            frame.Initialize(null);
            frame.SetSize((uint)patch.Width, (uint)patch.Height);
            var format = indexed;
            frame.SetPixelFormat(&format);
            frame.SetPalette(palette);
            frame.GetMetadataQueryWriter(out metadata);
            SetUShort(metadata, "/imgdesc/Left", (ushort)patch.X);
            SetUShort(metadata, "/imgdesc/Top", (ushort)patch.Y);
            SetUShort(metadata, "/grctlext/Delay", (ushort)Math.Clamp(delay, 1, ushort.MaxValue));
            // 1: leave this frame in place; the next one draws over it.
            SetByte(metadata, "/grctlext/Disposal", 1);
            SetBool(metadata, "/grctlext/TransparencyFlag", index >= 0);
            if (index >= 0) SetByte(metadata, "/grctlext/TransparentColorIndex", (byte)index);
            frame.WriteSource(converter, null);
            frame.Commit();
            Frames++;
        }
        finally
        {
            Release(metadata);
            Release(frame);
            Release(converter);
            Release(palette);
            Release(bitmap);
        }
    }

    /// <summary>Closes the file.</summary>
    public void Finish()
    {
        if (_finished) return;
        _encoder.Commit();
        _stream.Commit(0);
        _finished = true;
        Release(_encoder);
        Release(_stream);
    }

    private static unsafe int TransparentIndex(IWICPalette palette)
    {
        palette.GetColorCount(out var count);
        var colors = new uint[count];
        palette.GetColors(count, colors, out count);
        for (var i = (int)count - 1; i >= 0; i--)
            if (colors[i] >> 24 == 0) return i;
        return -1;
    }

    private static unsafe void SetUShort(IWICMetadataQueryWriter writer, string name, ushort value)
    {
        var v = default(PROPVARIANT);
        v.Anonymous.Anonymous.vt = VARENUM.VT_UI2;
        v.Anonymous.Anonymous.Anonymous.uiVal = value;
        writer.SetMetadataByName(name, in v);
    }

    private static unsafe void SetByte(IWICMetadataQueryWriter writer, string name, byte value)
    {
        var v = default(PROPVARIANT);
        v.Anonymous.Anonymous.vt = VARENUM.VT_UI1;
        v.Anonymous.Anonymous.Anonymous.bVal = value;
        writer.SetMetadataByName(name, in v);
    }

    private static unsafe void SetBool(IWICMetadataQueryWriter writer, string name, bool value)
    {
        var v = default(PROPVARIANT);
        v.Anonymous.Anonymous.vt = VARENUM.VT_BOOL;
        v.Anonymous.Anonymous.Anonymous.boolVal = new VARIANT_BOOL(value ? (short)-1 : (short)0);
        writer.SetMetadataByName(name, in v);
    }

    private static unsafe void SetBytes(IWICMetadataQueryWriter writer, string name, ReadOnlySpan<byte> value)
    {
        fixed (byte* data = value)
        {
            var v = default(PROPVARIANT);
            v.Anonymous.Anonymous.vt = VARENUM.VT_VECTOR | VARENUM.VT_UI1;
            v.Anonymous.Anonymous.Anonymous.caub.cElems = (uint)value.Length;
            v.Anonymous.Anonymous.Anonymous.caub.pElems = data;
            writer.SetMetadataByName(name, in v);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    public void Dispose()
    {
        if (!_finished)
        {
            _finished = true;
            Release(_encoder);
            Release(_stream);
        }
        Release(_factory);
    }
}
