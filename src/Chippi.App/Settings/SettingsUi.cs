// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Config;
using Chippi.UI.Controls;

namespace Chippi.App.Settings;

/// <summary>The app's version and license, for the sidebar footer and About.</summary>
internal static class AppInfo
{
    /// <summary>Informational version without the source-control suffix, e.g. "0.1.0".</summary>
    public static string Version { get; } = ReadVersion();

    public const string License = "GPL-3.0-or-later";

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}

/// <summary>
/// Builds the pieces every settings page shares: the header, setting rows, eyebrows and inline
/// messages. Layout only; the controls are Chippi.UI's, unchanged.
/// </summary>
internal static class SettingsUi
{
    /// <summary>Display title over a Body description, with an optional element on the right (the hub's count).</summary>
    public static FrameworkElement Header(string title, string description, FrameworkElement? right = null)
    {
        var text = new StackPanel();
        text.Children.Add(Styled(new TextBlock { Text = title }, "PageTitleStyle"));

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            right.Margin = new Thickness(24, 0, 0, 2);
            grid.Children.Add(right);
        }
        return grid;
    }

    /// <summary>A setting: title and caption on the left, its control on the right, on a Surface row.</summary>
    public static Border Row(string title, string caption, FrameworkElement control, FrameworkElement? below = null)
    {
        var row = new SettingRow { Header = title, Description = caption, Content = control };
        if (below is null)
            return new Border { Child = row };
        var stack = new StackPanel();
        stack.Children.Add(row);
        below.Margin = new Thickness(16, 0, 16, 10);
        stack.Children.Add(below);
        return new Border { Child = stack };
    }

    public static FrameworkElement Section(string text) => Styled(new TextBlock { Text = text, Margin = new Thickness(0, 24, 0, 8) }, "HeadlineTextStyle");

    public static Card Group(params UIElement[] rows)
    {
        var card = new Card();
        foreach (var row in rows)
            card.Children.Add(row);
        return card;
    }
    /// <summary>A caption line for results and errors ("Settings imported", "Not imported · Invalid JSON").</summary>
    public static TextBlock Message() => Styled(new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap }, "CaptionTextStyle");

    public static void Show(TextBlock message, string text, bool problem)
    {
        message.Text = text;
        message.SetResourceReference(TextBlock.ForegroundProperty, problem ? "WarningBrush" : "TextTertiaryBrush");
        message.Visibility = Visibility.Visible;
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
    }

    /// <summary>A Chippi.UI segmented control of <paramref name="items"/>, with the one equal to <paramref name="selected"/> active.</summary>
    public static SegmentedControl Segments<T>(string name, IEnumerable<(string Label, T Value)> items, T selected)
    {
        var list = items.Select(i => new SegmentItem { Label = i.Label, Value = i.Value }).ToList();
        var control = new SegmentedControl { ItemsSource = list, HorizontalAlignment = HorizontalAlignment.Left };
        control.SelectedItem = list.FirstOrDefault(i => Equals(i.Value, selected));
        AutomationProperties.SetName(control, name);
        return control;
    }

    public static ToggleButton Switch(bool on) => Styled(new ToggleButton { IsChecked = on, Content = on ? "On" : "Off" }, "SwitchWithLabelStyle");

    /// <summary>
    /// Runs <paramref name="changed"/> whenever the switch's state changes, however it changed:
    /// mouse, keyboard, or a UI Automation toggle (Narrator, switch access), which raises no Click.
    /// A change the handler makes itself (reverting after a failure) does not re-enter it.
    /// </summary>
    public static void OnSwitched(ToggleButton toggle, Action<bool> changed)
    {
        bool busy = false;
        void Handler(object sender, RoutedEventArgs e)
        {
            if (busy)
                return;
            busy = true;
            try
            {
                toggle.Content = toggle.IsChecked == true ? "On" : "Off";
                changed(toggle.IsChecked == true);
            }
            finally
            {
                toggle.Content = toggle.IsChecked == true ? "On" : "Off";
                busy = false;
            }
        }
        toggle.Checked += Handler;
        toggle.Unchecked += Handler;
    }

    public static Button Button(string text, bool primary = false) =>
        Styled(new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) }, primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle");


    public static AppSettings ReadApp(Core.Config.ConfigStore config) => config.GetSection<AppSettings>(AppSettings.SectionName);

    public static void SaveApp(Core.Config.ConfigStore config, Action<AppSettings> change)
    {
        var settings = ReadApp(config);
        change(settings);
        config.SetSection(AppSettings.SectionName, settings);
    }

    public static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, style);
        return element;
    }
}
