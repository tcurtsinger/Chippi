// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>One entry of an app's own menu, read from its classic Win32 menu bar.</summary>
public sealed record AppMenuEntry(string Text, string? Shortcut, uint Id, bool Enabled, bool Checked, bool Separator, nint SubMenu);

/// <summary>
/// The app's real menus (docs/mac-style/brief.md, Menu bar): a classic menu bar read with <c>GetMenu</c> and run with
/// <c>WM_COMMAND</c>, as if chosen in the app. Most modern apps have none, and then the bar shows nothing in their place.
/// </summary>
internal static class AppMenus
{
    /// <summary>The window's top-level menu titles (File, Edit…), empty when it has no classic menu bar.</summary>
    public static IReadOnlyList<AppMenuEntry> Titles(nint window)
    {
        if (window == 0 || !Native.IsWindow(window))
            return [];
        nint menu = Native.GetMenu(window);
        if (menu == 0 || !Native.IsMenu(menu))
            return [];
        return Read(menu).Where(e => !e.Separator && e.Text.Length > 0).ToArray();
    }

    /// <summary>
    /// The entries of a submenu, after telling the app it's about to open (<c>WM_INITMENUPOPUP</c>), so it can grey out and
    /// tick its entries as it would for its own menu. Waits at most 150 ms on a busy app.
    /// </summary>
    public static IReadOnlyList<AppMenuEntry> Open(nint window, nint subMenu, int position)
    {
        if (window == 0 || subMenu == 0 || !Native.IsMenu(subMenu))
            return [];
        const uint AbortIfHung = 0x2;
        Native.SendMessageTimeout(window, Native.WmInitMenuPopup, subMenu, position, AbortIfHung, 150, out _);
        return Read(subMenu);
    }

    /// <summary>Runs an entry in the app, as its own menu would.</summary>
    public static bool Invoke(nint window, uint id) =>
        window != 0 && Native.IsWindow(window) && Native.PostMessage(window, Native.WmCommand, (nint)(id & 0xFFFF), 0);

    /// <summary>Tells the app its menu bar is about to be used (<c>WM_INITMENU</c>), as a click on it would.</summary>
    public static void Begin(nint window)
    {
        nint menu = Native.GetMenu(window);
        if (menu != 0)
            Native.SendMessageTimeout(window, Native.WmInitMenu, menu, 0, 0x2, 150, out _);
    }

    private static List<AppMenuEntry> Read(nint menu)
    {
        var entries = new List<AppMenuEntry>();
        int count = Native.GetMenuItemCount(menu);
        for (int i = 0; i < count && i < 200; i++)
        {
            var info = new Native.MenuItemInfo
            {
                Size = Marshal.SizeOf<Native.MenuItemInfo>(),
                Mask = Native.MiimFType | Native.MiimState | Native.MiimId | Native.MiimSubmenu | Native.MiimString,
            };
            if (!Native.GetMenuItemInfo(menu, (uint)i, true, ref info))
                continue;
            if ((info.Type & Native.MftSeparator) != 0)
            {
                entries.Add(new AppMenuEntry("", null, 0, false, false, true, 0));
                continue;
            }
            string text = "";
            if ((info.Type & Native.MftOwnerDraw) == 0 && info.Length > 0)
            {
                var buffer = Marshal.AllocHGlobal(((int)info.Length + 1) * 2);
                try
                {
                    info.TypeData = buffer;
                    info.Length++;
                    info.Mask = Native.MiimString;
                    if (Native.GetMenuItemInfo(menu, (uint)i, true, ref info))
                        text = Marshal.PtrToStringUni(buffer) ?? "";
                }
                finally { Marshal.FreeHGlobal(buffer); }
                info.Mask = Native.MiimFType | Native.MiimState | Native.MiimId | Native.MiimSubmenu;
                Native.GetMenuItemInfo(menu, (uint)i, true, ref info);
            }
            var (label, shortcut) = Label(text);
            entries.Add(new AppMenuEntry(label, shortcut, info.Id, (info.State & Native.MfsDisabled) == 0,
                (info.State & Native.MfsChecked) != 0, false, info.SubMenu));
        }
        // No separator first, last or twice.
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].Separator && (i == 0 || i == entries.Count - 1 || entries[i - 1].Separator))
                entries.RemoveAt(i);
        return entries;
    }

    /// <summary>"&amp;Save\tCtrl+S" as ("Save", "Ctrl+S"): the mnemonic's &amp; gone ("&amp;&amp;" is a real one), the tab-split shortcut apart.</summary>
    public static (string Label, string? Shortcut) Label(string text)
    {
        int tab = text.IndexOf('\t', StringComparison.Ordinal);
        string label = tab >= 0 ? text[..tab] : text;
        string? shortcut = tab >= 0 ? text[(tab + 1)..].Trim() : null;
        var clean = new System.Text.StringBuilder(label.Length);
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] == '&')
            {
                if (i + 1 < label.Length && label[i + 1] == '&') { clean.Append('&'); i++; }
                continue;
            }
            clean.Append(label[i]);
        }
        return (clean.ToString().Trim(), string.IsNullOrEmpty(shortcut) ? null : shortcut);
    }
}
