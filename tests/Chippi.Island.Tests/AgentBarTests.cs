// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.Island.Tests;

/// <summary>AI agent usage on the bar (docs/phase-5.5/ai-agent-usage.md, Surfaces 3 and 4): the agent's letters, its own lines, its alerts.</summary>
public sealed class AgentBarTests
{
    private const string Key = "agent-usage";

    private static IReadOnlyList<IslandTileGroup> AgentsTile(out IslandTileContribution tile)
    {
        tile = new IslandTileContribution(Key, new IslandTileSpec(Key, "Agents", "Gauge", 85, 0, () => new Border()));
        tile.SetStatus("Claude 42%");
        return IslandPanelTests.Tiles(tile);
    }

    private static IslandActivity Session(bool waiting) =>
        new(waiting ? "waiting" : "working", IslandActivityKind.Agent, waiting ? "Waiting on you" : "Working · 42%", waiting ? "Claude Code · packrat" : "Claude Code · chippi")
        {
            Monogram = "CC", NeedsAttention = waiting, OpensTile = Key, ModuleId = Key,
        };

    private static string? ChipKey(IslandWindow window) =>
        (string?)typeof(IslandWindow).GetField("_chipKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static Color Color(IslandWindow window, string key) => ((SolidColorBrush)window.FindResource(key)).Color;

    private sealed class ManualTimer(Action tick) : IIslandTimer
    {
        public int Starts { get; private set; }
        public bool Running { get; private set; }
        public void Start() { Starts++; Running = true; }
        public void Stop() => Running = false;
        public void Fire()
        {
            Assert.True(Running);
            Running = false;
            tick();
        }
    }

    private static Dictionary<TimeSpan, ManualTimer> Timers(IslandWindow window)
    {
        var timers = new Dictionary<TimeSpan, ManualTimer>();
        window.CreateTimer = (delay, tick) => timers[delay] = new ManualTimer(tick);
        return timers;
    }

    [Fact]
    public void The_quiet_puck_winds_down_for_a_full_minute_after_the_last_bar_panel_or_hover() =>
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var timers = Timers(window);
            window.PointerAt = () => new Point(-30000, -30000);
            var waiting = Session(waiting: true);
            window.SetActivities([waiting], PetMood.Idle);
            timers[IslandWindow.AgentPeek].Fire();
            Assert.False(window.BarShown);
            Assert.Equal(PetMood.Idle, window.Pet.Mood);
            var rest = timers[TimeSpan.FromSeconds(60)];
            Assert.True(rest.Running);

            window.SetActivities([waiting with { Source = "2 sessions" }], PetMood.Idle);
            Assert.Equal(1, rest.Starts);
            Assert.True(window.ShowPages(keyboard: false));
            IslandWindowTests.Settle(window.Panel);
            Assert.False(rest.Running);
            window.Escape();
            IslandWindowTests.Settle(window.Panel);
            Assert.True(rest.Running);
            Assert.Equal(2, rest.Starts);

            void Hover(RoutedEvent e) => window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = e });
            Hover(Mouse.MouseEnterEvent);
            Assert.False(rest.Running);
            Hover(Mouse.MouseLeaveEvent);
            Assert.Equal(3, rest.Starts);
            rest.Fire();
            Assert.Equal(PetMood.Sleepy, window.Pet.Mood);
            Assert.Equal(Visibility.Visible, window.WaitingMark.Visibility);
            window.SetActivities([waiting], PetMood.Idle);
            Assert.False(rest.Running);
            Assert.Equal(PetMood.Sleepy, window.Pet.Mood);
        }));

    [Fact]
    public void A_progress_update_does_not_restart_a_peek_and_a_deliberate_hover_recalls_the_latest_lines() =>
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var timers = Timers(window);
            var puck = window.PuckRect;
            // Stationary just outside the puck, where the right-hand widget's growing bar can trip MouseEnter.
            var pointer = new Point(puck.Left - 15, puck.Top + puck.Height / 2);
            window.PointerAt = () => pointer;
            void Hover(RoutedEvent e) => window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = e });
            window.SetTiles(AgentsTile(out _));
            var working = Session(false);
            window.SetActivities([working], PetMood.Idle);
            var peek = timers[IslandWindow.AgentPeek];
            Hover(Mouse.MouseEnterEvent);
            var updated = working with { Title = "Working · 43%", Source = "2 sessions", Monogram = "Cx" };
            window.SetActivities([updated], PetMood.Idle);
            Assert.Equal(1, peek.Starts);
            peek.Fire();
            Assert.False(window.BarShown);
            Assert.Null(ChipKey(window));
            Assert.Equal(Visibility.Collapsed, window.WaitingMark.Visibility);

            // A quick pass leaves the puck alone. A dwell reveals the current reading and leaving folds it.
            Hover(Mouse.MouseLeaveEvent);
            pointer = new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2);
            Hover(Mouse.MouseEnterEvent);
            var dwell = timers[IslandWindow.AgentHoverDelay];
            Assert.False(window.BarShown);
            Hover(Mouse.MouseLeaveEvent);
            Assert.False(dwell.Running);
            Hover(Mouse.MouseEnterEvent);
            dwell.Fire();
            Assert.True(window.BarShown);
            Assert.Equal("Working · 43% · 2 sessions", window.ChipText);
            Assert.Equal(Key, ChipKey(window));
            Hover(Mouse.MouseLeaveEvent);
            Assert.False(window.BarShown);
            window.SetActivities([working], PetMood.Idle);
            Assert.False(window.BarShown);
        }));

    [Fact]
    public void Crossing_from_a_timers_text_onto_Chippi_starts_one_dwell_and_leaving_the_bar_returns_the_timer() =>
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var timers = Timers(window);
            var puck = window.PuckRect;
            var text = new Point(puck.Left - 35, puck.Top + puck.Height / 2);
            var pet = new Point(puck.Left + puck.Width / 2, text.Y);
            var pointer = text;
            window.PointerAt = () => pointer;
            void Move(Point to, RoutedEvent e)
            {
                pointer = to;
                window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = e });
            }
            window.SetActivities([Session(true), new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer")], PetMood.Idle);
            timers[IslandWindow.AgentPeek].Fire();
            Assert.Equal("18:42 · Focus timer", window.ChipText);
            Move(text, Mouse.MouseEnterEvent);
            Assert.DoesNotContain(IslandWindow.AgentHoverDelay, timers.Keys);
            Move(pet, Mouse.PreviewMouseMoveEvent);
            var dwell = timers[IslandWindow.AgentHoverDelay];
            Move(text, Mouse.PreviewMouseMoveEvent);
            Assert.False(dwell.Running);
            Move(pet, Mouse.PreviewMouseMoveEvent);
            Move(new Point(pet.X + 2, pet.Y), Mouse.PreviewMouseMoveEvent);
            Assert.Equal(2, dwell.Starts);
            dwell.Fire();
            Move(text, Mouse.PreviewMouseMoveEvent);
            Assert.Equal(Key, ChipKey(window));
            Move(text, Mouse.MouseLeaveEvent);
            Assert.Equal("18:42 · Focus timer", window.ChipText);
        }));

    [Fact]
    public void Waiting_previews_then_yields_to_the_next_activity_or_leaves_an_accessible_mark() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            var timers = Timers(window);
            var working = Session(false);
            var waiting = Session(true);
            var timer = new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer");
            var mic = new IslandActivity("mic", IslandActivityKind.Status, "Muted", "Microphone") { ModuleId = "mic-mute", NeedsAttention = true };
            window.SetActivities([working], PetMood.Idle);
            window.SetActivities([waiting, timer, working], PetMood.Idle);
            var peek = timers[IslandWindow.AgentPeek];
            Assert.Equal(2, peek.Starts);
            Assert.Equal(Key, ChipKey(window));
            peek.Fire();
            Assert.True(window.BarShown);
            Assert.Equal("18:42 · Focus timer", window.ChipText);
            Assert.Equal(Visibility.Collapsed, window.WaitingMark.Visibility);

            window.SetActivities([waiting, mic, working], PetMood.Idle);
            Assert.False(window.BarShown);
            Assert.Equal(Visibility.Visible, window.WaitingMark.Visibility);
            Assert.Equal("Microphone muted. Agent waiting on you", AutomationProperties.GetHelpText(window.PuckButton));
            window.SetActivities([working], PetMood.Idle);
            Assert.True(window.BarShown);
            Assert.Equal(3, peek.Starts);
            peek.Fire();
            Assert.Equal(Visibility.Collapsed, window.WaitingMark.Visibility);
            Assert.Empty(AutomationProperties.GetHelpText(window.PuckButton));
            window.SetActivities([], PetMood.Sleepy);
            window.SetActivities([working], PetMood.Idle);
            Assert.True(window.BarShown);
            Assert.Equal(4, peek.Starts);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false, "IslandAccentBrush", "Working · 42% · Claude Code · chippi")]
    [InlineData(true, "IslandCautionBrush", "Waiting on you · Claude Code · packrat")]
    public void An_agent_leads_with_its_letters_over_its_own_lines_and_opens_Agents(bool waiting, string brush, string lines) => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetActivities([Session(waiting)], PetMood.Idle);

            Assert.True(window.BarShown);
            Assert.Equal(Visibility.Visible, window.Monogram.Visibility);
            Assert.Equal("CC", window.Monogram.Text);
            Assert.Equal(Color(window, brush), ((SolidColorBrush)window.Monogram.Foreground).Color);
            Assert.Equal(Visibility.Collapsed, window.GlyphIcon.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Ring.Visibility);
            // Its own lines, not the tile's status over its name.
            Assert.Equal(lines, window.ChipText);
            Assert.Equal(Key, ChipKey(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_glyph_after_the_letters_hides_them() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetActivities([Session(waiting: false)], PetMood.Idle);
            window.SetActivities([new IslandActivity("t", IslandActivityKind.Timer, "Focus", "24:10")], PetMood.Idle);
            Assert.Equal(Visibility.Collapsed, window.Monogram.Visibility);
            Assert.Equal(Visibility.Visible, window.Ring.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void An_alert_shows_over_a_song_with_the_gauge_and_gives_the_bar_back() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetMedia(new IslandPanelTests.FakeMedia
            {
                Sessions = [new("player", "Player", "Invented track", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null)],
            });
            var media = new IslandActivity("track", IslandActivityKind.Media, "Invented track", "Player");
            var alert = new IslandActivity("alert", IslandActivityKind.Status, "80% used", "Claude Code · 5-hour")
            {
                IsFeedback = true, NeedsAttention = true, OpensTile = Key, ModuleId = Key,
            };
            window.SetActivities([media, alert], PetMood.Idle);

            Assert.Same(LucideIcons.Gauge, window.GlyphIcon.Data);
            Assert.Equal(Color(window, "IslandCautionBrush"), ((SolidColorBrush)window.GlyphIcon.Brush).Color);
            Assert.Equal("80% used · Claude Code · 5-hour", window.ChipText);
            Assert.Equal(Key, ChipKey(window));

            window.SetActivities([media], PetMood.Idle);
            Assert.Equal("Invented track", window.ChipText);
        }
        finally { window.Close(); }
    });

#if DEBUG
    /// <summary>The mockup's four bars (agent-bar-&lt;theme&gt;-*.png), when CHIPPI_ISLAND_SHOTS names a folder; the widget shows on the primary display.</summary>
    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        System.IO.Directory.CreateDirectory(folder);
        var backdrop = WpfTestThread.ShotsTheme == "Light" ? System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3) : System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20);
        string theme = WpfTestThread.ShotsTheme.ToLowerInvariant();
        IslandActivity Alert(string title, bool near) => new("alert", IslandActivityKind.Status, title, "Claude Code · 5-hour")
        {
            IsFeedback = true, NeedsAttention = near, OpensTile = Key, ModuleId = Key,
        };
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var timers = Timers(window);
            window.PointerAt = () => new Point(-20000, -20000);
            window.SetTiles(AgentsTile(out _));
            foreach (var (name, activity) in new[] { ("working", Session(false)), ("waiting", Session(true)), ("near", Alert("80% used", true)), ("reset", Alert("Reset", false)) })
            {
                window.SetActivities([activity], PetMood.Idle);
                IslandPanelTests.Pump(TimeSpan.FromMilliseconds(500));
                window.SaveScene(System.IO.Path.Combine(folder, $"agent-bar-{theme}-{name}.png"), backdrop);
                if (activity.Kind == IslandActivityKind.Agent)
                {
                    timers[IslandWindow.AgentPeek].Fire();
                    IslandPanelTests.Pump(TimeSpan.FromMilliseconds(300));
                    window.SaveScene(System.IO.Path.Combine(folder, $"agent-puck-{theme}-{name}.png"), backdrop);
                }
            }
            window.SetActivities([], PetMood.Sleepy);
        }));
    }
#endif

    [Fact]
    public void Plain_pastes_feedback_keeps_its_glyph_and_one_line() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            var plain = new IslandActivity("plain-paste", IslandActivityKind.Status, "Plain text copied", "") { ModuleId = "paste-plain-text" };
            window.SetActivities([plain], PetMood.Idle);
            Assert.Same(LucideIcons.ClipboardType, window.GlyphIcon.Data);
            Assert.Equal("Plain text copied", window.ChipText);
            Assert.Null(ChipKey(window));

            // An agent's alert that comes during it waits: Plain paste's line keeps the bar.
            var alert = new IslandActivity("alert", IslandActivityKind.Status, "80% used", "Claude Code · 5-hour") { IsFeedback = true, OpensTile = Key, ModuleId = Key };
            window.SetActivities([alert, plain], PetMood.Idle);
            Assert.Equal("Plain text copied", window.ChipText);
            window.SetActivities([alert], PetMood.Idle);
            Assert.Equal("80% used · Claude Code · 5-hour", window.ChipText);
        }
        finally { window.Close(); }
    });
}
