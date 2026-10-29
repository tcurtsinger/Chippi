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

/// <summary>Where a <see cref="MicMuteView"/> sits.</summary>
public enum MicMuteSurface
{
    /// <summary>The Sound page's mic card.</summary>
    Island,
    /// <summary>The Microphone row at the top of Settings › Microphone: the island's card.</summary>
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
    private bool _recording;

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
        _body.Children.Clear();
        bool muted = _module.Status.State == MicrophoneMuteState.Muted;
        if (_surface == MicMuteSurface.Island)
        {
            _body.Children.Add(MicCard(muted));
            if (_module.Error is { } failed) Error(failed);
            SoundColumn.SetWarning(this, _module.Error is not null);
            return;
        }
        if (_surface == MicMuteSurface.Microphone)
        {
            _body.Children.Add(SettingsLayout.Card(MicrophoneRow(muted)));
            if (_module.Error is { } failed) Error(failed);
            return;
        }
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

    /// <summary>The island's mic card as a Settings row: the state as the caption, and Mute or Unmute.</summary>
    private SettingRow MicrophoneRow(bool muted)
    {
        bool none = _module.Status.State == MicrophoneMuteState.NoMicrophone;
        var action = RoutingUi.Button(muted ? "Unmute" : "Mute", Toggle);
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

    /// <summary>The Sound page's mic card, one 44 row like the Output card: whether the mics are live, and Mute or Unmute.</summary>
    private Border MicCard(bool muted)
    {
        bool warning = _module.Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed;
        bool none = _module.Status.State == MicrophoneMuteState.NoMicrophone;
        var glyph = new LucideIcon { Data = warning || none ? LucideIcons.MicOff : LucideIcons.Mic };
        glyph.SetResourceReference(LucideIcon.BrushProperty, warning ? "WarningBrush" : none ? "IslandTextTertiaryBrush" : "IslandAccentBrush");
        var state = new TextBlock { Text = muted ? "Muted" : _module.StatusText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (warning || none) state.SetResourceReference(TextBlock.ForegroundProperty, warning ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        var action = RoutingUi.IslandButton(muted ? "Unmute" : "Mute", Toggle);
        AutomationProperties.SetName(action, muted ? "Unmute all microphones" : "Mute all microphones");
        action.IsEnabled = !none;
        return RoutingUi.IslandCard(glyph, state, action);
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
