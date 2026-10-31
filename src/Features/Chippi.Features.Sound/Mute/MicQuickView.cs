// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Mute;

/// <summary>
/// The Microphone section's top row on the Sound page, the Output row's twin (DESIGN.md §5 Island
/// pages, revised 2026-10-31): the mic glyph, the input level of the mic in use and its percentage,
/// and Mute or Unmute for every mic. Muted, the glyph is MicOff and "Muted" takes the percentage's
/// place, both in Caution, and the slider keeps its level in tertiary. Built once and updated in
/// place, so a drag or a focused button survives each change.
/// </summary>
public sealed class MicQuickView : UserControl
{
    private readonly MicMuteModule _module;
    private readonly LucideIcon _glyph = new();
    private readonly Slider _level = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 12, MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly DockPanel _levels = new();
    private readonly TextBlock _state = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _mute;
    private readonly TextBlock _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private readonly LatestWrite _write = new();
    private bool _refreshing;

    public MicQuickView(MicMuteModule module)
    {
        _module = module;
        _level.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(_level, "Microphone volume");
        _level.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing)
                _write.Send(() => _module.SetMicLevelAsync((float)e.NewValue / 100));
        };
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        DockPanel.SetDock(_percent, Dock.Right);
        _levels.Children.Add(_percent);
        _levels.Children.Add(_level);
        AutomationProperties.SetLiveSetting(_state, AutomationLiveSetting.Polite);
        _mute = RoutingUi.IslandButton("Mute", Toggle);
        _error.Margin = new Thickness(9, 0, 9, 8);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        Content = new StackPanel { Children = { RoutingUi.IslandRow(_glyph, new Grid { Children = { _levels, _state } }, _mute), _error } };
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private MicrophoneMuteState State => _module.Status.State;

    private bool Warning => State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed;

    private void ShowPercent()
    {
        _percent.Text = Warning ? _module.StatusText : $"{Math.Round(_level.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, Warning ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        RoutingQuickView.Quiet(_level, Warning);
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            bool none = State == MicrophoneMuteState.NoMicrophone;
            var level = none ? null : _module.MicLevel;
            // The level shows for the mic in use; without one to read (no mic, or VB-CABLE as Windows'
            // default with routing off) the row says the mics' state in its place.
            _levels.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
            _state.Visibility = level is null ? Visibility.Visible : Visibility.Collapsed;
            if (level is { } volume && !_level.IsMouseCaptureWithin && !_level.IsKeyboardFocusWithin)
                _level.Value = volume * 100;
            ShowPercent();
            _state.Text = _module.StatusText;
            if (Warning || none)
                _state.SetResourceReference(TextBlock.ForegroundProperty, Warning ? "WarningTextBrush" : "IslandTextTertiaryBrush");
            else
                _state.ClearValue(TextBlock.ForegroundProperty);
            _glyph.Data = Warning || none ? LucideIcons.MicOff : LucideIcons.Mic;
            _glyph.SetResourceReference(LucideIcon.BrushProperty, Warning ? "WarningBrush" : none ? "IslandTextTertiaryBrush" : "IslandAccentBrush");
            bool muted = State == MicrophoneMuteState.Muted;
            _mute.Content = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(_mute, muted ? "Unmute all microphones" : "Mute all microphones");
            _mute.IsEnabled = !none;
            _error.Text = _module.Error ?? "";
            _error.Visibility = _module.Error is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
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
            if (focused) _mute.Focus(); // a second Enter undoes the first
        }
    }
}
