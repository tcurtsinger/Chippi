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

namespace Chippi.Features.Sound;

/// <summary>One device, its current level and its controls. Routing supplies a picker when installed.</summary>
internal sealed class AudioDeviceCard : Border
{
    private readonly AudioFlow _flow;
    private readonly ComboBox _picker = new()
    {
        Height = 27, MinWidth = 0, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left,
        FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(-11, 0, 0, 0),
    };
    private readonly TextBlock _display = new()
    {
        FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _routingError;
    private AudioRoutingModule? _routing;
    private IReadOnlyList<PriorityEntry> _connected = [];
    private bool _synchronizing;
    public event Action? RoutingChanged;

    public AudioDeviceCard(AudioFlow flow, LucideIcon icon, TextBlock? value, Slider? slider, Button? mute, TextBlock? error)
    {
        _flow = flow;
        string label = flow == AudioFlow.Render ? SoundTile.Output : SoundTile.Microphone;
        SetResourceReference(StyleProperty, "IslandCardStyle");
        Padding = new Thickness(14, 6, 14, 6);

        var caption = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Medium };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        icon.Size = 22;
        icon.Margin = new Thickness(3, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var heading = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        heading.Children.Add(icon);
        if (value is not null)
        {
            value.FontSize = 17;
            value.MinWidth = 38;
            value.TextAlignment = TextAlignment.Right;
            value.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(value, Dock.Right);
            heading.Children.Add(value);
        }
        _picker.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        _picker.SetResourceReference(Control.ForegroundProperty, "IslandTextBrush");
        // Keep the collapsed device name and chevron on the card surface; the menu keeps the normal combo template.
        _picker.Resources["ButtonBackgroundBrush"] = System.Windows.Media.Brushes.Transparent;
        _picker.Resources["ButtonBorderBrush"] = System.Windows.Media.Brushes.Transparent;
        AutomationProperties.SetName(_picker, label + " device");
        _picker.SelectionChanged += Pick;
        heading.Children.Add(new StackPanel
        {
            Children = { caption, new Grid { Height = 27, Children = { _display, _picker } } },
        });

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { heading } };
        if (slider is not null && mute is not null)
        {
            mute.MinWidth = 54;
            mute.Height = 26;
            mute.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(mute, Dock.Right);
            content.Children.Add(new DockPanel { Margin = new Thickness(0, 7, 0, 0), Children = { mute, slider } });
        }
        _routingError = ErrorText();
        content.Children.Add(_routingError);
        if (error is not null)
            content.Children.Add(error);
        Child = content;
        Loaded += (_, _) => { if (_routing is not null) _routing.Changed += RefreshPicker; RefreshPicker(); };
        Unloaded += (_, _) => { if (_routing is not null) _routing.Changed -= RefreshPicker; };
        RefreshPicker();
    }

    public static TextBlock ErrorText()
    {
        var text = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
        text.Visibility = Visibility.Collapsed;
        text.Margin = new Thickness(0, 2, 0, 0);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return text;
    }

    public static void ShowError(TextBlock text, string? message)
    {
        text.Text = message ?? "";
        text.ToolTip = message;
        text.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetRouting(AudioRoutingModule? routing)
    {
        if (ReferenceEquals(_routing, routing)) return;
        if (IsLoaded && _routing is not null) _routing.Changed -= RefreshPicker;
        _routing = routing;
        if (IsLoaded && _routing is not null) _routing.Changed += RefreshPicker;
        RefreshPicker();
    }

    public void ShowName(string? name)
    {
        _display.Text = name is null ? _flow == AudioFlow.Render ? "No output" : "No microphone" : DeviceName.Short(name);
        _display.ToolTip = name;
    }

    private void RefreshPicker()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshPicker); return; }
        var routing = _routing;
        string? inUse = routing?.InUse(_flow);
        IReadOnlyList<PriorityEntry> connected = routing is null ? [] :
            routing.Settings.For(_flow).Devices.Where(e => routing.Snapshot.Find(e.Id)?.IsActive == true).ToList();
        _synchronizing = true;
        try
        {
            var focused = Keyboard.FocusedElement as ComboBoxItem;
            bool focusInList = _picker.IsDropDownOpen && focused is not null && _picker.Items.Contains(focused);
            if (!_connected.SequenceEqual(connected))
            {
                _connected = connected;
                UpdateChoices(connected);
            }
            var current = _picker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, inUse, StringComparison.OrdinalIgnoreCase));
            _picker.SelectedItem = current;
            _picker.Tag = routing?.Snapshot.Find(inUse ?? "")?.Name ?? (_flow == AudioFlow.Render ? "Choose output" : "Choose microphone");
            _picker.ToolTip = current?.ToolTip ?? _picker.Tag;
            _picker.Visibility = connected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _display.Visibility = connected.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            ShowError(_routingError, routing?.Error);
            if (focusInList && _picker.IsDropDownOpen && focused is not null && !focused.IsKeyboardFocused)
                (_picker.Items.Contains(focused) ? focused : current)?.Focus();
        }
        finally { _synchronizing = false; }
        RoutingChanged?.Invoke();
    }

    // Keep surviving items in place while the list is open, including when an earlier endpoint disappears.
    private void UpdateChoices(IReadOnlyList<PriorityEntry> connected)
    {
        var names = DeviceName.Shown(connected);
        var items = _picker.Items.OfType<ComboBoxItem>().ToDictionary(i => (string)i.Tag, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, item) in items)
            if (!names.ContainsKey(id)) _picker.Items.Remove(item);
        for (int i = 0; i < connected.Count; i++)
        {
            var device = connected[i];
            if (!items.TryGetValue(device.Id, out var item))
            {
                item = new ComboBoxItem { Tag = device.Id };
                _picker.Items.Insert(i, item);
            }
            else if (_picker.Items.IndexOf(item) != i)
            {
                _picker.Items.Remove(item);
                _picker.Items.Insert(i, item);
            }
            item.Content = names[device.Id];
            item.ToolTip = device.Name;
            AutomationProperties.SetName(item, device.Name);
        }
    }

    private async void Pick(object sender, SelectionChangedEventArgs args)
    {
        if (_synchronizing || _routing is not { } routing || _picker.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (string.Equals(id, routing.InUse(_flow), StringComparison.OrdinalIgnoreCase)) return;
        try { await routing.UseAsync(_flow, id); }
        catch { /* The module reports its error beside the picker. */ }
        RefreshPicker();
    }
}
