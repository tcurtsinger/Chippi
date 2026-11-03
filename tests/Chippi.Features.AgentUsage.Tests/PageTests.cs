// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Features.AgentUsage.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class PageTests
{
    private static AgentStatus Agent(AgentKind kind, int windows) =>
        new(kind, true, CheckState.Ok, new UsageReading("Max", [.. Enumerable.Range(0, windows).Select(i => new UsageWindow($"W{i}", 10, null))], Harness.Start), true, "Max");

    [Fact]
    public void Whole_session_rows_only_fewer_with_per_model_windows()
    {
        // The mockup's two states: Claude's two windows and Codex's one leave room for 3 rows; with Opus and Sonnet, 2.
        Assert.Equal(3, AgentsIslandView.SessionsThatFit([Agent(AgentKind.Claude, 2), Agent(AgentKind.Codex, 1)], 4));
        Assert.Equal(2, AgentsIslandView.SessionsThatFit([Agent(AgentKind.Claude, 4), Agent(AgentKind.Codex, 1)], 4));
        Assert.Equal(1, AgentsIslandView.SessionsThatFit([Agent(AgentKind.Claude, 2)], 1));
        Assert.Equal(0, AgentsIslandView.SessionsThatFit([Agent(AgentKind.Claude, 2)], 0));
    }

    private static Harness Busy(int sessions)
    {
        var h = new Harness(p =>
        {
            p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(42, 23);
            p.Scripts[AgentKind.Codex] = () => Answers.Codex();
        });
        for (int i = 0; i < sessions; i++)
        {
            int pid = 100 + i;
            h.Processes.Running[pid] = null;
            h.Write($".claude/sessions/{pid}.json",
                $$"""{"pid":{{pid}},"sessionId":"s{{pid}}","name":"session {{i}}","status":"{{(i % 2 == 0 ? "busy" : i == 1 ? "idle" : "waiting")}}","statusUpdatedAt":{{Harness.Start.AddMinutes(-i).ToUnixTimeMilliseconds()}},"entrypoint":"claude-desktop"}""");
        }
        h.Step(UsageSchedule.First);
        return h;
    }

    internal static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }

    private static IEnumerable<string> Texts(DependencyObject root) => Find<TextBlock>(root).Select(t => t.Text);

    [Fact]
    public void The_page_shows_a_card_per_agent_then_the_sessions_that_fit()
    {
        WpfTestThread.Run(() =>
        {
            using var h = Busy(4);
            var view = new AgentsIslandView(h.Module);

            var texts = Texts(view).ToList();
            Assert.Contains("Claude Code", texts);
            Assert.Contains("Codex", texts);
            Assert.Contains("Max", texts);
            Assert.Contains("Pro", texts);
            Assert.Contains("42%", texts);
            Assert.Contains("9%", texts);
            Assert.Contains("Sessions", texts);
            var rows = Find<Button>(view).ToList();
            Assert.Equal(3, rows.Count); // of 4
            Assert.Contains("Idle", texts);
            Assert.Contains("Working · 1 min", texts);
            Assert.Contains("Claude", texts);
        });
    }

    [Fact]
    public void Clicking_a_session_brings_its_window_forward_and_closes_the_island()
    {
        WpfTestThread.Run(() =>
        {
            using var h = Busy(1);
            h.Processes.Apps[100] = new Sessions.SessionApp(77, "Terminal");
            var view = new AgentsIslandView(h.Module);
            var row = Assert.Single(Find<Button>(view));

            row.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal([77], h.Processes.Brought);
            Assert.Equal(1, h.Closed);
        });
    }

    [Fact]
    public void With_neither_agent_the_page_says_so()
    {
        WpfTestThread.Run(() =>
        {
            using var h = new Harness();
            h.Step(UsageSchedule.First);
            var view = new AgentsIslandView(h.Module);
            var none = Find<TextBlock>(view).Single(t => t.Text == "No agents found");
            Assert.Equal(Visibility.Visible, none.Visibility);
            Assert.Empty(Find<Button>(view));
        });
    }

    [Fact]
    public void The_empty_page_distinguishes_detection_disabled_and_missing_agents() => WpfTestThread.Run(() =>
    {
        using var h = new Harness();
        var view = new AgentsIslandView(h.Module);
        Assert.Contains("Checking agents…", Texts(view));
        h.Module.ApplyAsync(h.Module.Settings with { Claude = false, Codex = false }).GetAwaiter().GetResult();
        h.Step();
        view.Refresh();
        Assert.Contains("Agent checks are off", Texts(view));
    });

    [Fact]
    public void Installed_agents_show_before_the_first_usage_check() => WpfTestThread.Run(() =>
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => Answers.Codex());
        var view = new AgentsIslandView(h.Module);
        Assert.Contains("Codex", Texts(view));
        Assert.Contains("Checking…", Texts(view));
    });

    [Fact]
    public void Settings_lists_every_session_and_each_agents_state()
    {
        WpfTestThread.Run(() =>
        {
            using var h = Busy(5);
            var page = (AgentUsageSettingsPage)h.Ui.SettingsPage!(null);

            var texts = Texts(page).ToList();
            Assert.Equal(5, Find<Button>(page).Count(b => Equals(b.Content, "Switch to")));
            Assert.Contains("Claude Code", texts);
            var rows = Find<UI.Controls.SettingRow>(page).ToList();
            Assert.Equal("Signed in · Max", rows.Single(r => r.Header == "Claude Code").Description);
            Assert.Equal("Signed in · Pro", rows.Single(r => r.Header == "Codex").Description);
        });
    }

    [Fact]
    public void Settings_switches_turn_an_agent_off_and_set_the_alerts()
    {
        WpfTestThread.Run(() =>
        {
            using var h = Busy(0);
            var page = (AgentUsageSettingsPage)h.Ui.SettingsPage!(null);
            var rows = Find<UI.Controls.SettingRow>(page).ToList();

            ((ToggleButton)rows.Single(r => r.Header == "Codex").Content).IsChecked = false;
            Assert.False(h.Module.Settings.Codex);
            ((ToggleButton)rows.Single(r => r.Header == "Resets").Content).IsChecked = false;
            Assert.False(h.Module.Settings.ResetAlerts);
            ((ComboBox)rows.Single(r => r.Header == "Warning level").Content).SelectedIndex = 8;
            Assert.Equal(90, h.Module.Settings.WarningLevel);
            Assert.Equal(90, h.Settings.Get<AgentUsageSettings>().WarningLevel);
        });
    }
}
