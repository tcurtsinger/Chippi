// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Chippi.UI.Controls;

/// <summary>
/// Shared Settings layout (refresh brief F2 #1). Feature pages supply state and actions;
/// this toolkit supplies sections, cards and rows. Settings pages may scroll.
/// </summary>
public static class SettingsLayout
{
    public const double RowHeight = 48;
    public const double CaptionRowHeight = 60;
    public const double ComboWidth = 180;
    public static Thickness RowPadding { get; } = new(16, 8, 16, 8);
    public static Thickness DependentPadding { get; } = new(50, 8, 16, 8);

    /// <summary>
    /// What a feature's page shows at the right of its page title, such as Scratchpad's "Saved". Set it on the
    /// view the feature gives Settings; the shell puts it in the title row.
    /// </summary>
    public static readonly DependencyProperty TitleAsideProperty = DependencyProperty.RegisterAttached(
        "TitleAside", typeof(FrameworkElement), typeof(SettingsLayout), new PropertyMetadata(null));

    public static FrameworkElement? GetTitleAside(DependencyObject view) => (FrameworkElement?)view.GetValue(TitleAsideProperty);

    public static void SetTitleAside(DependencyObject view, FrameworkElement? aside) => view.SetValue(TitleAsideProperty, aside);

    /// <summary>A 20-high heading, 24 above and 8 below; a header action does not add height.</summary>
    public static Grid Section(string title, FrameworkElement? action = null, bool first = false)
    {
        var heading = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "HeadlineTextStyle");
        var header = new Grid { Height = 20, Margin = new Thickness(0, first ? 0 : 24, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(heading);
        if (action is not null)
        {
            Grid.SetColumn(action, 1);
            action.VerticalAlignment = VerticalAlignment.Center;
            action.Margin = new Thickness(12, -6, 0, -6);
            header.Children.Add(action);
        }
        return header;
    }

    public static Card Card(params UIElement[] rows)
    {
        var card = new Card();
        foreach (var row in rows) card.Children.Add(row);
        return card;
    }

    /// <summary>48 high, or 60 with a caption; the existing row control owns its live template.</summary>
    public static SettingRow Row(string title, UIElement? control = null, string? caption = null) =>
        new() { Header = title, Description = caption ?? "", Content = control };

    /// <summary>One caption-color row in a card, with the same content inset as a setting.</summary>
    public static Border Empty(string text)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(FrameworkElement.StyleProperty, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        return new Border { MinHeight = RowHeight, Padding = RowPadding, Child = label };
    }

    /// <summary>
    /// Name, current value and an existing switch in the header, followed by the chevron.
    /// Dependent rows share the enclosing card and use its 50 DIP inset. The caller owns the switch.
    /// </summary>
    public static ExpanderRow Expander(string title, object? value, ToggleButton toggle, params UIElement[] rows)
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (value is not null)
        {
            var summary = new ContentControl { Content = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            summary.SetResourceReference(Control.ForegroundProperty, "TextSecondaryBrush");
            controls.Children.Add(summary);
        }
        controls.Children.Add(toggle);
        var children = Card(rows);
        children.SetResourceReference(FrameworkElement.StyleProperty, "CardRowsStyle");
        return new ExpanderRow { Header = title, Value = controls, Content = children };
    }

    public static ComboBox Combo(IEnumerable<object> items)
    {
        var combo = new ComboBox { Width = ComboWidth, ItemsSource = items };
        combo.SetResourceReference(FrameworkElement.StyleProperty, "HopperComboBoxStyle");
        return combo;
    }

    /// <summary>The plus is a glyph, so the visible label stays the same as other verb buttons.</summary>
    public static Button AddButton(string thing, Action clicked)
    {
        string label = $"Add {thing}";
        var plus = new LucideIcon { Data = LucideIcons.Plus, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        plus.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal,
            Children = { plus, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } } } };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => clicked();
        return button;
    }
}
