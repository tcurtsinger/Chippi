// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Diagnostics;
using Chippi.Features.CommandBar.Search;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.Features.CommandBar.Windows;

/// <summary>
/// The Start menu's apps, as <c>shell:AppsFolder</c> lists them (desktop and Store apps alike), and their icons. The shell
/// is asked on one STA thread of its own, so a slow read never holds up typing.
/// </summary>
internal sealed class StartMenu : IDisposable
{
    private static readonly Guid EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039"); // BHID_EnumItems
    private const uint NormalDisplay = 0; // SIGDN_NORMALDISPLAY
    private const uint ParentRelativeParsing = 0x80018001; // SIGDN_PARENTRELATIVEPARSING
    private const int IconOnly = 0x4; // SIIGBF_ICONONLY

    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;
    private readonly ConcurrentDictionary<(string, int), ImageSource?> _icons = new();

    public StartMenu()
    {
        _thread = new Thread(() =>
        {
            foreach (var work in _work.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { Log.Warn("Command bar: reading the Start menu failed", ex); }
            }
        })
        { IsBackground = true, Name = "Command bar Start menu" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Every app, by name, without duplicates of the same name and target.</summary>
    public Task<IReadOnlyList<StartApp>> ReadAsync() => Run<IReadOnlyList<StartApp>>(() =>
    {
        var folder = Item("shell:AppsFolder");
        folder.BindToHandler(IntPtr.Zero, EnumItems, typeof(IEnumShellItems).GUID, out object found);
        var items = (IEnumShellItems)found;
        var apps = new List<StartApp>();
        var one = new IShellItem[1];
        while (items.Next(1, one, out uint fetched) == 0 && fetched == 1)
        {
            var item = one[0];
            string? name = Name(item, NormalDisplay), parsing = Name(item, ParentRelativeParsing);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parsing))
                apps.Add(new StartApp(name, parsing));
            Marshal.ReleaseComObject(item);
        }
        Marshal.ReleaseComObject(items);
        Marshal.ReleaseComObject(folder);
        return [.. apps.DistinctBy(a => (a.Name, a.ParsingName)).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    });

    /// <summary>The app's icon, <paramref name="pixels"/> square; kept once read.</summary>
    public Task<ImageSource?> IconAsync(StartApp app, int pixels) => IconOfAsync(@"shell:AppsFolder\" + app.ParsingName, pixels);

    /// <summary>A file's, a folder's or a program's icon (the Radial menu's slices), <paramref name="pixels"/> square; kept once read.</summary>
    public Task<ImageSource?> PathIconAsync(string path, int pixels) => IconOfAsync(path, pixels);

    private Task<ImageSource?> IconOfAsync(string parsing, int pixels)
    {
        var key = (parsing, pixels);
        if (_icons.TryGetValue(key, out var known))
            return Task.FromResult(known);
        return Run(() => _icons.GetOrAdd(key, _ => Icon(parsing, pixels)));
    }

    private static unsafe ImageSource? Icon(string parsing, int pixels)
    {
        var item = Item(parsing);
        try
        {
            if (((IShellItemImageFactory)item).GetImage(new Size32(pixels, pixels), IconOnly, out IntPtr bitmap) != 0 || bitmap == IntPtr.Zero)
                return null;
            try { return Pixels((HBITMAP)bitmap); }
            finally { PInvoke.DeleteObject((HGDIOBJ)bitmap); }
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    /// <summary>A 32-bit bitmap's pixels, top row first, as a frozen image. One without alpha is opaque.</summary>
    private static unsafe BitmapSource? Pixels(HBITMAP bitmap)
    {
        BITMAP shape;
        if (PInvoke.GetObject((HGDIOBJ)bitmap.Value, sizeof(BITMAP), &shape) == 0 || shape.bmWidth <= 0 || shape.bmHeight == 0)
            return null;
        int width = shape.bmWidth, height = Math.Abs(shape.bmHeight);
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height; // top row first
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        var pixels = new byte[width * height * 4];
        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* bits = pixels)
            {
                if (PInvoke.GetDIBits(screen, bitmap, 0, (uint)height, bits, &info, DIB_USAGE.DIB_RGB_COLORS) == 0)
                    return null;
            }
        }
        finally { PInvoke.ReleaseDC(HWND.Null, screen); }
        bool alpha = false;
        for (int i = 3; i < pixels.Length && !alpha; i += 4)
            alpha = pixels[i] != 0;
        if (!alpha)
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 0xFF;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static IShellItem Item(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out object item);
        return (IShellItem)item;
    }

    private static string? Name(IShellItem item, uint form)
    {
        if (item.GetDisplayName(form, out IntPtr text) != 0 || text == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private Task<T> Run<T>(Func<T> read)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _work.Add(() =>
            {
                try { done.SetResult(read()); }
                catch (Exception ex) { done.SetException(ex); }
            });
        }
        catch (InvalidOperationException) { done.SetException(new ObjectDisposedException(nameof(StartMenu))); }
        return done.Task;
    }

    public void Dispose() => _work.CompleteAdding();

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Size32(int Width, int Height);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid handler, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object found);

        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);

        [PreserveSig]
        int GetDisplayName(uint form, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IShellItem[] items, out uint fetched);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size32 size, int flags, out IntPtr bitmap);
    }
}
