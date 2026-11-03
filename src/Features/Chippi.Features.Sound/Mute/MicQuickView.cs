// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Mute;

/// <summary>The microphone in use, its input gain and the all-microphone mute switch.</summary>
public sealed class MicQuickView : UserControl
{
    private readonly MicMuteModule _module;
    private readonly AudioDeviceCard _card;
    private readonly LucideIcon _glyph = new();
    private readonly Slider _level = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new();
    private readonly Button _mute;
    private readonly TextBlock _error = AudioDeviceCard.ErrorText();
    private readonly LatestWrite _write = new();
    private readonly LevelTarget _shown = new();
    private bool _refreshing;

    public MicQuickView(MicMuteModule module)
    {
        _module = module;
        _level.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(_level, "Microphone volume");
        _level.ValueChanged += (_, e) =>
        {
            if (_refreshing || _shown.Id is not { } id) return;
            ShowPercent(hasLevel: true);
            _write.Send(() => _module.SetMicLevelAsync(id, (float)e.NewValue / 100));
        };
        _mute = RoutingUi.IslandButton("Mute", Toggle);
        _card = new AudioDeviceCard(AudioFlow.Capture, _glyph, _percent, _level, _mute, _error);
        _card.RoutingChanged += Refresh;
        Content = _card;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    internal void SetRouting(AudioRoutingModule? routing) => _card.SetRouting(routing);
    private bool Warning => _module.Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed;

    private void ShowPercent(bool hasLevel)
    {
        _percent.Text = Warning || !hasLevel ? _module.StatusText : $"{Math.Round(_level.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, Warning ? "WarningTextBrush" : "IslandTextSecondaryBrush");
        RoutingQuickView.Quiet(_level, Warning);
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        bool none = _module.Status.State == MicrophoneMuteState.NoMicrophone;
        var level = none ? null : _module.MicLevel;
        _card.ShowName(_module.LevelMicrophoneName ?? (none ? null : "All microphones"));
        _level.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
        _percent.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _refreshing = true;
        try { _shown.Show(_level, level?.Id, level?.Volume ?? 0); }
        finally { _refreshing = false; }
        ShowPercent(level is not null);
        _glyph.Data = Warning || none ? LucideIcons.MicOff : LucideIcons.Mic;
        _glyph.SetResourceReference(LucideIcon.BrushProperty, Warning ? "WarningBrush" : none ? "IslandTextTertiaryBrush" : "IslandTextSecondaryBrush");
        bool muted = _module.Status.State == MicrophoneMuteState.Muted;
        _mute.Content = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(_mute, muted ? "Unmute all microphones" : "Mute all microphones");
        _mute.ToolTip = AutomationProperties.GetName(_mute);
        _mute.IsEnabled = !none;
        AudioDeviceCard.ShowError(_error, _module.Error);
    }

    private async void Toggle()
    {
        bool focused = _mute.IsKeyboardFocused;
        IsEnabled = false;
        try { await _module.ToggleAsync(); }
        catch { /* The module displays Error. */ }
        finally
        {
            IsEnabled = true;
            Refresh();
            if (focused) _mute.Focus();
        }
    }
}
