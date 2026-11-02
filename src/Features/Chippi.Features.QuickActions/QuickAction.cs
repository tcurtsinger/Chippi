// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions;

/// <summary>Each Quick action, in the order the page and Settings list them (docs/phase-5.5/quick-actions.md).</summary>
public enum QuickActionId { Dark, Hidden, Icons, AutoHide, Extend, Bluetooth, WiFi, Camera, Plan, Lock, Displays, ScreenSaver, Recycle, Eject, Quit, Frozen, Explorer, Power, Cleaning }

/// <summary>One Quick action.</summary>
/// <param name="Key">Its name in settings ("dark", "auto-hide"); never changes.</param>
/// <param name="Label">The island's one word.</param>
/// <param name="Name">Its full name, in Settings.</param>
/// <param name="Toggle">An on/off toggle (Settings' Toggles section, Plan included); otherwise an action.</param>
public sealed record QuickAction(QuickActionId Id, string Key, string Label, string Name, bool Toggle, Geometry Icon)
{
    /// <summary>The confirm's question and verb, for an action that asks first ("Empty Recycle Bin?", Empty).</summary>
    public (string Question, string Verb)? Confirm { get; init; }

    /// <summary>The list it steps into (Plan, Eject, Quit, Power); Eject's only with two or more drives.</summary>
    public bool OpensList { get; init; }

    public static IReadOnlyList<QuickAction> All { get; } =
    [
        new(QuickActionId.Dark, "dark", "Dark", "Dark mode", true, LucideIcons.Moon),
        new(QuickActionId.Hidden, "hidden", "Hidden", "Hidden files", true, LucideIcons.Eye),
        new(QuickActionId.Icons, "icons", "Icons", "Desktop icons", true, LucideIcons.LayoutGrid),
        new(QuickActionId.AutoHide, "auto-hide", "Auto-hide", "Taskbar auto-hide", true, LucideIcons.PanelBottom),
        new(QuickActionId.Extend, "extend", "Extend", "Second screen", true, LucideIcons.Screens),
        new(QuickActionId.Bluetooth, "bluetooth", "Bluetooth", "Bluetooth", true, LucideIcons.Bluetooth),
        new(QuickActionId.WiFi, "wifi", "Wi-Fi", "Wi-Fi", true, LucideIcons.Wifi),
        new(QuickActionId.Camera, "camera", "Camera", "Camera", true, LucideIcons.Camera),
        new(QuickActionId.Plan, "plan", "Plan", "Power plan", true, LucideIcons.Gauge) { OpensList = true },
        new(QuickActionId.Lock, "lock", "Lock", "Lock", false, LucideIcons.Lock),
        new(QuickActionId.Displays, "displays", "Displays", "Turn off displays", false, LucideIcons.MonitorOff),
        new(QuickActionId.ScreenSaver, "screensaver", "Screensaver", "Screen saver", false, LucideIcons.ScreenSaver),
        new(QuickActionId.Recycle, "recycle", "Recycle", "Empty Recycle Bin", false, LucideIcons.Trash) { Confirm = ("Empty Recycle Bin?", "Empty") },
        new(QuickActionId.Eject, "eject", "Eject", "Eject", false, LucideIcons.Eject) { OpensList = true },
        new(QuickActionId.Quit, "quit", "Quit", "Quit apps", false, LucideIcons.SquareX) { OpensList = true },
        new(QuickActionId.Frozen, "frozen", "Frozen", "End frozen apps", false, LucideIcons.Snowflake) { Confirm = ("End frozen apps?", "End") },
        new(QuickActionId.Explorer, "explorer", "Explorer", "Restart Explorer", false, LucideIcons.RefreshCw) { Confirm = ("Restart Explorer?", "Restart") },
        new(QuickActionId.Power, "power", "Power", "Power", false, LucideIcons.Power) { OpensList = true },
        new(QuickActionId.Cleaning, "cleaning", "Cleaning", "Cleaning mode", false, LucideIcons.Droplet),
    ];

    public static QuickAction Of(QuickActionId id) => All[(int)id];

    public static QuickAction? FromKey(string key) => All.FirstOrDefault(a => a.Key == key);
}
