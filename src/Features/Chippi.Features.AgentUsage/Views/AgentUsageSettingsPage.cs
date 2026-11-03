// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using Chippi.Features.AgentUsage.Sessions;
using Chippi.UI.Controls;

namespace Chippi.Features.AgentUsage.Views;

/// <summary>
/// Settings › AI agent usage (the brief's Surfaces 5; mockup approved 2026-11-01): the page's limits first, a section per
/// agent, then Sessions, every one with Switch to (Settings can do everything the island can), then Agents, a switch each
/// with its state, then Alerts: the warning level and the two alerts' switches.
/// </summary>
public sealed class AgentUsageSettingsPage : UserControl
{
    private readonly AgentUsageModule _module;
    private readonly StackPanel _limits = new();
    private readonly StackPanel _sessions = new();
    private readonly Dictionary<AgentKind, (ToggleButton Switch, SettingRow Row)> _agents = [];
    private readonly ComboBox _level = SettingsLayout.Combo(AgentUsageSettings.Levels.Select(l => (object)$"{l}%"));
    private readonly ToggleButton _near = Switch("Near a limit");
    private readonly ToggleButton _resets = Switch("Resets");
    private readonly DispatcherTimer _minute = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
    private string _shape = "\0";
    private bool _shown, _updating;

    public AgentUsageSettingsPage(AgentUsageModule module)
    {
        _module = module;
        var body = new StackPanel();
        body.Children.Add(_limits);
        body.Children.Add(_sessions);
        body.Children.Add(SettingsLayout.Section("Agents"));
        var agents = SettingsLayout.Card();
        foreach (var kind in new[] { AgentKind.Claude, AgentKind.Codex })
        {
            var toggle = Switch(kind.Name());
            var row = SettingsLayout.Row(kind.Name(), toggle, "");
            OnFlip(toggle, () => Apply(kind == AgentKind.Claude
                ? _module.Settings with { Claude = toggle.IsChecked == true }
                : _module.Settings with { Codex = toggle.IsChecked == true }));
            _agents[kind] = (toggle, row);
            agents.Children.Add(row);
        }
        body.Children.Add(agents);
        body.Children.Add(SettingsLayout.Section("Alerts"));
        _level.Width = 120;
        AutomationProperties.SetName(_level, "Warning level");
        _level.SelectionChanged += (_, _) =>
        {
            if (!_updating && _level.SelectedIndex >= 0)
                Apply(_module.Settings with { WarningLevel = AgentUsageSettings.Levels[_level.SelectedIndex] });
        };
        OnFlip(_near, () => Apply(_module.Settings with { NearLimitAlerts = _near.IsChecked == true }));
        OnFlip(_resets, () => Apply(_module.Settings with { ResetAlerts = _resets.IsChecked == true }));
        body.Children.Add(SettingsLayout.Card(
            SettingsLayout.Row("Warning level", _level),
            SettingsLayout.Row("Near a limit", _near),
            SettingsLayout.Row("Resets", _resets)));
        Content = body;
        _minute.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => Shown(IsVisible);
        Refresh();
    }

    private void Shown(bool shown)
    {
        if (shown == _shown)
            return;
        _shown = shown;
        _module.Showing(shown);
        if (shown)
        {
            _module.Changed += OnChanged;
            _minute.Start();
            Refresh();
        }
        else
        {
            _module.Changed -= OnChanged;
            _minute.Stop();
        }
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Apply(AgentUsageSettings settings)
    {
        if (_updating || settings == _module.Settings)
            return;
        _ = _module.ApplyAsync(settings);
    }

    internal void Refresh()
    {
        var now = _module.Now;
        var settings = _module.Settings;
        var agents = _module.Agents.Where(a => a.Found && AgentLines.Rows(a).Count > 0).ToList();
        var sessions = _module.Sessions;
        string shape = string.Join('|', agents.Select(a => $"{a.Agent}:{string.Join(',', AgentLines.Rows(a).Select(w => w.Label))}"))
            + "#" + string.Join('|', sessions.Select(s => $"{s.Agent}:{s.Id}"));
        if (shape != _shape)
        {
            _shape = shape;
            Build(agents, sessions);
        }
        Fill(agents, sessions, settings.WarningLevel, now);
        _updating = true;
        try
        {
            foreach (var (kind, (toggle, row)) in _agents)
            {
                toggle.IsChecked = settings.Reads(kind);
                row.Description = _module.All.TryGetValue(kind, out var status) ? AgentLines.Caption(status) : "";
            }
            _level.SelectedIndex = AgentUsageSettings.Levels.ToList().IndexOf(settings.WarningLevel);
            _near.IsChecked = settings.NearLimitAlerts;
            _resets.IsChecked = settings.ResetAlerts;
        }
        finally { _updating = false; }
    }

    private void Build(IReadOnlyList<AgentStatus> agents, IReadOnlyList<AgentSession> sessions)
    {
        _limits.Children.Clear();
        bool first = true;
        foreach (var agent in agents)
        {
            var name = new TextBlock { Text = agent.Agent.Name(), VerticalAlignment = VerticalAlignment.Bottom };
            name.SetResourceReference(StyleProperty, "HeadlineTextStyle");
            var plan = new TextBlock { Tag = "plan", Margin = new Thickness(8, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom };
            plan.SetResourceReference(StyleProperty, "CaptionTextStyle");
            var state = new TextBlock { Tag = "state", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
            state.SetResourceReference(StyleProperty, "CaptionTextStyle");
            DockPanel.SetDock(state, Dock.Right);
            _limits.Children.Add(new DockPanel { Height = 20, Margin = new Thickness(0, first ? 0 : 24, 0, 8), Tag = agent.Agent, Children = { state, name, plan } });
            first = false;
            var card = SettingsLayout.Card();
            foreach (var window in AgentLines.Rows(agent))
            {
                var meter = new AgentsIslandView.Meter("BorderStrongBrush", "AccentBrush") { Width = 240, VerticalAlignment = VerticalAlignment.Center };
                var value = Value(40);
                var reset = Value(150);
                reset.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                var controls = new StackPanel { Orientation = Orientation.Horizontal, Children = { meter, value, reset } };
                card.Children.Add(SettingsLayout.Row(window.Label, controls));
            }
            _limits.Children.Add(card);
        }

        // Sessions opens the page when no agent has limits to list.
        _sessions.Children.Clear();
        _sessions.Children.Add(SettingsLayout.Section("Sessions", first: agents.Count == 0));
        var list = SettingsLayout.Card();
        if (sessions.Count == 0)
            list.Children.Add(SettingsLayout.Empty("No sessions"));
        foreach (var session in sessions)
            list.Children.Add(SessionRow(session));
        _sessions.Children.Add(list);
    }

    private void Fill(IReadOnlyList<AgentStatus> agents, IReadOnlyList<AgentSession> sessions, int level, DateTimeOffset now)
    {
        int section = 0;
        foreach (var agent in agents)
        {
            if (_limits.Children[section * 2] is DockPanel head)
            {
                foreach (var text in head.Children.OfType<TextBlock>())
                {
                    if (Equals(text.Tag, "plan"))
                        text.Text = agent.Plan ?? "";
                    else if (Equals(text.Tag, "state"))
                        text.Text = AgentLines.State(agent, now).Text;
                }
            }
            if (_limits.Children[section * 2 + 1] is Card card)
            {
                var windows = AgentLines.Rows(agent);
                for (int i = 0; i < windows.Count && i < card.Children.Count; i++)
                {
                    if (card.Children[i] is not SettingRow { Content: StackPanel { Children: [AgentsIslandView.Meter meter, TextBlock value, TextBlock reset] } })
                        continue;
                    var window = windows[i];
                    bool hasReset = window.HasReset(now);
                    bool hot = !hasReset && window.Used >= level;
                    meter.Set(hasReset ? 0 : window.Used / 100, hot);
                    value.Text = AgentText.Value(window, now);
                    value.SetResourceReference(TextBlock.ForegroundProperty, hot ? "WarningTextBrush" : "TextPrimaryBrush");
                    reset.Text = AgentText.ResetsLong(window, now);
                }
            }
            section++;
        }
        if (_sessions.Children is [_, Card list])
        {
            for (int i = 0; i < sessions.Count && i < list.Children.Count; i++)
                if (list.Children[i] is FrameworkElement { Tag: SessionParts parts })
                    parts.Fill(sessions[i], _module.AppOf(sessions[i]), now);
        }
    }

    private sealed class SessionParts(TextBlock name, TextBlock app, TextBlock state, Button switchTo)
    {
        public AgentSession? Session { get; private set; }

        public void Fill(AgentSession session, string appName, DateTimeOffset now)
        {
            Session = session;
            name.Text = session.Name;
            app.Text = appName;
            state.Text = session.State(now);
            state.SetResourceReference(TextBlock.ForegroundProperty, session.NeedsInput ? "WarningTextBrush" : "TextSecondaryBrush");
            AutomationProperties.SetName(switchTo, $"Switch to {session.Name}");
        }
    }

    // The agent's letters, the session's name over the app it runs in, its state, then Switch to.
    private FrameworkElement SessionRow(AgentSession session)
    {
        var letters = new TextBlock { Text = session.Agent.Monogram(), FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letters.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var mono = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Child = letters, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        mono.SetResourceReference(Border.BackgroundProperty, "ChipBrush"); // as Add features' icon chips
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(StyleProperty, "BodyTextStyle");
        var app = new TextBlock();
        app.SetResourceReference(StyleProperty, "CaptionTextStyle");
        var state = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        state.SetResourceReference(StyleProperty, "BodyTextStyle");
        Typography.SetNumeralAlignment(state, FontNumeralAlignment.Tabular);
        var switchTo = new Button { Content = "Switch to", VerticalAlignment = VerticalAlignment.Center };
        switchTo.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        var parts = new SessionParts(name, app, state, switchTo);
        switchTo.Click += (_, _) =>
        {
            if (parts.Session is { } s && !_module.SwitchTo(s))
                state.Text = "Switch failed";
        };
        var grid = new Grid();
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, app } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(state, 2);
        Grid.SetColumn(switchTo, 3);
        grid.Children.Add(mono);
        grid.Children.Add(text);
        grid.Children.Add(state);
        grid.Children.Add(switchTo);
        var row = new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = SettingsLayout.RowPadding, Child = grid, Tag = parts };
        parts.Fill(session, _module.AppOf(session), _module.Now);
        return row;
    }

    private static TextBlock Value(double width)
    {
        var block = new TextBlock { Width = width, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    private static ToggleButton Switch(string name)
    {
        var toggle = new ToggleButton();
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    // A UI Automation toggle raises no Click, so the switches act on Checked and Unchecked (AGENTS.md).
    private void OnFlip(ToggleButton toggle, Action flipped)
    {
        toggle.Checked += (_, _) => { if (!_updating) flipped(); };
        toggle.Unchecked += (_, _) => { if (!_updating) flipped(); };
    }
}
