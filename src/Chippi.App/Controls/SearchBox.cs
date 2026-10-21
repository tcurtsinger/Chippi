// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Chippi.App.Controls;

/// <summary>
/// 36px search field: Input fill, hairline, a search glyph and placeholder. Esc clears the text;
/// on an empty box Esc is left unhandled so it can close the surrounding flyout or dialog.
/// </summary>
public class SearchBox : TextBox
{
    static SearchBox() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchBox), new FrameworkPropertyMetadata(typeof(SearchBox)));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata("Search"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text))
        {
            Clear();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
