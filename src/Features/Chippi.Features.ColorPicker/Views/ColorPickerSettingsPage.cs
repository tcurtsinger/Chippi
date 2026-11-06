// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.ColorPicker.Views;

/// <summary>
/// Settings › Color picker (Settings › Capture v4, color-picker.md Settings): Recent colors, the palette with Clear and
/// its confirm; Copying, Copy as with each format written out; then Shortcuts, read-only with Edit, since they're set in
/// Settings › Shortcuts. Open Capture is the Capture host's slot, shown here too.
/// </summary>
public sealed class ColorPickerSettingsPage : UserControl
{
    private readonly IColorPickerSettingsModel _model;
    private readonly Button _clear = new() { Content = "Clear" };
    private readonly WrapPanel _swatches = new();
    private readonly Border _swatchBox;
    private readonly Border _none = SettingsLayout.Empty("No recent colors");
    private readonly Card _recent;
    private readonly ComboBox _copyAs = SettingsLayout.Combo([]);
    private readonly Dictionary<string, ShortcutSetting> _shortcuts = [];
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<ColorFormat> _formats = [];
    private IReadOnlyList<string> _palette = [];
    private bool _updating, _listening;

    public ColorPickerSettingsPage(IColorPickerSettingsModel model)
    {
        _model = model;
        _clear.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(_clear, "Clear recent colors");
        _clear.Click += (_, _) => Confirm(_clear, "Clear recent colors?", "Clear", () => _model.ClearRecent());
        AutomationProperties.SetName(_swatches, "Recent colors");
        _swatchBox = new Border { Padding = new Thickness(16, 12, 8, 4), Child = _swatches };
        _recent = SettingsLayout.Card(_swatchBox);
        _copyAs.Width = 260;
        AutomationProperties.SetName(_copyAs, "Copy as");
        _copyAs.SelectionChanged += (_, _) =>
        {
            if (!_updating && _copyAs.SelectedIndex >= 0 && _copyAs.SelectedIndex < _formats.Count)
                Change(() => _model.SetCopyAs(_formats[_copyAs.SelectedIndex].Name));
        };
        _error.SetResourceReference(StyleProperty, "BodyTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);

        var shortcuts = SettingsLayout.Card();
        foreach (string name in ColorPickerShortcuts.All)
        {
            var setting = new ShortcutSetting(name, () => _model.EditShortcut(name));
            _shortcuts[name] = setting;
            shortcuts.Children.Add(SettingsLayout.Row(name, setting));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Recent colors", _clear, first: true), _recent,
                SettingsLayout.Section("Copying"), SettingsLayout.Card(SettingsLayout.Row("Copy as", _copyAs)), _error,
                SettingsLayout.Section("Shortcuts"), shortcuts,
            },
        };
        // Listen from the start, not only once loaded: the page is built before Settings shows it.
        Listen(true);
        Loaded += (_, _) => { Listen(true); Refresh(); };
        Unloaded += (_, _) => Listen(false);
        Refresh();
    }

    internal ComboBox CopyAs => _copyAs;

    internal Button Clear => _clear;

    internal IReadOnlyList<FrameworkElement> Swatches => [.. _swatches.Children.OfType<FrameworkElement>()];

    internal TextBlock Error => _error;

    internal ShortcutSetting ShortcutOf(string name) => _shortcuts[name];

    /// <summary>"HEX · #7C5CFF": the format and what it writes.</summary>
    internal static string Label(ColorFormat format) => $"{format.Name} · {format.Value}";

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) _model.Changed += OnChanged;
        else _model.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(() => { if (_listening) Refresh(); });
    }

    private void Refresh()
    {
        var formats = _model.Formats;
        _updating = true;
        try
        {
            if (!_formats.SequenceEqual(formats))
            {
                _formats = [.. formats];
                _copyAs.ItemsSource = _formats.Select(Label).ToArray();
            }
            _copyAs.SelectedIndex = _formats.ToList().FindIndex(f => f.Name == _model.CopyAs);
        }
        finally { _updating = false; }

        var recent = _model.Recent;
        if (!_palette.SequenceEqual(recent))
        {
            _palette = [.. recent];
            _swatches.Children.Clear();
            foreach (string hex in recent) _swatches.Children.Add(Swatch(hex));
        }
        bool none = recent.Count == 0;
        _swatchBox.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        if (none && !_recent.Children.Contains(_none)) _recent.Children.Add(_none);
        if (!none) _recent.Children.Remove(_none);
        _clear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;

        foreach (string name in ColorPickerShortcuts.All)
        {
            var (keys, error) = _model.Shortcut(name);
            _shortcuts[name].Show(keys, error);
        }
    }

    /// <summary>One recent color, 32 square with its HEX as its name; the island's Color page is where a swatch opens.</summary>
    private static Border Swatch(string hex)
    {
        Brush fill = ColorConverter.ConvertFromString(hex) is Color color ? new SolidColorBrush(color) : Brushes.Transparent;
        fill.Freeze();
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = fill, Margin = new Thickness(0, 0, 8, 8), ToolTip = hex };
        swatch.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        AutomationProperties.SetName(swatch, hex);
        return swatch;
    }

    private void Change(Func<bool> write)
    {
        bool saved = write();
        Refresh();
        _error.Text = saved ? "" : _model.SettingsError ?? "Save failed";
        _error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved) UIElementAutomationPeer.FromElement(_error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>A short question under <paramref name="anchor"/>, with Cancel and the verb; Esc or Cancel goes back to the anchor.</summary>
    private static void Confirm(FrameworkElement anchor, string question, string verb, Action confirmed)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var cancel = new Button { Content = "Cancel" };
        cancel.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        var act = new Button { Content = verb, Margin = new Thickness(8, 0, 0, 0) };
        act.SetResourceReference(StyleProperty, "CriticalButtonStyle");
        cancel.Click += (_, _) => { popup.IsOpen = false; anchor.Focus(); };
        act.Click += (_, _) => { popup.IsOpen = false; confirmed(); };
        var asked = new TextBlock { Text = question, FontWeight = FontWeights.SemiBold };
        asked.SetResourceReference(StyleProperty, "BodyTextStyle");
        var body = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { asked, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, act } } },
        };
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        var panel = new Border { MinWidth = 260, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
        panel.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        panel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        AutomationProperties.SetName(panel, question);
        popup.Child = panel;
        popup.Opened += (_, _) => cancel.Focus();
        popup.IsOpen = true;
    }
}
