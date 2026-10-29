// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chippi.Features.Scratchpad;

/// <summary>A deliberately small, local Markdown renderer. Text only: no HTML, navigation or fetching.</summary>
public static class ScratchpadMarkdown
{
    private static readonly Regex Inline = new(@"!\[([^\]]*)\]\([^)]*\)|\[([^\]]*)\]\([^)]*\)|\*\*(.+?)\*\*|`([^`]+)`", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static StackPanel Render(string text)
    {
        var panel = new StackPanel();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var value = line;
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, MinHeight = 20 };
            block.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
            int heading = value.TakeWhile(c => c == '#').Count();
            if (heading is >= 1 and <= 6 && value.Length > heading && value[heading] == ' ')
            { value = value[(heading + 1)..]; block.FontSize = heading == 1 ? 18 : 15; block.FontWeight = FontWeights.SemiBold; block.Margin = new Thickness(0, 4, 0, 4); }
            else if (value.StartsWith("- [ ] ") || value.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                var box = new CheckBox { IsChecked = char.ToLowerInvariant(value[3]) == 'x', IsHitTestVisible = false, Focusable = false, Width = 20, Height = 20, LayoutTransform = new ScaleTransform(0.7, 0.7), VerticalAlignment = VerticalAlignment.Center };
                block.Inlines.Add(new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center }); value = " " + value[6..];
            }
            else if (value.StartsWith("- ") || value.StartsWith("* ")) value = "\u2022 " + value[2..];
            try { AddInline(block, value); }
            catch (RegexMatchTimeoutException) { block.Inlines.Clear(); block.Text = value; }
            panel.Children.Add(block);
        }
        return panel;
    }
    private static void AddInline(TextBlock block, string value)
    {
        int at = 0;
        foreach (Match match in Inline.Matches(value))
        {
            block.Inlines.Add(new Run(value[at..match.Index]));
            if (match.Groups[1].Success)
            {
                var label = new TextBlock { Text = match.Groups[1].Value, FontSize = 12, Padding = new Thickness(6, 1, 6, 1) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
                label.SetResourceReference(TextBlock.BackgroundProperty, "IslandPanelBrush");
                block.Inlines.Add(new InlineUIContainer(label));
            }
            else
            {
                var run = new Run(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value);
                if (match.Groups[2].Success) run.TextDecorations = TextDecorations.Underline;
                if (match.Groups[3].Success) run.FontWeight = FontWeights.SemiBold;
                if (match.Groups[4].Success) run.FontFamily = new FontFamily("Cascadia Code, Consolas");
                block.Inlines.Add(run);
            }
            at = match.Index + match.Length;
        }
        block.Inlines.Add(new Run(value[at..]));
    }
}
