// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.Core.Config;
using Microsoft.Win32;

namespace Chippi.App.Themes;

/// <summary>
/// Swaps the Dark/Light dictionary in Application resources (ported from Hopper) and adds
/// "Follow Windows": with <see cref="ThemePreference.System"/> it reads the Windows app theme and
/// re-applies when the user changes it. Palette.xaml (island, pet, badges) is never swapped.
/// </summary>
public static class ThemeManager
{
    private const string DarkSource = "pack://application:,,,/Themes/Dark.xaml";
    private const string LightSource = "pack://application:,,,/Themes/Light.xaml";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool _listening;

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
            ThemeChanged?.Invoke(null, EventArgs.Empty);
            return;
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = source });
        IsLight = light;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
