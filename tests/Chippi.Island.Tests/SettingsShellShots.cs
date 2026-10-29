// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Config;
using Chippi.UI.Themes;
using static Chippi.Island.Tests.SettingsShellTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders the Settings shell off screen in the states the settings-shell mockups (s1-s5) show.
/// Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class SettingsShellShots
{
    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            try
            {
                foreach (var (theme, name) in new[] { (ThemePreference.Dark, "dark"), (ThemePreference.Light, "light") })
                {
                    ThemeManager.Apply(theme);
                    using var seven = new SettingsScene(Seven);
                    Save(seven.Show("general"), Path.Combine(folder, $"s1-{name}.png"));
                }
                ThemeManager.Apply(ThemePreference.Dark);

                using (var all = new SettingsScene(All))
                {
                    var window = all.Show("about", height: 600);
                    Save(window, Path.Combine(folder, "s3-about.png"));
                    window.Width = 1800;
                    window.Height = 900;
                    window.UpdateLayout();
                    Save(window, Path.Combine(folder, "s4-wide.png"));
                }

                using (var rail = new SettingsScene(Seven))
                {
                    var window = rail.Show("general", width: 900, height: 640);
                    Save(window, Path.Combine(folder, "s5-rail.png"));
                    var menu = (Button)((DockPanel)window.Rail).Children[0];
                    menu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    window.UpdateLayout();
                    Save(window, Path.Combine(folder, "s5-open.png"));
                }
            }
            finally
            {
                ThemeManager.Apply(ThemePreference.Dark);
            }
        });
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var pixels = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        pixels.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pixels));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
