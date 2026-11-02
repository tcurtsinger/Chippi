// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>Dark mode, hidden files, desktop icons and taskbar auto-hide: what Windows' own switches change, and how they tell the shell.</summary>
internal static class ShellToggles
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>The folder view's Refresh (F5).</summary>
    private const nuint RefreshCommand = 0x7103;

    /// <summary>The desktop menu's View › Show desktop icons.</summary>
    private const nuint ShowIconsCommand = 0x7402;

    /// <summary>Apps and Windows both dark.</summary>
    public static bool Dark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Personalize);
            return Dword(key, "AppsUseLightTheme", 1) == 0 && Dword(key, "SystemUsesLightTheme", 1) == 0;
        }
    }

    public static void SetDark(bool on)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Personalize, writable: true))
        {
            key.SetValue("AppsUseLightTheme", on ? 0 : 1, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", on ? 0 : 1, RegistryValueKind.DWord);
        }
        Broadcast("ImmersiveColorSet");
    }

    /// <summary>Hidden files and file extensions both shown.</summary>
    public static bool Hidden
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Advanced);
            return Dword(key, "Hidden", 2) == 1 && Dword(key, "HideFileExt", 1) == 0;
        }
    }

    public static void SetHidden(bool on)
    {
        // The shell's setter tells every process its cached settings changed; a folder re-reads them when it refreshes.
        var state = new SHELLSTATEA { fShowAllObjects = on, fShowExtensions = on };
        PInvoke.SHGetSetSettings(ref state, SSF_MASK.SSF_SHOWALLOBJECTS | SSF_MASK.SSF_SHOWEXTENSIONS, true);
        using (var key = Registry.CurrentUser.CreateSubKey(Advanced, writable: true))
        {
            key.SetValue("Hidden", on ? 1 : 2, RegistryValueKind.DWord);
            key.SetValue("HideFileExt", on ? 0 : 1, RegistryValueKind.DWord);
        }
        Broadcast("ShellState");
        foreach (var view in FolderViews())
            PInvoke.PostMessage(view, PInvoke.WM_COMMAND, RefreshCommand, 0);
    }

    /// <summary>Whether the desktop shows its icons: its list is visible, or, with no desktop to look at, the saved setting.</summary>
    public static bool Icons
    {
        get
        {
            var view = Win32Windows.DesktopView();
            var list = view.IsNull ? HWND.Null : PInvoke.FindWindowEx(view, HWND.Null, "SysListView32", null);
            if (!list.IsNull)
                return PInvoke.IsWindowVisible(list);
            using var key = Registry.CurrentUser.OpenSubKey(Advanced);
            return Dword(key, "HideIcons", 0) == 0;
        }
    }

    public static void SetIcons(bool on)
    {
        if (Icons == on)
            return;
        var view = Win32Windows.DesktopView();
        if (view.IsNull)
            throw new InvalidOperationException("Desktop icons failed: the desktop wasn't found");
        if (!PInvoke.PostMessage(view, PInvoke.WM_COMMAND, ShowIconsCommand, 0))
            throw new Win32Exception();
    }

    /// <summary>Whether the taskbar hides itself, or null when the taskbar isn't there or isn't responding.</summary>
    public static unsafe bool? AutoHide
    {
        get
        {
            var taskbar = Win32Windows.Taskbar;
            if (taskbar.IsNull || PInvoke.IsHungAppWindow(taskbar))
                return null;
            var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA) };
            return (PInvoke.SHAppBarMessage(PInvoke.ABM_GETSTATE, ref data) & PInvoke.ABS_AUTOHIDE) != 0;
        }
    }

    public static unsafe void SetAutoHide(bool on)
    {
        var taskbar = Win32Windows.Taskbar;
        if (taskbar.IsNull || PInvoke.IsHungAppWindow(taskbar))
            throw new InvalidOperationException("Taskbar auto-hide failed: the taskbar isn't responding");
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = taskbar };
        uint state = (uint)PInvoke.SHAppBarMessage(PInvoke.ABM_GETSTATE, ref data);
        state = on ? state | PInvoke.ABS_AUTOHIDE : state & ~PInvoke.ABS_AUTOHIDE;
        data.lParam = (nint)state;
        PInvoke.SHAppBarMessage(PInvoke.ABM_SETSTATE, ref data);
    }

    /// <summary>Tells every window a setting changed, skipping any that's hung instead of waiting on it.</summary>
    private static unsafe void Broadcast(string area)
    {
        fixed (char* text = area)
        {
            PInvoke.SendMessageTimeout(HWND.HWND_BROADCAST, PInvoke.WM_SETTINGCHANGE, 0, (nint)text,
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 1000, out _);
        }
    }

    /// <summary>The desktop's folder view and every open folder's (one per tab).</summary>
    private static List<HWND> FolderViews()
    {
        var views = new List<HWND>();
        var desktop = Win32Windows.DesktopView();
        if (!desktop.IsNull)
            views.Add(desktop);
        foreach (var folder in Win32Windows.TopLevel("CabinetWClass"))
        {
            PInvoke.EnumChildWindows(folder, (child, _) =>
            {
                if (Win32Windows.ClassIs(child, "SHELLDLL_DefView"))
                    views.Add(child);
                return true;
            }, default);
        }
        return views;
    }

    private static int Dword(RegistryKey? key, string name, int fallback) => key?.GetValue(name) is int value ? value : fallback;
}
