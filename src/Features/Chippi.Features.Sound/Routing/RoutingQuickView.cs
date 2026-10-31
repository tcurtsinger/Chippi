// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.Audio;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>
/// Routing's sections of the Sound page (DESIGN.md §5 Island pages, revised 2026-10-31). Output: the
/// system volume and mute on the section's top row, a hairline, then the connected outputs.
/// Microphone: the connected mics, under Mic mute's row. The list fills the rest of its column's
/// card and shows the whole rows that fit there. Rows are one line and lead with the device; a pick
/// always works, now, and leaves the priority list alone. Rows that don't fit, and unplugged
/// devices, are only in Settings, but the one in use always shows.
/// </summary>
public sealed class RoutingQuickView : UserControl
{
    internal const double RowHeight = 30;
    private const double ListInset = 4; // the rows sit 4 in from the card's edge, so a highlight keeps its corners

    private readonly AudioRoutingModule _module;
    private readonly AudioFlow _flow;
    private readonly Grid _list = new() { ClipToBounds = true };
    private readonly StackPanel _rows = new() { Margin = new Thickness(ListInset), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private readonly LucideIcon _glyph = new();
    private readonly TextBlock _none = new() { Text = "No output", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _mute;
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 12, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly DockPanel _levels = new();
    private readonly LatestWrite _volumeWrite = new();
    private int _fit = int.MaxValue; // every row until the list is laid out and knows its room
    private string _shape = "";
    private bool _refreshing;

    public RoutingQuickView(AudioRoutingModule module, AudioFlow flow)
    {
        _module = module;
        _flow = flow;
        _mute = RoutingUi.IslandButton("Mute", () =>
        {
            if (_module.InUse(AudioFlow.Render) is { } id)
                Run(_module.SetOutputMutedAsync(id, !Level(id).Muted));
        });
        var root = new DockPanel();
        if (flow == AudioFlow.Render)
        {
            var top = OutputRow();
            var line = RoutingUi.Hairline();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(line, Dock.Top);
            root.Children.Add(top);
            root.Children.Add(line);
        }
        _error.Margin = new Thickness(9, 0, 9, 8);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        DockPanel.SetDock(_error, Dock.Bottom);
        root.Children.Add(_error);
        _list.Children.Add(_rows);
        _list.SizeChanged += (_, e) => Refit(e.NewSize.Height);
        root.Children.Add(_list);
        Content = root;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    /// <summary>The Output section's top row: the speaker, the system volume of the output in use, and Mute.</summary>
    private Border OutputRow()
    {
        _volume.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing && _module.InUse(AudioFlow.Render) is { } id)
                _volumeWrite.Send(() => _module.SetOutputVolumeAsync(id, (float)e.NewValue / 100));
        };
        _percent.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        _percent.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(_percent, Dock.Right);
        _levels.Children.Add(_percent);
        _levels.Children.Add(_volume);
        _none.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        return RoutingUi.IslandRow(_glyph, new Grid { Children = { _levels, _none } }, _mute);
    }

    private (float Volume, bool Muted) Level(string id) =>
        _module.Snapshot.Levels.TryGetValue(id, out var level) ? (level.Volume, level.Muted) : (0, false);

    private bool OutputMuted() => _module.InUse(AudioFlow.Render) is { } id && Level(id).Muted;

    private void ShowPercent()
    {
        bool muted = OutputMuted();
        _percent.Text = muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        Quiet(_volume, muted);
    }

    /// <summary>A muted level's slider keeps its level in tertiary, not the accent, as a paused song's bar does (DESIGN.md §5).</summary>
    internal static void Quiet(Slider slider, bool muted)
    {
        foreach (var key in new[] { "IslandAccentFillBrush", "IslandAccentBrush" })
        {
            if (muted)
                slider.Resources[key] = slider.TryFindResource("IslandTextTertiaryBrush");
            else
                slider.Resources.Remove(key);
        }
    }

    /// <summary>The list's room changed (the page was laid out, or a warning line came or went): show the whole rows that fit.</summary>
    private void Refit(double height)
    {
        int fit = Math.Max(0, (int)Math.Floor((height - 2 * ListInset + 0.5) / RowHeight));
        if (fit == _fit)
            return;
        _fit = fit;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            var inUse = _module.InUse(_flow);
            var connected = _module.Settings.For(_flow).Devices.Where(e => _module.Snapshot.Find(e.Id)?.IsActive == true).ToList();
            _error.Text = _module.Error ?? "";
            _error.Visibility = _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            var shown = connected.Take(_fit).ToList();
            // The one in use always shows: past the rows that fit, it takes the last one's place.
            if (shown.Count > 0 && connected.FindIndex(e => string.Equals(e.Id, inUse, StringComparison.OrdinalIgnoreCase)) >= shown.Count)
                shown[^1] = connected.First(e => string.Equals(e.Id, inUse, StringComparison.OrdinalIgnoreCase));
            var names = DeviceName.Shown(connected);
            var shape = string.Join("|", shown.Select(e => e.Id + names[e.Id])) + ":" + inUse;
            if (shape != _shape)
            {
                _shape = shape;
                _rows.Children.Clear();
                foreach (var entry in shown)
                    _rows.Children.Add(Row(entry, names[entry.Id], string.Equals(entry.Id, inUse, StringComparison.OrdinalIgnoreCase)));
            }
            if (_flow == AudioFlow.Render)
                RefreshOutput(inUse);
            else // Mic mute's row above says when there's no microphone; nothing shows here then
                Visibility = connected.Count == 0 && _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }

    private void RefreshOutput(string? inUse)
    {
        bool none = _module.Snapshot.Find(inUse ?? "") is null;
        bool known = inUse is not null && _module.Snapshot.Levels.ContainsKey(inUse);
        var (volume, muted) = inUse is null ? (0f, false) : Level(inUse);
        _levels.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _none.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _volume.IsEnabled = _mute.IsEnabled = known && !none;
        if (!_volume.IsMouseCaptureWithin && !_volume.IsKeyboardFocusWithin)
            _volume.Value = volume * 100;
        ShowPercent();
        _glyph.Data = muted || none ? LucideIcons.VolumeMuted : LucideIcons.Speaker;
        _glyph.SetResourceReference(LucideIcon.BrushProperty, none ? "IslandTextTertiaryBrush" : muted ? "WarningBrush" : "IslandAccentBrush");
        _mute.Content = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(_mute, muted ? "Unmute system volume" : "Mute system volume");
    }

    /// <summary>
    /// A connected device on one line: a radio under the top row's glyph and its shown name (the full
    /// one in its tooltip), semibold while in use. Picking it uses it now.
    /// </summary>
    private Button Row(PriorityEntry entry, string shownName, bool selected)
    {
        var radio = new Ellipse
        {
            Width = 14, Height = 14, StrokeThickness = selected ? 4 : 1, Margin = new Thickness(1, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.Transparent,
        };
        radio.SetResourceReference(Shape.StrokeProperty, selected ? "IslandAccentBrush" : "IslandTextTertiaryBrush");
        if (selected)
            radio.SetResourceReference(Shape.FillProperty, "IslandOnAccentBrush");
        var name = new TextBlock
        {
            Text = shownName, FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(radio, Dock.Left);
        var button = new Button { Content = new DockPanel { Children = { radio, name } }, ToolTip = entry.Name, Tag = selected ? "Selected" : null };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        button.Padding = new Thickness(5, 0, 5, 0); // 4 in the list, 1 the card's edge: the radio's center is the glyph's
        AutomationProperties.SetName(button, selected ? $"{entry.Name}, in use" : $"Use {entry.Name}");
        button.Click += (_, _) =>
        {
            if (!selected)
                Run(_module.UseAsync(_flow, entry.Id));
        };
        return button;
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch { /* The module exposes the error to the view. */ }
        Refresh();
    }
}
