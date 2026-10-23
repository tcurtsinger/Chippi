// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Chippi.UI.Themes;
using Windows.UI.ViewManagement;

namespace Chippi.App.Shell;

/// <summary>
/// Resolves the Accent setting into shades for <see cref="ThemeManager.SetAccent"/> (DESIGN.md §2).
/// Follow Windows reads the Windows accent's AccentLight2 (dark theme) and AccentDark1 (light) and
/// follows it live. Re-applies whenever the "app" settings section is saved.
/// </summary>
internal sealed class AccentSource
{
    private readonly ConfigStore _config;
    private readonly Dispatcher _dispatcher;
    private readonly UISettings? _windows;

    public AccentSource(ConfigStore config, Dispatcher dispatcher)
    {
        _config = config;
        _dispatcher = dispatcher;
        try
        {
            _windows = new UISettings();
            // Raised on a thread-pool thread (R0 spike), so always hop to the UI thread.
            _windows.ColorValuesChanged += (_, _) => _dispatcher.BeginInvoke(Apply);
        }
        catch (Exception ex)
        {
            Log.Warn("Can't read the Windows accent; Follow Windows uses Blue", ex);
        }
        config.SectionChanged += (_, e) =>
        {
            if (e.Name == AppSettings.SectionName)
                _dispatcher.BeginInvoke(Apply);
        };
    }

    public void Apply()
    {
        var choice = _config.GetSection<AppSettings>(AppSettings.SectionName).Accent;
        var (dark, light) = ThemeManager.NamedAccent(choice) ?? WindowsAccent() ?? ThemeManager.NamedAccent(AccentChoice.Blue).GetValueOrDefault();
        ThemeManager.SetAccent(dark, light);
    }

    private (Color Dark, Color Light)? WindowsAccent()
    {
        if (_windows is null)
            return null;
        try
        {
            return (ToColor(_windows.GetColorValue(UIColorType.AccentLight2)), ToColor(_windows.GetColorValue(UIColorType.AccentDark1)));
        }
        catch (Exception ex)
        {
            Log.Warn("Reading the Windows accent failed; using Blue", ex);
            return null;
        }
    }

    private static Color ToColor(Windows.UI.Color c) => Color.FromRgb(c.R, c.G, c.B);
}
