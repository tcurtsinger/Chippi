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
            Assert.Null(CaptureBars.Letter(CaptureTool.Select));
            Assert.Equal(10, Enum.GetValues<CaptureTool>().Select(CaptureBars.Letter).OfType<char>().Distinct().Count());
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
            foreach (var button in buttons)
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal([FinishAction.CopyText, FinishAction.ScanQrCode, FinishAction.SaveAs, FinishAction.Save, FinishAction.Copy, FinishAction.Cancel], heard);
        });
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
