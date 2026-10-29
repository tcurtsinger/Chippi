// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

public sealed class SettingsLayoutTests
{
    [Fact]
    public void Combo_button_and_segment_rows_are_48_and_captions_are_60() => WpfTestThread.Run(() =>
    {
        var combo = SettingsLayout.Combo(new[] { "One", "Two" });
        var button = SettingsLayout.AddButton("site", () => { });
        var segment = new SegmentedControl { ItemsSource = new[] { "Auto", "Manual", "Paused" } };
        foreach (var control in new UIElement[] { combo, button, segment })
        {
            var row = SettingsLayout.Row("Choice", control);
            Fit(row);
            Assert.Equal(48, row.ActualHeight);
            Assert.Equal(new Thickness(16, 8, 16, 8), row.Padding);
            row.Description = "Current state";
            Fit(row);
            Assert.Equal(60, row.ActualHeight);
            row.Description = "";
            Fit(row);
            Assert.Equal(48, row.ActualHeight);
        }
        Assert.Equal(180, combo.ActualWidth);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cards_are_52_apart_and_a_header_action_adds_no_height(bool withAction) => WpfTestThread.Run(() =>
    {
        int called = 0;
        var first = SettingsLayout.Card(SettingsLayout.Row("First"));
        var action = withAction ? SettingsLayout.AddButton("site", () => called++) : null;
        var section = SettingsLayout.Section("Next", action);
        var second = SettingsLayout.Card(SettingsLayout.Row("Second"));
        var page = new StackPanel { Children = { first, section, second } };
        Fit(page);
        double firstBottom = first.TranslatePoint(new Point(0, first.ActualHeight), page).Y;
        double secondTop = second.TranslatePoint(new Point(), page).Y;
        Assert.Equal(52, secondTop - firstBottom);
        Assert.Equal(20, section.ActualHeight);
        Assert.Equal(0, section.Margin.Left);
        if (action is not null)
        {
            ((IInvokeProvider)new ButtonAutomationPeer(action)).Invoke();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Assert.Equal(1, called);
        }
    });

    [Fact]
    public void Empty_is_one_48_row_in_caption_color() => WpfTestThread.Run(() =>
    {
        var empty = SettingsLayout.Empty("No sites");
        var card = SettingsLayout.Card(empty);
        Fit(card);
        Assert.Equal(48, empty.ActualHeight);
        Assert.Equal(new Thickness(16, 8, 16, 8), empty.Padding);
        var text = Assert.IsType<TextBlock>(empty.Child);
        Assert.Equal("No sites", text.Text);
        Assert.Equal(14, text.FontSize);
        Assert.Same(text.FindResource("TextTertiaryBrush"), text.Foreground);
        Assert.Equal(50, card.ActualHeight); // the existing card's 1 DIP border on both edges
    });

    [Fact]
    public void Expander_keeps_value_and_switch_and_indents_dependent_rows() => WpfTestThread.Run(() =>
    {
        var toggle = new ToggleButton { IsChecked = false };
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "SwitchWithLabelStyle");
        var dependent = SettingsLayout.Row("Threshold", SettingsLayout.Combo(new[] { "90%", "95%" }));
        var expander = SettingsLayout.Expander("CPU load", "90%", toggle, dependent);
        var card = SettingsLayout.Card(expander);
        Fit(card);
        Assert.Equal(50, card.ActualHeight);
        ((IToggleProvider)new ToggleButtonAutomationPeer(toggle)).Toggle();
        Assert.True(toggle.IsChecked);
        Assert.False(expander.IsExpanded);
        expander.IsExpanded = true;
        Fit(card);
        Assert.Equal(new Thickness(50, 8, 16, 8), dependent.Padding);
        Assert.Equal(48, dependent.ActualHeight);
        Assert.True(card.ActualHeight > 98);
        expander.IsExpanded = false;
        Fit(card);
        Assert.Equal(50, card.ActualHeight);
        Assert.True(toggle.IsChecked); // folding does not switch the feature off
    });

    [Fact]
    public void About_uses_the_shared_rows_and_retains_its_actions() => WpfTestThread.Run(() =>
    {
        var page = new Chippi.App.Settings.Pages.AboutPage();
        Fit(page);
        var card = ((StackPanel)page.Content).Children.OfType<Card>().Single();
        var rows = card.Children.OfType<SettingRow>().ToArray();
        Assert.Equal(new[] { "License", "Privacy", "Third-party notices", "VB-CABLE" }, rows.Select(r => r.Header));
        Assert.Equal(new double[] { 60, 60, 48, 60 }, rows.Select(r => r.ActualHeight));
        Assert.Equal("View", Assert.IsType<Button>(rows[0].Content).Content);
        Assert.Equal("View", Assert.IsType<Button>(rows[2].Content).Content);
        Assert.Equal("Donate", Assert.IsType<Button>(rows[3].Content).Content);
    });

    private static void Fit(FrameworkElement view)
    {
        view.Measure(new Size(768, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, 768, view.DesiredSize.Height));
        view.UpdateLayout();
    }
}
