// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.UI.Controls;

/// <summary>Native icon ownership and transparent bitmap conversion. All reads run on an STA worker.</summary>
internal static class WindowsAppIcons
{
    internal static BitmapSource? Read(string? parsing, int pixels)
    {
        if (string.IsNullOrWhiteSpace(parsing)) return null;
        try
        {
            if (Path.IsPathFullyQualified(parsing)) parsing = Path.GetFullPath(parsing);
            var item = Item(parsing);
            try
            {
                // BIGGERSIZEOK | ICONONLY: retain original artwork and let WPF resample it smoothly.
                if (((IShellItemImageFactory)item).GetImage(new Size32(pixels, pixels), 0x1 | 0x4, out nint bitmap) != 0 || bitmap == 0)
                    return null;
                try { return Pixels(bitmap); }
                finally { DeleteObject(bitmap); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    internal static BitmapSource? ReadInstalled(IReadOnlyList<string> identities)
    {
        var folder = Item("shell:AppsFolder");
        var matches = new List<string>();
        try
        {
            folder.BindToHandler(0, new Guid("94f60519-2850-4924-aa5a-d15e84868039"), typeof(IEnumShellItems).GUID, out object found);
            try
            {
                var items = (IEnumShellItems)found;
                var one = new IShellItem[1];
                while (items.Next(1, one, out uint fetched) == 0 && fetched == 1)
                {
                    var item = one[0];
                    try
                    {
                        if (item.GetDisplayName(0x80018001, out nint text) != 0 || text == 0) continue;
                        try
                        {
                            if (Marshal.PtrToStringUni(text) is { } identity && identities.Any(prefix => Matches(identity, prefix)))
                                matches.Add(identity);
                        }
                        finally { Marshal.FreeCoTaskMem(text); }
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(found); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        // Caller order expresses preference; enumeration order must not choose a different product.
        foreach (var prefix in identities)
            foreach (var identity in matches.Where(id => Matches(id, prefix)).Order(StringComparer.OrdinalIgnoreCase))
                if (Read(@"shell:AppsFolder\" + identity, 128) is { } icon) return icon;
        return null;
    }

    internal static bool Matches(string identity, string prefix) => !string.IsNullOrWhiteSpace(prefix) &&
        (identity.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
         (prefix.EndsWith('_') && identity.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    internal static BitmapSource? ReadWindow(nint window, int pixels)
    {
        const uint getIcon = 0x007F;
        nint handle = 0;
        foreach (nuint size in new nuint[] { 1, 2 })
        {
            if (SendMessageTimeout(window, getIcon, size, (nint)Math.Clamp(Math.Round(96d * pixels / 20), 96, 768), 0x2 | 0x20, 100, out nuint found) == 0)
                return null;
            if (found != 0) { handle = (nint)found; break; }
        }
        if (handle == 0) handle = ClassIcon(window, -14);
        if (handle == 0) handle = ClassIcon(window, -34);
        if (handle == 0) return null;
        nint owned = CopyIcon(handle);
        if (owned == 0) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(owned, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or InvalidOperationException) { return null; }
        finally { DestroyIcon(owned); }
    }

    private static nint ClassIcon(nint window, int index) => IntPtr.Size == 8 ? GetClassLongPtr(window, index) : (nint)GetClassLong(window, index);

    private static BitmapSource? Pixels(nint bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out var shape) == 0 || shape.Width <= 0 || shape.Height == 0)
            return null;
        int width = shape.Width;
        long absoluteHeight = Math.Abs((long)shape.Height);
        if (width > 1024 || absoluteHeight > 1024) return null;
        int height = (int)absoluteHeight;
        var info = new BitmapInfo { Header = new BitmapInfoHeader
        { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 } };
        byte[] pixels = new byte[width * height * 4];
        nint screen = GetDC(0);
        try { if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0) != height) return null; }
        finally { ReleaseDC(0, screen); }
        bool alpha = false;
        for (int i = 3; i < pixels.Length && !alpha; i += 4) alpha = pixels[i] != 0;
        if (!alpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static IShellItem Item(string parsing)
    {
        SHCreateItemFromParsingName(parsing, 0, typeof(IShellItem).GUID, out object item);
        return (IShellItem)item;
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct Size32(int Width, int Height);
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint bitmap, int size, out Bitmap result);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong(nint window, int index);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, [MarshalAs(UnmanagedType.LPStruct)] Guid handler, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object found);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint form, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IShellItem[] items, out uint fetched);
    }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(Size32 size, int flags, out nint bitmap); }
}
