// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Chippi.App.Pet;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

internal sealed class ShortcutsPage : UserControl
{
    public ShortcutsPage()
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Shortcuts", ""));
        page.Children.Add(EmptyState.Create(LucideIcons.Keyboard, "No shortcuts yet", ""));
        Content = page;
    }
}

internal sealed class AboutPage : UserControl
{
    private readonly StackPanel _page = new();

    public AboutPage()
    {
        _page.Children.Add(SettingsUi.Header("About", ""));
        var product = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 10, 16, 10) };
        product.Children.Add(new Gumdrop { Size = 32, Margin = new Thickness(0, 0, 14, 0) });
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(SettingsUi.Styled(new TextBlock { Text = "Chippi" }, "BodyTextStyle"));
        name.Children.Add(SettingsUi.Styled(new TextBlock { Text = "Version " + AppInfo.Version }, "CaptionTextStyle"));
        product.Children.Add(name);
        var license = SettingsUi.Button("View");
        AutomationProperties.SetName(license, "View license");
        license.Click += (_, _) => ShowDocument("License", ReadLicense());
        var notices = SettingsUi.Button("View");
        AutomationProperties.SetName(notices, "View third-party notices");
        notices.Click += (_, _) => ShowDocument("Third-party notices", ReadNotices());
        _page.Children.Add(SettingsUi.Group(product,
            SettingsUi.Row("License", AppInfo.License, license),
            SettingsUi.Row("Privacy", "No telemetry. Your settings stay on this PC.", new FrameworkElement()),
            SettingsUi.Row("Third-party notices", "", notices)));
        Content = _page;
    }

    private void ShowDocument(string title, string text)
    {
        var back = SettingsUi.Button("Back");
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, 0, 0, 14);
        back.Click += (_, _) => Content = _page;
        var page = new StackPanel();
        page.Children.Add(back);
        page.Children.Add(SettingsUi.Header(title, ""));
        var document = new FlowDocument { PagePadding = new Thickness(16), FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont"), FontSize = 14 };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextPrimaryBrush");
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            int depth = line.TakeWhile(c => c == '#').Count();
            string words = depth > 0 ? line[depth..].Trim() : line;
            var paragraph = new Paragraph { Margin = new Thickness(0, depth > 0 ? 16 : 0, 0, 8) };
            if (depth > 0)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = depth == 1 ? 20 : 14;
            }
            // Notices use Markdown headings and bold spans. Render those without showing markup.
            var pieces = words.Split("**", StringSplitOptions.None);
            for (int i = 0; i < pieces.Length; i++)
            {
                var run = new Run(pieces[i].Replace("`", ""));
                paragraph.Inlines.Add(i % 2 == 0 ? run : new Bold(run) { FontWeight = FontWeights.SemiBold });
            }
            document.Blocks.Add(paragraph);
        }
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(viewer, title);
        page.Children.Add(viewer);
        Content = page;
        back.Focus();
    }

    private static string ReadLicense()
    {
        try
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LICENSE"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.Log.Warn("Could not read LICENSE", ex);
            return "GNU General Public License, version 3 or later. The full license is in LICENSE in the source repository.";
        }
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
        return "The notices file is missing from this build. Chippi uses system-installed fonts, "
            + "Lucide-style icons (ISC) and .NET libraries (MIT); see THIRD-PARTY-NOTICES.md in the source repository.";
    }
}

internal sealed class ModulePageHost : UserControl
{
    public ModulePageHost(string title, FrameworkElement view)
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header(title, ""));
        page.Children.Add(view);
        Content = page;
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
