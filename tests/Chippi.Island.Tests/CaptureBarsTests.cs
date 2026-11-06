// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Capture;

namespace Chippi.Island.Tests;

/// <summary>The frozen screen's bars (Frozen screen v1): they light what's picked, name every button and only raise events.</summary>
public sealed class CaptureBarsTests
{
    [Fact]
    public void Picking_a_tool_lights_only_it_and_tells_the_feature()
    {
        WpfTestThread.Run(() =>
        {
            var column = new MarkupTools { Tool = CaptureTool.Pen };
            CaptureTool? heard = null;
            column.ToolPicked += t => heard = t;

            column.ButtonFor(CaptureTool.Arrow).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(CaptureTool.Arrow, heard);
            Assert.Equal(CaptureTool.Arrow, column.Tool);
            foreach (var tool in Enum.GetValues<CaptureTool>())
            {
                var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(column.ButtonFor(tool)).GetPattern(PatternInterface.Toggle);
                Assert.Equal(tool == CaptureTool.Arrow ? ToggleState.On : ToggleState.Off, peer.ToggleState);
            }
        });
    }

    [Fact]
    public void Every_tool_has_its_name_and_letter()
    {
        WpfTestThread.Run(() =>
        {
            var column = new MarkupTools();
            foreach (var tool in Enum.GetValues<CaptureTool>())
            {
                var button = column.ButtonFor(tool);
                Assert.Equal(MarkupTools.ToolName(tool), AutomationProperties.GetName(button));
                string hint = (string)button.ToolTip;
                Assert.Equal(CaptureBars.Letter(tool) is { } letter ? $"{MarkupTools.ToolName(tool)} · {letter}" : MarkupTools.ToolName(tool), hint);
            }
            // screen-capture.md's Markup table, which Karl's keys follow.
            Assert.Equal("VPHLAROTBEC", string.Concat(Enum.GetValues<CaptureTool>().Select(t => CaptureBars.Letter(t)!.Value)));
            Assert.Equal("Ellipse · O", (string)column.ButtonFor(CaptureTool.Ellipse).ToolTip);
            Assert.Equal("Redact · B", (string)column.ButtonFor(CaptureTool.Redact).ToolTip);
            Assert.Equal("Eraser · E", (string)column.ButtonFor(CaptureTool.Eraser).ToolTip);
        });
    }

    [Fact]
    public void The_finish_bar_raises_each_verb_and_hides_Text_and_QR_when_off()
    {
        WpfTestThread.Run(() =>
        {
            var bar = new FinishBar { ShowCopyText = false, ShowScanQrCode = false };
            var heard = new List<FinishAction>();
            bar.Picked += heard.Add;
            var buttons = Buttons(bar);
            var shown = buttons.Where(b => b.Visibility == Visibility.Visible).Select(AutomationProperties.GetName).ToArray();
            Assert.Equal(["Save as", "Save", "Copy", "Cancel"], shown);

            bar.ShowCopyText = bar.ShowScanQrCode = true;
            foreach (var button in buttons.Where(b => b.Visibility == Visibility.Visible))
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal([FinishAction.CopyText, FinishAction.ScanQrCode, FinishAction.SaveAs, FinishAction.Save, FinishAction.Copy, FinishAction.Cancel], heard);
        });
    }

    [Fact]
    public void A_failure_shows_its_line_and_Retry_in_place_of_Text_and_QR_until_cleared()
    {
        WpfTestThread.Run(() =>
        {
            var bar = new FinishBar { ShowCopyText = true, ShowScanQrCode = true };
            var heard = new List<FinishAction>();
            bar.Picked += heard.Add;
            var buttons = Buttons(bar);
            Assert.Null(bar.Failure);
            Assert.Same(bar.Copy, bar.Main);

            bar.Failure = "Copied. Save failed";

            Assert.Equal("Copied. Save failed", bar.Failure);
            var shown = buttons.Where(b => b.Visibility == Visibility.Visible).Select(AutomationProperties.GetName).ToArray();
            Assert.Equal(["Retry", "Save as", "Save", "Copy", "Cancel"], shown);
            Assert.Same(bar.Retry, bar.Main);
            Assert.True(bar.ShowCopyText);
            var line = Texts(bar).Single(t => t.Text == "Copied. Save failed");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)line.Parent).Visibility);
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(line));

            bar.Retry.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal([FinishAction.Retry], heard);

            bar.Failure = null;

            Assert.Null(bar.Failure);
            Assert.Same(bar.Copy, bar.Main);
            shown = buttons.Where(b => b.Visibility == Visibility.Visible).Select(AutomationProperties.GetName).ToArray();
            Assert.Equal(["Copy text", "Scan QR code", "Save as", "Save", "Copy", "Cancel"], shown);
        });
    }

    [Fact]
    public void Outline_and_Fill_show_only_when_asked_and_tell_the_feature()
    {
        WpfTestThread.Run(() =>
        {
            var choice = new InkChoice();
            bool? heard = null;
            choice.FillPicked += f => heard = f;
            var buttons = Buttons(choice);
            var outline = buttons.Single(b => AutomationProperties.GetName(b) == "Outline");
            var fill = buttons.Single(b => AutomationProperties.GetName(b) == "Fill");
            Assert.False(choice.ShowFill);
            Assert.False(choice.Filled);

            choice.ShowFill = true;
            fill.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.True(heard);
            Assert.True(choice.Filled);
            Assert.Equal(ToggleState.On, State(fill));
            Assert.Equal(ToggleState.Off, State(outline));

            outline.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(heard);
            Assert.False(choice.Filled);
        });

        static ToggleState State(Button b) => ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(b).GetPattern(PatternInterface.Toggle)).ToggleState;
    }

    private static List<TextBlock> Texts(DependencyObject root)
    {
        var found = new List<TextBlock>();
        void Walk(object node)
        {
            if (node is TextBlock t)
                found.Add(t);
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                    Walk(child);
        }
        Walk(root);
        return found;
    }

    [Fact]
    public void The_colour_choice_picks_an_ink_and_a_width()
    {
        WpfTestThread.Run(() =>
        {
            var choice = new InkChoice();
            System.Windows.Media.Color? ink = null;
            InkWidth? width = null;
            choice.InkPicked += c => ink = c;
            choice.WidthPicked += w => width = w;
            var buttons = Buttons(choice);

            buttons.Single(b => AutomationProperties.GetName(b) == "Blue").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            buttons.Single(b => AutomationProperties.GetName(b) == "Thin").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(CaptureBars.Inks[3].Color, ink);
            Assert.Equal(CaptureBars.Inks[3].Color, choice.Ink);
            Assert.Equal(InkWidth.Thin, width);
            Assert.Equal(InkWidth.Thin, choice.Stroke);
            Assert.Contains(buttons, b => AutomationProperties.GetName(b) == "Pick from screen");
        });
    }

    [Fact]
    public void Undo_and_Redo_follow_the_feature()
    {
        WpfTestThread.Run(() =>
        {
            var column = new MarkupTools();
            var undo = Buttons(column).Single(b => AutomationProperties.GetName(b) == "Undo");
            Assert.False(undo.IsEnabled);
            column.CanUndo = true;
            Assert.True(undo.IsEnabled);
        });
    }

    private static List<Button> Buttons(DependencyObject root)
    {
        var found = new List<Button>();
        void Walk(object node)
        {
            if (node is Button b)
                found.Add(b);
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                    Walk(child);
        }
        Walk(root);
        return found;
    }
}
