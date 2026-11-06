// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenText.Tests;

/// <summary>The island's themes, invented captures, and the panel the pages are shot in.</summary>
internal static class Shots
{
    /// <summary>The folder the shots go to, or null when CHIPPI_ISLAND_SHOTS isn't set.</summary>
    public static string? Folder => Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is { Length: > 0 } folder ? folder : null;

    public static void IslandTheme()
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        if (!resources.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        if (!resources.Any(d => d.Source?.OriginalString.EndsWith("IslandStyles.xaml", StringComparison.Ordinal) == true))
            resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
    }

    public static T Laid<T>(T view) where T : FrameworkElement
    {
        view.Measure(new Size(IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.Arrange(new Rect(0, 0, IslandPage.BodyWidth, IslandPage.BodyHeight));
        view.UpdateLayout();
        return view;
    }

    public static List<string> Texts(DependencyObject root) => [.. Find<TextBlock>(root).Select(t => t.Text)];

    public static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    public static void Click(System.Windows.Controls.Primitives.ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    /// <summary>The lines of an invented document, as Keep lines would read them.</summary>
    public static readonly string[] DocumentLines =
    [
        "Quarterly review",
        "Revenue grew 12% over the last quarter, led by the",
        "new subscription plans.",
        "Next steps",
        "· Ship the onboarding update by Friday",
        "· Review pricing with the team",
        "· Draft the launch email for the new plans",
    ];

    /// <summary>The invented document drawn as a light window's capture, 640 × 420, with each line's box.</summary>
    public static TextResult Document(string language = "English")
    {
        var visual = new DrawingVisual();
        var boxes = new List<Rect>();
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 420));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, 640, 36));
            double y = 60;
            foreach (string line in DocumentLines)
            {
                bool heading = line is "Quarterly review" or "Next steps";
                if (line == "Next steps") y += 18;
                var text = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, heading ? bold : face, heading ? 24 : 18,
                    new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), 1);
                dc.DrawText(text, new Point(32, y));
                boxes.Add(new Rect(28, y - 2, text.Width + 8, text.Height + 4));
                y += text.Height + 12;
            }
        }
        var bitmap = new RenderTargetBitmap(640, 420, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var lines = new List<TextLine>();
        int at = 0;
        for (int i = 0; i < DocumentLines.Length; i++)
        {
            lines.Add(new TextLine(at, DocumentLines[i].Length, boxes[i]));
            at += DocumentLines[i].Length + 1;
        }
        return new TextResult(bitmap, string.Join("\n", DocumentLines), lines, language);
    }

    /// <summary>An invented code: three finder squares and seeded modules, 25 modules with a 4-module quiet zone, 6 px each.</summary>
    public static BitmapSource Code(int seed)
    {
        const int Modules = 25, Quiet = 4, Px = 6, Side = (Modules + 2 * Quiet) * Px;
        var random = new Random(seed);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Side, Side));
            void Module(int x, int y) => dc.DrawRectangle(Brushes.Black, null, new Rect((Quiet + x) * Px, (Quiet + y) * Px, Px, Px));
            bool Finder(int x, int y, int ox, int oy)
            {
                int dx = x - ox, dy = y - oy;
                if (dx is < 0 or > 6 || dy is < 0 or > 6) return false;
                return dx is 0 or 6 || dy is 0 or 6 || (dx is >= 2 and <= 4 && dy is >= 2 and <= 4);
            }
            for (int y = 0; y < Modules; y++)
                for (int x = 0; x < Modules; x++)
                {
                    bool inFinder = (x < 8 && y < 8) || (x >= Modules - 8 && y < 8) || (x < 8 && y >= Modules - 8);
                    if (inFinder ? Finder(x, y, 0, 0) || Finder(x, y, Modules - 7, 0) || Finder(x, y, 0, Modules - 7) : random.Next(2) == 0)
                        Module(x, y);
                }
        }
        var bitmap = new RenderTargetBitmap(Side, Side, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The island at 520 × 360: Back, the page's header and its 496 × 304 body, on the window's background, at 2x.</summary>
    public static void Panel(FrameworkElement view, DockPanel header, string name)
    {
        if (Folder is not { } folder) return;
        var glyph = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 16 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var back = new Button { Content = glyph, Margin = new Thickness(-4, 0, 4, 0) };
        back.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        DockPanel.SetDock(back, Dock.Left);
        var row = new DockPanel { Height = IslandPage.HeaderHeight, Margin = new Thickness(0, 0, 0, IslandPage.Gap), LastChildFill = true, Children = { back, header } };
        var panel = new Border
        {
            Width = IslandPage.Width + 2, // the 1px edge sits outside the 520 × 360
            Height = IslandPage.Height + 2,
            Padding = new Thickness(IslandPage.Inset),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel { Children = { row, view } },
        };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        panel.SetResourceReference(TextElement.ForegroundProperty, "IslandTextBrush");
        var backdrop = new Border { Padding = new Thickness(16), Child = panel };
        backdrop.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        backdrop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        backdrop.Arrange(new Rect(backdrop.DesiredSize));
        backdrop.UpdateLayout();
        Assert.InRange(view.ActualHeight, 0, IslandPage.BodyHeight);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(backdrop.ActualWidth * 2), (int)Math.Ceiling(backdrop.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
        row.Children.Clear();
        panel.Child = null;
    }
}
