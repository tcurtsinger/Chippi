// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.ColorPicker.Views;

/// <summary>
/// The island's Color page (Capture results v3): the color as a wide band with its HEX, its other formats in two
/// columns, each with Copy, and the recent palette under them with Clear and Pick again. Only a Copy writes the
/// clipboard; a recent swatch puts its color on the page and nothing else.
/// </summary>
public sealed class ColorResultView : UserControl
{
    internal const string NoColors = "No colors yet";
    internal const string ClearQuestion = "Clear recent colors?";
    internal const string CopyFailed = "Copy failed";

    private readonly IColorPageModel _model;
    private readonly Border _band = new() { Height = 96, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 10, 12) };
    private readonly TextBlock _hex = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _copied = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    // On the band, Copy is drawn in the band's own ink, so it reads on any color.
    private readonly TextBlock _copyHexText = new() { Text = "Copy", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _copyHexEdge = new() { Height = 24, Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
    private readonly Button _copyHex = new() { Height = 24 };
    private readonly UniformGrid _formats = new() { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _color = new();
    private readonly StackPanel _none;
    private readonly StackPanel _recent = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _noRecent = Text(NoColors, 11, "IslandTextTertiaryBrush");
    private readonly Button _clear = Button("Clear");
    private readonly Button _pickAgain = Button("Pick again", LucideIcons.Pipette);
    private readonly DockPanel _foot = new() { Height = 28, LastChildFill = true };
    private readonly DockPanel _confirm = new() { Height = 28, LastChildFill = true };
    private readonly Button _cancel = Button("Cancel");
    private readonly Button _confirmClear = Button("Clear", accent: true);
    private PickedColor? _shown;
    private string? _status;

    public ColorResultView(IColorPageModel model)
    {
        _model = model;
        Header = new DockPanel { Children = { new TextBlock { Text = "Color", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };

        // The band: HEX at the bottom left; what was copied and Copy HEX at the bottom right.
        _band.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        AutomationProperties.SetLiveSetting(_copied, AutomationLiveSetting.Polite);
        _copyHexEdge.Child = _copyHexText;
        _copyHex.Content = _copyHexEdge;
        _copyHex.SetResourceReference(StyleProperty, "IslandBareButtonStyle");
        AutomationProperties.SetName(_copyHex, "Copy HEX");
        _copyHex.VerticalAlignment = VerticalAlignment.Bottom;
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Children = { _copied, _copyHex } };
        DockPanel.SetDock(right, Dock.Right);
        _band.Child = new DockPanel { LastChildFill = true, Children = { right, _hex } };
        _copyHex.Click += (_, _) => { if (_shown is { } c) Copy(new ColorFormat("HEX", c.Hex)); };
        _color.Children.Add(_band);
        _color.Children.Add(_formats);

        var noneGlyph = Glyph(LucideIcons.Pipette, 18, "IslandTextTertiaryBrush");
        noneGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        var noneText = Text(NoColors, 12, "IslandTextSecondaryBrush");
        noneText.Margin = new Thickness(0, 6, 0, 0);
        _none = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { noneGlyph, noneText } };

        _clear.Click += (_, _) => Confirming(true);
        _cancel.Click += (_, _) => Confirming(false);
        _confirmClear.Click += (_, _) => { Confirming(false); _model.ClearRecent(); };
        _pickAgain.Click += (_, _) => _model.PickAgain();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _clear, _pickAgain } };
        DockPanel.SetDock(buttons, Dock.Right);
        AutomationProperties.SetName(_recent, "Recent colors");
        _foot.Children.Add(buttons);
        _foot.Children.Add(new Grid { Children = { _recent, _noRecent } });

        var question = Text(ClearQuestion, 12, "IslandTextBrush");
        question.FontWeight = FontWeights.SemiBold;
        var answers = new StackPanel { Orientation = Orientation.Horizontal, Children = { _cancel, _confirmClear } };
        DockPanel.SetDock(answers, Dock.Right);
        _confirm.Children.Add(answers);
        _confirm.Children.Add(question);
        _confirm.Visibility = Visibility.Collapsed;

        var body = new Grid
        {
            Width = IslandPage.BodyWidth,
            Height = IslandPage.BodyHeight,
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } },
            Children = { _color, _none, _foot, _confirm },
        };
        Grid.SetRow(_foot, 1);
        Grid.SetRow(_confirm, 1);
        Content = body;

        _model.Changed += () => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Refresh();
    }

    /// <summary>The page's header: "Color".</summary>
    public DockPanel Header { get; }

    internal string HexText => _hex.Text;

    internal string Status => _copied.Text;

    internal Brush BandInk => _hex.Foreground;

    internal Button CopyHexButton => _copyHex;

    internal IReadOnlyList<Button> FormatButtons => [.. Rows.Select(r => r.Children.OfType<Button>().Single())];

    internal IReadOnlyList<string> FormatTexts => [.. Rows.SelectMany(r => r.Children.OfType<TextBlock>()).Select(t => t.Text)];

    internal IReadOnlyList<Button> Swatches => [.. _recent.Children.OfType<Button>()];

    private IEnumerable<DockPanel> Rows => _formats.Children.OfType<Border>().Select(b => (DockPanel)b.Child);

    internal Button ClearButton => _clear;

    internal Button PickAgainButton => _pickAgain;

    internal Button CancelButton => _cancel;

    internal Button ConfirmClearButton => _confirmClear;

    internal bool AskingToClear => _confirm.Visibility == Visibility.Visible;

    internal FrameworkElement NoColor => _none;

    internal TextBlock NoRecent => _noRecent;

    private void Refresh()
    {
        var color = _model.Color;
        if (!ReferenceEquals(color, _shown))
        {
            bool first = _shown is null;
            _shown = color;
            // What the pick copied shows until the page copies something else; a recent color copied nothing.
            _status = first && _model.CopiedAs is { } format ? $"Copied as {format}" : null;
            if (color is not null) ShowColor(color);
        }
        _color.Visibility = color is null ? Visibility.Collapsed : Visibility.Visible;
        _none.Visibility = color is null ? Visibility.Visible : Visibility.Collapsed;
        ShowStatus();
        ShowRecent();
    }

    private void ShowColor(PickedColor color)
    {
        var fill = Parse(color.Hex) ?? Colors.Transparent;
        _band.Background = new SolidColorBrush(fill);
        var ink = new SolidColorBrush(Ink(fill));
        _hex.Text = color.Hex;
        _hex.Foreground = _copied.Foreground = _copyHexText.Foreground = _copyHexEdge.BorderBrush = ink;
        AutomationProperties.SetName(_band, $"Color {color.Hex}");

        _formats.Children.Clear();
        for (int i = 0; i < color.Formats.Count; i++)
        {
            var format = color.Formats[i];
            var name = Text(format.Name, 11, "IslandTextSecondaryBrush");
            name.Width = 44;
            var value = Text(format.Value, 13, "IslandTextBrush");
            var copy = SmallButton("Copy", $"Copy {format.Name}");
            copy.Click += (_, _) => Copy(format);
            DockPanel.SetDock(name, Dock.Left);
            DockPanel.SetDock(copy, Dock.Right);
            var row = new DockPanel { Height = 32, LastChildFill = true, Children = { name, copy, value } };
            // Two columns 12 apart, each row over a hairline.
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = row, Margin = new Thickness(i % 2 == 0 ? 0 : 6, 0, i % 2 == 0 ? 6 : 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
            _formats.Children.Add(line);
        }
    }

    private void Copy(ColorFormat format)
    {
        _status = !_model.Copy(format) ? CopyFailed : $"Copied as {format.Name}";
        ShowStatus();
    }

    private void ShowStatus() => _copied.Text = _status ?? "";

    private void ShowRecent()
    {
        var recent = _model.Recent;
        _recent.Children.Clear();
        foreach (string hex in recent.Take(10))
        {
            if (Parse(hex) is not { } color) continue;
            bool current = string.Equals(hex, _shown?.Hex, StringComparison.OrdinalIgnoreCase);
            var dot = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(color), StrokeThickness = current ? 2 : 1 };
            dot.SetResourceReference(Shape.StrokeProperty, current ? "IslandAccentBrush" : "IslandHairBrush");
            var swatch = new Button { Content = dot, Width = 24, Height = 24, ToolTip = hex };
            swatch.SetResourceReference(StyleProperty, "IslandBareButtonStyle");
            swatch.SetResourceReference(FocusVisualStyleProperty, "IslandRoundFocusVisual");
            AutomationProperties.SetName(swatch, current ? $"{hex}, on the page" : hex);
            swatch.Click += (_, _) => _model.Show(hex);
            _recent.Children.Add(swatch);
        }
        bool any = _recent.Children.Count > 0;
        _noRecent.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _clear.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any) Confirming(false);
    }

    /// <summary>"Clear recent colors?" with Cancel and Clear, in the footer's place.</summary>
    private void Confirming(bool asking)
    {
        _confirm.Visibility = asking ? Visibility.Visible : Visibility.Collapsed;
        _foot.Visibility = asking ? Visibility.Collapsed : Visibility.Visible;
        if (asking) _cancel.Focus();
    }

    internal static Color? Parse(string hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return null;
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>Black or white, whichever has more contrast on the color (WCAG relative luminance).</summary>
    internal static Color Ink(Color color)
    {
        static double Channel(byte c)
        {
            double s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        double l = 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        return (l + 0.05) / 0.05 >= 1.05 / (l + 0.05) ? Colors.Black : Colors.White;
    }

    private static TextBlock Text(string text, double size, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static LucideIcon Glyph(Geometry icon, double size, string brush)
    {
        var glyph = new LucideIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    private static Button Button(string text, Geometry? glyph = null, bool accent = false)
    {
        object content = text;
        if (glyph is not null)
        {
            var icon = Glyph(glyph, 14, "IslandTextSecondaryBrush");
            icon.Margin = new Thickness(0, 0, 6, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
        }
        var button = new Button { Content = content, Height = 28, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        AutomationProperties.SetName(button, text);
        return button;
    }

    /// <summary>A 24 button in 11, for a row's Copy.</summary>
    private static Button SmallButton(string text, string name)
    {
        var button = new Button { Content = text, Height = 24, MinWidth = 0, Padding = new Thickness(8, 0, 8, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }
}
