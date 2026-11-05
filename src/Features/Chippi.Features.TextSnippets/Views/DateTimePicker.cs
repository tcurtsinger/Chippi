// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.UI.Controls;

namespace Chippi.Features.TextSnippets.Views;

/// <summary>
/// Insert date or time (wave-3.html, sheet-snippets-states): the type, a style or a pattern of its own, the time zone and a
/// preview, then Insert puts the tag at the text's caret: <c>{{date:ddd, MMM d}}</c>, <c>{{time-tz(America/New_York)}}</c>.
/// </summary>
internal static class DateTimePicker
{
    private const double LabelWidth = 110, ControlWidth = 240;

    internal enum Kind { Date, Time, DateTime }

    private static readonly Dictionary<Kind, string[]> Styles = new()
    {
        [Kind.Date] = ["d", "D", "ddd, MMM d", "yyyy-MM-dd"],
        [Kind.Time] = ["t", "T", "HH:mm"],
        [Kind.DateTime] = ["g", "f", "yyyy-MM-dd HH:mm"],
    };

    /// <summary>The tag for a choice; the kind's own default format is left out, so <c>{{date}}</c> stays short.</summary>
    internal static string Token(Kind kind, string pattern, string? zone)
    {
        string name = kind switch { Kind.Date => "date", Kind.Time => "time", _ => "datetime" };
        string fallback = Styles[kind][0];
        return "{{" + name + (zone is null ? "" : $"-tz({zone})") + (pattern == fallback ? "" : ":" + pattern) + "}}";
    }

    /// <summary>The pattern's output now, or null when it can't be one (empty, a brace, or a format .NET refuses).</summary>
    internal static string? Preview(string pattern, DateTimeOffset now)
    {
        if (pattern.Length == 0 || pattern.Contains('}'))
            return null;
        try { return now.ToString(pattern, CultureInfo.CurrentCulture); }
        catch (FormatException) { return null; }
    }

    public static void Open(FrameworkElement anchor, Action<string> insert) => Create(anchor, insert).IsOpen = true;

    /// <summary>The popup, not yet open; its child is the panel a test can render without a window.</summary>
    internal static Popup Create(FrameworkElement anchor, Action<string> insert)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var kind = Kind.Date;
        string? zone = null;

        var type = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left };
        var types = new[] { new SegmentItem { Label = "Date", Value = Kind.Date }, new SegmentItem { Label = "Time", Value = Kind.Time }, new SegmentItem { Label = "Date and time", Value = Kind.DateTime } };
        type.ItemsSource = types;
        type.SelectedItem = types[0];
        AutomationProperties.SetName(type, "Type");

        var style = SettingsLayout.Combo([]);
        style.Width = ControlWidth;
        AutomationProperties.SetName(style, "Style");

        var zones = SettingsLayout.Combo([]);
        zones.Width = ControlWidth;
        AutomationProperties.SetName(zones, "Time zone");
        var zoneItems = new List<ComboBoxItem> { new() { Content = "Local", Tag = null } };
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out string? iana))
                zoneItems.Add(new ComboBoxItem { Content = tz.DisplayName, Tag = iana });
        zones.ItemsSource = zoneItems;
        zones.SelectedIndex = 0;

        var pattern = SnippetsPage.Field("", "Pattern");
        pattern.Width = ControlWidth;
        pattern.SetResourceReference(TextBox.FontFamilyProperty, "CodeFont");

        var preview = SnippetsPage.Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = ControlWidth }, "BodyTextStyle");
        AutomationProperties.SetName(preview, "Preview");
        AutomationProperties.SetLiveSetting(preview, AutomationLiveSetting.Polite);

        var ok = SnippetsPage.Button("Insert", primary: true);
        ok.Margin = new Thickness(0);
        var cancel = SnippetsPage.Button("Cancel");
        cancel.Click += (_, _) => { popup.IsOpen = false; anchor.Focus(); };
        ok.Click += (_, _) =>
        {
            popup.IsOpen = false;
            insert(Token(kind, pattern.Text, zone));
        };

        bool filling = false;
        void FillStyles()
        {
            filling = true;
            var now = Now(zone);
            var items = Styles[kind].Select(p => new ComboBoxItem { Content = now.ToString(p, CultureInfo.CurrentCulture), Tag = p }).ToList();
            items.Add(new ComboBoxItem { Content = "Custom", Tag = null });
            style.ItemsSource = items;
            style.SelectedIndex = PatternIndex(items);
            filling = false;
            Show();
        }
        int PatternIndex(List<ComboBoxItem> items)
        {
            int i = items.FindIndex(x => x.Tag as string == pattern.Text);
            return i >= 0 ? i : pattern.Text.Length > 0 ? items.Count - 1 : 0;
        }
        void Show()
        {
            string? text = Preview(pattern.Text, Now(zone));
            preview.Text = text ?? "Invalid pattern";
            preview.SetResourceReference(TextBlock.ForegroundProperty, text is null ? "WarningTextBrush" : "TextPrimaryBrush");
            ok.IsEnabled = text is not null;
        }

        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is not SegmentItem { Value: Kind chosen } || chosen == kind) return;
            kind = chosen;
            pattern.Text = Styles[kind][0];
            FillStyles();
        };
        style.SelectionChanged += (_, _) =>
        {
            if (filling || style.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is string p) pattern.Text = p;
            else pattern.Focus();
            Show();
        };
        zones.SelectionChanged += (_, _) =>
        {
            zone = (zones.SelectedItem as ComboBoxItem)?.Tag as string;
            FillStyles();
        };
        pattern.TextChanged += (_, _) =>
        {
            if (filling) return;
            if (style.ItemsSource is List<ComboBoxItem> items)
            {
                filling = true;
                style.SelectedIndex = PatternIndex(items);
                filling = false;
            }
            Show();
        };
        pattern.Text = Styles[kind][0];
        FillStyles();

        var title = SnippetsPage.Styled(new TextBlock { Text = "Insert date or time", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) }, "BodyTextStyle");
        var body = new StackPanel { Margin = new Thickness(16), Children = { title } };
        body.Children.Add(Row("Type", type));
        body.Children.Add(Row("Style", style));
        body.Children.Add(Row("Time zone", zones));
        body.Children.Add(Row("Pattern", pattern));
        body.Children.Add(Row("Preview", preview));
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { ok, cancel } });
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.OriginalSource is ComboBox { IsDropDownOpen: true }) return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        popup.Child = SnippetsPage.Flyout(body);
        popup.Opened += (_, _) => type.Focus();
        return popup;
    }

    private static DateTimeOffset Now(string? zone)
    {
        var now = DateTimeOffset.Now;
        if (zone is null) return now;
        try { return TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return now; }
    }

    private static Grid Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = SnippetsPage.Styled(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }
}
