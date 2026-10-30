// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Chippi.UI.Controls;

/// <summary>
/// Reads the program file's large icon (App volume's strips, the island's Now Playing card). Handles never outlive the
/// conversion. Call on the UI thread, which owns the cache.
/// </summary>
public static class AppIcon
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        IntPtr handle = IntPtr.Zero;
        BitmapSource? image = null;
        try
        {
            if (ExtractIconEx(path, 0, out handle, IntPtr.Zero, 1) > 0 && handle != IntPtr.Zero)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(20, 20));
                image.Freeze();
            }
        }
        catch (Exception) { /* inaccessible program file: use the name */ }
        finally { if (handle != IntPtr.Zero) DestroyIcon(handle); }
        // The UI thread owns the cache. Bound it for long-running sessions with many short-lived apps.
        if (Cache.Count >= 256) Cache.Clear();
        Cache[path] = image;
        return image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, IntPtr small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
