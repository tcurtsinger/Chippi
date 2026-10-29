// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Volume;

public enum VolumeSurface { Settings, Island }

/// <summary>
/// App volume's views. On the island's App volume page (DESIGN.md §5 Island) a vertical strip per
/// app, its output in the strip's menu; the system volume is the Sound page's. In Settings (revised
/// 2026-10-28) a row per app, every app, then the options.
/// </summary>
public sealed class VolumeView : UserControl
{
    private readonly AppVolumeModule _module;
    private readonly VolumeSurface _surface;
    private readonly Grid _root = new();
    private readonly List<Action> _updates = [];
    private string _shape = "";
    private bool _refreshing;

    public VolumeView(AppVolumeModule module, VolumeSurface surface)
    {
        _module = module; _surface = surface;
        Content = _root;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var apps = _module.Apps.ToList();
        if (_surface == VolumeSurface.Island) apps = apps.Count > 14 ? apps.OrderByDescending(a => a.IsActive).Take(14).ToList() : apps;
        var shape = string.Join("|", apps.Select(a => a.Key + a.Name + a.ExePath + _module.CanPickOutput(a.Key)))
            + ":" + string.Join("|", _module.Snapshot.Known(AudioFlow.Render).Select(e => e.Id + e.Name + e.State))
            + ":" + _module.Settings.ShowQuietApps + _module.Settings.RememberOutputs + ":" + _module.Error;
        _refreshing = true;
        try
        {
            if (_shape != shape)
            {
                _shape = shape; _root.Children.Clear(); _updates.Clear();
                Build(apps);
            }
            foreach (var update in _updates) update();
        }
        finally { _refreshing = false; }
    }

    private void Build(IReadOnlyList<AppAudio> apps)
    {
        if (_surface == VolumeSurface.Settings)
        {
            // Every app, with no limit: the island's apps beyond 14 are changed here.
            var list = SettingsLayout.Card();
            foreach (var app in apps) list.Children.Add(AppRow(app.Key));
            if (apps.Count == 0) list.Children.Add(SettingsLayout.Empty("No apps"));
            _root.Children.Add(new StackPanel
            {
                Children =
                {
                    list,
                    SettingsLayout.Section("Options"),
                    SettingsLayout.Card(
                        SettingsLayout.Row("Show apps that aren't playing", RoutingUi.Switch("Show apps that aren't playing", _module.Settings.ShowQuietApps, on => Run(_module.SetShowQuietAsync(on)))),
                        SettingsLayout.Row("Remember each app's output", RoutingUi.Switch("Remember each app's output", _module.Settings.RememberOutputs, on => Run(_module.SetRememberAsync(on))))),
                },
            });
        }
        else
        {
            var strips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < apps.Count; i++) strips.Children.Add(Strip(apps[i].Key, apps.Count, i == apps.Count - 1));
            _root.Children.Add(strips);
            if (apps.Count == 0)
            {
                var glyph = new LucideIcon { Data = LucideIcons.Sliders, Size = 20, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
                glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
                _root.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { glyph, RoutingUi.Text("No apps playing", "CaptionTextStyle", "IslandTextTertiaryBrush") } });
            }
        }
        if (_module.Error is { } error)
        {
            _root.RowDefinitions.Clear();
            _root.RowDefinitions.Add(new RowDefinition());
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var message = RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush");
            Grid.SetRow(message, 1); _root.Children.Add(message);
        }
        else _root.RowDefinitions.Clear();
    }

    private AppAudio? App(AppKey key) => _module.Snapshot.Apps.FirstOrDefault(a => a.Key == key);
    private (float Volume, bool Muted) Level(AppKey key) => App(key) is { } app ? (app.Volume, app.Muted) : (0, false);
    private string AppName(AppKey key) => App(key)?.Name ?? "App";

    internal static double StripWidth(int count) => count <= 7 ? 56 : count <= 12 ? 34 : 28;

    internal static string Initials(string name) => new string(name.Trim().Take(2).ToArray());

    private Button Mute(AppKey key)
    {
        var button = new Button { Width = 28, Height = 24, ToolTip = "Mute", HorizontalAlignment = HorizontalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        var app = App(key);
        var icon = AppIcon.Load(app?.ExePath);
        FrameworkElement Normal()
        {
            if (app?.Name == "System sounds")
            {
                var system = new LucideIcon { Data = LucideIcons.Volume, Size = 20 };
                system.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
                return system;
            }
            if (icon is not null) return new Image { Source = icon, Width = 20, Height = 20 };
            var label = new TextBlock { Text = Initials(AppName(key)), FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
            var tile = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Child = label };
            tile.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
            return tile;
        }
        var normal = Normal();
        var mutedIcon = new LucideIcon { Data = LucideIcons.VolumeMuted, Size = 20 };
        mutedIcon.SetResourceReference(LucideIcon.BrushProperty, "WarningBrush");
        button.Click += (_, _) => Run(_module.SetMutedAsync(key, !Level(key).Muted));
        _updates.Add(() =>
        {
            bool muted = Level(key).Muted;
            button.Content = muted ? mutedIcon : normal;
            button.ToolTip = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(button, $"{button.ToolTip} {AppName(key)}");
        });
        return button;
    }

    /// <summary>
    /// A Settings row for one app, the island's strip laid flat: icon, name, volume and percentage,
    /// Mute, and the output. Each control calls the module as the strip does.
    /// </summary>
    private Border AppRow(AppKey key)
    {
        var grid = RoutingUi.Columns(GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto);
        var icon = Glyph(key);
        icon.Margin = new Thickness(0, 0, 12, 0);
        RoutingUi.Add(grid, icon, 0);
        var name = RoutingUi.Text(AppName(key));
        name.Margin = new Thickness(0, 0, 12, 0);
        name.ToolTip = AppName(key);
        RoutingUi.Add(grid, name, 1);
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Width = 200, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(slider, AppName(key) + " volume");
        RoutingUi.Add(grid, slider, 2);
        var percentage = RoutingUi.Text("", "BodyTextStyle", "TextSecondaryBrush");
        percentage.MinWidth = 52;
        percentage.TextAlignment = TextAlignment.Right;
        percentage.Margin = new Thickness(12, 0, 8, 0);
        RoutingUi.Add(grid, percentage, 3);
        var mute = new IconButton { Icon = LucideIcons.Volume };
        mute.Click += (_, _) => Run(_module.SetMutedAsync(key, !Level(key).Muted));
        RoutingUi.Add(grid, mute, 4);
        var output = OutputCombo(key);
        output.Margin = new Thickness(8, 0, 0, 0);
        RoutingUi.Add(grid, output, 5);
        void Paint()
        {
            bool muted = Level(key).Muted;
            percentage.Text = muted ? "Muted" : $"{Math.Round(slider.Value)}%";
            percentage.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "TextSecondaryBrush");
            mute.Icon = muted ? LucideIcons.VolumeMuted : LucideIcons.Volume;
            mute.ToolTip = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(mute, $"{mute.ToolTip} {AppName(key)}");
        }
        var write = new LatestWrite();
        slider.ValueChanged += (_, _) => { Paint(); if (!_refreshing) write.Send(() => _module.SetVolumeAsync(key, (float)slider.Value / 100)); };
        _updates.Add(() => { if (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) slider.Value = Level(key).Volume * 100; Paint(); });
        return new Border { Child = grid, MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding };
    }

    /// <summary>The app's icon at 16, System sounds' speaker, or its first two letters.</summary>
    private FrameworkElement Glyph(AppKey key)
    {
        var app = App(key);
        if (app?.Name == "System sounds")
        {
            var system = new LucideIcon { Data = LucideIcons.Volume, Size = 16, VerticalAlignment = VerticalAlignment.Center };
            system.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
            return system;
        }
        if (AppIcon.Load(app?.ExePath) is { } icon) return new Image { Source = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = Initials(AppName(key)), FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var tile = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Child = label, VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return tile;
    }

    /// <summary>Where the app plays: "Default output" or one of the connected outputs, as the strip's menu.</summary>
    private ComboBox OutputCombo(AppKey app)
    {
        var choices = new[] { (Id: (string?)null, Name: "Default output") }.Concat(_module.Outputs.Select(o => (Id: (string?)o.Id, o.Name))).ToList();
        var combo = SettingsLayout.Combo(choices.Select(c => (object)c.Name).ToList());
        AutomationProperties.SetName(combo, $"{AppName(app)} output");
        combo.IsEnabled = _module.CanPickOutput(app);
        combo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && combo.SelectedIndex >= 0 && choices[combo.SelectedIndex].Id != _module.ChosenOutput(app))
                Run(_module.SetOutputAsync(app, choices[combo.SelectedIndex].Id));
        };
        _updates.Add(() => combo.SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Id == _module.ChosenOutput(app))));
        return combo;
    }

    /// <summary>A fixed-height strip; narrower variants leave names and then percentages out.</summary>
    private FrameworkElement Strip(AppKey key, int count, bool last)
    {
        double width = StripWidth(count), barWidth = count <= 7 ? 32 : 24;
        var strip = new Grid { Width = width, Margin = new Thickness(0, 0, last ? 0 : 8, 0) };
        foreach (var height in new[] { new GridLength(24), new GridLength(1, GridUnitType.Star), new GridLength(count <= 12 ? 24 : 0), new GridLength(count <= 7 ? 36 : 0), new GridLength(24) })
            strip.RowDefinitions.Add(new RowDefinition { Height = height });
        strip.Children.Add(Mute(key));
        var bar = new Grid { Width = barWidth, Margin = new Thickness(0, 8, 0, 8) };
        Grid.SetRow(bar, 1); strip.Children.Add(bar);
        var track = new Border { CornerRadius = new CornerRadius(8) };
        track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush"); bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Bottom };
        bar.Children.Add(fill);
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Orientation = Orientation.Vertical, Height = double.NaN, IsMoveToPointEnabled = true, Opacity = 0 };
        slider.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(slider, AppName(key) + " volume");
        bar.Children.Add(slider);
        var percentage = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = count <= 12 ? Visibility.Visible : Visibility.Collapsed };
        Grid.SetRow(percentage, 2); strip.Children.Add(percentage);
        void Paint()
        {
            bool muted = Level(key).Muted;
            percentage.Text = muted ? "Muted" : $"{Math.Round(slider.Value)}%";
            percentage.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "IslandTextBrush");
            if (muted) fill.Background = new SolidColorBrush(Color.FromArgb(71, 255, 255, 255));
            else fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentBrush");
            fill.Height = Math.Max(0, bar.ActualHeight) * slider.Value / 100;
        }
        var write = new LatestWrite();
        slider.ValueChanged += (_, _) => { Paint(); if (!_refreshing) write.Send(() => _module.SetVolumeAsync(key, (float)slider.Value / 100)); };
        bar.SizeChanged += (_, _) => Paint();
        _updates.Add(() => { if (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) slider.Value = Level(key).Volume * 100; Paint(); });
        var name = new TextBlock { Text = AppName(key), FontSize = 11, Width = width, Height = 28, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 28, TextWrapping = TextWrapping.WrapWithOverflow, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, ToolTip = AppName(key), Visibility = count <= 7 ? Visibility.Visible : Visibility.Collapsed };
        name.Text = FitName(AppName(key), name);
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        Grid.SetRow(name, 3); strip.Children.Add(name);
        var menu = OutputMenu(key); menu.IsEnabled = _module.CanPickOutput(key);
        Grid.SetRow(menu, 4); strip.Children.Add(menu);
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
        for (int length = full.Length - 1; length > 0; length--)
        {
            var candidate = full[..length].TrimEnd() + "\u2026";
            if (Fits(candidate)) return candidate;
        }
        return "\u2026";
    }

    /// <summary>The strip's ⋯: where the app plays, Default or one of the connected outputs.</summary>
    private Button OutputMenu(AppKey app)
    {
        var glyph = new LucideIcon { Data = LucideIcons.Ellipsis, Size = 14 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        var more = new Button { Content = glyph, Width = 28, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Output" };
        more.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(more, $"{AppName(app)} output");
        more.Click += (_, _) =>
        {
            var chosen = _module.ChosenOutput(app);
            RoutingUi.Menu(more, new[] { (Id: (string?)null, Name: "Default") }.Concat(_module.Outputs.Select(o => (Id: (string?)o.Id, o.Name)))
                .Select(o => ((o.Id == chosen ? "✓ " : "") + o.Name, (Action)(() => Run(_module.SetOutputAsync(app, o.Id))), true)));
        };
        return more;
    }

    private async void Run(Task change)
    {
        try { await change; } catch { /* module exposes the error */ }
        Refresh();
    }
}