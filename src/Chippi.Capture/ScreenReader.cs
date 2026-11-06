// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Numerics;
using System.Runtime.InteropServices;

namespace Chippi.Capture;

/// <summary>A connected display as the reader sees it: its HMONITOR, bounds in physical pixels and effective DPI.</summary>
public sealed record MonitorBounds(nint Monitor, PixelRect Bounds, int Dpi, bool IsMain);

/// <summary>Reads the screen for a freeze. Tests pass a fake with known pixels.</summary>
public interface IScreenReader
{
    /// <summary>The connected displays, main first.</summary>
    IReadOnlyList<MonitorBounds> Monitors();

    /// <summary>One display's pixels (BGRA, top row first, alpha 255), or null when the read failed (the secure desktop).</summary>
    byte[]? Read(PixelRect bounds);
}

/// <summary>
/// GDI <c>BitBlt</c> from the screen DC with <c>CAPTUREBLT</c>, one call a display (capture-layer.md, spike results: the
/// fastest cold, and its warm tail better than WGC's). The pointer isn't included. Any thread.
/// </summary>
public sealed partial class ScreenReader : IScreenReader
{
    public IReadOnlyList<MonitorBounds> Monitors()
    {
        var found = new List<MonitorBounds>();
        var handle = GCHandle.Alloc(found);
        try
        {
            unsafe { EnumDisplayMonitors(0, 0, &Add, GCHandle.ToIntPtr(handle)); }
        }
        finally { handle.Free(); }
        return [.. found.OrderByDescending(m => m.IsMain)];
    }

    [UnmanagedCallersOnly]
    private static unsafe int Add(nint monitor, nint dc, Rect* clip, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var found = (List<MonitorBounds>)handle.Target!;
        var info = new MonitorInfo { Size = sizeof(MonitorInfo) };
        if (GetMonitorInfoW(monitor, ref info))
        {
            var r = info.Monitor;
            int dpi = GetDpiForMonitor(monitor, 0, out uint dx, out _) == 0 ? (int)dx : 96;
            found.Add(new MonitorBounds(monitor, PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom), dpi, (info.Flags & 1) != 0));
        }
        return 1;
    }

    public byte[]? Read(PixelRect bounds)
    {
        if (bounds.IsEmpty)
            return null;
        nint screen = GetDC(0);
        if (screen == 0)
            return null;
        nint memory = 0, bitmap = 0, old = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bounds.Width,
                Height = -bounds.Height, // top row first
                Planes = 1,
                BitCount = 32,
            };
            bitmap = CreateDIBSection(screen, ref header, 0, out nint bits, 0, 0);
            if (memory == 0 || bitmap == 0 || bits == 0)
                return null;
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SrcCopy | CaptureBlt))
                return null;
            GdiFlush();
            var pixels = new byte[(long)bounds.Width * bounds.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            Opaque(pixels);
            return pixels;
        }
        finally
        {
            if (old != 0) SelectObject(memory, old);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>GDI leaves alpha undefined (usually 0): every read pixel is opaque.</summary>
    internal static void Opaque(Span<byte> pixels)
    {
        var words = MemoryMarshal.Cast<byte, uint>(pixels);
        var alpha = new Vector<uint>(0xFF000000u);
        int i = 0;
        for (; i <= words.Length - Vector<uint>.Count; i += Vector<uint>.Count)
            (new Vector<uint>(words[i..]) | alpha).CopyTo(words[i..]);
        for (; i < words.Length; i++)
            words[i] |= 0xFF000000u;
    }

    private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial int EnumDisplayMonitors(nint dc, nint clip, delegate* unmanaged<nint, nint, Rect*, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateDIBSection(nint dc, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GdiFlush();

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);
}
