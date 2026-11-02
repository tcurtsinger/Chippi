// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.CommandBar.Search;

/// <summary>One page of Windows Settings: its name as Settings shows it, more words to find it by, and its ms-settings: address.</summary>
public sealed record SettingsPage(string Name, string Words, string Uri);

/// <summary>The Windows 11 Settings pages the Command bar finds ("Bluetooth & devices", "Display", "Default apps").</summary>
public static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> All { get; } =
    [
        // System
        new("Display", "screen monitor resolution brightness scale hdr refresh", "ms-settings:display"),
        new("Night light", "blue light warm", "ms-settings:nightlight"),
        new("Sound", "audio speakers output input microphone", "ms-settings:sound"),
        new("Volume mixer", "app volume sound", "ms-settings:apps-volume"),
        new("Notifications", "do not disturb alerts", "ms-settings:notifications"),
        new("Power & battery", "sleep power mode battery saver", "ms-settings:powersleep"),
        new("Storage", "disk space storage sense cleanup drives", "ms-settings:storagesense"),
        new("Nearby sharing", "share", "ms-settings:crossdevice"),
        new("Multitasking", "snap windows alt tab desktops", "ms-settings:multitasking"),
        new("Activation", "product key license", "ms-settings:activation"),
        new("Troubleshoot", "fix problems", "ms-settings:troubleshoot"),
        new("Recovery", "reset this pc restart advanced startup", "ms-settings:recovery"),
        new("Projecting to this PC", "miracast wireless display", "ms-settings:project"),
        new("Remote Desktop", "rdp", "ms-settings:remotedesktop"),
        new("Clipboard", "clipboard history", "ms-settings:clipboard"),
        new("About", "pc name device specifications system info windows version", "ms-settings:about"),
        new("Optional features", "add features", "ms-settings:optionalfeatures"),
        // Bluetooth & devices
        new("Bluetooth & devices", "bluetooth pair devices", "ms-settings:bluetooth"),
        new("Printers & scanners", "printer scanner print", "ms-settings:printers"),
        new("Mouse", "pointer cursor scroll", "ms-settings:mousetouchpad"),
        new("Touchpad", "gestures trackpad", "ms-settings:devices-touchpad"),
        new("Pen & Windows Ink", "pen stylus", "ms-settings:pen"),
        new("AutoPlay", "autoplay removable", "ms-settings:autoplay"),
        new("USB", "usb notifications", "ms-settings:usb"),
        new("Cameras", "webcam camera", "ms-settings:camera"),
        new("Mobile devices", "phone link android", "ms-settings:mobile-devices"),
        // Network & internet
        new("Network & internet", "network internet connection", "ms-settings:network"),
        new("Wi-Fi", "wifi wireless network", "ms-settings:network-wifi"),
        new("Ethernet", "wired network cable", "ms-settings:network-ethernet"),
        new("VPN", "vpn private network", "ms-settings:network-vpn"),
        new("Mobile hotspot", "hotspot tethering share connection", "ms-settings:network-mobilehotspot"),
        new("Airplane mode", "flight mode", "ms-settings:network-airplanemode"),
        new("Proxy", "proxy server", "ms-settings:network-proxy"),
        new("Advanced network settings", "adapters network reset", "ms-settings:network-advancedsettings"),
        // Personalization
        new("Personalization", "personalize appearance", "ms-settings:personalization"),
        new("Background", "wallpaper desktop picture", "ms-settings:personalization-background"),
        new("Colors", "dark mode light mode accent color theme transparency", "ms-settings:colors"),
        new("Themes", "theme", "ms-settings:themes"),
        new("Lock screen", "lock screen picture", "ms-settings:lockscreen"),
        new("Start", "start menu", "ms-settings:personalization-start"),
        new("Taskbar", "taskbar icons system tray", "ms-settings:taskbar"),
        new("Fonts", "font typeface", "ms-settings:fonts"),
        new("Text input", "touch keyboard emoji", "ms-settings:personalization-textinput"),
        // Apps
        new("Installed apps", "uninstall programs apps features", "ms-settings:appsfeatures"),
        new("Default apps", "default browser open with file types", "ms-settings:defaultapps"),
        new("Startup apps", "startup login", "ms-settings:startupapps"),
        new("Offline maps", "maps", "ms-settings:maps"),
        new("Apps for websites", "links", "ms-settings:appsforwebsites"),
        new("Video playback", "video hdr", "ms-settings:videoplayback"),
        // Accounts
        new("Your info", "account microsoft account picture", "ms-settings:yourinfo"),
        new("Email & accounts", "email accounts", "ms-settings:emailandaccounts"),
        new("Sign-in options", "password pin windows hello fingerprint face", "ms-settings:signinoptions"),
        new("Family", "family kids", "ms-settings:family-group"),
        new("Other users", "users accounts", "ms-settings:otherusers"),
        new("Windows Backup", "backup onedrive", "ms-settings:backup"),
        new("Access work or school", "work school organization", "ms-settings:workplace"),
        // Time & language
        new("Date & time", "clock time zone", "ms-settings:dateandtime"),
        new("Language & region", "language region keyboard layout format", "ms-settings:regionlanguage"),
        new("Typing", "autocorrect spelling suggestions", "ms-settings:typing"),
        new("Speech", "voice speech recognition", "ms-settings:speech"),
        // Gaming
        new("Game Mode", "gaming games", "ms-settings:gaming-gamemode"),
        new("Game Bar", "xbox game bar gaming", "ms-settings:gaming-gamebar"),
        new("Captures", "game recording clips", "ms-settings:gaming-gamedvr"),
        // Accessibility
        new("Accessibility", "ease of access", "ms-settings:easeofaccess"),
        new("Text size", "bigger text font size", "ms-settings:easeofaccess-display"),
        new("Visual effects", "animation effects transparency scrollbars", "ms-settings:easeofaccess-visualeffects"),
        new("Mouse pointer and touch", "cursor size pointer color", "ms-settings:easeofaccess-mousepointer"),
        new("Text cursor", "caret indicator", "ms-settings:easeofaccess-cursor"),
        new("Magnifier", "zoom", "ms-settings:easeofaccess-magnifier"),
        new("Color filters", "colorblind grayscale", "ms-settings:easeofaccess-colorfilter"),
        new("Contrast themes", "high contrast", "ms-settings:easeofaccess-highcontrast"),
        new("Narrator", "screen reader", "ms-settings:easeofaccess-narrator"),
        new("Captions", "subtitles live captions", "ms-settings:easeofaccess-closedcaptioning"),
        new("Keyboard", "sticky keys on-screen keyboard filter keys", "ms-settings:easeofaccess-keyboard"),
        // Privacy & security
        new("Privacy & security", "privacy permissions", "ms-settings:privacy"),
        new("Windows Security", "defender antivirus firewall", "ms-settings:windowsdefender"),
        new("Find my device", "lost", "ms-settings:findmydevice"),
        new("Device encryption", "bitlocker", "ms-settings:deviceencryption"),
        new("For developers", "developer mode", "ms-settings:developers"),
        new("Location", "gps location privacy", "ms-settings:privacy-location"),
        new("Camera privacy", "webcam permissions", "ms-settings:privacy-webcam"),
        new("Microphone privacy", "mic permissions", "ms-settings:privacy-microphone"),
        new("Diagnostics & feedback", "telemetry", "ms-settings:privacy-feedback"),
        new("Activity history", "timeline", "ms-settings:privacy-activityhistory"),
        new("Search permissions", "safe search", "ms-settings:search-permissions"),
        new("Searching Windows", "indexing index search locations", "ms-settings:cortana-windowssearch"),
        // Windows Update
        new("Windows Update", "updates patches", "ms-settings:windowsupdate"),
        new("Update history", "updates installed", "ms-settings:windowsupdate-history"),
        new("Advanced options", "windows update active hours", "ms-settings:windowsupdate-options"),
        new("Delivery Optimization", "update bandwidth", "ms-settings:delivery-optimization"),
        new("Windows Insider Program", "insider preview", "ms-settings:windowsinsider"),
    ];
}
