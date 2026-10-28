// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.Core.Island;
using Chippi.Features.PlainPaste;
using Chippi.UI.Controls;
namespace Chippi.Island.Tests;
public sealed class PlainPasteGlyphTests
{
    [Theory]
    [InlineData(false, "Plain text copied")]
    [InlineData(true, "No text copied")]
    [InlineData(true, "Clipboard busy")]
    public void Feedback_has_the_right_glyph_color_and_name_without_a_dot_or_page_activity(bool caution, string name) => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetActivities([new IslandActivity(PlainPasteModule.FeedbackKey, IslandActivityKind.Status, name, "") { ModuleId = PlainPasteModule.Id, NeedsAttention = caution }], PetMood.Idle);
            var glyph = (LucideIcon)window.FindName("PlainPasteGlyph");
            Assert.Equal(Visibility.Visible, glyph.Visibility);
            Assert.Equal(((SolidColorBrush)window.FindResource(caution ? "WarningBrush" : "IslandAccentBrush")).Color, ((SolidColorBrush)glyph.Brush).Color);
            Assert.Equal(name, AutomationProperties.GetName((Button)window.FindName("Tab")));
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("Dot")).Visibility);
            var pageActivities = (IReadOnlyList<IslandActivity>)typeof(IslandPanelWindow).GetField("_activities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.Panel)!;
            Assert.Empty(pageActivities);
            window.SetActivities([], PetMood.Sleepy);
            Assert.Equal(Visibility.Collapsed, glyph.Visibility);
            Assert.Equal("Chippi", AutomationProperties.GetName((Button)window.FindName("Tab")));
        }
        finally { window.Close(); }
    });
    [Fact]
    public void Feedback_temporarily_uses_the_existing_glyph_slot_and_restores_media() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            var media = new IslandActivity("track", IslandActivityKind.Media, "Invented track", "Player");
            var feedback = new IslandActivity(PlainPasteModule.FeedbackKey, IslandActivityKind.Status, "Clipboard busy", "") { ModuleId = PlainPasteModule.Id, NeedsAttention = true };
            window.SetActivities([media, feedback], PetMood.Idle);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("Equalizer")).Visibility);
            window.SetActivities([media], PetMood.Idle);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("Equalizer")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("PlainPasteGlyph")).Visibility);
        }
        finally { window.Close(); }
    });
}
