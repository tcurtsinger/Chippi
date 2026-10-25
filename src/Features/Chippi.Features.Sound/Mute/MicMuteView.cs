// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Mute;

public sealed class MicMuteView : UserControl
{
    private readonly MicMuteModule _module;
    private readonly bool _compact;
    private readonly StackPanel _body = new();
    private bool _recording;

    public MicMuteView(MicMuteModule module, bool compact = false)
    {
        _module = module;
        _compact = compact;
        Content = _body;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _recording = false; _module.CaptureShortcut(false); };
        PreviewKeyDown += Capture;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_recording) return;
        _body.Children.Clear();
        bool muted = _module.Status.State == MicrophoneMuteState.Muted;
        var action = RoutingUi.Button(muted ? "Unmute" : "Mute", Toggle);
        AutomationProperties.SetName(action, muted ? "Unmute all microphones" : "Mute all microphones");
        action.IsEnabled = _module.Status.State != MicrophoneMuteState.NoMicrophone;
        var row = new SettingRow { Header = "Microphone", Description = _module.StatusText, Icon = LucideIcons.MicOff, Content = action };
        bool warning = _module.Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed;
        var glyph = new LucideIcon { Data = LucideIcons.MicOff, Size = 20, Margin = new Thickness(0, 0, 12, 0) };
        glyph.SetResourceReference(LucideIcon.BrushProperty, warning ? "WarningTextBrush" : "TextSecondaryBrush");
        row.Icon = null;
        var statusRow = RoutingUi.Columns(GridLength.Auto, new GridLength(1, GridUnitType.Star));
        RoutingUi.Add(statusRow, glyph, 0);
        RoutingUi.Add(statusRow, row, 1);
        _body.Children.Add(_compact ? statusRow : RoutingUi.Card(statusRow));
        if (_module.Error is { } error) Error(error);
        if (_compact) return;
        _body.Children.Add(RoutingUi.Section("Shortcut"));
        var change = RoutingUi.Button("Change", () => BeginCapture());
        var shortcut = new StackPanel { Orientation = Orientation.Horizontal };
        var keys = RoutingUi.Text(_module.Settings.Shortcut.ToString());
        keys.Margin = new Thickness(0, 0, 12, 0);
        shortcut.Children.Add(keys);
        shortcut.Children.Add(change);
        _body.Children.Add(RoutingUi.Card(
            new SettingRow { Header = "Shortcut", Content = shortcut },
            new SettingRow { Header = "Mics it mutes", Content = RoutingUi.Text("All microphones") }));
        if (_module.ShortcutError is { } shortcutError) Error(shortcutError);
        _body.Children.Add(RoutingUi.Section("Options"));
        _body.Children.Add(RoutingUi.Card(new SettingRow { Header = "Play a sound", Content = RoutingUi.Switch("Play a sound", _module.Settings.PlaySound, _module.SetPlaySound) }));
    }

    private void BeginCapture()
    {
        _recording = true;
        _module.CaptureShortcut(true);
        Focusable = true;
        _body.Children.Clear();
        _body.Children.Add(RoutingUi.Card(new SettingRow { Header = "Press a shortcut", Content = RoutingUi.Button("Cancel", CancelCapture) }));
        Focus();
    }

    private void CancelCapture() { _module.CaptureShortcut(false); _recording = false; Refresh(); }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        if (e.Key == Key.Escape) { CancelCapture(); e.Handled = true; return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        var mapped = (modifiers.HasFlag(ModifierKeys.Control) ? ShortcutModifiers.Control : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? ShortcutModifiers.Shift : 0)
            | (modifiers.HasFlag(ModifierKeys.Alt) ? ShortcutModifiers.Alt : 0)
            | (modifiers.HasFlag(ModifierKeys.Windows) ? ShortcutModifiers.Windows : 0);
        _module.ChangeShortcut(new(mapped, (uint)KeyInterop.VirtualKeyFromKey(key)));
        _module.CaptureShortcut(false);
        _recording = false;
        e.Handled = true;
        Refresh();
    }

    private async void Toggle()
    {
        IsEnabled = false;
        try { await _module.ToggleAsync(); }
        catch { /* The module displays Error. */ }
        finally { Dispatcher.Invoke(() => { IsEnabled = true; Refresh(); }); }
    }

    private void Error(string message)
    {
        var text = RoutingUi.Text(message, "CaptionTextStyle", "WarningTextBrush");
        text.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        _body.Children.Add(text);
    }
}