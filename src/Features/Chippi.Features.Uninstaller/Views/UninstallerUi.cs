// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.UI.Controls;

namespace Chippi.Features.Uninstaller.Views;

public enum TagKind { Neutral, Accent, Caution, Good }
public enum VerbKind { Normal, Accent, Critical }

/// <summary>The glyphs the mockups draw that the shared set lacks (Lucide, ISC). They stay in this feature.</summary>
internal static class UninstallerIcons
{
    private static Geometry G(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

    public static Geometry FolderOpen { get; } = G("M6,14 l1.5,-2.9 A2,2 0 0 1 9.24,10 H20 a2,2 0 0 1 1.94,2.5 l-1.54,6 a2,2 0 0 1 -1.95,1.5 H4 a2,2 0 0 1 -2,-2 V5 a2,2 0 0 1 2,-2 h3.9 a2,2 0 0 1 1.69,0.9 l0.81,1.2 a2,2 0 0 0 1.67,0.9 H18 a2,2 0 0 1 2,2 v2");
    public static Geometry KeyRound { get; } = G("M2.586,17.414 A2,2 0 0 0 2,18.828 V21 a1,1 0 0 0 1,1 h3 a1,1 0 0 0 1,-1 v-1 a1,1 0 0 1 1,-1 h1 a1,1 0 0 0 1,-1 v-1 a1,1 0 0 1 1,-1 h0.172 a2,2 0 0 0 1.414,-0.586 l0.814,-0.814 a6.5,6.5 0 1 0 -4,-4 Z M16.5,7.5 h0.01");
    public static Geometry ScanSearch { get; } = G("M3,7 V5 a2,2 0 0 1 2,-2 h2 M17,3 h2 a2,2 0 0 1 2,2 v2 M21,17 v2 a2,2 0 0 1 -2,2 h-2 M7,21 H5 a2,2 0 0 1 -2,-2 v-2 M9,12 a3,3 0 1 0 6,0 a3,3 0 1 0 -6,0 Z M16,16 l-1.9,-1.9");
    public static Geometry History { get; } = G("M3,12 a9,9 0 1 0 9,-9 9.75,9.75 0 0 0 -6.74,2.74 L3,8 M3,3 v5 h5 M12,7 v5 l4,2");
    public static Geometry RotateCw { get; } = G("M21,12 a9,9 0 1 1 -9,-9 c2.52,0 4.93,1 6.74,2.74 L21,8 M21,3 v5 h-5");
    public static Geometry PackageX { get; } = G("M21,10 V8 a2,2 0 0 0 -1,-1.73 l-7,-4 a2,2 0 0 0 -2,0 l-7,4 A2,2 0 0 0 3,8 v8 a2,2 0 0 0 1,1.73 l7,4 a2,2 0 0 0 2,0 l2,-1.14 M7.5,4.27 l9,5.15 M3.29,7 L12,12 L20.71,7 M12,22 V12 M17,13 l5,5 m-5,0 l5,-5");
}

/// <summary>Small builders the Uninstaller's Settings screens share. Colors are the app's theme resources; there are no new ones.</summary>
internal static class UninstallerUi
{
    /// <summary>False in the offscreen shots: an app's own icon is read from its file, and a fixture has none.</summary>
    internal static bool LoadIcons { get; set; } = true;

    internal static TextBlock Text(string text = "", double size = 14, string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    internal static LucideIcon Glyph(Geometry data, double size = 16, string brush = "TextSecondaryBrush")
    {
        var icon = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    /// <summary>The loader glyph, turning while it is on screen.</summary>
    internal static LucideIcon Spinner(double size)
    {
        var icon = Glyph(LucideIcons.LoaderCircle, size, "AccentBrush");
        var turn = new RotateTransform();
        icon.RenderTransformOrigin = new Point(.5, .5);
        icon.RenderTransform = turn;
        icon.Loaded += (_, _) => turn.BeginAnimation(RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        icon.Unloaded += (_, _) => turn.BeginAnimation(RotateTransform.AngleProperty, null);
        return icon;
    }

    /// <summary>An app's letters on the accent tint, replaced by its own icon once read.</summary>
    internal static FrameworkElement Mark(string name, string? iconPath, double size)
    {
        string letters = Letters(name);
        var label = Text(letters, size <= 20 ? 9 : size <= 28 ? 11 : 13, "AccentBrush");
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size <= 20 ? 4 : 6), Child = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        tile.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
        return LoadIcons && !string.IsNullOrEmpty(iconPath) ? AppIcon.View(iconPath, size, tile) : tile;
    }

    internal static string Letters(string name)
    {
        var words = name.Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => new string([.. w.Where(char.IsLetterOrDigit)])).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0) return "?";
        string text = words.Length > 1 ? words[0][..1] + words[1][..1] : words[0][..Math.Min(2, words[0].Length)];
        return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    /// <summary>An 18-high tag: neutral (Store), accent (New), caution (a warning count, No Recycle Bin) or good (a count that went right).</summary>
    internal static FrameworkElement Tag(string text, TagKind kind)
    {
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        var back = new Border { CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        var box = new Grid { Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        string ink, fill;
        double tint;
        switch (kind)
        {
            case TagKind.Accent: ink = "AccentBrush"; fill = "AccentBrush"; tint = .16; break;
            case TagKind.Caution: ink = "BadgeOverlapTextBrush"; fill = "BadgeOverlapBackgroundBrush"; tint = 1; break;
            case TagKind.Good: ink = "SuccessBrush"; fill = "SuccessBrush"; tint = .14; break;
            default: ink = "TextSecondaryBrush"; fill = "ChipBrush"; tint = 1; break;
        }
        back.SetResourceReference(Border.BackgroundProperty, fill);
        back.Opacity = tint;
        label.SetResourceReference(TextBlock.ForegroundProperty, ink);
        box.Children.Add(back);
        box.Children.Add(label);
        return box;
    }

    /// <summary>A bare-verb button. <paramref name="shield"/> puts the UAC shield inside it, left of the verb; critical is only for a confirm.</summary>
    internal static Button Verb(string text, Action action, VerbKind kind = VerbKind.Normal, bool small = false, bool shield = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        object content = label;
        if (shield)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new UacShield(small ? 14 : 16) { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(label);
            content = row;
        }
        var button = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(FrameworkElement.StyleProperty, kind switch { VerbKind.Accent => "PrimaryButtonStyle", VerbKind.Critical => "CriticalButtonStyle", _ => "SecondaryButtonStyle" });
        if (small) { button.Height = 28; button.Padding = new Thickness(10, 0, 10, 0); button.FontSize = 13; }
        else button.FontSize = 14;
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A 28-square glyph button with no chrome until hovered.</summary>
    internal static Button Icon(Geometry data, string name, Action action, double size = 16, double box = 28)
    {
        var button = new Button { Content = Glyph(data, size), Width = box, Height = box, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, ToolTip = name };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SubtleButtonStyle");
        button.Height = box;
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>The list's checkbox: the themed 20-DIP one.</summary>
    internal static CheckBox Check(bool? on, string name, bool enabled = true)
    {
        var box = new CheckBox { IsChecked = on, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center, IsThreeState = false };
        AutomationProperties.SetName(box, name);
        return box;
    }

    internal static void Add(Grid grid, UIElement element, int column = 0, int row = 0)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    internal static Grid Columns(IEnumerable<GridLength> widths, double gap)
    {
        var grid = new Grid();
        bool first = true;
        foreach (var width in widths)
        {
            if (!first) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            first = false;
        }
        return grid;
    }

    /// <summary>Column index of the n-th content column in a <see cref="Columns"/> grid (gaps sit between).</summary>
    internal static int At(int n) => n * 2;

    /// <summary>"1 KB", "312 KB", "1.2 MB", "6.4 GB": the review's sizes. Empty for none.</summary>
    internal static string Size(long? bytes)
    {
        if (bytes is not { } b || b <= 0) return "";
        var c = CultureInfo.InvariantCulture;
        if (b < 1_000_000) return Math.Max(1, (int)Math.Round(b / 1024.0)).ToString(c) + " KB";
        double mb = b / 1048576.0;
        return mb >= 1024 ? (mb / 1024).ToString("0.0", c) + " GB" : mb < 10 ? mb.ToString("0.0", c) + " MB" : Math.Round(mb).ToString("0", c) + " MB";
    }

    /// <summary>A 16-DIP shield column: the shield when the verb will ask for admin, empty otherwise.</summary>
    internal static FrameworkElement ShieldCell(bool shown, double size = 14)
    {
        var cell = new Grid { Width = 16 };
        if (shown) cell.Children.Add(new UacShield(size) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return cell;
    }

    /// <summary>The tinted, rimmed overlay card the confirms are drawn as.</summary>
    internal static Border Popover(UIElement content, double width)
    {
        var box = new Border { Width = width, Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = content };
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        box.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = .4, Color = Colors.Black };
        return box;
    }

    internal static void FocusLater(UIElement element)
    {
        element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (element.IsVisible && element.IsEnabled && !element.IsKeyboardFocusWithin) element.Focus(); });
    }

    /// <summary>The page's divider.</summary>
    internal static Border Rule()
    {
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        rule.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
        return rule;
    }
}
