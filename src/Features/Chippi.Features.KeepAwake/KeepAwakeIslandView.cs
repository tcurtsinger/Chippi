// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.Features.KeepAwake;

/// <summary>
/// Keep awake's island page (DESIGN.md §5 Island pages). On top, Chippi (asleep while it's off, wide awake while it runs)
/// beside the state ("58 min left") and its detail ("Until 7:42 PM"), with Stop while it runs. Under it the lengths:
/// 30 min · 1 hr · 2 hr · 4 hr in a row, then No limit and Until…. A card starts its length and the lit one stops it;
/// Until… swaps the cards for a time picker. The module owns the state.
/// </summary>
public sealed class KeepAwakeIslandView : UserControl
{
    private const string UntilLabel = "Until…";
    private const int MinuteStep = 5;

    private readonly KeepAwakeModule _module;
    // His body starts 10 into his 72 box (17 of its 120 units), so he sits 10 out to line up with the cards' edge.
    private readonly ChippiPet _pet = new() { Size = 72, Mood = PetMood.Sleepy, IsDecorative = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-10, 0, 0, 0) };
    private readonly TextBlock _state = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Button _stop = new() { Content = "Stop", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly StackPanel _choices = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Card> _cards = [];
    private readonly DockPanel _picker = new() { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hour = Value(), _minute = Value(), _half = Value();
    private readonly TextBlock _day = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly FrameworkElement _hourColumn;
    private readonly bool _twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
    // While the picker shows, its day is checked again as each minute turns: Today becomes Tomorrow once the time passes.
    private readonly DispatcherTimer _dayClock = new(DispatcherPriority.Background);
    private int _untilMinute;
    private int _changes; // changes made from here; only the latest one's outcome shows
    private bool _failed; // the latest change from here failed, beyond what the module reports

    private sealed record Card(ToggleButton Button, KeepAwakeSettings? Length, string Label);

    public KeepAwakeIslandView(KeepAwakeModule module)
    {
        _module = module;

        // The top row: Chippi, the state over its detail (or "Keep awake failed"), and Stop while it runs.
        Typography.SetNumeralAlignment(_state, FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(_detail, FontNumeralAlignment.Tabular);
        AutomationProperties.SetLiveSetting(_state, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_detail, AutomationLiveSetting.Polite);
        _stop.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _stop.Click += (_, _) => Run(_module.StopSessionAsync());
        var readout = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 12, 0), Children = { _state, _detail } };
        var top = new Grid { Height = 72 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(readout, 1);
        Grid.SetColumn(_stop, 2);
        top.Children.Add(_pet);
        top.Children.Add(readout);
        top.Children.Add(_stop);

        // The lengths, grouped by meaning: the four durations in a row, the two open-ended choices under them. Each row's
        // cards are 8 apart (the row runs 8 past its last); the arrow keys move among all six.
        var durations = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -8, 0) };
        foreach (var (label, length) in KeepAwakeText.Lengths.Where(l => l.Length.Mode == AwakeMode.Duration))
            durations.Children.Add(Chip(label, length));
        var open = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, -8, 0) };
        foreach (var (label, length) in KeepAwakeText.Lengths.Where(l => l.Length.Mode != AwakeMode.Duration))
            open.Children.Add(Chip(label, length));
        open.Children.Add(Chip(UntilLabel, null));
        _choices.Children.Add(durations);
        _choices.Children.Add(open);
        AutomationProperties.SetName(_choices, "Keep awake length");
        KeyboardNavigation.SetDirectionalNavigation(_choices, KeyboardNavigationMode.Contained);

        // Until…: hour, minute (5-minute steps) and AM or PM, each stepped by its arrows, the wheel or the arrow keys, then the
        // day it lands on, in one row in the cards' place on the page's edges: the hour under Chippi, Cancel and Start on the
        // right under Stop. Enter on a column starts. Minute precision is in Settings.
        _hourColumn = Column(_hour, "Hour", StepHour);
        var separator = Value();
        separator.Text = CultureInfo.CurrentCulture.DateTimeFormat.TimeSeparator;
        separator.Width = double.NaN;
        var time = new StackPanel { Orientation = Orientation.Horizontal, Children = { _hourColumn, separator } };
        time.Children.Add(Column(_minute, "Minute", up => Step(Minute(_untilMinute) + (up ? MinuteStep : -MinuteStep), 60, minute => _untilMinute / 60 * 60 + minute)));
        if (_twelveHour)
            time.Children.Add(Column(_half, "AM or PM", _ => _untilMinute = (_untilMinute + 720) % 1440));
        _day.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var cancel = new Button { Content = "Cancel", VerticalAlignment = VerticalAlignment.Center };
        cancel.SetResourceReference(StyleProperty, "IslandButtonStyle");
        cancel.Click += (_, _) => CloseUntil();
        var start = new Button { Content = "Start", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        start.SetResourceReference(StyleProperty, "IslandAccentButtonStyle");
        start.Click += (_, _) => StartUntil();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, start } }; // Tab reaches them as they read
        DockPanel.SetDock(actions, Dock.Right);
        _picker.Children.Add(time);
        _picker.Children.Add(_day);
        _picker.Children.Add(actions);
        AutomationProperties.SetName(_picker, "End time");
        _dayClock.Tick += (_, _) => ShowUntil();

        // 24 between the top row and the lengths; the lengths' room (two 52 rows) holds the picker too, so nothing moves.
        var lengths = new Grid { Height = 112, Margin = new Thickness(0, 24, 0, 0), Children = { _choices, _picker } };
        Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, lengths } };
        AutomationProperties.SetName(this, "Keep awake");

        // The module ticks with its tile (twice a minute while a timed session counts down), so the page and the tile agree.
        Loaded += (_, _) => { _module.Changed += Refresh; _module.Finished += Celebrate; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _module.Finished -= Celebrate; CloseUntil(); };
        Refresh();
    }

    /// <summary>The clock the picker's day reads; tests set it.</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    private static TextBlock Value() => new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Width = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private ToggleButton Chip(string label, KeepAwakeSettings? length)
    {
        var chip = new ToggleButton { Content = label, Tag = label, Height = 52, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) };
        chip.SetResourceReference(StyleProperty, "IslandChoiceStyle");
        OnFlip(chip, () => OnChip(chip, length));
        _cards.Add(new(chip, length, label));
        return chip;
    }

    /// <summary>
    /// One of the picker's columns: its value between an up and a down arrow. It takes the keyboard focus (the arrows
    /// don't, so Tab goes column to column); Up and Down step it as its arrows do, and the wheel a step per notch.
    /// </summary>
    private Border Column(TextBlock value, string name, Action<bool> step)
    {
        Button Arrow(bool up)
        {
            var glyph = new LucideIcon { Data = up ? LucideIcons.Up : LucideIcons.Down, Size = 14 };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
            var arrow = new Button { Content = glyph, Focusable = false, HorizontalAlignment = HorizontalAlignment.Center };
            arrow.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
            AutomationProperties.SetName(arrow, $"{name} {(up ? "up" : "down")}");
            arrow.Click += (_, _) => Stepped(up);
            return arrow;
        }
        void Stepped(bool up)
        {
            step(up);
            ShowUntil();
        }
        value.Margin = new Thickness(0, 2, 0, 2);
        var column = new Border { Focusable = true, Background = System.Windows.Media.Brushes.Transparent, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 0, 4, 0) };
        column.Child = new StackPanel { Children = { Arrow(up: true), value, Arrow(up: false) } };
        column.SetResourceReference(FocusVisualStyleProperty, "IslandFocusVisual");
        AutomationProperties.SetName(column, name);
        column.MouseLeftButtonDown += (_, _) => column.Focus();
        column.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
            {
                Stepped(e.Key == Key.Up);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                StartUntil();
                e.Handled = true;
            }
        };
        // A touchpad or a free-spinning wheel reports a notch in small parts: they add up to a step, so a swipe doesn't
        // spin the hours. Turning back starts the count again.
        int wheel = 0;
        column.MouseWheel += (_, e) =>
        {
            if (Math.Sign(e.Delta) != Math.Sign(wheel))
                wheel = 0;
            wheel += e.Delta;
            for (; Math.Abs(wheel) >= Mouse.MouseWheelDeltaForOneLine; wheel -= Math.Sign(wheel) * Mouse.MouseWheelDeltaForOneLine)
                Stepped(wheel > 0);
            e.Handled = true;
        };
        return column;
    }

    private static int Minute(int minuteOfDay) => minuteOfDay % 60;

    // A column loops on its own (59 to 00 keeps the hour; 12 to 1 keeps AM or PM), as Windows' time picker does.
    private void Step(int value, int count, Func<int, int> apply) => _untilMinute = apply((value % count + count) % count);

    private void StepHour(bool up)
    {
        int hour = _untilMinute / 60, delta = up ? 1 : -1;
        hour = _twelveHour ? (hour % 12 + delta + 12) % 12 + (hour >= 12 ? 12 : 0) : (hour + delta + 24) % 24;
        _untilMinute = hour * 60 + Minute(_untilMinute);
    }

    /// <summary>
    /// Runs <paramref name="changed"/> when the button's state flips by mouse, keyboard or UI
    /// Automation (whose Toggle raises no Click). Handlers compare with the module's state, so
    /// Refresh writing that state back does nothing.
    /// </summary>
    internal static void OnFlip(ToggleButton button, Action changed)
    {
        button.Checked += (_, _) => changed();
        button.Unchecked += (_, _) => changed();
    }

    private bool IsSelected(KeepAwakeSettings? length) => _module.Active is { } active && (length is null
        ? active.Mode == AwakeMode.UntilTime
        : KeepAwakeText.SameLength(active, length));

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var now = DateTimeOffset.Now;
        bool on = _module.IsOn, failed = _failed || _module.Error is not null;
        // The state is the tile's ("58 min left", "Until 6:00 PM", "No limit", "Off"); the detail is the other half of a
        // timed session: when a length ends, or how long until an Until time. A failure takes the detail's place in Caution,
        // under the state it explains.
        _state.Text = _module.TileStatus(now);
        _state.SetResourceReference(TextBlock.ForegroundProperty, on ? "IslandTextBrush" : "IslandTextSecondaryBrush");
        _detail.Text = failed ? "Keep awake failed"
            : !on || _module.Deadline is not { } end ? ""
            : _module.Active?.Mode == AwakeMode.UntilTime ? $"{KeepAwakeText.Left(end - now)} left"
            : $"Until {end.LocalDateTime:t}";
        _detail.SetResourceReference(TextBlock.ForegroundProperty, failed ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        _detail.Visibility = _detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pet.ShowMood(on ? PetMood.WideAwake : PetMood.Sleepy);
        var lit = _cards.FirstOrDefault(c => c.Button.IsChecked == true)?.Button;
        foreach (var card in _cards)
        {
            bool selected = IsSelected(card.Length);
            card.Button.IsChecked = selected;
            card.Button.Content = selected && card.Length is null ? $"Until {KeepAwakeText.Until(_module.Active!.UntilMinute)}" : card.Label;
        }
        // Stop pressed from the keyboard hides itself: the focus goes to the card that was lit, so Space starts it again,
        // rather than falling back to the panel.
        if (!on && _stop.IsKeyboardFocusWithin)
            (lit ?? _cards[0].Button).Focus();
        _stop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_picker.Visibility == Visibility.Visible)
            ShowUntil();
    }

    // A timed session ran out while the page shows: Chippi celebrates, then falls asleep.
    private void Celebrate()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Celebrate); return; }
        _pet.Celebrate();
    }

    /// <summary>A card starts its length; the lit card stops. Compared with the module, so Refresh writing state back does nothing.</summary>
    private void OnChip(ToggleButton chip, KeepAwakeSettings? length)
    {
        bool selected = IsSelected(length);
        if (chip.IsChecked == selected)
            return;
        Refresh(); // the module's state shows until the change lands
        if (selected)
            Run(_module.StopSessionAsync());
        else if (length is not null)
            Run(_module.StartSessionAsync(length));
        else
            OpenUntil();
    }

    /// <summary>Swaps the cards for the time picker, at the last Until time used (to its 5 minutes).</summary>
    internal void OpenUntil()
    {
        _untilMinute = (int)Math.Round(_module.Settings.UntilMinute / (double)MinuteStep) * MinuteStep % 1440;
        _choices.Visibility = Visibility.Hidden; // its room stays, so the page doesn't move
        _picker.Visibility = Visibility.Visible;
        ShowUntil();
        _dayClock.Start();
        _hourColumn.Focus();
    }

    // Start, or Enter on a column: runs until the picked time.
    private void StartUntil()
    {
        CloseUntil();
        Run(_module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = _untilMinute }));
    }

    /// <summary>Back to the cards from the time picker (Cancel, Start, Enter or Esc). False when the picker wasn't open.</summary>
    internal bool CloseUntil()
    {
        if (_picker.Visibility != Visibility.Visible)
            return false;
        _dayClock.Stop();
        _picker.Visibility = Visibility.Collapsed;
        _choices.Visibility = Visibility.Visible;
        if (_picker.IsKeyboardFocusWithin)
            _cards[^1].Button.Focus();
        return true;
    }

    // The picker's time, and the day it lands on: a time already past today is tomorrow's. Checked again just after the
    // next minute turns.
    private void ShowUntil()
    {
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        int hour = _untilMinute / 60;
        _hour.Text = _twelveHour ? (hour % 12 == 0 ? 12 : hour % 12).ToString(CultureInfo.CurrentCulture) : hour.ToString("00", CultureInfo.CurrentCulture);
        _minute.Text = Minute(_untilMinute).ToString("00", CultureInfo.CurrentCulture);
        _half.Text = hour < 12 ? format.AMDesignator : format.PMDesignator;
        var now = Now();
        _day.Text = _untilMinute <= now.Hour * 60 + now.Minute ? "Tomorrow" : "Today";
        _dayClock.Interval = TimeSpan.FromMilliseconds(60_000 - now.Second * 1000 - now.Millisecond + 50);
    }

    // Two quick changes: if the first fails after the second started, the second's outcome is the one that shows.
    private async void Run(Task change)
    {
        int mine = ++_changes;
        _failed = false;
        try { await change; }
        catch (Exception ex)
        {
            Log.Error("Keep awake: a change from the island failed", ex);
            _failed = mine == _changes;
        }
        Refresh();
    }
}
