// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>The shared switch (SwitchTrackTemplate): a list that rebuilds its switches made every On knob slide in from Off (Travis, 2026-10-24).</summary>
public sealed class SwitchKnobTests
{
    private static TranslateTransform Knob(ToggleButton toggle)
    {
        toggle.ApplyTemplate();
        return (TranslateTransform)((UIElement)toggle.Template.FindName("Knob", toggle)).RenderTransform;
    }

    private static ToggleButton Switch(bool on, string style = "SwitchStyle") =>
        new() { Style = (Style)Application.Current.FindResource(style), IsChecked = on };

    [Theory]
    [InlineData("SwitchStyle")]
    [InlineData("SwitchWithLabelStyle")]
    public void A_switch_that_appears_On_is_drawn_On_with_no_slide(string style)
    {
        WpfTestThread.Run(() =>
        {
            var knob = Knob(Switch(true, style));

            Assert.Equal(SwitchKnob.Travel, knob.X);
            Assert.False(knob.HasAnimatedProperties);
        });
    }

    [Fact]
    public void A_switch_that_appears_Off_is_drawn_Off()
    {
        WpfTestThread.Run(() =>
        {
            var knob = Knob(Switch(false));

            Assert.Equal(0, knob.X);
            Assert.False(knob.HasAnimatedProperties);
        });
    }

    [Fact]
    public void Flipping_a_switch_still_slides_both_ways()
    {
        WpfTestThread.Run(() =>
        {
            var toggle = Switch(true);
            var knob = Knob(toggle);

            toggle.IsChecked = false;
            Assert.True(knob.HasAnimatedProperties);
            Assert.Equal(SwitchKnob.Travel, knob.X); // it starts where it was and slides to Off

            toggle.IsChecked = true;
            Assert.True(knob.HasAnimatedProperties);
        });
    }
}
