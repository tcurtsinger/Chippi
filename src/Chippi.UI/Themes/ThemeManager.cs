// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using Chippi.Core.Config;
using Microsoft.Win32;

namespace Chippi.UI.Themes;

/// <summary>
/// Swaps the Dark/Light dictionary in Application resources (ported from Hopper) and adds
/// "Follow Windows": with <see cref="ThemePreference.System"/> it reads the Windows app theme and
/// re-applies when the user changes it. Palette.xaml (island, pet) is never swapped. The accent
/// is a dark and a light shade (DESIGN.md §2); the theme picks which one shows.
/// </summary>
public static class ThemeManager
{
    private const string DarkSource = "pack://application:,,,/Chippi.UI;component/Themes/Dark.xaml";
    private const string LightSource = "pack://application:,,,/Chippi.UI;component/Themes/Light.xaml";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool _listening;
    private static (Color Dark, Color Light)? _accent;

    /// <summary>The named accents' shades (DESIGN.md §2); null for Follow Windows, which the app reads from Windows.</summary>
    public static (Color Dark, Color Light)? NamedAccent(AccentChoice choice) => choice switch
    {
        AccentChoice.Blue => (Rgb(0x4CC2FF), Rgb(0x005FB8)),
        AccentChoice.Violet => (Rgb(0xB79CFF), Rgb(0x6B4FBB)),
        AccentChoice.Green => (Rgb(0x6CCB5F), Rgb(0x107C10)),
        AccentChoice.Pink => (Rgb(0xFF99A4), Rgb(0xB4235A)),
        _ => null,
    };

    /// <summary>
    /// Sets AccentBrush and OnAccentBrush for the showing theme, and IslandAccentBrush (always the
    /// dark shade). Written at the application level, so they win over the theme dictionaries' defaults.
    /// </summary>
    public static void SetAccent(Color dark, Color light)
    {
        _accent = (dark, light);
        ApplyAccent();
    }

    public static ThemePreference Mode { get; private set; } = ThemePreference.Dark;

    /// <summary>The theme actually showing, after resolving <see cref="ThemePreference.System"/>.</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Raised on the UI thread after the effective theme changed.</summary>
    public static event EventHandler? ThemeChanged;

    public static void Apply(ThemePreference mode)
    {
        Mode = mode;
        if (mode == ThemePreference.System && !_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        else if (mode != ThemePreference.System && _listening)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _listening = false;
        }
        ApplyEffective();
    }

    /// <summary>True when Windows is set to the light app theme. Defaults to dark if the value is missing.</summary>
    public static bool WindowsUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Application.Current?.Dispatcher.BeginInvoke(ApplyEffective);
    }

    private static void ApplyEffective()
    {
        bool light = Mode switch
        {
            ThemePreference.Light => true,
            ThemePreference.System => WindowsUsesLightTheme(),
            _ => false,
        };
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var source = new Uri(light ? LightSource : DarkSource, UriKind.Absolute);

        for (int i = 0; i < dictionaries.Count; i++)
        {
            var uri = dictionaries[i].Source?.OriginalString;
            if (uri is null ||
                !(uri.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) || uri.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (uri.EndsWith(light ? "Light.xaml" : "Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                IsLight = light;
                return; // already showing
            }
            dictionaries[i] = new ResourceDictionary { Source = source };
            IsLight = light;
            ApplyAccent();
            ThemeChanged?.Invoke(null, EventArgs.Empty);
            return;
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = source });
        IsLight = light;
        ApplyAccent();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyAccent()
    {
        if (_accent is not { } accent || Application.Current is null)
            return;
        var resources = Application.Current.Resources;
        resources["AccentBrush"] = Frozen(IsLight ? accent.Light : accent.Dark);
        resources["OnAccentBrush"] = Frozen(IsLight ? Colors.White : Colors.Black);
        resources["IslandAccentBrush"] = Frozen(accent.Dark);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
