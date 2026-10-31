// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
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
    private readonly TextBlock _error = new() { Text = "Keep awake failed", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private readonly StackPanel _picker = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hour = Value(), _minute = Value(), _half = Value();
    private readonly TextBlock _day = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 20, 0) };
    private readonly FrameworkElement _hourColumn;
    private readonly bool _twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
    private int _untilMinute;
    private bool _failed; // the last change from here failed, beyond what the module reports

    private sealed record Card(ToggleButton Button, KeepAwakeSettings? Length, string Label);

    public KeepAwakeIslandView(KeepAwakeModule module)
    {
        _module = module;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);

        // The top row: Chippi, the state over its detail, and Stop while it runs.
        _detail.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        Typography.SetNumeralAlignment(_state, FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(_detail, FontNumeralAlignment.Tabular);
        AutomationProperties.SetLiveSetting(_state, AutomationLiveSetting.Polite);
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
        // day it lands on, Cancel and Start, in one row in the cards' place. Minute precision is in Settings.
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
        start.Click += (_, _) =>
        {
            CloseUntil();
            Run(_module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = _untilMinute }));
        };
        _picker.Children.Add(time);
        _picker.Children.Add(_day);
        _picker.Children.Add(cancel);
        _picker.Children.Add(start);
        AutomationProperties.SetName(_picker, "End time");

        // 24 between the top row and the lengths; the lengths' room (two 52 rows) holds the picker too, so nothing moves.
        var lengths = new Grid { Height = 112, Margin = new Thickness(0, 24, 0, 0), Children = { _choices, _picker } };
        // The error sits at the bottom, under the centered block, so showing it moves nothing.
        Content = new Grid { Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, lengths } }, _error } };
        AutomationProperties.SetName(this, "Keep awake");

        // The module ticks with its tile (twice a minute while a timed session counts down), so the page and the tile agree.
        Loaded += (_, _) => { _module.Changed += Refresh; _module.Finished += Celebrate; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _module.Finished -= Celebrate; CloseUntil(); };
        Refresh();
    }

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
    /// don't, so Tab goes column to column); Up and Down and the wheel step it as its arrows do.
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
        };
        column.MouseWheel += (_, e) =>
        {
            Stepped(e.Delta > 0);
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
        _error.Visibility = _failed || _module.Error is not null ? Visibility.Visible : Visibility.Collapsed;
        var now = DateTimeOffset.Now;
        bool on = _module.IsOn;
        // The state is the tile's ("58 min left", "Until 6:00 PM", "No limit", "Off"); the detail is the other half of a
        // timed session: when a length ends, or how long until an Until time.
        _state.Text = _module.TileStatus(now);
        _state.SetResourceReference(TextBlock.ForegroundProperty, on ? "IslandTextBrush" : "IslandTextSecondaryBrush");
        _detail.Text = !on || _module.Deadline is not { } end ? ""
            : _module.Active?.Mode == AwakeMode.UntilTime ? $"{KeepAwakeText.Left(end - now)} left"
            : $"Until {end.LocalDateTime:t}";
        _detail.Visibility = _detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _stop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _pet.ShowMood(on ? PetMood.WideAwake : PetMood.Sleepy);
        foreach (var card in _cards)
        {
            bool selected = IsSelected(card.Length);
            card.Button.IsChecked = selected;
            card.Button.Content = selected && card.Length is null ? $"Until {KeepAwakeText.Until(_module.Active!.UntilMinute)}" : card.Label;
        }
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
        _hourColumn.Focus();
    }

    /// <summary>Back to the cards from the time picker (Cancel, Start or Esc). False when the picker wasn't open.</summary>
    internal bool CloseUntil()
    {
        if (_picker.Visibility != Visibility.Visible)
            return false;
        _picker.Visibility = Visibility.Collapsed;
        _choices.Visibility = Visibility.Visible;
        if (_picker.IsKeyboardFocusWithin)
            _cards[^1].Button.Focus();
        return true;
    }

    // The picker's time, and the day it lands on: a time already past today is tomorrow's.
    private void ShowUntil()
    {
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        int hour = _untilMinute / 60;
        _hour.Text = _twelveHour ? (hour % 12 == 0 ? 12 : hour % 12).ToString(CultureInfo.CurrentCulture) : hour.ToString("00", CultureInfo.CurrentCulture);
        _minute.Text = Minute(_untilMinute).ToString("00", CultureInfo.CurrentCulture);
        _half.Text = hour < 12 ? format.AMDesignator : format.PMDesignator;
        var now = DateTime.Now;
        _day.Text = _untilMinute <= now.Hour * 60 + now.Minute ? "Tomorrow" : "Today";
    }

    private async void Run(Task change)
    {
        _failed = false;
        try { await change; }
        catch (Exception ex)
        {
            Log.Error("Keep awake: a change from the island failed", ex);
            _failed = true;
        }
        Refresh();
    }
}
