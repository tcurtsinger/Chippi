// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>Shortcuts: an honest empty state until a feature with shortcuts ships.</summary>
internal sealed class ShortcutsPage : UserControl
{
    public ShortcutsPage()
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Shortcuts", "Keyboard shortcuts for Chippi's features."));
        page.Children.Add(EmptyState.Create(LucideIcons.Keyboard, "No shortcuts yet",
            "Features that use keyboard shortcuts list them here, so you can change them in one place."));
        Content = page;
    }
}

/// <summary>Appearance: the theme lives in General (it is one row); this page says so instead of holding a copy.</summary>
internal sealed class AppearancePage : UserControl
{
    public AppearancePage(Action openGeneral, Action openIsland)
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Appearance", "How Chippi looks."));
        var general = SettingsUi.Button("Open General");
        general.Click += (_, _) => openGeneral();
        page.Children.Add(SettingsUi.Row("Theme", "Dark, Light or Follow Windows is set in General.", general));
        var island = SettingsUi.Button("Open Island & pet");
        island.Click += (_, _) => openIsland();
        page.Children.Add(SettingsUi.Row("Pet color and motion", "Gumdrop's color and reduced motion are set in Island & pet.", island));
        Content = page;
    }
}

/// <summary>About: version, license and the third-party notices shipped beside the exe.</summary>
internal sealed class AboutPage : UserControl
{
    public AboutPage()
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("About", "Everyday Windows utilities behind one tray icon."));

        var version = new TextBlock();
        version.SetResourceReference(StyleProperty, "CaptionTextStyle");
        version.Inlines.Add(new Run("Version "));
        version.Inlines.Add(SettingsUi.Mono(AppInfo.Version));
        page.Children.Add(SettingsUi.Row("Chippi", "", version));
        page.Children.Add(SettingsUi.Row("License", "Free software: you can share and change it under the GNU General Public License, version 3 or later.",
            SettingsUi.Styled(new TextBlock { Text = AppInfo.License }, "MonoTextStyle")));
        page.Children.Add(SettingsUi.Row("Privacy", "No telemetry and no network calls. Your settings stay on this PC.", new FrameworkElement()));

        page.Children.Add(SettingsUi.Eyebrow("Third-party notices"));
        var notices = new TextBlock { Text = ReadNotices(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 12, 14, 12) };
        notices.SetResourceReference(StyleProperty, "CaptionTextStyle");
        var viewer = new ScrollViewer
        {
            Content = notices,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = true,
            FocusVisualStyle = (Style)Application.Current.FindResource("FocusRing12"),
        };
        System.Windows.Automation.AutomationProperties.SetName(viewer, "Third-party notices");
        var box = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = viewer };
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        page.Children.Add(box);
        Content = page;
    }

    private static string ReadNotices()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        try
        {
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.Log.Warn("Could not read THIRD-PARTY-NOTICES.md", ex);
        }
        return "The notices file is missing from this build. Chippi bundles Manrope and JetBrains Mono (SIL Open Font License 1.1), "
            + "Lucide-style icons (ISC) and .NET libraries (MIT); see THIRD-PARTY-NOTICES.md in the source repository.";
    }
}

/// <summary>A feature's own settings page, with a Back button to the hub.</summary>
internal sealed class ModulePageHost : UserControl
{
    public ModulePageHost(string title, FrameworkElement view, Action back)
    {
        var backButton = new IconButton { Icon = LucideIcons.ChevronLeft, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(backButton, "Back to Features");
        backButton.ToolTip = "Back to Features (Esc)";
        backButton.Click += (_, _) => back();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 0, 0, 18) };
        header.Children.Add(backButton);
        header.Children.Add(SettingsUi.Styled(new TextBlock { Text = title, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, "PageTitleStyle"));

        var page = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        page.Children.Add(header);
        page.Children.Add(view);
        Content = page;
        Loaded += (_, _) => backButton.Focus();
    }
}

internal static class EmptyState
{
    /// <summary>A quiet centered block: chip-colored icon tile, title, one line of explanation.</summary>
    public static FrameworkElement Create(System.Windows.Media.Geometry icon, string title, string text)
    {
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        var glyph = new LucideIcon { Data = icon, Size = 18 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextControlBrush");
        tile.Child = glyph;
        var panel = new StackPanel { Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(tile);
        panel.Children.Add(SettingsUi.Styled(new TextBlock { Text = title, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }, "TitleTextStyle"));
        panel.Children.Add(SettingsUi.Styled(new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, MaxWidth = 380, TextWrapping = TextWrapping.Wrap }, "CaptionTextStyle"));
        return panel;
    }
}
