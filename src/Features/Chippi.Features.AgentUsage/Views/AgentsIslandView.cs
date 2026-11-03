// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage.Views;

/// <summary>
/// The Agents page (the brief's Surfaces 2; mockup approved 2026-11-01), top to bottom and nothing scrolling: a card per
/// agent found, its name and plan, a state at the right when there is one, then a 22 row per window (name, a 4 bar in the
/// accent fill, the share, its reset); then Sessions, 28 rows that each bring the session's window forward. Whole rows
/// only: with more windows, fewer sessions fit, and Settings lists them all.
/// </summary>
public sealed class AgentsIslandView : UserControl
{
    internal const double CardChrome = 38; // 1 + 8, the 20 head, 8 + 1
    internal const double WindowRow = 24; // 2 above a 22 row
    internal const double SessionsChrome = 30; // 1 + 6, the 16 head and 2, 4 + 1
    internal const double SessionRow = 28;
    private const string Caution = "WarningTextBrush";

    private readonly AgentUsageModule _module;
    private readonly StackPanel _cards = new();
    private readonly TextBlock _none = Text("No agents found", 13, "IslandTextSecondaryBrush");
    private readonly DispatcherTimer _minute = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _failedClock = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
    private readonly List<CardParts> _cardParts = [];
    private readonly List<SessionParts> _sessionParts = [];
    private string _shape = "\0";
    private bool _shown;
    private string? _failed; // the session whose switch just failed

    private sealed record CardParts(AgentKind Agent, TextBlock Plan, TextBlock State, List<RowParts> Rows);
    private sealed record RowParts(Grid Row, TextBlock Label, Meter Meter, TextBlock Value, TextBlock Reset);
    private sealed record SessionParts(Button Row, AgentImage Icon, TextBlock Name, TextBlock State) { public AgentSession? Session { get; set; } }

    public AgentsIslandView(AgentUsageModule module)
    {
        _module = module;
        _none.HorizontalAlignment = HorizontalAlignment.Center;
        _none.Visibility = Visibility.Collapsed;
        Content = new Grid { Height = IslandPage.BodyHeight, ClipToBounds = true, Children = { _cards, _none } };
        AutomationProperties.SetName(this, "Agents");
        _minute.Tick += (_, _) => Refresh();
        _failedClock.Tick += (_, _) => { _failedClock.Stop(); _failed = null; Refresh(); };
        IsVisibleChanged += (_, _) => Shown(IsVisible);
        Refresh();
    }

    // While it shows, checks run every minute and the page follows the module; "Working · 12 min" moves on its own.
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

    /// <summary>How many whole session rows fit under <paramref name="agents"/>' cards in the 304 body.</summary>
    internal static int SessionsThatFit(IEnumerable<AgentStatus> agents, int sessions)
    {
        double room = IslandPage.BodyHeight;
        foreach (var agent in agents)
            room -= CardChrome + WindowRow * AgentLines.Rows(agent).Count + IslandPage.Gap;
        return sessions == 0 ? 0 : Math.Clamp((int)Math.Floor((room - SessionsChrome) / SessionRow), 0, sessions);
    }

    internal void Refresh()
    {
        var now = _module.Now;
        var agents = _module.Agents.Where(a => a.Found).ToList();
        var sessions = _module.Sessions;
        var shown = sessions.Take(SessionsThatFit(agents, sessions.Count)).ToList();
        string shape = string.Join('|', agents.Select(a => $"{a.Agent}:{string.Join(',', AgentLines.Rows(a).Select(w => w.Label))}")) + "#" + shown.Count;
        if (shape != _shape)
        {
            _shape = shape;
            Build(agents, shown.Count);
        }
        _none.Visibility = agents.Count == 0 && shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var enabled = Enum.GetValues<AgentKind>().Where(_module.Settings.Reads).ToArray();
        _none.Text = enabled.Length == 0 ? "Agent checks are off"
            : enabled.Any(k => !_module.All.TryGetValue(k, out var status) || status.Check == CheckState.None)
                ? "Checking agents…" : "No agents found";
        int level = _module.Settings.WarningLevel;
        for (int i = 0; i < agents.Count; i++)
            Fill(_cardParts[i], agents[i], level, now);
        for (int i = 0; i < shown.Count; i++)
            Fill(_sessionParts[i], shown[i], now);
    }

    // A new set of cards, windows or session rows builds them again; anything else changes in place, so a hover holds.
    private void Build(IReadOnlyList<AgentStatus> agents, int sessions)
    {
        _cards.Children.Clear();
        _cardParts.Clear();
        _sessionParts.Clear();
        foreach (var agent in agents)
            _cards.Children.Add(Card(agent));
        if (sessions == 0)
            return;
        var list = new StackPanel();
        var head = Text("Sessions", 12, "IslandTextBrush");
        head.FontWeight = FontWeights.SemiBold;
        head.Height = 16;
        head.Margin = new Thickness(6, 0, 6, 2);
        list.Children.Add(head);
        for (int i = 0; i < sessions; i++)
            list.Children.Add(SessionRowFor());
        // 4 in at the sides, so a row's highlight keeps its corners and its icon sits 10 in as the cards' content does.
        var card = new Border { Padding = new Thickness(3, 6, 3, 4), Child = list };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        AutomationProperties.SetName(list, "Sessions");
        _cards.Children.Add(card);
    }

    private Border Card(AgentStatus agent)
    {
        var name = Text(agent.Agent.Name(), 12, "IslandTextBrush");
        name.FontWeight = FontWeights.SemiBold;
        var plan = Text("", 11, "IslandTextTertiaryBrush");
        plan.Margin = new Thickness(6, 0, 0, 0);
        var state = Text("", 11, "IslandTextTertiaryBrush");
        state.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(state, Dock.Right);
        var icon = new AgentImage(16, agent.Agent) { Margin = new Thickness(0, 0, 6, 0) };
        var head = new DockPanel { Height = 20, LastChildFill = false, Children = { state, icon, name, plan } };
        var body = new StackPanel { Children = { head } };
        var rows = new List<RowParts>();
        foreach (var _ in AgentLines.Rows(agent))
        {
            var row = WindowRowFor();
            rows.Add(row);
            body.Children.Add(row.Row);
        }
        _cardParts.Add(new CardParts(agent.Agent, plan, state, rows));
        var card = new Border { Padding = new Thickness(9, 8, 9, 8), Child = body, Margin = new Thickness(0, 0, 0, IslandPage.Gap) };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        AutomationProperties.SetName(card, agent.Agent.Name());
        return card;
    }

    // Name 84, the bar, the share 34, the reset 104, 10 apart (the gaps are the bar's and the reset's margins).
    private static RowParts WindowRowFor()
    {
        var grid = new Grid { Height = 22, Margin = new Thickness(0, 2, 0, 0), Background = System.Windows.Media.Brushes.Transparent };
        foreach (var width in new[] { new GridLength(84), new GridLength(1, GridUnitType.Star), new GridLength(34), new GridLength(114) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var label = Text("", 12, "IslandTextSecondaryBrush");
        var meter = new Meter { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var value = Text("", 12, "IslandTextBrush");
        value.TextAlignment = TextAlignment.Right;
        var reset = Text("", 11, "IslandTextTertiaryBrush");
        reset.TextAlignment = TextAlignment.Right;
        reset.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(meter, 1);
        Grid.SetColumn(value, 2);
        Grid.SetColumn(reset, 3);
        grid.Children.Add(label);
        grid.Children.Add(meter);
        grid.Children.Add(value);
        grid.Children.Add(reset);
        return new RowParts(grid, label, meter, value, reset);
    }

    private Button SessionRowFor()
    {
        var icon = new AgentImage(20);
        var name = Text("", 12, "IslandTextBrush");
        name.Margin = new Thickness(10, 0, 10, 0);
        var state = Text("", 11, "IslandTextTertiaryBrush");
        state.TextAlignment = TextAlignment.Right;
        var grid = new Grid();
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(state, 2);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(state);
        var row = new Button { Content = grid, Height = SessionRow, Padding = new Thickness(6, 0, 6, 0) };
        row.SetResourceReference(StyleProperty, "IslandListRowStyle");
        var parts = new SessionParts(row, icon, name, state);
        row.Click += (_, _) => Switch(parts.Session);
        _sessionParts.Add(parts);
        return row;
    }

    private void Fill(CardParts card, AgentStatus agent, int level, DateTimeOffset now)
    {
        card.Plan.Text = agent.Plan ?? "";
        var (state, caution) = AgentLines.State(agent, now);
        card.State.Text = state;
        card.State.SetResourceReference(TextBlock.ForegroundProperty, caution ? Caution : "IslandTextTertiaryBrush");
        var windows = AgentLines.Rows(agent);
        for (int i = 0; i < card.Rows.Count && i < windows.Count; i++)
        {
            var (row, window) = (card.Rows[i], windows[i]);
            bool reset = window.HasReset(now);
            bool hot = !reset && window.Used >= level;
            row.Label.Text = window.Label;
            row.Meter.Set(reset ? 0 : window.Used / 100, hot);
            row.Value.Text = AgentText.Value(window, now);
            row.Value.SetResourceReference(TextBlock.ForegroundProperty, hot ? Caution : "IslandTextBrush");
            row.Reset.Text = AgentText.Resets(window, now);
            row.Row.ToolTip = AgentText.Tip(window, now);
            AutomationProperties.SetName(row.Row, $"{window.Label}, {AgentText.Value(window, now)}");
        }
    }

    private void Fill(SessionParts parts, AgentSession session, DateTimeOffset now)
    {
        parts.Session = session;
        parts.Icon.Source = AgentIcons.Source(session.Agent);
        parts.Name.Text = session.Name;
        bool failed = _failed == session.Id;
        string state = failed ? "Switch failed" : session.State(now);
        parts.State.Text = state;
        parts.State.SetResourceReference(TextBlock.ForegroundProperty, failed || session.NeedsInput ? Caution : "IslandTextTertiaryBrush");
        string app = _module.AppOf(session);
        parts.Row.ToolTip = $"{session.Name} · {state} · {app}";
        AutomationProperties.SetName(parts.Row, $"{session.Agent.Name()}, {session.Name}, {state}, {app}");
    }

    // The session's window comes forward and the island closes, as the mockup does; a window not found says so for 4 seconds.
    private void Switch(AgentSession? session)
    {
        if (session is null)
            return;
        if (_module.SwitchTo(session))
        {
            _module.CloseIsland();
            return;
        }
        _failed = session.Id;
        _failedClock.Stop();
        _failedClock.Start();
        Refresh();
    }

    private static TextBlock Text(string text, double size, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    /// <summary>A 4 tall bar: the track and the share used from the left, in the accent fill or Caution.</summary>
    internal sealed class Meter : Grid
    {
        private readonly Border _fill = new() { CornerRadius = new CornerRadius(2) };

        public Meter(string track = "IslandTrackBrush", string fill = "IslandAccentFillBrush")
        {
            Fill = fill;
            Height = 4;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var back = new Border { CornerRadius = new CornerRadius(2) };
            back.SetResourceReference(Border.BackgroundProperty, track);
            SetColumnSpan(back, 2);
            Children.Add(back);
            Children.Add(_fill);
        }

        private string Fill { get; }

        /// <summary>The share filled, 0 to 1.</summary>
        public double Value { get; private set; } = -1;

        public bool Hot { get; private set; }

        public void Set(double share, bool hot)
        {
            share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
            if (share == Value && hot == Hot)
                return;
            Value = share;
            Hot = hot;
            ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
            ColumnDefinitions[1].Width = new GridLength(1 - share, GridUnitType.Star);
            _fill.Visibility = share > 0 ? Visibility.Visible : Visibility.Collapsed;
            _fill.SetResourceReference(Border.BackgroundProperty, hot ? Caution : Fill);
        }
    }
}
