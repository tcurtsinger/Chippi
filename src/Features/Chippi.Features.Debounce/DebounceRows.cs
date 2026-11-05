// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.UI.Controls;

namespace Chippi.Features.Debounce;

/// <summary>
/// Debounce's rows for Settings > Windows > Mouse (the click switch) and Keyboard (the key switch and the keys with their
/// own time). The shell owns the page title and the section heading. A switch shows what it ignored, in memory only.
/// </summary>
public sealed class DebounceRows : UserControl
{
    private readonly DebounceModule _module;
    private readonly bool _keyboard;
    private readonly DispatcherTimer _counts = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _keys = new();
    private readonly ToggleButton _toggle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SettingRow _header = new();
    private readonly SettingRow? _ownHeader;
    private readonly Slider _slider;
    private readonly TextBlock _value;
    private readonly FrameworkElement _timeRow;
    private readonly Border _error;
    private readonly TextBlock _errorText = new();
    private bool _updating;
    private KeyPicker? _picker;

    public DebounceRows(DebounceModule module, string section)
    {
        _module = module;
        _keyboard = section == DebounceModule.KeyboardSection;
        string title = _keyboard ? "Key debounce" : "Click debounce";
        _toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(_toggle, title);
        _header.Header = title;
        _header.Content = _toggle;

        (int min, int max) = _keyboard ? (DebounceSettings.KeyMinMs, DebounceSettings.KeyMaxMs) : (DebounceSettings.ClickMinMs, DebounceSettings.ClickMaxMs);
        _slider = TimeSlider("Debounce time", min, max, out _value);
        _timeRow = Dependent("Debounce time", Pair(_slider, _value));
        _slider.ValueChanged += (_, e) =>
        {
            if (_updating) return;
            int ms = (int)e.NewValue;
            Save(s => _keyboard ? s with { KeyMs = ms } : s with { ClickMs = ms });
        };

        _errorText.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_errorText, AutomationLiveSetting.Polite);
        _error = new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = _errorText };

        var cards = new StackPanel();
        cards.Children.Add(SettingsLayout.Card(_header, _timeRow, _error));
        if (_keyboard)
        {
            var add = SettingsLayout.AddButton("key", AddKey);
            _ownHeader = SettingsLayout.Row("Keys with their own time", add);
            cards.Children.Add(new Border { Height = 8 });
            cards.Children.Add(SettingsLayout.Card(_ownHeader, _keys));
        }
        Content = cards;

        // A UIA Toggle raises Checked and Unchecked, not Click.
        _toggle.Checked += (_, _) => Switch(true);
        _toggle.Unchecked += (_, _) => Switch(false);
        _counts.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { module.Changed += OnChanged; _counts.Start(); Refresh(); };
        Unloaded += (_, _) => { module.Changed -= OnChanged; _counts.Stop(); _picker?.Dispose(); _picker = null; };
        Refresh();
    }

    private void Switch(bool on)
    {
        if (_updating) return;
        Save(s => _keyboard ? s with { KeyEnabled = on } : s with { ClickEnabled = on });
    }

    private void Save(Func<DebounceSettings, DebounceSettings> change)
    {
        try { _module.Update(change); }
        catch (Exception ex) { Chippi.Core.Diagnostics.Log.Warn("Debounce: saving failed", ex); }
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var s = _module.Settings;
        bool on = _keyboard ? s.KeyEnabled : s.ClickEnabled;
        int ms = _keyboard ? s.KeyMs : s.ClickMs;
        string? error = _keyboard ? _module.KeyboardError : _module.MouseError;
        _updating = true;
        try
        {
            _toggle.IsChecked = on;
            _toggle.Content = on ? "On" : "Off";
            int ignored = _keyboard ? _module.KeyIgnored : _module.ClickIgnored;
            _header.Description = on && error is null ? $"{ignored} ignored" : "";
            _slider.Value = ms;
            _value.Text = $"{ms} ms";
            _timeRow.IsEnabled = on;
            _timeRow.Opacity = on ? 1 : 0.45;
            _errorText.Text = error ?? "";
            _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetItemStatus(_toggle, error ?? (on ? "On" : "Off"));
            if (_keyboard)
                RebuildKeys(on);
        }
        finally { _updating = false; }
    }

    private void RebuildKeys(bool on)
    {
        var keys = _module.Settings.Keys;
        _ownHeader!.Description = keys.Length == 1 ? "1 key" : $"{keys.Length} keys";
        _ownHeader.Opacity = on ? 1 : 0.45;
        _keys.Opacity = on ? 1 : 0.45;
        _keys.Children.Clear();
        foreach (var key in keys)
        {
            var captured = key;
            var slider = TimeSlider($"Debounce time for {KeyName(key.Vk)}", DebounceSettings.KeyMinMs, DebounceSettings.KeyMaxMs, out var value);
            slider.Value = key.Ms;
            value.Text = $"{key.Ms} ms";
            slider.ValueChanged += (_, e) =>
            {
                if (_updating) return;
                int ms = (int)e.NewValue;
                value.Text = $"{ms} ms";
                Save(s => s with { Keys = [.. s.Keys.Select(k => k.Vk == captured.Vk ? k with { Ms = ms } : k)] });
            };
            var remove = new Button { Content = "Remove", Margin = new Thickness(12, 0, 0, 0) };
            remove.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            AutomationProperties.SetName(remove, $"Remove {KeyName(key.Vk)}");
            remove.Click += (_, _) => Save(s => s with { Keys = [.. s.Keys.Where(k => k.Vk != captured.Vk)] });
            var capText = new TextBlock { Text = KeyName(key.Vk), VerticalAlignment = VerticalAlignment.Center };
            capText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var cap = new Border { Child = capText, Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            cap.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            _keys.Children.Add(Dependent(null, Pair(slider, value, remove), cap));
        }
    }

    private void AddKey()
    {
        var hub = _module.Hub;
        if (hub is null) return;
        _picker?.Dispose();
        try
        {
            _picker = new KeyPicker(hub, vk =>
            {
                _picker = null;
                if (vk is not { } code) return;
                Save(s => s.Keys.Any(k => k.Vk == code) ? s : s with { Keys = [.. s.Keys, new KeyWindow(code, s.KeyMs == 0 ? 15 : s.KeyMs)] });
            });
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Debounce: key capture failed", ex);
        }
    }

    internal static string KeyName(uint vk) => KeyInterop.KeyFromVirtualKey((int)vk) switch
    {
        Key.None => $"Key {vk}",
        var k => k.ToString(),
    };

    private static Slider TimeSlider(string name, int min, int max, out TextBlock value)
    {
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 240, VerticalAlignment = VerticalAlignment.Center };
        slider.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(slider, name);
        value = new TextBlock { MinWidth = 52, TextAlignment = TextAlignment.Right, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(StyleProperty, "NumberTextStyle");
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return slider;
    }

    private static StackPanel Pair(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    /// <summary>A row inset like the dependent rows of a switch: a label (or a key cap) at the left, the control at the right.</summary>
    private static Border Dependent(string? title, UIElement control, UIElement? lead = null)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        UIElement left;
        if (lead is not null)
            left = lead;
        else
        {
            var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(StyleProperty, "BodyTextStyle");
            left = label;
        }
        if (left is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(left);
        return new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.DependentPadding, Child = dock };
    }
}
