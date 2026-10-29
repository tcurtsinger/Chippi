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
    public ShortcutsPage(Chippi.App.Shell.ShellContext shell)
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Shortcuts", ""));
        void Build()
        {
            while (page.Children.Count > 1) page.Children.RemoveAt(1);
            foreach (var entry in shell.Registry.Features.Where(f => f.IsRunning && f.Id is "mic-mute" or "paste-plain-text"))
                if (entry.SettingsPage?.CreateView("shortcut-list") is FrameworkElement view)
                    page.Children.Add(view);
            if (page.Children.Count == 1) page.Children.Add(SettingsLayout.Card(SettingsLayout.Empty("No shortcuts")));
        }
        void Changed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Build);
        Loaded += (_, _) => { shell.Registry.Changed += Changed; Build(); };
        Unloaded += (_, _) => shell.Registry.Changed -= Changed;
        Build();
        Content = page;
    }
}

internal sealed class AboutPage : UserControl
{
    private readonly StackPanel _page = new();

    public AboutPage()
    {
        _page.Children.Add(SettingsUi.Header("About", ""));
        var product = new DockPanel { Margin = SettingsLayout.RowPadding };
        var version = SettingsUi.Styled(new TextBlock { Text = "Version " + AppInfo.Version, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        DockPanel.SetDock(version, Dock.Right);
        product.Children.Add(version);
        product.Children.Add(new Gumdrop { Size = 32, Margin = new Thickness(0, 0, 14, 0) });
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(SettingsUi.Styled(new TextBlock { Text = "Chippi" }, "BodyTextStyle"));
        product.Children.Add(name);
        var license = SettingsUi.Button("View");
        AutomationProperties.SetName(license, "View license");
        license.Click += (_, _) => ShowDocument("License", ReadLicense());
        var notices = SettingsUi.Button("View");
        AutomationProperties.SetName(notices, "View third-party notices");
        notices.Click += (_, _) => ShowDocument("Third-party notices", ReadNotices());
        // VB-CABLE's license asks redistributors to name its origin and that it is donationware.
        var donate = SettingsUi.Button("Donate");
        AutomationProperties.SetName(donate, "Donate to VB-Audio");
        donate.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true })?.Dispose();
        _page.Children.Add(SettingsLayout.Card(product,
            SettingsLayout.Row("License", license, AppInfo.License),
            SettingsLayout.Row("Privacy", new FrameworkElement(), "No telemetry"),
            SettingsLayout.Row("Third-party notices", notices),
            SettingsLayout.Row("VB-CABLE", donate, "Donationware from vb-audio.com")));
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
        page.Children.Add(SettingsUi.Header(title, "", SettingsLayout.GetTitleAside(view)));
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
