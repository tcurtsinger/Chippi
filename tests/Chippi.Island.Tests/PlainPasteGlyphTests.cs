// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.UI.Pet;
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
    public void Feedback_shows_on_the_bar_with_the_right_glyph_color_and_name_without_a_dot_or_page_activity(bool caution, string name) => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetActivities([new IslandActivity(PlainPasteModule.FeedbackKey, IslandActivityKind.Status, name, "") { ModuleId = PlainPasteModule.Id, NeedsAttention = caution }], PetMood.Idle);
            var glyph = window.GlyphIcon;
            Assert.True(window.BarShown);
            Assert.Same(LucideIcons.ClipboardType, glyph.Data);
            Assert.Equal(((SolidColorBrush)window.FindResource(caution ? "IslandCautionBrush" : "IslandAccentBrush")).Color, ((SolidColorBrush)glyph.Brush).Color);
            Assert.Equal($"Chippi, {name}", AutomationProperties.GetName(window.PuckButton));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(window.PuckButton));
            Assert.Equal(Visibility.Collapsed, window.Dot.Visibility);
            var pageActivities = (IReadOnlyList<IslandActivity>)typeof(IslandPanelWindow).GetField("_activities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.Panel)!;
            Assert.Empty(pageActivities);
            window.SetActivities([], PetMood.Sleepy);
            Assert.False(window.BarShown);
            Assert.Equal("Chippi", AutomationProperties.GetName(window.PuckButton));
        }
        finally { window.Close(); }
    });
    [Fact]
    public void Feedback_takes_the_bar_for_a_moment_and_gives_it_back_to_the_song() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetMedia(new IslandPanelTests.FakeMedia
            {
                Sessions = [new("player", "Player", "Invented track", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null)],
            });
            var media = new IslandActivity("track", IslandActivityKind.Media, "Invented track", "Player");
            var feedback = new IslandActivity(PlainPasteModule.FeedbackKey, IslandActivityKind.Status, "Clipboard busy", "") { ModuleId = PlainPasteModule.Id, NeedsAttention = true };
            window.SetActivities([media, feedback], PetMood.Idle);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("Equalizer")).Visibility);
            window.SetActivities([media], PetMood.Idle);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("Equalizer")).Visibility);
            Assert.Equal("Invented track", window.ChipText);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void Pending_close_stays_visible_over_plain_paste_until_its_second_press_deadline() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            var paste = new IslandActivity(PlainPasteModule.FeedbackKey, IslandActivityKind.Status, "Plain text copied", "")
            { ModuleId = PlainPasteModule.Id };
            var close = new IslandActivity("close-protection/pending", IslandActivityKind.Status, "Press again to close", "Alt+F4 · Notepad")
            { ModuleId = "close-protection", IsFeedback = true, Icon = nameof(LucideIcons.ShieldX) };
            window.SetActivities([paste, close], PetMood.Idle);
            Assert.Equal("Press again to close · Alt+F4 · Notepad", window.ChipText);
            Assert.Same(LucideIcons.ShieldX, window.GlyphIcon.Data);
            window.SetActivities([paste], PetMood.Idle);
            Assert.Equal("Plain text copied", window.ChipText);
        }
        finally { window.Close(); }
    });
}
