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
/// app playing, its output on the strip's menu button; the system volume is the Sound page's. In
/// Settings (revised 2026-10-28) a row per app, every app, then the options. Each app keeps its strip
/// or row while it's shown, updated in place, so an app starting or a level changing never rebuilds
/// the one under a drag.
/// </summary>
public sealed class VolumeView : UserControl
{
    /// <summary>The island's most strips; past it, the last one is "+N", which opens Settings.</summary>
    internal const int MostStrips = 14;

    private readonly AppVolumeModule _module;
    private readonly VolumeSurface _surface;
    private readonly Grid _root = new();
    private readonly Panel _list; // the strips, or the Settings card
    private readonly Dictionary<AppKey, Item> _items = [];
    private readonly HashSet<AppKey> _shown = []; // the island's apps: playing when the page opened or since, until they close
    private readonly TextBlock _error;
    private readonly FrameworkElement _empty;
    private readonly List<Action> _updates = []; // Settings' options
    private Button? _more;
    private Grid? _moreStrip;
    private int _built = -1; // the density (StripWidth) the strips were made at
    private int _floor = -1; // while the page is open the strips only get narrower
    private string _watched = "";
    private string? _errorShown; // the error this page showed: seen once the page closes on it
    private IDisposable? _meters;
    private bool _loaded;
    private bool _refreshing;

    private sealed record Item(FrameworkElement View, Action Update, Action<float>? Peak = null);

    public VolumeView(AppVolumeModule module, VolumeSurface surface)
    {
        _module = module; _surface = surface;
        Content = _root;
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
        if (surface == VolumeSurface.Island)
        {
            _list = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var glyph = new LucideIcon { Data = LucideIcons.Sliders, Size = 20, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
            _empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { glyph, RoutingUi.Text("No apps playing", "CaptionTextStyle", "IslandTextTertiaryBrush") } };
            _root.Children.Add(_list);
            _root.Children.Add(_empty);
            Grid.SetRow(_error, 1); _root.Children.Add(_error);
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
            // The island keeps this page: opened again, it starts from the apps playing now, at their
            // widest, and keeps the strips it can (a rebuild is 10 to 20 ms for 14).
            if (_loaded && island) { _shown.Clear(); _floor = -1; }
            _loaded = true;
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
            _meters?.Dispose(); (_meters, _watched) = (null, "");
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
            foreach (var update in _updates) update();
            _errorShown = _module.Error;
            _error.Text = _errorShown ?? "";
            _error.ToolTip = _errorShown;
            _error.Visibility = _errorShown is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }

    private void Clear()
    {
        _list.Children.Clear(); _items.Clear(); (_more, _moreStrip) = (null, null);
    }

    /// <summary>The strips of the apps shown, by name; a new app's strip joins without touching the others.</summary>
    private void Strips()
    {
        var apps = _module.Apps;
        _shown.RemoveWhere(k => apps.All(a => a.Key != k));
        foreach (var app in apps.Where(a => a.IsActive)) _shown.Add(app.Key);
        var shown = apps.Where(a => _shown.Contains(a.Key)).ToList();
        if (shown.Count > MostStrips)
        {
            // The apps playing now keep their strips; the rest are behind "+N".
            var kept = shown.OrderByDescending(a => a.IsActive).Take(MostStrips - 1).Select(a => a.Key).ToHashSet();
            shown = shown.Where(a => kept.Contains(a.Key)).ToList();
        }
        int more = _shown.Count - shown.Count;
        int density = _floor = Math.Max(_floor, Density(shown.Count + (more > 0 ? 1 : 0)));
        if (density != _built) { Clear(); _built = density; }
        Sync(shown.Select(a => a.Key).ToList(), key => Strip(key, density));
        if (more > 0)
        {
            _moreStrip ??= More(density);
            if (_list.Children.IndexOf(_moreStrip) != _list.Children.Count - 1) { _list.Children.Remove(_moreStrip); _list.Children.Add(_moreStrip); }
            _more!.Content = more > 99 ? "99+" : $"+{more}";
            _more.ToolTip = $"{more} more in Settings";
            AutomationProperties.SetName(_more, $"{more} more apps, open Settings"); // never fewer than 2: 13 strips are kept
        }
        else if (_moreStrip is not null) { _list.Children.Remove(_moreStrip); (_more, _moreStrip) = (null, null); }
        foreach (FrameworkElement strip in _list.Children)
            strip.Margin = new Thickness(0, 0, strip == _list.Children[^1] ? 0 : 8, 0);
        _empty.Visibility = _list.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Watch(shown);
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

    // The island's peaks, for the apps it shows and only while it shows them.
    private void Watch(IReadOnlyList<AppAudio> apps)
    {
        if (!IsLoaded) return;
        var watched = string.Join("|", apps.Select(a => a.Key.Value));
        if (watched == _watched) return;
        _meters?.Dispose(); (_meters, _watched) = (null, watched);
        if (apps.Count == 0) return;
        try { _meters = _module.WatchMeters(apps.Select(a => a.Key).ToList()); }
        catch (InvalidOperationException) { } // audio isn't running: the bars show no level
    }

    private void OnMeters(object? sender, MeterReadings readings)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnMeters(sender, readings)); return; }
        foreach (var (key, item) in _items) item.Peak?.Invoke(readings.Apps.GetValueOrDefault(key));
    }

    private AppAudio? App(AppKey key) => _module.Snapshot.Apps.FirstOrDefault(a => a.Key == key);
    private (float Volume, bool Muted) Level(AppKey key) => App(key) is { } app ? (app.Volume, app.Muted) : (0, false);
    private string AppName(AppKey key) => App(key) is { } app ? AppVolumeModule.NameOf(app) : "App";

    internal static int Density(int count) => count <= 7 ? 0 : count <= 12 ? 1 : 2;
    internal static double StripWidth(int count) => Widths[Density(count)];
    private static readonly double[] Widths = [56, 34, 28];

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

    /// <summary>A fixed-height strip; narrower ones leave the name and then the percentage out.</summary>
    private Item Strip(AppKey key, int density)
    {
        double width = Widths[density], barWidth = density == 0 ? 32 : 24;
        var strip = new Grid { Width = width, Background = Brushes.Transparent };
        // The mock's order: the bar, then the app's icon (its mute), the percentage, the name and the output.
        foreach (var height in new[] { new GridLength(1, GridUnitType.Star), new GridLength(28), new GridLength(20), new GridLength(density == 0 ? 36 : 0), new GridLength(24) })
            strip.RowDefinitions.Add(new RowDefinition { Height = height });

        // The app's icon is its Mute, and stays the icon when muted: the percentage row says so.
        var icon = Icon(key, 20, island: true);
        var mute = new Button { Content = icon, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center };
        mute.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        mute.Click += (_, _) => Run(_module.SetMutedAsync(key, !Level(key).Muted));

        var bar = new Grid { Width = barWidth, Margin = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent };
        Grid.SetRow(bar, 0); strip.Children.Add(bar);
        // The trough: the control fill with its 1px edge, 6px corners; the level fills it from the bottom.
        var track = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        track.SetResourceReference(Border.BackgroundProperty, "IslandButtonBrush");
        track.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush"); bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1) };
        bar.Children.Add(fill);
        // What the app plays now: a 2px line in the fill at its peak, falling back slowly.
        var peak = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.6, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        peak.SetResourceReference(Border.BackgroundProperty, "IslandOnAccentBrush");
        bar.Children.Add(peak);
        // The slider holds the value, the keyboard and UI Automation; the bar takes the pointer.
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Orientation = Orientation.Vertical, Height = double.NaN, Opacity = 0, IsHitTestVisible = false };
        slider.SetResourceReference(StyleProperty, "IslandSliderStyle");
        bar.Children.Add(slider);
        // Tab reads the strip from the top: the level, then Mute, then the output.
        Grid.SetRow(mute, 1); strip.Children.Add(mute);

        var percentage = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        percentage.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        var muted = new LucideIcon { Data = LucideIcons.VolumeMuted, Size = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        muted.SetResourceReference(LucideIcon.BrushProperty, "WarningBrush");
        Grid.SetRow(percentage, 2); strip.Children.Add(percentage);
        Grid.SetRow(muted, 2); strip.Children.Add(muted);

        var name = new TextBlock { FontSize = 11, Width = width, Height = 28, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 28, TextWrapping = TextWrapping.WrapWithOverflow, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Visibility = density == 0 ? Visibility.Visible : Visibility.Collapsed };
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        Grid.SetRow(name, 3); strip.Children.Add(name);

        var (output, paintOutput) = OutputButton(key);
        Grid.SetRow(output, 4); strip.Children.Add(output);

        float level = 0; // the peak line's place, 0 to 1
        bool? painted = null; // the muted state the fill and marks show
        void Paint()
        {
            bool isMuted = Level(key).Muted;
            percentage.Text = $"{Math.Round(slider.Value)}%";
            if (painted != isMuted)
            {
                painted = isMuted;
                percentage.Visibility = isMuted || density == 2 ? Visibility.Collapsed : Visibility.Visible;
                muted.Visibility = isMuted ? Visibility.Visible : Visibility.Collapsed;
                icon.Opacity = isMuted ? 0.5 : 1;
                fill.SetResourceReference(Border.BackgroundProperty, isMuted ? "IslandMutedFillBrush" : "IslandAccentFillBrush");
            }
            fill.Height = Math.Max(0, bar.ActualHeight - 2) * slider.Value / 100;
            PaintPeak();
        }
        void PaintPeak()
        {
            peak.Visibility = painted == true || level < 0.02 || fill.Height < 8 ? Visibility.Collapsed : Visibility.Visible;
            peak.Margin = new Thickness(4, 0, 4, 1 + Math.Max(3, fill.Height * level - 3));
        }
        var write = new LatestWrite();
        slider.ValueChanged += (_, _) => { Paint(); if (!_refreshing) write.Send(() => _module.SetVolumeAsync(key, (float)slider.Value / 100)); };
        bar.SizeChanged += (_, _) => Paint();

        // Pressing the bar doesn't move the level: dragging moves it from where it was, by as far as the
        // pointer goes, and a click without a drag sets it there.
        double pressY = 0, pressValue = 0;
        bool dragging = false;
        double Span() => Math.Max(1, bar.ActualHeight - 2);
        bar.MouseLeftButtonDown += (_, e) => { (pressY, pressValue, dragging) = (e.GetPosition(bar).Y, slider.Value, false); bar.CaptureMouse(); e.Handled = true; };
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
            bar.ReleaseMouseCapture();
            if (!dragging) slider.Value = Math.Clamp((bar.ActualHeight - 1 - e.GetPosition(bar).Y) / Span() * 100, 0, 100);
            e.Handled = true;
        };
        // The wheel moves 2 a notch; a touchpad's smaller steps move it as far.
        strip.MouseWheel += (_, e) => { slider.Value = Math.Clamp(slider.Value + e.Delta / 60.0, 0, 100); e.Handled = true; };

        string shownName = "";
        return new Item(strip, () =>
        {
            // Not while a drag or a write is under way: the level would jump back to a step already passed.
            if (!bar.IsMouseCaptured && !write.Busy) slider.Value = Level(key).Volume * 100;
            if (AppName(key) != shownName)
            {
                shownName = AppName(key);
                name.Text = density == 0 ? FitName(shownName, name) : shownName; // narrower strips have no name row
                name.ToolTip = shownName;
                AutomationProperties.SetName(slider, shownName + " volume");
            }
            mute.ToolTip = Level(key).Muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(mute, $"{mute.ToolTip} {shownName}");
            paintOutput();
            Paint();
        }, value =>
        {
            float next = value >= level ? value : Math.Max(value, level - 0.04f); // up at once, down by a 25th a reading
            if (Math.Abs(next - level) < 0.002f) return;
            level = next;
            PaintPeak();
        });
    }

    /// <summary>"+N" in an empty trough where the next bar would be: the apps past the island's room, in Settings.</summary>
    private Grid More(int density)
    {
        double barWidth = density == 0 ? 32 : 24;
        var strip = new Grid { Width = Widths[density] };
        strip.RowDefinitions.Add(new RowDefinition());
        strip.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28 + 20 + (density == 0 ? 36 : 0) + 24) });
        var trough = new Border { Width = barWidth, Margin = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        trough.SetResourceReference(Border.BackgroundProperty, "IslandButtonBrush");
        trough.SetResourceReference(Border.BorderBrushProperty, "IslandControlBorderBrush");
        _more = new Button { Width = barWidth, Height = double.NaN, Margin = new Thickness(0, 6, 0, 6), FontSize = 12 };
        _more.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        _more.SetResourceReference(ForegroundProperty, "IslandTextSecondaryBrush");
        _more.Click += (_, _) => _module.OpenSettings();
        strip.Children.Add(trough);
        strip.Children.Add(_more);
        return strip;
    }

    // TextBlock trims a long word at its width, but MaxHeight alone silently hides later lines.
    // Measure with the same wrapping/font and mark the last visible line when more would follow.
    private static string FitName(string full, TextBlock target)
    {
        var measure = new TextBlock
        {
            FontFamily = target.FontFamily, FontSize = target.FontSize, FontWeight = target.FontWeight,
            LineHeight = target.LineHeight, LineStackingStrategy = target.LineStackingStrategy,
            TextWrapping = target.TextWrapping,
        };
        bool Fits(string text)
        {
            measure.Text = text;
            measure.Measure(new Size(target.Width, double.PositiveInfinity));
            return measure.DesiredSize.Height <= target.Height + 0.01;
        }
        if (Fits(full)) return full;
        // The longest start that fits with the ellipsis, found by halving, cut between whole characters.
        var starts = StringInfo.ParseCombiningCharacters(full);
        string? best = null;
        for (int low = 1, high = starts.Length - 1; low <= high;)
        {
            int middle = (low + high) / 2, length = starts[middle];
            var candidate = full[..length].TrimEnd() + "\u2026";
            if (Fits(candidate)) (best, low) = (candidate, middle + 1);
            else high = middle - 1;
        }
        return best ?? "\u2026";
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
    /// Where the app plays, as a glyph of the device's kind: tertiary while it follows the default,
    /// accent when it has an output of its own, Caution when it still plays somewhere other than its
    /// pick (an open stream moves only when the app opens a new one, docs/phase-3/architecture.md).
    /// It opens the output menu. System sounds always plays on the default, so it has none.
    /// </summary>
    private (Button Button, Action Paint) OutputButton(AppKey key)
    {
        var glyph = new LucideIcon { Size = 14 };
        var button = new Button { Content = glyph, Width = 28, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        ToolTipService.SetShowOnDisabled(button, true);
        button.Visibility = key == AppKey.SystemSounds ? Visibility.Hidden : Visibility.Visible;
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
            glyph.Data = DeviceName.Glyph(plays is null ? null : _module.Snapshot.Find(plays));
            glyph.SetResourceReference(LucideIcon.BrushProperty, elsewhere ? "WarningBrush" : own ? "IslandAccentBrush" : "IslandTextTertiaryBrush");
            button.IsEnabled = _module.CanPickOutput(key);
            var where = plays is null ? "" : "plays on " + OutputName(plays, names);
            var connected = _module.Outputs.Any(o => string.Equals(o.Id, chosen, StringComparison.OrdinalIgnoreCase));
            string tip = !button.IsEnabled ? (where.Length == 0 ? "Output" : char.ToUpperInvariant(where[0]) + where[1..])
                    // Off, it says why: per-app output is off here, or this app has no program to set it for.
                    + " · " + (_module.OutputsOff ?? "Its output can't be set")
                : !elsewhere ? (where.Length == 0 ? "Output" : char.ToUpperInvariant(where[0]) + where[1..] + (own ? "" : " · Default"))
                : chosen is null ? $"Default · {OutputName(_module.DefaultOutput, names)} · still {where}"
                : connected ? $"Picked {OutputName(chosen, names)} · still {where}"
                : $"{OutputName(chosen, names)} not connected · {where}";
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
