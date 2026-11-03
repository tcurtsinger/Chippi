// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Audio;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>The Devices card for routing: a picker and, on Output, level and mute.</summary>
public sealed class RoutingQuickView : UserControl
{
    internal AudioRoutingModule Module { get; }
    internal AudioFlow Flow { get; }
    private readonly AudioDeviceCard _card;
    private readonly LucideIcon _glyph = new();
    private readonly TextBlock _percent = new();
    private readonly Button _mute;
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly LatestWrite _write = new();
    private readonly LevelTarget _shown = new();
    private bool _refreshing;

    public RoutingQuickView(AudioRoutingModule module, AudioFlow flow)
    {
        Module = module;
        Flow = flow;
        _mute = RoutingUi.IslandButton("Mute", ToggleMute);
        _volume.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            if (_refreshing || _shown.Id is not { } id) return;
            ShowPercent(Module.Snapshot.Levels.TryGetValue(id, out var level) && level.Muted);
            _write.Send(() => Module.SetOutputVolumeAsync(id, (float)e.NewValue / 100));
        };
        _card = flow == AudioFlow.Render
            ? new AudioDeviceCard(flow, _glyph, _percent, _volume, _mute, null)
            : new AudioDeviceCard(flow, _glyph, null, null, null, null);
        _card.RoutingChanged += Refresh;
        _card.SetRouting(module);
        Content = _card;
    }

    private async void ToggleMute()
    {
        if (_shown.Id is not { } id || !Module.Snapshot.Levels.TryGetValue(id, out var level)) return;
        try { await Module.SetOutputMutedAsync(id, !level.Muted); }
        catch { /* The module reports its error in the card. */ }
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var id = Module.InUse(Flow);
        var endpoint = Module.Snapshot.Find(id ?? "");
        _card.ShowName(endpoint?.Name);
        _glyph.Data = Flow == AudioFlow.Capture ? LucideIcons.Mic : LucideIcons.Speaker;
        _glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        if (Flow == AudioFlow.Capture) return;

        var known = Module.Snapshot.Levels.TryGetValue(id ?? "", out var level) && endpoint is not null;
        _mute.IsEnabled = _volume.IsEnabled = known;
        _volume.Visibility = _percent.Visibility = endpoint is null ? Visibility.Collapsed : Visibility.Visible;
        _refreshing = true;
        try { _shown.Show(_volume, known ? id : null, known ? level.Volume : 0); }
        finally { _refreshing = false; }
        bool muted = known && level.Muted;
        if (muted || endpoint is null)
        {
            _glyph.Data = LucideIcons.VolumeMuted;
            _glyph.SetResourceReference(LucideIcon.BrushProperty, muted ? "WarningBrush" : "IslandTextTertiaryBrush");
        }
        ShowPercent(muted);
        _mute.Content = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(_mute, muted ? "Unmute system volume" : "Mute system volume");
    }

    private void ShowPercent(bool muted)
    {
        _percent.Text = muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "IslandTextBrush");
        Quiet(_volume, muted);
    }

    internal static void Quiet(Slider slider, bool muted)
    {
        foreach (var key in new[] { "IslandAccentFillBrush", "IslandAccentBrush" })
        {
            if (muted)
                slider.Resources[key] = slider.TryFindResource("IslandTextTertiaryBrush");
            else
                slider.Resources.Remove(key);
        }
    }
}
