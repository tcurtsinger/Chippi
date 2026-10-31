// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Mute;

/// <summary>Where a <see cref="MicMuteView"/> sits in Settings; the Sound page's row is <see cref="MicQuickView"/>.</summary>
public enum MicMuteSurface
{
    /// <summary>The Microphone row at the top of Settings › Microphone: the island's row.</summary>
    Microphone,
    /// <summary>The Mute shortcut section of Settings › Microphone.</summary>
    Shortcut,
    /// <summary>The app-wide Shortcuts list.</summary>
    ShortcutList,
}

public sealed class MicMuteView : UserControl
{
    private readonly MicMuteModule _module;
    private readonly MicMuteSurface _surface;
    private readonly StackPanel _body = new();
    private readonly Slider _level = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = RoutingUi.Text("", "BodyTextStyle", "TextSecondaryBrush");
    private readonly LatestWrite _levelWrite = new();
    private readonly LevelTarget _shown = new();
    private readonly TextBlock _microphoneError = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private Card? _levelCard;
    private SettingRow? _levelRow;
    private Button? _levelMute;
    private string _microphoneShape = "";
    private Button? _action; // Mute or Unmute as shown now, focused again after a press from the keyboard rebuilds it
    private bool _recording, _refreshingLevel;

    public MicMuteView(MicMuteModule module, MicMuteSurface surface = MicMuteSurface.Shortcut)
    {
        _module = module;
        _surface = surface;
        Content = _body;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            if (_recording) { _recording = false; _module.CaptureShortcut(false); }
        };
        PreviewKeyDown += Capture;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_recording) return;
        if (_surface == MicMuteSurface.Microphone) { RefreshMicrophone(); return; }
        _body.Children.Clear();
        if (_surface == MicMuteSurface.ShortcutList)
        {
            var card = SettingsLayout.Card(SettingsLayout.Row(_module.Descriptor.Name, ShortcutControls()));
            card.SetResourceReference(StyleProperty, "CardRowsStyle");
            _body.Children.Add(card);
            if (_module.ShortcutError is { } error) Error(error);
            return;
        }
        _body.Children.Add(SettingsLayout.Card(
            SettingsLayout.Row("Shortcut", ShortcutControls()),
            SettingsLayout.Row("Sound feedback", RoutingUi.Switch("Sound feedback", _module.Settings.PlaySound, _module.SetPlaySound))));
        if (_module.ShortcutError is { } shortcutError) Error(shortcutError);
    }

    /// <summary>
    /// The top of Settings › Microphone, the island's Microphone row as a Settings row, as Output's
    /// Volume is (revised 2026-10-31): the level of the mic in use, its percentage and Mute, captioned
    /// with the mic. Without a mic whose level it can tell, the mics' state and Mute. Only switching
    /// between the two (or the state row's state changing) rebuilds it: a level, a mute or an error
    /// updates it in place, so none of them drops a drag.
    /// </summary>
    private void RefreshMicrophone()
    {
        bool muted = _module.Status.State == MicrophoneMuteState.Muted;
        var level = _module.Status.State == MicrophoneMuteState.NoMicrophone ? null : _module.MicLevel;
        var shape = level is null ? $"state|{_module.Status.State}" : "level";
        if (shape != _microphoneShape)
        {
            _microphoneShape = shape;
            _body.Children.Clear();
            if (level is null)
                _body.Children.Add(SettingsLayout.Card(MicrophoneRow(muted)));
            else
            {
                _body.Children.Add(_levelCard ??= SettingsLayout.Card(_levelRow = LevelRow()));
                _action = _levelMute;
            }
            _microphoneError.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetLiveSetting(_microphoneError, AutomationLiveSetting.Polite);
            _body.Children.Add(_microphoneError);
        }
        _microphoneError.Text = _module.Error ?? "";
        _microphoneError.Visibility = _module.Error is null ? Visibility.Collapsed : Visibility.Visible;
        if (level is not { } shown || _levelRow is null || _levelMute is null)
            return;
        _refreshingLevel = true;
        try
        {
            _shown.Show(_level, shown.Id, shown.Volume);
            ShowPercent();
            _levelRow.Description = _module.LevelMicrophoneName ?? "";
            _levelMute.Content = muted ? "Unmute" : "Mute";
            AutomationProperties.SetName(_levelMute, muted ? "Unmute all microphones" : "Mute all microphones");
        }
        finally { _refreshingLevel = false; }
    }

    private SettingRow LevelRow()
    {
        _level.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(_level, "Microphone volume");
        _level.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshingLevel && _shown.Id is { } id)
                _levelWrite.Send(() => _module.SetMicLevelAsync(id, (float)e.NewValue / 100));
        };
        _percent.MinWidth = 52;
        _percent.TextAlignment = TextAlignment.Right;
        _percent.Margin = new Thickness(12, 0, 12, 0);
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        _levelMute = RoutingUi.Button("Mute", Toggle);
        return SettingsLayout.Row("Volume", new StackPanel { Orientation = Orientation.Horizontal, Children = { _level, _percent, _levelMute } });
    }

    /// <summary>The percentage, or "Muted" or "Partly muted" in Caution in its place.</summary>
    private void ShowPercent()
    {
        bool warning = _module.Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed;
        _percent.Text = warning ? _module.StatusText : $"{Math.Round(_level.Value).ToString(System.Globalization.CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, warning ? "WarningTextBrush" : "TextSecondaryBrush");
    }

    /// <summary>The island's mic row as a Settings row when no level can be told: the state as the caption, and Mute or Unmute.</summary>
    private SettingRow MicrophoneRow(bool muted)
    {
        bool none = _module.Status.State == MicrophoneMuteState.NoMicrophone;
        var action = _action = RoutingUi.Button(muted ? "Unmute" : "Mute", Toggle);
        AutomationProperties.SetName(action, muted ? "Unmute all microphones" : "Mute all microphones");
        action.IsEnabled = !none;
        var row = SettingsLayout.Row("Microphone", action, muted ? "Muted" : _module.StatusText);
        // A muted or partly muted mic is the one state that needs attention: the caption says it in the warning color.
        if (_module.Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed)
            row.Loaded += (_, _) =>
            {
                row.ApplyTemplate();
                (row.Template?.FindName("Caption", row) as TextBlock)?.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            };
        return row;
    }

    /// <summary>The keys (or "Not set"), Change or Set, and Remove, as Paste as plain text's shortcut.</summary>
    private StackPanel ShortcutControls()
    {
        var shortcut = _module.Settings.Shortcut;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var keys = shortcut is null ? RoutingUi.Text("Not set", brush: "TextSecondaryBrush") : RoutingUi.Text(shortcut.ToString());
        keys.Margin = new Thickness(0, 0, 12, 0);
        panel.Children.Add(keys);
        var change = RoutingUi.Button(shortcut is null ? "Set" : "Change", BeginCapture);
        AutomationProperties.SetName(change, shortcut is null ? "Set shortcut" : "Change shortcut");
        panel.Children.Add(change);
        if (shortcut is not null)
        {
            var remove = RoutingUi.Button("Remove", _module.RemoveShortcut);
            remove.Margin = new Thickness(8, 0, 0, 0);
            AutomationProperties.SetName(remove, "Remove shortcut");
            panel.Children.Add(remove);
        }
        return panel;
    }

    private void BeginCapture()
    {
        _recording = true;
        _module.CaptureShortcut(true);
        Focusable = true;
        _body.Children.Clear();
        var card = SettingsLayout.Card(SettingsLayout.Row("New shortcut", RoutingUi.Button("Cancel", CancelCapture)));
        if (_surface == MicMuteSurface.ShortcutList) card.SetResourceReference(StyleProperty, "CardRowsStyle");
        _body.Children.Add(card);
        Focus();
    }

    private void CancelCapture() { _module.CaptureShortcut(false); _recording = false; Refresh(); }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        if (e.Key == Key.Escape) { CancelCapture(); e.Handled = true; return; }
        if (MicShortcut.From(e) is not { } shortcut) return;
        _module.ChangeShortcut(shortcut);
        _module.CaptureShortcut(false);
        _recording = false;
        e.Handled = true;
        Refresh();
    }

    private async void Toggle()
    {
        bool focused = IsKeyboardFocusWithin;
        IsEnabled = false;
        try { await _module.ToggleAsync(); }
        catch { /* The module displays Error. */ }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                IsEnabled = true;
                Refresh();
                if (focused) _action?.Focus(); // a second Enter undoes the first
            });
        }
    }

    private void Error(string message)
    {
        var text = RoutingUi.Text(message, "CaptionTextStyle", "WarningTextBrush");
        text.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        _body.Children.Add(text);
    }
}
