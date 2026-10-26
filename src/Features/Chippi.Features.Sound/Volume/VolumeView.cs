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

public enum VolumeSurface { Settings, Flyout, Island, Output }

/// <summary>One shared session model, with horizontal controls in Settings/flyout and vertical island strips.</summary>
public sealed class VolumeView : UserControl
{
    private readonly AppVolumeModule _module;
    private readonly VolumeSurface _surface;
    private readonly StackPanel _root = new();
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
        var shape = string.Join("|", apps.Select(a => a.Key + a.Name + _module.CanPickOutput(a.Key)))
            + ":" + string.Join("|", _module.Snapshot.Known(AudioFlow.Render).Select(e => e.Id + e.Name + e.State))
            + ":" + SystemOutput?.Id + ":" + _module.Settings.ShowQuietApps + _module.Settings.RememberOutputs + ":" + _module.Error;
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
        var island = _surface == VolumeSurface.Island;
        if (island)
        {
            var picker = new ComboBox { ItemsSource = _module.Outputs, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = _module.Output?.Id, MaxWidth = 280, Margin = new Thickness(0, 8, 0, 16) };
            picker.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
            AutomationProperties.SetName(picker, "Mixer output");
            picker.SelectionChanged += (_, _) => { if (!_refreshing) _module.ViewOutput(picker.SelectedValue as string); };
            _root.Children.Add(picker);
            var strips = new StackPanel { Orientation = Orientation.Horizontal };
            strips.Children.Add(Strip(null));
            var divider = new Border { Width = 1, Margin = new Thickness(8, 0, 8, 0), Background = new SolidColorBrush(Color.FromArgb(26,255,255,255)) };
            strips.Children.Add(divider);
            foreach (var app in apps) strips.Children.Add(Strip(app.Key));
            _root.Children.Add(new ScrollViewer { Content = strips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        else
        {
            if (_surface != VolumeSurface.Flyout) _root.Children.Add(Row(null));
            if (_surface != VolumeSurface.Output)
            {
                _root.Children.Add(RoutingUi.Section("Apps"));
                foreach (var app in apps) _root.Children.Add(Row(app.Key));
            }
        }
        if (apps.Count == 0 && _surface != VolumeSurface.Output)
            _root.Children.Add(RoutingUi.Text("No apps playing", "CaptionTextStyle", "TextSecondaryBrush"));
        if (_surface == VolumeSurface.Settings)
        {
            _root.Children.Add(RoutingUi.Section("Options"));
            _root.Children.Add(RoutingUi.Card(
                new SettingRow { Header = "Inactive apps", Content = RoutingUi.Switch("Inactive apps", _module.Settings.ShowQuietApps, on => Run(_module.SetShowQuietAsync(on))) },
                new SettingRow { Header = "Saved app outputs", Content = RoutingUi.Switch("Saved app outputs", _module.Settings.RememberOutputs, on => Run(_module.SetRememberAsync(on))) }));
        }
        if (_module.Error is { } error) _root.Children.Add(RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush"));
    }

    // The mixer's strip follows its output picker; every other system row is the default output's.
    private AudioEndpoint? SystemOutput => _surface == VolumeSurface.Island ? _module.Output : _module.DefaultOutput;
    private AppAudio? App(AppKey? key) => key is { } value ? _module.Snapshot.Apps.FirstOrDefault(a => a.Key == value) : null;
    private (float Volume, bool Muted) Level(AppKey? key) => key is null
        ? SystemOutput is { } output && _module.Snapshot.Levels.TryGetValue(output.Id, out var level) ? (level.Volume, level.Muted) : (0, false)
        : App(key) is { } app ? (app.Volume, app.Muted) : (0, false);
    private string AppName(AppKey? key) => key is null ? "System" : App(key)?.Name ?? "App";
    private Task SetVolume(AppKey? key, double value) => key is { } app ? _module.SetVolumeAsync(app, (float)value / 100) : _module.SetSystemVolumeAsync((float)value / 100, SystemOutput);
    private Task SetMute(AppKey? key) => key is { } app ? _module.SetMutedAsync(app, !Level(key).Muted) : _module.SetSystemMutedAsync(!Level(key).Muted, SystemOutput);

    private IconButton Mute(AppKey? key)
    {
        var button = new IconButton { Width = 28, Height = 28, ToolTip = "Mute" };
        button.Click += (_, _) => Run(SetMute(key));
        _updates.Add(() =>
        {
            var muted = Level(key).Muted;
            button.Icon = muted ? LucideIcons.VolumeMuted : key is null ? LucideIcons.Volume :
                Monogram(AppName(key));
            button.ToolTip = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(button, $"{button.ToolTip} {AppName(key)}");
            button.IsEnabled = key is not null || SystemOutput is not null;
        });
        return button;
    }

    private static Geometry Monogram(string name)
    {
        var glyph = new FormattedText(name[..1],System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.White,1).BuildGeometry(new Point());
        var bounds = glyph.Bounds;
        if (bounds.IsEmpty || bounds.Width == 0 || bounds.Height == 0) return LucideIcons.Volume;
        var scale = 22 / Math.Max(bounds.Width,bounds.Height);
        glyph.Transform = new MatrixTransform(new Matrix(scale,0,0,scale,1-bounds.X*scale,1-bounds.Y*scale));
        return glyph;
    }
    private Slider VolumeSlider(AppKey? key, bool vertical, out TextBlock percentage)
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, IsMoveToPointEnabled = true };
        AutomationProperties.SetName(slider, AppName(key) + " volume");
        var label = RoutingUi.Text("", "NumberTextStyle", "TextSecondaryBrush");
        label.MinWidth = 36;
        slider.ValueChanged += (_, e) =>
        {
            label.Text = $"{Math.Round(e.NewValue)}%";
            if (!_refreshing) Run(SetVolume(key,e.NewValue));
        };
        _updates.Add(() =>
        {
            if (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) slider.Value = Level(key).Volume * 100;
            label.Text = $"{Math.Round(slider.Value)}%";
            slider.IsEnabled = key is not null || SystemOutput is not null;
        });
        percentage = label;
        return slider;
    }

    private FrameworkElement Row(AppKey? key)
    {
        var settings = _surface == VolumeSurface.Settings;
        var row = RoutingUi.Columns(new(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(RoutingUi.Text(key is null ? "System volume" : AppName(key)));
        if (key is null && SystemOutput is { } output) name.Children.Add(RoutingUi.Text(output.Name,"CaptionTextStyle","TextSecondaryBrush"));
        RoutingUi.Add(row,name,0); RoutingUi.Add(row,Mute(key),1);
        var slider = VolumeSlider(key,false,out var percent);
        slider.SetResourceReference(StyleProperty,"HopperSliderStyle");
        slider.Width = settings ? 180 : 92; slider.Margin = new Thickness(12,0,12,0);
        RoutingUi.Add(row,slider,2); RoutingUi.Add(row,percent,3);
        var content = new StackPanel();
        content.Children.Add(row);
        if (key is { } app && _module.CanPickOutput(app))
        {
            var picker = DevicePicker(app);
            picker.Width = settings ? 172 : 220;
            if (settings) { picker.Margin = new Thickness(12,0,0,0); RoutingUi.Add(row,picker,4); }
            else { picker.HorizontalAlignment = HorizontalAlignment.Left; picker.Margin = new Thickness(0,6,0,0); content.Children.Add(picker); }
        }
        var pad = RoutingUi.Pad(content,settings ? 60 : 48);
        return settings ? RoutingUi.Card(pad) : pad;
    }

    private sealed record OutputItem(string Id, string Name);

    private ComboBox DevicePicker(AppKey key)
    {
        var items = new List<OutputItem> { new("", "Default") };
        items.AddRange(_module.Outputs.Select(d => new OutputItem(d.Id,d.Name)));
        var chosen = _module.ChosenOutput(key);
        if (chosen is not null && _module.Outputs.All(e => e.Id != chosen))
            items.Add(new(chosen,(_module.Snapshot.Find(chosen)?.Name ?? "Output") + " (Unavailable)"));
        var combo = new ComboBox { ItemsSource = items, SelectedValuePath = "Id", DisplayMemberPath = "Name" };        combo.SetResourceReference(StyleProperty,"HopperComboBoxStyle");
        AutomationProperties.SetName(combo,AppName(key) + " output");
        combo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && combo.SelectedValue is string id) Run(_module.SetOutputAsync(key,id.Length == 0 ? null : id));
        };
        _updates.Add(() => { if (!combo.IsDropDownOpen) combo.SelectedValue = _module.ChosenOutput(key) ?? ""; });
        return combo;
    }

    private FrameworkElement Strip(AppKey? key)
    {
        var strip = new StackPanel { Width = 56, Margin = new Thickness(0,0,4,0) };
        var mute = Mute(key); mute.HorizontalAlignment = HorizontalAlignment.Center;
        strip.Children.Add(mute);
        var slider = VolumeSlider(key,true,out var percentage);
        var bar = new Grid { Width = 40, Height = 136, Margin = new Thickness(0,6,0,6) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(31,255,255,255)) });
        var fill = new Border { CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Bottom };
        fill.SetResourceReference(Border.BackgroundProperty,"AccentBrush");
        bar.Children.Add(fill);
        slider.Opacity = 0; slider.Height = 136; slider.Width = 40;
        slider.ValueChanged += (_, _) => fill.Height = 136 * slider.Value / 100;
        bar.Children.Add(slider);
        strip.Children.Add(bar);
        percentage.FontSize = 12; percentage.HorizontalAlignment = HorizontalAlignment.Center;
        strip.Children.Add(percentage);
        var name = RoutingUi.Text(AppName(key),"CaptionTextStyle","TextSecondaryBrush");
        name.FontSize = 12; name.Margin = new Thickness(0,6,0,0); name.TextAlignment = TextAlignment.Center;
        strip.Children.Add(name);
        return strip;
    }

    private async void Run(Task change)
    {
        try { await change; } catch { /* module exposes the error */ }
        Refresh();
    }
}