// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using Chippi.UI.Shortcuts;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.PlainPaste;

/// <summary>The same shortcut row in Clipboard settings and the shared shortcut list.</summary>
public sealed class PlainPasteView : UserControl
{
    private readonly PlainPasteModule _module;
    private readonly string _title;
    private readonly SettingRow _row;
    private readonly TextBlock _keys = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new();
    private readonly StackPanel _errorLine = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _edit, _remove, _cancel;
    private bool _capturing;

    public PlainPasteView(PlainPasteModule module, string title = "Shortcut")
    {
        _module = module;
        _title = title;
        _edit = Button("Set", BeginCapture);
        _remove = Button("Remove", () =>
        {
            _module.RemoveShortcut();
            Refresh();
            _edit.Focus();
        });
        _cancel = Button("Cancel", CancelCapture);
        _keys.SetResourceReference(StyleProperty, "BodyTextStyle");
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Children = { _keys, _edit, _remove, _cancel } };
        _row = SettingsLayout.Row(title, controls);
        var card = SettingsLayout.Card(_row);
        if (title != "Shortcut") card.SetResourceReference(StyleProperty, "CardRowsStyle");
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "WarningBrush");
        _errorLine.Children.Add(dot);
        _errorLine.Children.Add(_error);
        Content = new StackPanel { Children = { card, _errorLine } };
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; EndCapture(); };
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (_capturing && !IsKeyboardFocusWithin)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (!IsKeyboardFocusWithin) EndCapture(); });
        };
        PreviewKeyDown += Capture;
        PreviewKeyUp += (_, _) => { if (_capturing) ShowModifiers(Keyboard.Modifiers); };
        Refresh();
    }

    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        button.Click += (_, _) => action();
        return button;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var shortcut = _module.Settings.Shortcut;
        _row.Header = _capturing ? "New shortcut" : _title;
        _edit.Visibility = _capturing ? Visibility.Collapsed : Visibility.Visible;
        _remove.Visibility = !_capturing && shortcut is not null ? Visibility.Visible : Visibility.Collapsed;
        _cancel.Visibility = _capturing ? Visibility.Visible : Visibility.Collapsed;
        _edit.Content = shortcut is null ? "Set" : "Change";
        AutomationProperties.SetName(_edit, (string)_edit.Content);
        AutomationProperties.SetName(_remove, "Remove");
        AutomationProperties.SetName(_cancel, "Cancel");
        string error = _capturing ? "" : _module.ShortcutError ?? "";
        _error.Text = error;
        _errorLine.Visibility = error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _keys.SetResourceReference(TextBlock.ForegroundProperty, _capturing || shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush");
        if (_capturing) ShowModifiers(Keyboard.Modifiers);
        else _keys.Text = shortcut?.ToString() ?? "Not set";
    }

    private void ShowModifiers(ModifierKeys modifiers)
    {
        var held = new List<string>(3);
        if (modifiers.HasFlag(ModifierKeys.Control)) held.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) held.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) held.Add("Shift");
        _keys.Text = held.Count == 0 ? "Press Ctrl or Alt with a key" : string.Join("+", held) + "+…";
        AutomationProperties.SetHelpText(_cancel, _keys.Text);
    }

    private void BeginCapture()
    {
        _capturing = true;
        _module.CaptureShortcut(true);
        Refresh();
        _cancel.Focus();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _module.CaptureShortcut(false);
        Refresh();
    }

    private void CancelCapture()
    {
        EndCapture();
        _edit.Focus();
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        if (e.Key == Key.Escape) { CancelCapture(); e.Handled = true; return; }
        if (e.Key == Key.Tab || (e.Key is Key.Enter or Key.Space && Keyboard.Modifiers == ModifierKeys.None))
            return; // Normal focus traversal and keyboard activation of Cancel.
        if (ShortcutKeys.From(e) is not { } shortcut)
        {
            ShowModifiers(Keyboard.Modifiers);
            e.Handled = true;
            return;
        }
        e.Handled = true;
        _module.ChangeShortcut(shortcut);
        CancelCapture();
    }
}
