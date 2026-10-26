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
/// app, its output in the strip's menu; the system volume is the Sound page's. In Settings only the
/// options, since volume is changed on the page.
/// </summary>
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
            _root.Children.Add(RoutingUi.Card(
                new SettingRow { Header = "Show apps that aren't playing", Content = RoutingUi.Switch("Show apps that aren't playing", _module.Settings.ShowQuietApps, on => Run(_module.SetShowQuietAsync(on))) },
                new SettingRow { Header = "Remember each app's output", Content = RoutingUi.Switch("Remember each app's output", _module.Settings.RememberOutputs, on => Run(_module.SetRememberAsync(on))) }));
        }
        else
        {
            var strips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var app in apps) strips.Children.Add(Strip(app.Key));
            _root.Children.Add(new ScrollViewer { Content = strips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            if (apps.Count == 0)
                _root.Children.Add(RoutingUi.Text("No apps playing", "CaptionTextStyle", "IslandTextTertiaryBrush"));
        }
        if (_module.Error is { } error) _root.Children.Add(RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush"));
    }

    private AppAudio? App(AppKey key) => _module.Snapshot.Apps.FirstOrDefault(a => a.Key == key);
    private (float Volume, bool Muted) Level(AppKey key) => App(key) is { } app ? (app.Volume, app.Muted) : (0, false);
    private string AppName(AppKey key) => App(key)?.Name ?? "App";

    private IconButton Mute(AppKey key)
    {
        var button = new IconButton { Width = 28, Height = 28, ToolTip = "Mute" };
        button.Click += (_, _) => Run(_module.SetMutedAsync(key, !Level(key).Muted));
        _updates.Add(() =>
        {
            var muted = Level(key).Muted;
            button.Icon = muted ? LucideIcons.VolumeMuted : Monogram(AppName(key));
            button.ToolTip = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(button, $"{button.ToolTip} {AppName(key)}");
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

    /// <summary>An app's strip: mute, its vertical volume bar and percentage, its name and its output menu.</summary>
    private FrameworkElement Strip(AppKey key)
    {
        var strip = new StackPanel { Width = 56, Margin = new Thickness(0,0,4,0) };
        var mute = Mute(key); mute.HorizontalAlignment = HorizontalAlignment.Center;
        strip.Children.Add(mute);
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Orientation = Orientation.Vertical, IsMoveToPointEnabled = true };
        AutomationProperties.SetName(slider, AppName(key) + " volume");
        var percentage = RoutingUi.Text("", "NumberTextStyle", "TextSecondaryBrush");
        percentage.MinWidth = 36;
        slider.ValueChanged += (_, e) =>
        {
            percentage.Text = $"{Math.Round(e.NewValue)}%";
            if (!_refreshing) Run(_module.SetVolumeAsync(key, (float)e.NewValue / 100));
        };
        _updates.Add(() =>
        {
            if (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) slider.Value = Level(key).Volume * 100;
            percentage.Text = $"{Math.Round(slider.Value)}%";
        });
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
        name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = AppName(key);
        strip.Children.Add(name);
        if (_module.CanPickOutput(key)) strip.Children.Add(OutputMenu(key));
        return strip;
    }

    /// <summary>The strip's ⋯: where the app plays, Default or one of the connected outputs.</summary>
    private IconButton OutputMenu(AppKey app)
    {
        var more = new IconButton { Icon = LucideIcons.Ellipsis, Width = 28, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Output" };
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