// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.Features.Clipboard.Views;

/// <summary>Show both ends of a long link at rest. Editing always uses the complete, original text.</summary>
internal sealed class CleanLinkField : Grid
{
    public TextBox Field { get; } = new();

    public CleanLinkField(double fontSize = 12, double lineHeight = 16)
    {
        ClipToBounds = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi.Features.Clipboard;component/Views/CleanUrlStyles.xaml") });
        Field.Style = (Style)Resources["CleanUrlFieldStyle"];
        Field.FontSize = fontSize;
        Field.Tag = "Link";
        Field.TextWrapping = TextWrapping.Wrap;
        Field.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Field.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Field.MaxLines = 3;
        Field.MaxHeight = 3 * lineHeight + 12;
        TextBlock.SetLineHeight(Field, lineHeight);
        AutomationProperties.SetName(Field, "Link");
        var display = new MiddleTrimmedText(fontSize, lineHeight) { IsHitTestVisible = false, Margin = new Thickness(10, 6, 10, 6) };
        display.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        Field.Foreground = Brushes.Transparent;
        Field.TextChanged += (_, _) => display.FullText = Field.Text;
        Field.GotKeyboardFocus += (_, _) =>
        {
            display.Visibility = Visibility.Collapsed;
            Field.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            Field.SelectAll();
            Field.ScrollToHome();
        };
        Field.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Field.IsKeyboardFocusWithin) return;
            if (Field.Focus()) e.Handled = true; // Keep the full selection on the first click.
        };
        Field.LostKeyboardFocus += (_, _) =>
        {
            Field.Foreground = Brushes.Transparent;
            display.Visibility = Visibility.Visible;
        };
        Children.Add(Field);
        Children.Add(display);
    }
}
