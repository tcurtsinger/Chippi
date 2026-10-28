// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Island;
using Chippi.Core.Modules;
namespace Chippi.Island.Tests;

public sealed class PageHookTests
{
    [Fact]
    public void Shared_page_has_one_custom_header_and_reuses_sections_without_parent_conflicts() => WpfTestThread.Run(() =>
    {
        var panel = new IslandPanelWindow();
        try
        {
            var header = new TextBlock { Text = "Custom commands" };
            var first = new Border(); var second = new Border();
            var parts = new[] {
                new IslandTileContribution("first", new IslandTileSpec("shared", "Shared", "NotebookPen", 80, 0, () => first) { CreateHeader = () => header }),
                new IslandTileContribution("second", new IslandTileSpec("shared", "Shared", "NotebookPen", 80, 1, () => second) { CreateHeader = () => new TextBlock { Text = "Must not duplicate" } }),
            };
            panel.SetTiles(IslandTileContribution.Group(parts));
            var attach = typeof(IslandPanelWindow).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)!;
            attach.Invoke(panel, ["shared"]);
            var page = (StackPanel)((ContentControl)panel.FindName("Page")).Content;
            Assert.Equal(3, page.Children.Count); Assert.Same(header, page.Children[0]);
            Assert.Same(first, page.Children[1]); Assert.Same(second, page.Children[2]);
            attach.Invoke(panel, [null]); attach.Invoke(panel, ["shared"]);
            page = (StackPanel)((ContentControl)panel.FindName("Page")).Content;
            Assert.Equal(3, page.Children.Count); Assert.Same(header, page.Children[0]);
        }
        finally { panel.Close(); }
    });

    [Fact]
    public void Initial_focus_is_requested_only_when_the_host_takes_the_keyboard() => WpfTestThread.Run(() =>
    {
        var panel = new IslandPanelWindow();
        int requests = 0;
        try
        {
            var editor = new TextBox();
            var part = new IslandTileContribution("notes", new IslandTileSpec("notes", "Notes", "NotebookPen", 80, 0, () => editor) { InitialFocus = () => { requests++; return editor; } });
            panel.SetTiles(IslandTileContribution.Group([part]));
            typeof(IslandPanelWindow).GetField("_page", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, "notes");
            typeof(IslandPanelWindow).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, ["notes"]);
            Assert.Equal(0, requests);
            typeof(IslandPanelWindow).GetMethod("FocusFirst", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);
            Assert.Equal(1, requests);
        }
        finally { panel.Close(); }
    });
}
