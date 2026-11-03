// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.Features.Sound.Volume;

public enum VolumeSurface { Settings, Island }

/// <summary>
/// App volume's views. On the island's App volume page (DESIGN.md §5 Island) a vertical strip per
/// app in the Windows mixer, with its route on a hover/focus menu. System sounds has a pinned strip. In
/// Settings (revised 2026-10-28) a row per app, every app, then the options. Each app keeps its strip
/// or row while it's shown, updated in place, so an app starting or a level changing never rebuilds
/// the one under a drag.
/// </summary>
public sealed class VolumeView : UserControl
{
    private const int AppsPerPage = 8;

    private readonly AppVolumeModule _module;
    private readonly VolumeSurface _surface;
    private readonly Grid _root = new();
    private readonly Panel _list; // the strips, or the Settings card
    private readonly Dictionary<AppKey, Item> _items = [];
    private readonly TextBlock _error;
    private readonly FrameworkElement _empty;
    private readonly List<Action> _updates = []; // Settings' options
    private readonly Button _previous = new(), _next = new();
    private readonly TextBlock _range = new(), _hint = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid? _pager;
    private int _page;
    private bool _paging;
    private HashSet<AppKey> _watched = [];
    private string? _errorShown; // the error this page showed: seen once the page closes on it
    private IDisposable? _meters;
    private bool _refreshing;

    private sealed record Item(FrameworkElement View, Action Update, Action<float>? Peak = null, FrameworkElement? Trough = null);

    public VolumeView(AppVolumeModule module, VolumeSurface surface)
    {
        _module = module; _surface = surface;
        Content = _root;
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
        if (surface == VolumeSurface.Island)
        {
            _list = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pager = BuildPager();
            Grid.SetRow(_pager, 1);
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
            var glyph = new LucideIcon { Data = LucideIcons.Sliders, Size = 20, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
            _empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { glyph, RoutingUi.Text("No apps in the Windows mixer", "CaptionTextStyle", "IslandTextTertiaryBrush") } };
            _root.Children.Add(_list);
            _root.Children.Add(_empty);
            _root.Children.Add(_hint);
            _root.Children.Add(_pager);
            Grid.SetRow(_error, 2); _root.Children.Add(_error);
        }
        else
        {
            _list = SettingsLayout.Card();
            _empty = SettingsLayout.Empty("No apps");
            _error.Margin = new Thickness(0, 0, 0, 8);
            _root.Children.Add(new StackPanel { Children = { _error, _list, SettingsLayout.Section("Options"), Options() } });
        }
        bool island = surface == VolumeSurface.Island;
        Loaded += (_, _) =>
        {
            _page = 0;
            _module.Changed += Refresh;
            ThemeManager.ThemeChanged += OnTheme;
            if (island) _module.Meters += OnMeters;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            ThemeManager.ThemeChanged -= OnTheme;
            if (island) _module.Meters -= OnMeters;
            _meters?.Dispose();
            _meters = null;
            _watched.Clear();
            // An error this page showed has been seen: the next visit starts clean, unless a newer one came.
            if (_errorShown is not null) _module.ClearError(_errorShown);
        };
        Refresh();
    }

    private void OnTheme(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            if (_surface == VolumeSurface.Island) Strips(); else Rows();
            foreach (var item in _items.Values) item.Update();
            if (_surface == VolumeSurface.Island)
            {
                Watch();
                Hint();
            }
            foreach (var update in _updates) update();
            _errorShown = _module.Error;
            _error.Text = _errorShown ?? "";
            _error.ToolTip = _errorShown;
            _error.Visibility = _errorShown is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }

    private Grid BuildPager()
    {
        var pager = RoutingUi.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
        pager.Height = 26;
        var line = RoutingUi.Hairline();
        line.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumnSpan(line, 3);
        pager.Children.Add(line);
        _range.FontSize = 11;
        _range.Margin = new Thickness(3, 0, 0, 0);
        _range.VerticalAlignment = VerticalAlignment.Center;
        _range.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        pager.Children.Add(_range);
        foreach (var (button, delta, name, icon) in new[]
        {
            (_previous, -1, "Previous apps", LucideIcons.ChevronLeft),
            (_next, 1, "Next apps", LucideIcons.ChevronRight),
        })
        {
            var glyph = new LucideIcon { Data = icon, Size = 15 };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
            button.Content = glyph;
            button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
            (button.Width, button.Height) = (28, 24);
            button.ToolTip = name;
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                bool focused = button.IsKeyboardFocused;
                _page += delta;
                _paging = true;
                try { Refresh(); } finally { _paging = false; }
                if (focused && !button.IsEnabled) (delta > 0 ? _previous : _next).Focus();
            };
            RoutingUi.Add(pager, button, delta < 0 ? 1 : 2);
        }
        return pager;
    }

    /// <summary>System sounds keeps its own column. Pages address the remaining apps in name order.</summary>
    private void Strips()
    {
        var apps = _module.Apps;
        // Keep the strip and its vertical track exactly under a live drag. Losing capture refreshes the layout.
        if (_items.Any(p => p.Value.View.IsMouseCaptureWithin && apps.Any(a => a.Key == p.Key))) return;

        var other = apps.Where(a => a.Key != AppKey.SystemSounds).ToList();
        var system = apps.FirstOrDefault(a => a.Key == AppKey.SystemSounds);
        int pages = Math.Max(1, (other.Count + AppsPerPage - 1) / AppsPerPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        // A session appearing ahead of a focused strip must not push it off its page.
        int focusAt = other.FindIndex(a => _items.TryGetValue(a.Key, out var item) && item.View.IsKeyboardFocusWithin);
        if (focusAt >= 0 && !_paging) _page = focusAt / AppsPerPage;

        int first = _page * AppsPerPage;
        var visible = other.Skip(first).Take(AppsPerPage).ToList();
        int onPage = visible.Count;
        if (system is not null) visible.Insert(0, system);
        var bank = (Grid)_list;
        int columns = Math.Min(AppsPerPage, other.Count) + (system is null ? 0 : 1);
        if (bank.ColumnDefinitions.Count != columns || (columns > 0 && bank.ColumnDefinitions[0].Width.IsAbsolute != (system is not null)))
        {
            bank.ColumnDefinitions.Clear();
            for (int i = 0; i < columns; i++)
                bank.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 && system is not null ? new GridLength(65) : new GridLength(1, GridUnitType.Star) });
        }
        Sync(visible.Select(a => a.Key).ToList(), Strip);
        for (int i = 0; i < visible.Count; i++)
        {
            var strip = _items[visible[i].Key].View;
            Grid.SetColumn(strip, i);
            _items[visible[i].Key].Trough!.Width = other.Count > 6 ? 27 : 33;
        }
        _empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _pager!.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        _range.Text = $"{first + 1}–{first + onPage} of {other.Count} apps";
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = _page < pages - 1;
    }


    private void Rows()
    {
        var apps = _module.Apps;
        Sync(apps.Select(a => a.Key).ToList(), AppRow);
        if (apps.Count == 0) { if (!_list.Children.Contains(_empty)) _list.Children.Add(_empty); }
        else _list.Children.Remove(_empty);
    }

    /// <summary>Puts each key's element at its place, making the new ones and removing the gone ones.</summary>
    private void Sync(List<AppKey> keys, Func<AppKey, Item> make)
    {
        foreach (var gone in _items.Keys.Except(keys).ToList()) { _list.Children.Remove(_items[gone].View); _items.Remove(gone); }
        for (int i = 0; i < keys.Count; i++)
        {
            if (!_items.TryGetValue(keys[i], out var item)) _items[keys[i]] = item = make(keys[i]);
            int at = _list.Children.IndexOf(item.View);
            if (at == i) continue;
            if (at >= 0) _list.Children.RemoveAt(at);
            _list.Children.Insert(i, item.View);
        }
    }

    // Idle apps keep their controls without keeping the meter timer running.
    private void Watch()
    {
        if (!IsLoaded) return;
        var active = _module.Snapshot.Apps.Where(a => a.IsActive && _items.ContainsKey(a.Key)).Select(a => a.Key).ToHashSet();
        if (_watched.SetEquals(active)) return;
        _meters?.Dispose();
        _meters = null;
        _watched = [];
        if (active.Count == 0) return;
        try
        {
            _meters = _module.WatchMeters(active);
            _watched = active;
        }
        catch (InvalidOperationException) { } // audio isn't running: the bars show no level
    }

    private void OnMeters(object? sender, MeterReadings readings)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnMeters(sender, readings)); return; }
        foreach (var key in _watched)
            if (_items.TryGetValue(key, out var item)) item.Peak?.Invoke(readings.Apps.GetValueOrDefault(key));
    }

    private AppAudio? App(AppKey key) => _module.Snapshot.Apps.FirstOrDefault(a => a.Key == key);
    private (float Volume, bool Muted) Level(AppKey key) => App(key) is { } app ? (app.Volume, app.Muted) : (0, false);
    private string AppName(AppKey key) => App(key) is { } app ? AppVolumeModule.NameOf(app) : "App";

    /// <summary>
    /// "MT" for Microsoft Teams, its words' first letters; a one-word name keeps its first two ("Py" for
    /// Python). Only letters and digits, as whole characters: "SP" for "🎧 Studio Pro", "7Z" for 7-Zip,
    /// and an accented letter is never split from its accent. A name with none keeps its first character.
    /// </summary>
    internal static string Initials(string name)
    {
        static List<string> Letters(string word)
        {
            var letters = new List<string>();
            var each = StringInfo.GetTextElementEnumerator(word);
            while (each.MoveNext())
                if (char.IsLetterOrDigit(each.GetTextElement(), 0)) letters.Add(each.GetTextElement());
            return letters;
        }
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Letters).Where(w => w.Count > 0).ToList();
        return words.Count switch
        {
            0 => name.Trim() is { Length: > 0 } rest ? StringInfo.GetNextTextElement(rest) : "",
            1 => string.Concat(words[0].Take(2)),
            _ => string.Concat(words.Take(2).Select(w => w[0].ToUpperInvariant())),
        };
    }

    /// <summary>The app's icon, System sounds' speaker, or its initials on a tile.</summary>
    private FrameworkElement Icon(AppKey key, double size, bool island)
    {
        var app = App(key);
        if (key == AppKey.SystemSounds)
        {
            var system = new LucideIcon { Data = LucideIcons.Volume, Size = size, VerticalAlignment = VerticalAlignment.Center };
            system.SetResourceReference(LucideIcon.BrushProperty, island ? "IslandTextTertiaryBrush" : "TextSecondaryBrush");
            return system;
        }
        if (AppIcon.Load(app?.ExePath) is { } icon) return new Image { Source = icon, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = Initials(AppName(key)), FontSize = island ? 10 : 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, island ? "IslandTextBrush" : "TextPrimaryBrush");
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(island ? 4 : 3), Child = label, VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, island ? "IslandTrackBrush" : "ChipBrush");
        return tile;
    }

    /// <summary>One quiet channel: app icon (mute), vertical level, percentage. Output appears on hover or focus.</summary>
    private Item Strip(AppKey key)
    {
        bool system = key == AppKey.SystemSounds;
        var strip = new Grid { Background = Brushes.Transparent, Height = 239, VerticalAlignment = VerticalAlignment.Center };
        foreach (double height in new double[] { 1, 32, 8, 162, 8, 16, 1 })
            strip.RowDefinitions.Add(new RowDefinition { Height = height == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(height) });
        if (system)
        {
            var divider = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Right };
            divider.SetResourceReference(Border.BackgroundProperty, "IslandHairBrush");
            Grid.SetRowSpan(divider, 7);
            strip.Children.Add(divider);
        }
        var top = new Grid { MaxWidth = 52, Margin = system ? new Thickness(0, 0, 7, 0) : new Thickness(0) };
        Grid.SetRow(top, 1);
        strip.Children.Add(top);
        var icon = Icon(key, system ? 21 : 24, island: true);
        var mute = new Button { Content = icon, Width = 28, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
        mute.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        mute.Click += (_, _) => Run(_module.SetMutedAsync(key, !Level(key).Muted));
        top.Children.Add(mute);

        var (output, paintOutput) = OutputButton(key);
        if (!system) top.Children.Add(output);
        void Reveal()
        {
            bool reveal = strip.IsMouseOver || strip.IsKeyboardFocusWithin || strip.AreAnyTouchesOver;
            output.Opacity = reveal ? 1 : 0;
            output.IsHitTestVisible = reveal;
            Hint();
        }
        strip.MouseEnter += (_, _) => Reveal();
        strip.MouseLeave += (_, _) => Reveal();
        strip.IsKeyboardFocusWithinChanged += (_, _) => Reveal();
        strip.TouchEnter += (_, _) => Reveal();
        strip.TouchLeave += (_, _) => Reveal();
        strip.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (!system && output.IsEnabled) { output.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
        };

        var bar = new Grid { Width = 44, Background = Brushes.Transparent, Margin = top.Margin };
        Grid.SetRow(bar, 3);
        strip.Children.Add(bar);
        var trough = new Grid { Width = 33, HorizontalAlignment = HorizontalAlignment.Center };
        trough.SetResourceReference(Panel.BackgroundProperty, "IslandMixerTroughBrush");
        trough.SizeChanged += (_, _) =>
        {
            var radius = trough.ActualWidth <= 27 ? 8 : 9;
            trough.Clip = new RectangleGeometry(new Rect(trough.RenderSize), radius, radius);
        };
        bar.Children.Add(trough);
        var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom };
        trough.Children.Add(fill);
        var peak = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        peak.SetResourceReference(Border.BackgroundProperty, "IslandOnAccentBrush");
        trough.Children.Add(peak);

        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Orientation = Orientation.Vertical, Opacity = 0, IsHitTestVisible = false };
        slider.SetResourceReference(StyleProperty, "IslandSliderStyle");
        bar.Children.Add(slider);
        var focus = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Margin = new Thickness(0, -3, 0, -3), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        focus.SetResourceReference(Border.BorderBrushProperty, "IslandTextBrush");
        bar.Children.Add(focus);
        slider.IsKeyboardFocusedChanged += (_, _) => focus.Visibility = slider.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
        var percentage = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = top.Margin };
        Grid.SetRow(percentage, 5);
        strip.Children.Add(percentage);

        float level = 0;
        bool? painted = null;
        void Paint()
        {
            bool isMuted = Level(key).Muted;
            percentage.Text = isMuted ? "Muted" : $"{Math.Round(slider.Value)}%";
            if (painted != isMuted)
            {
                painted = isMuted;
                percentage.SetResourceReference(TextBlock.ForegroundProperty, isMuted ? "WarningTextBrush" : "IslandTextSecondaryBrush");
                icon.Opacity = isMuted ? 0.5 : 1;
                fill.SetResourceReference(Border.BackgroundProperty, isMuted ? "IslandMutedFillBrush" : system ? "IslandMixerSystemBrush" : "IslandMixerLevelBrush");
            }
            fill.Height = trough.ActualHeight * slider.Value / 100;
            PaintPeak();
        }
        void PaintPeak()
        {
            peak.Visibility = painted == true || level < 0.02 || fill.Height < 8 ? Visibility.Collapsed : Visibility.Visible;
            peak.Margin = new Thickness(4, 0, 4, Math.Max(3, fill.Height * level - 3));
        }
        var write = new LatestWrite();
        slider.ValueChanged += (_, _) => { Paint(); if (!_refreshing) write.Send(() => _module.SetVolumeAsync(key, (float)slider.Value / 100)); };
        trough.SizeChanged += (_, _) => Paint();

        double pressY = 0, pressValue = 0;
        bool dragging = false;
        double Span() => Math.Max(1, bar.ActualHeight);
        bar.MouseLeftButtonDown += (_, e) => { (pressY, pressValue, dragging) = (e.GetPosition(bar).Y, slider.Value, false); slider.Focus(); bar.CaptureMouse(); e.Handled = true; };
        bar.MouseMove += (_, e) =>
        {
            if (!bar.IsMouseCaptured) return;
            double y = e.GetPosition(bar).Y;
            if (!dragging && Math.Abs(y - pressY) < SystemParameters.MinimumVerticalDragDistance) return;
            dragging = true;
            slider.Value = Math.Clamp(pressValue + (pressY - y) / Span() * 100, 0, 100);
        };
        bar.MouseLeftButtonUp += (_, e) =>
        {
            if (!bar.IsMouseCaptured) return;
            if (!dragging) slider.Value = Math.Clamp(100 - e.GetPosition(bar).Y / Span() * 100, 0, 100);
            bar.ReleaseMouseCapture();
            e.Handled = true;
        };
        bar.LostMouseCapture += (_, _) => Dispatcher.BeginInvoke(() => { if (IsLoaded) Refresh(); });
        strip.MouseWheel += (_, e) => { slider.Value = Math.Clamp(slider.Value + e.Delta / 60.0, 0, 100); e.Handled = true; };

        return new Item(strip, () =>
        {
            if (App(key) is not { IsActive: true }) level = 0;
            if (!bar.IsMouseCaptured && !write.Busy) slider.Value = Level(key).Volume * 100;
            string name = AppName(key);
            string action = Level(key).Muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(slider, name + " volume");
            AutomationProperties.SetName(mute, $"{action} {name}");
            mute.ToolTip = $"{action} {name}";
            if (!system) paintOutput();
            string hint = $"{name} · {(system ? "System output" : output.ToolTip)}";
            AutomationProperties.SetHelpText(strip, hint);
            AutomationProperties.SetHelpText(slider, hint);
            bar.ToolTip = hint;
            Paint();
        }, value =>
        {
            float next = value >= level ? value : Math.Max(value, level - 0.04f);
            if (Math.Abs(next - level) < 0.002f) return;
            level = next;
            PaintPeak();
        }, trough);
    }

    private void Hint()
    {
        var active = _items.Values.FirstOrDefault(i => i.View.IsMouseOver) ?? _items.Values.FirstOrDefault(i => i.View.IsKeyboardFocusWithin);
        _hint.Text = active is null ? "" : AutomationProperties.GetHelpText(active.View);
    }


    private string OutputName(string? id, IReadOnlyDictionary<string, string> names) =>
        id is null ? "" : names.TryGetValue(id, out var name) ? name : _module.Snapshot.Find(id) is { } known ? DeviceName.Short(known.Name) : "Unknown output";

    /// <summary>
    /// The output choices: "Default · Desk speakers" first, then each connected output, and the pick
    /// when it isn't connected (it can't be picked again). Current is the app's pick.
    /// </summary>
    private (List<(string? Id, string Label, bool Enabled)> Choices, int Current) OutputChoices(AppKey key)
    {
        var names = DeviceName.Shown(_module.Outputs);
        var fallback = _module.DefaultOutput is { } id ? " · " + OutputName(id, names) : "";
        var choices = new List<(string? Id, string Label, bool Enabled)> { (null, "Default" + fallback, true) };
        choices.AddRange(_module.Outputs.Select(o => ((string?)o.Id, names[o.Id], true)));
        var chosen = _module.ChosenOutput(key);
        int current = choices.FindIndex(c => string.Equals(c.Id, chosen, StringComparison.OrdinalIgnoreCase));
        if (current < 0) { choices.Add((chosen, OutputName(chosen, names) + " · Not connected", false)); current = choices.Count - 1; }
        return (choices, current);
    }

    /// <summary>
    /// The route menu shows where the app plays: tertiary while it follows the default,
    /// accent when it has an output of its own, Caution when it still plays somewhere other than its
    /// pick (an open stream moves only when the app opens a new one, docs/phase-3/architecture.md).
    /// It opens the output menu. System sounds always plays on the default, so it has none.
    /// </summary>
    private (Button Button, Action Paint) OutputButton(AppKey key)
    {
        var glyph = new LucideIcon { Data = LucideIcons.Ellipsis, Size = 13 };
        var button = new Button { Content = glyph, Width = 16, Height = 24, Padding = new Thickness(0), Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += (_, _) =>
        {
            var (choices, current) = OutputChoices(key);
            RoutingUi.Menu(button, choices.Select(c => (c.Label, (Action)(() => Run(_module.SetOutputAsync(key, c.Id))), c.Enabled)), current);
        };
        return (button, () =>
        {
            var names = DeviceName.Shown(_module.Outputs);
            var plays = _module.PlaysOn(key);
            var chosen = _module.ChosenOutput(key);
            bool elsewhere = _module.PlaysElsewhere(key), own = chosen is not null && _module.CanPickOutput(key);
            glyph.SetResourceReference(LucideIcon.BrushProperty, elsewhere ? "WarningBrush" : own ? "IslandAccentBrush" : "IslandTextTertiaryBrush");
            button.IsEnabled = _module.CanPickOutput(key);
            var where = plays is null ? "" : "plays on " + OutputName(plays, names);
            var connected = _module.Outputs.Any(o => string.Equals(o.Id, chosen, StringComparison.OrdinalIgnoreCase));
            string tip = plays is null ? "Not playing · " + (chosen is null ? "Default" : OutputName(chosen, names) + (connected ? "" : " not connected"))
                : !elsewhere ? $"Plays on {OutputName(plays, names)}" + (button.IsEnabled && !own ? " · Default" : "")
                : chosen is null ? $"Default · {OutputName(_module.DefaultOutput, names)} · still {where}"
                : connected ? $"Picked {OutputName(chosen, names)} · still {where}"
                : $"{OutputName(chosen, names)} not connected · {where}";
            if (!button.IsEnabled) tip += " · " + (_module.OutputsOff ?? "Its output can't be set");
            button.ToolTip = tip;
            AutomationProperties.SetName(button, $"{AppName(key)} output");
            AutomationProperties.SetHelpText(button, tip);
        });
    }

    /// <summary>
    /// A Settings row for one app, the island's strip laid flat: icon, name (and where it plays when
    /// that isn't its pick, or that it isn't playing), volume and percentage, Mute, and the output.
    /// Each control calls the module as the strip does.
    /// </summary>
    private Item AppRow(AppKey key)
    {
        var grid = RoutingUi.Columns(GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto);
        var icon = Icon(key, 16, island: false);
        icon.Margin = new Thickness(0, 0, 12, 0);
        RoutingUi.Add(grid, icon, 0);
        var name = RoutingUi.Text(AppName(key));
        var caption = RoutingUi.Text("", "CaptionTextStyle");
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Children = { name, caption } };
        RoutingUi.Add(grid, label, 1);
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Width = 200, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.SetResourceReference(StyleProperty, "HopperSliderStyle");
        RoutingUi.Add(grid, slider, 2);
        var percentage = RoutingUi.Text("", "BodyTextStyle", "TextSecondaryBrush");
        percentage.MinWidth = 52;
        percentage.TextAlignment = TextAlignment.Right;
        percentage.Margin = new Thickness(12, 0, 8, 0);
        RoutingUi.Add(grid, percentage, 3);
        var mute = new IconButton { Icon = LucideIcons.Volume };
        mute.Click += (_, _) => Run(_module.SetMutedAsync(key, !Level(key).Muted));
        RoutingUi.Add(grid, mute, 4);
        var (output, updateOutput) = OutputCombo(key);
        output.Margin = new Thickness(8, 0, 0, 0);
        RoutingUi.Add(grid, output, 5);
        // Every row is a captioned row's height, so a caption coming and going as apps start and stop
        // playing never moves the rows under it.
        var row = new Border { Child = grid, MinHeight = SettingsLayout.CaptionRowHeight, Padding = SettingsLayout.RowPadding };
        void Paint()
        {
            bool muted = Level(key).Muted;
            percentage.Text = muted ? "Muted" : $"{Math.Round(slider.Value)}%";
            percentage.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "TextSecondaryBrush");
            mute.Icon = muted ? LucideIcons.VolumeMuted : LucideIcons.Volume;
            if (muted) mute.SetResourceReference(ForegroundProperty, "WarningTextBrush"); else mute.ClearValue(ForegroundProperty);
            mute.ToolTip = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(mute, $"{mute.ToolTip} {AppName(key)}");
            // A muted app's level stays where it was, in grey, as on the island.
            if (muted && slider.TryFindResource("TextTertiaryBrush") is Brush grey) slider.Resources["AccentBrush"] = grey; else slider.Resources.Remove("AccentBrush");
        }
        var write = new LatestWrite();
        slider.ValueChanged += (_, _) => { Paint(); if (!_refreshing) write.Send(() => _module.SetVolumeAsync(key, (float)slider.Value / 100)); };
        return new Item(row, () =>
        {
            if (!slider.IsMouseCaptureWithin && !write.Busy) slider.Value = Level(key).Volume * 100;
            name.Text = AppName(key);
            name.ToolTip = AppName(key);
            AutomationProperties.SetName(slider, AppName(key) + " volume");
            var (text, brush) = _module.PlaysElsewhere(key) ? ("Still plays on " + OutputName(_module.PlaysOn(key), DeviceName.Shown(_module.Outputs)), "WarningTextBrush")
                : App(key) is { IsActive: false } && key != AppKey.SystemSounds ? ("Not playing", "TextTertiaryBrush") : ("", "TextTertiaryBrush");
            caption.Text = text;
            caption.ToolTip = text.Length == 0 ? null : text;
            caption.SetResourceReference(TextBlock.ForegroundProperty, brush);
            caption.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            updateOutput();
            Paint();
        });
    }

    /// <summary>Where the app plays, as the strip's menu: the same choices in a combo.</summary>
    private (ComboBox Combo, Action Update) OutputCombo(AppKey key)
    {
        var combo = SettingsLayout.Combo([]);
        combo.Width = 220; // "Default · Desk speakers" whole
        combo.Visibility = key == AppKey.SystemSounds ? Visibility.Hidden : Visibility.Visible;
        List<(string? Id, string Label, bool Enabled)> choices = [];
        combo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && combo.SelectedIndex >= 0 && choices[combo.SelectedIndex] is { Enabled: true } choice
                && !string.Equals(choice.Id, _module.ChosenOutput(key), StringComparison.OrdinalIgnoreCase))
                Run(_module.SetOutputAsync(key, choice.Id));
        };
        return (combo, () =>
        {
            var (next, current) = OutputChoices(key);
            if (!next.SequenceEqual(choices)) { choices = next; combo.ItemsSource = choices.Select(c => c.Label).ToList(); }
            combo.SelectedIndex = current;
            combo.IsEnabled = _module.CanPickOutput(key);
            AutomationProperties.SetName(combo, $"{AppName(key)} output");
        });
    }

    /// <summary>Remember each app's output, or, while no app's output can be picked, why not.</summary>
    private Card Options()
    {
        var remember = SettingsLayout.Row("Remember each app's output", RoutingUi.Switch("Remember each app's output", _module.Settings.RememberOutputs, on => Run(_module.SetRememberAsync(on))));
        var off = SettingsLayout.Row("Output per app", RoutingUi.Text("Off", "BodyTextStyle", "TextSecondaryBrush"));
        _updates.Add(() =>
        {
            var reason = _module.OutputsOff;
            off.Description = reason ?? "";
            off.Visibility = reason is null ? Visibility.Collapsed : Visibility.Visible;
            remember.Visibility = reason is null ? Visibility.Visible : Visibility.Collapsed;
        });
        return SettingsLayout.Card(remember, off);
    }

    private async void Run(Task change)
    {
        try { await change; } catch { /* module exposes the error */ }
        Refresh();
    }
}
