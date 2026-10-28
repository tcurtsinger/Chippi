// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.PlainPaste;

public sealed class PlainPasteView : UserControl
{
    private readonly PlainPasteModule _module;
    private readonly StackPanel _body = new();
    private bool _capturing;
    public PlainPasteView(PlainPasteModule module)
    {
        _module = module; Content = _body;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; CancelCapture(); };
        PreviewKeyDown += Capture;
        Refresh();
    }
    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle"); AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action(); return button;
    }
    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_capturing) return;
        _body.Children.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = _module.Settings.Shortcut?.ToString() ?? "Not set", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        row.Children.Add(Button(_module.Settings.Shortcut is null ? "Set" : "Change", BeginCapture));
        if (_module.Settings.Shortcut is not null) row.Children.Add(Button("Remove", () => { _module.RemoveShortcut(); Refresh(); }));
        var card = new Card(); card.Children.Add(new SettingRow { Header = "Shortcut", Content = row }); _body.Children.Add(card);
        if (_module.ShortcutError is { } error)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = (System.Windows.Media.Brush)FindResource("WarningBrush"), Margin = new Thickness(0, 0, 8, 0) });
            var text = new TextBlock { Text = error, FontSize = 12 }; text.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite); line.Children.Add(text); _body.Children.Add(line);
        }
    }
    private void BeginCapture()
    {
        _capturing = true; _module.CaptureShortcut(true); Focusable = true;
        _body.Children.Clear(); var card = new Card();
        card.Children.Add(new SettingRow { Header = "New shortcut", Content = Button("Cancel", CancelCapture) });
        _body.Children.Add(card); Focus();
    }
    private void CancelCapture() { _capturing = false; _module.CaptureShortcut(false); Refresh(); }
    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        if (e.Key == Key.Escape) { CancelCapture(); e.Handled = true; return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var mods = Keyboard.Modifiers;
        var flags = (mods.HasFlag(ModifierKeys.Control) ? ShortcutModifiers.Control : 0)
            | (mods.HasFlag(ModifierKeys.Alt) ? ShortcutModifiers.Alt : 0)
            | (mods.HasFlag(ModifierKeys.Shift) ? ShortcutModifiers.Shift : 0)
            | (mods.HasFlag(ModifierKeys.Windows) ? ShortcutModifiers.Windows : 0);
        _module.ChangeShortcut(new MicShortcut(flags, (uint)KeyInterop.VirtualKeyFromKey(key)));
        CancelCapture(); e.Handled = true;
    }
}
