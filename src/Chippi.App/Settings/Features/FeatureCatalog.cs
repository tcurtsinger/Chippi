// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.App.Settings.Features;

/// <summary>
/// "Coming soon" entries for the PLAN.md features of Phases 2 to 7, so the Features hub shows the
/// whole suite. Each is a descriptor with no module: listed, never installable. When a feature
/// ships, delete its entry here and register the real module under the same id.
/// Preset membership is a draft for Travis to approve. Keep awake is not listed: WS4 registers
/// the real module under "keep-awake".
/// </summary>
public static class FeatureCatalog
{
    private const PresetMembership Essentials = PresetMembership.Essentials;
    private const PresetMembership Audio = PresetMembership.Audio;
    private const PresetMembership PowerUser = PresetMembership.PowerUser;

    public static IReadOnlyList<FeatureDescriptor> Placeholders { get; } =
    [
        // Phase 3: Sound
        new("volume-boost", "Volume boost", "App volume past 100%", FeatureGroup.Sound)
        {
            Icon = "Volume",
            Presets = Audio,
        },

        // System (System monitor is a real module since Phase 4: Chippi.Features.SystemMonitor)
        new("temperatures", "Temperatures", "CPU and GPU temperatures with alerts when they run hot", FeatureGroup.System)
        {
            Icon = "Cpu",
            Requires = [Capability.Admin],
            Badges = [FeatureBadge.Needs("Admin for temps")],
            Presets = PowerUser,
        },
        new("fan-control", "Fan control", "Fan curves for boards and laptops that expose them", FeatureGroup.System)
        {
            Icon = "Wrench",
            Requires = [Capability.Admin],
            Badges = [FeatureBadge.Needs("Admin for fans")],
        },
        new("displays", "Displays and brightness", "Brightness for every monitor, external ones included", FeatureGroup.System)
        {
            Icon = "Sun",
            Presets = Essentials | PowerUser,
        },
        new("quick-toggles", "Quick toggles", "Dark mode, hidden files, desktop icons and more in one click", FeatureGroup.System)
        {
            Icon = "Power",
            Presets = PowerUser,
        },
        new("cleaning-mode", "Cleaning mode", "Locks the keyboard and mouse for a minute so you can wipe them", FeatureGroup.System)
        {
            Icon = "Droplet",
            Requires = [Capability.InputHooks],
        },
        new("agent-usage", "AI agent usage", "Your coding agents' usage and reset times in the island", FeatureGroup.System)
        {
            Icon = "Activity",
            Presets = PowerUser,
        },
        new("port-manager", "Port manager", "Shows which app holds a network port and frees it", FeatureGroup.System)
        {
            Icon = "Monitor",
            Presets = PowerUser,
        },

        // Clipboard and text (Phase 5's five features are real modules: Chippi.Features.Clipboard, PlainPaste, Scratchpad, Shelf)
        new("text-snippets", "Text snippets", "Type a short abbreviation and it expands into saved text", FeatureGroup.ClipboardText)
        {
            Icon = "Keyboard",
            Requires = [Capability.InputHooks],
            Presets = PowerUser,
        },

        // Phase 5.5 and 6: Windows and input
        new("command-bar", "Command bar", "Type to launch apps, run commands and reach any Chippi feature", FeatureGroup.Windows)
        {
            Icon = "Search",
            Badges = [FeatureBadge.Overlap("Overlaps PowerToys Run")],
            Presets = PowerUser,
        },
        new("radial-menu", "Radial menu", "Hold a mouse button for a ring of actions under the pointer", FeatureGroup.Windows)
        {
            Icon = "Zap",
            Requires = [Capability.InputHooks],
            Presets = PowerUser,
        },
        new("window-layouts", "Window layouts", "Snap windows into saved layouts with a shortcut or a drag", FeatureGroup.Windows)
        {
            Icon = "LayoutGrid",
            Badges = [FeatureBadge.Overlap("Overlaps FancyZones")],
            Presets = PowerUser,
        },
        new("app-switcher", "App switcher", "Switch apps with large live previews", FeatureGroup.Windows)
        {
            Icon = "AppWindow",
            Badges = [FeatureBadge.Overlap("Overlaps Alt+Tab")],
            Presets = PowerUser,
        },
        new("close-protection", "Close protection", "Hold Alt+F4 to close, so a slip never loses your work", FeatureGroup.Windows)
        {
            Icon = "Shield",
            Requires = [Capability.InputHooks],
            Presets = PowerUser,
        },
        new("focus-follows-mouse", "Focus follows mouse", "Activates the window under the pointer", FeatureGroup.Windows)
        {
            Icon = "Eye",
            Requires = [Capability.InputHooks],
            Badges = [FeatureBadge.Overlap("Overlaps a Windows setting")],
        },
        new("smooth-scrolling", "Smooth scrolling", "Smooth or linear wheel scrolling for mice that step", FeatureGroup.Windows)
        {
            Icon = "Expand",
            Requires = [Capability.InputHooks],
            Presets = PowerUser,
        },
        new("mouse-buttons", "Mouse button shortcuts", "Maps extra mouse buttons to actions and shortcuts", FeatureGroup.Windows)
        {
            Icon = "Sliders",
            Requires = [Capability.InputHooks],
            Presets = PowerUser,
        },
        new("debounce", "Click and key debounce", "Ignores the double clicks and key chatter of worn switches", FeatureGroup.Windows)
        {
            Icon = "Keyboard",
            Requires = [Capability.InputHooks],
            Badges = [FeatureBadge.Overlap("Overlaps Filter Keys")],
        },
        new("super-key", "Super key", "Turns Caps Lock into a modifier for your own shortcuts", FeatureGroup.Windows)
        {
            Icon = "Keyboard",
            Requires = [Capability.InputHooks],
            Badges = [FeatureBadge.Overlap("Overlaps PowerToys")],
            Presets = PowerUser,
        },
        new("shortcut-manager", "Shortcut manager", "Every shortcut Chippi and your apps use, with clashes flagged", FeatureGroup.Windows)
        {
            Icon = "Keyboard",
            Badges = [FeatureBadge.Overlap("Overlaps PowerToys")],
            Presets = PowerUser,
        },

        // Phase 7: Capture
        new("screen-capture", "Screen capture", "Capture a region, window or screen, then mark it up", FeatureGroup.Capture)
        {
            Icon = "Camera",
            Requires = [Capability.ScreenCapture],
            Badges = [FeatureBadge.Overlap("Overlaps Snipping Tool")],
            Presets = PowerUser,
        },
        new("screen-recording", "Screen recording", "Record a region or window to video or GIF", FeatureGroup.Capture)
        {
            Icon = "Video",
            Requires = [Capability.ScreenCapture],
            Badges = [FeatureBadge.Overlap("Overlaps Snipping Tool")],
            Presets = PowerUser,
        },
        new("screen-text", "Text and QR from screen", "Copies text or reads a QR code from anything on screen", FeatureGroup.Capture)
        {
            Icon = "Eye",
            Requires = [Capability.ScreenCapture],
            Badges = [FeatureBadge.Overlap("Overlaps PowerToys")],
            Presets = PowerUser,
        },
        new("color-picker", "Color picker", "Picks a color from anywhere on screen and copies it", FeatureGroup.Capture)
        {
            Icon = "Droplet",
            Requires = [Capability.ScreenCapture],
            Badges = [FeatureBadge.Overlap("Overlaps PowerToys")],
            Presets = PowerUser,
        },
        new("media-tools", "Media tools", "Quick convert, resize and compress for images and video", FeatureGroup.Capture)
        {
            Icon = "Video",
        },

        // Phase 7: Apps
        new("app-updates", "App updates", "Finds and installs app updates through winget", FeatureGroup.Apps)
        {
            Icon = "Download",
            Requires = [Capability.Admin],
            Badges = [FeatureBadge.Needs("Admin for some apps")],
            Presets = PowerUser,
        },
        new("uninstaller", "Uninstaller", "Removes apps along with the leftovers they leave behind", FeatureGroup.Apps)
        {
            Icon = "Trash",
            Requires = [Capability.Admin],
            Badges = [FeatureBadge.Needs("Admin for some apps")],
            Presets = PowerUser,
        },
        new("cleaner", "Cleaner", "Clears temp files, caches and other space hogs", FeatureGroup.Apps)
        {
            Icon = "Trash",
            Badges = [FeatureBadge.Overlap("Overlaps Storage Sense")],
        },
    ];

    /// <summary>Registers every placeholder whose id no real module has claimed. Call before the registry starts.</summary>
    public static void RegisterPlaceholders(ModuleRegistry registry)
    {
        foreach (var descriptor in Placeholders)
        {
            if (registry.Find(descriptor.Id) is null)
                registry.RegisterComingSoon(descriptor);
        }
    }
}
