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

namespace Chippi.Features.KeepAwake;

/// <summary>
/// Keep awake's island page (DESIGN.md §5 Island pages): six cards, three across (30 min · 1 hr · 2 hr · 4 hr · No limit ·
/// Until…). A card starts its length and the lit one stops it. The lit card is the live readout: its length over the time
/// left, and "Stop" on hover or keyboard focus. Until… swaps the cards for a time picker in the page. The module owns the state.
/// </summary>
public sealed class KeepAwakeIslandView : UserControl
{
    private const string UntilLabel = "Until…";
    private const int MinuteStep = 5;

    private readonly KeepAwakeModule _module;
    private readonly UniformGrid _cards = new() { Columns = 3, Margin = new Thickness(0, 0, -8, -8), VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Card> _chips = [];
    private readonly TextBlock _error = new() { Text = "Keep awake failed", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private readonly StackPanel _picker = new() { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hour = Value(), _minute = Value(), _half = Value();
    private readonly TextBlock _day = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    private readonly FrameworkElement _hourColumn;
    private readonly bool _twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
    private int _untilMinute;
    private bool _failed; // the last change from here failed, beyond what the module reports

    private sealed record Card(ToggleButton Button, KeepAwakeSettings? Length, string Label, TextBlock Name, TextBlock Status);

    public KeepAwakeIslandView(KeepAwakeModule module)
    {
        _module = module;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);

        // Six cards (104 tall), with an 8 DIP gap in each direction; the arrow keys move among them.
        foreach (var (label, length) in KeepAwakeText.Lengths)
            _cards.Children.Add(Chip(label, length));
        _cards.Children.Add(Chip(UntilLabel, null));
        AutomationProperties.SetName(_cards, "Keep awake length");
        KeyboardNavigation.SetDirectionalNavigation(_cards, KeyboardNavigationMode.Contained);

        // Until…: hour, minute (5-minute steps) and AM or PM, each stepped by its arrows, the wheel or the arrow keys,
        // with the day it lands on under them. Minute precision is in Settings.
        _hourColumn = Column(_hour, "Hour", up => StepHour(up));
        var time = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        time.Children.Add(_hourColumn);
        var separator = Value();
        separator.Text = CultureInfo.CurrentCulture.DateTimeFormat.TimeSeparator;
        separator.Width = double.NaN;
        time.Children.Add(separator);
        time.Children.Add(Column(_minute, "Minute", up => Step(Minute(_untilMinute) + (up ? MinuteStep : -MinuteStep), 60, minute => _untilMinute / 60 * 60 + minute)));
        if (_twelveHour)
            time.Children.Add(Column(_half, "AM or PM", _ => _untilMinute = (_untilMinute + 720) % 1440));
        _day.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var cancel = new Button { Content = "Cancel" };
        cancel.SetResourceReference(StyleProperty, "IslandButtonStyle");
        cancel.Click += (_, _) => CloseUntil();
        var start = new Button { Content = "Start", Margin = new Thickness(8, 0, 0, 0) };
        start.SetResourceReference(StyleProperty, "IslandAccentButtonStyle");
        start.Click += (_, _) =>
        {
            CloseUntil();
            Run(_module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = _untilMinute }));
        };
        _picker.Children.Add(time);
        _picker.Children.Add(_day);
        _picker.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, start } });
        AutomationProperties.SetName(_picker, "End time");

        // The error sits at the bottom, under the centered cards, so showing it moves nothing.
        Content = new Grid { Children = { _cards, _picker, _error } };
        AutomationProperties.SetName(this, "Keep awake");

        // The module ticks with its tile (twice a minute while a timed session counts down), so the lit card and the tile agree.
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; CloseUntil(); };
        Refresh();
    }

    private static TextBlock Value() => new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Width = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private ToggleButton Chip(string label, KeepAwakeSettings? length)
    {
        var name = new TextBlock { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center };
        // Regular under the semibold length: it's a reading, not the choice.
        var status = new TextBlock { FontSize = 12, FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
        var chip = new ToggleButton { Height = 104, Margin = new Thickness(0, 0, 8, 8), Tag = label, Content = new StackPanel { Children = { name, status } } };
        chip.SetResourceReference(StyleProperty, "IslandChoiceStyle");
        Typography.SetNumeralAlignment(status, FontNumeralAlignment.Tabular);
        chip.MouseEnter += (_, _) => Refresh();
        chip.MouseLeave += (_, _) => Refresh();
        chip.GotKeyboardFocus += (_, _) => Refresh();
        chip.LostKeyboardFocus += (_, _) => Refresh();
        OnFlip(chip, () => OnChip(chip, length));
        _chips.Add(new(chip, length, label, name, status));
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

    // The lit card says Stop while it's pointed at or reached by the keyboard (a click's focus isn't a request to stop).
    private static bool Stopping(ToggleButton chip) =>
        chip.IsMouseOver || (chip.IsKeyboardFocused && InputManager.Current.MostRecentInputDevice is KeyboardDevice);

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _error.Visibility = _failed || _module.Error is not null ? Visibility.Visible : Visibility.Collapsed;
        var now = DateTimeOffset.Now;
        foreach (var card in _chips)
        {
            bool selected = IsSelected(card.Length);
            card.Button.IsChecked = selected;
            card.Name.Text = !selected ? card.Label
                : Stopping(card.Button) ? "Stop"
                : card.Length is null ? $"Until {KeepAwakeText.Until(_module.Active!.UntilMinute)}"
                : card.Label;
            // Lit, its second line is the time left: the one live fact the page has.
            card.Status.Text = selected && _module.Deadline is { } end ? $"{KeepAwakeText.Left(end - now)} left" : "";
            card.Status.Visibility = card.Status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(card.Button, card.Status.Text.Length > 0 ? $"{card.Name.Text}, {card.Status.Text}" : card.Name.Text);
        }
        if (_picker.Visibility == Visibility.Visible)
            ShowUntil();
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
        _cards.Visibility = Visibility.Hidden; // its room stays, so the page doesn't move
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
        _cards.Visibility = Visibility.Visible;
        if (_picker.IsKeyboardFocusWithin)
            _chips[^1].Button.Focus();
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
