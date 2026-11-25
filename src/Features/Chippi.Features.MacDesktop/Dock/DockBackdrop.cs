// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// The blur behind the dock's glass. WPF can't blur behind a layered window, so a plain window the shelf's size and shape sits
/// just under the dock with Windows' acrylic in it, and the shelf's tint is drawn over it. Click-through, never activated,
/// never in Alt+Tab. Absent with transparency effects off, or where Windows refuses the acrylic; the shelf is then denser.
/// </summary>
internal sealed class DockBackdrop : IDisposable
{
    private readonly HwndSource _source;
    private Native.Rect _bounds;
    private double _corner = -1;
    private bool _shown;

    private DockBackdrop(HwndSource source) => _source = source;

    public nint Handle => _source.Handle;

    /// <summary>A backdrop, or null when transparency effects are off or the acrylic is refused.</summary>
    public static DockBackdrop? Create(bool light)
    {
        if (!TransparencyOn())
            return null;
        var source = new HwndSource(new HwndSourceParameters("Chippi dock backdrop")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = (int)(Native.WsExToolWindow | Native.WsExNoActivate | Native.WsExTransparent | Native.WsExTopmost),
            Width = 1,
            Height = 1,
        });
        if (source.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;
        int corner = 1; // DWMWCP_DONOTROUND: the region below gives the shelf's own corners
        _ = DwmSetWindowAttribute(source.Handle, 33, ref corner, sizeof(int));
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no system border
        _ = DwmSetWindowAttribute(source.Handle, 34, ref none, sizeof(int));
        var backdrop = new DockBackdrop(source);
        if (!backdrop.SetTint(light))
        {
            source.Dispose();
            return null;
        }
        return backdrop;
    }

    /// <summary>Windows' acrylic with a faint tint of the theme's glass under it.</summary>
    public bool SetTint(bool light)
    {
        var policy = new AccentPolicy
        {
            AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
            GradientColor = light ? 0x30F8F6F6u : 0x302E2828u, // ABGR
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        nint data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, data, false);
            var attribute = new CompositionAttributeData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = data, Size = size };
            return SetWindowCompositionAttribute(_source.Handle, ref attribute);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>
    /// Puts the backdrop on <paramref name="bounds"/> (physical pixels, the shelf inside its 1px edge) with
    /// <paramref name="corner"/> pixel corners, just under <paramref name="dock"/>. An empty rectangle hides it.
    /// </summary>
    public void Place(nint dock, System.Windows.Rect bounds, double corner)
    {
        if (bounds.IsEmpty || bounds.Width < 2 || bounds.Height < 2)
        {
            Hide();
            return;
        }
        var rect = new Native.Rect((int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
        bool resized = rect.Width != _bounds.Width || rect.Height != _bounds.Height || corner != _corner;
        if (_shown && !resized && rect.Left == _bounds.Left && rect.Top == _bounds.Top)
            return;
        _bounds = rect;
        _corner = corner;
        Native.SetWindowPos(_source.Handle, dock, rect.Left, rect.Top, rect.Width, rect.Height,
            Native.SwpNoActivate | Native.SwpNoOwnerZOrder | Native.SwpShowWindow);
        if (resized)
        {
            int d = (int)Math.Round(corner * 2);
            _ = SetWindowRgn(_source.Handle, CreateRoundRectRgn(0, 0, rect.Width + 1, rect.Height + 1, d, d), true);
        }
        _shown = true;
    }

    /// <summary>Puts the backdrop back just under the dock, where it stays.</summary>
    public void Restack(nint dock)
    {
        if (_shown)
            Native.SetWindowPos(_source.Handle, dock, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);
    }

    public void Hide()
    {
        if (!_shown)
            return;
        _shown = false;
        Native.ShowWindow(_source.Handle, 0); // SW_HIDE
    }

    public void Dispose() => _source.Dispose();

    /// <summary>Settings › Personalization › Colors › Transparency effects.</summary>
    internal static bool TransparencyOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int Size;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(nint window, ref CompositionAttributeData data);

    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
