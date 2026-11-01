// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The top of Settings › System: the island's widgets (but Drives, which has its own section) in the
/// island's rows, CPU, GPU and Memory, then Network, Battery and Temperature (Network alone across the
/// whole width), live while Settings shows them. Settings' cards, graphs and meters in the Windows
/// accent; nothing to click.
/// </summary>
internal sealed class SettingsReadings : UserControl
{
    /// <summary>A row of widgets with graphs, and Network alone in its row.</summary>
    public const double RowHeight = 126, WideNetworkHeight = 92;

    private readonly SystemMonitorModule _module;
    private readonly Present _present;
    private readonly StackPanel _grid = new();
    private readonly Dictionary<Detail, Widget> _widgets = [];
    private SensorWatch? _watch;
    private SensorSampler? _sampler;
    private string _key = "";

    public SettingsReadings(SystemMonitorModule module)
    {
        _module = module;
        _present = new Present(module);
        Content = _grid;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    internal IReadOnlyList<Widget> Widgets { get; private set; } = [];

    private void Start()
    {
        if (_module.Sampler is not { } sampler || _sampler is not null) return;
        _sampler = sampler;
        _module.Changed += OnSettingsChanged;
        sampler.Updated += OnUpdated;
        _watch = sampler.Watch(SystemView.Need(_module.Settings) & ~SensorNeed.Disks);
        Show(SystemView.Opening(sampler.Latest, _module.PageSnapshot));
    }

    private void Stop()
    {
        _module.Changed -= OnSettingsChanged;
        if (_sampler is not null) _sampler.Updated -= OnUpdated;
        _watch?.Dispose();
        _watch = null;
        _sampler = null;
    }

    private void OnSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnSettingsChanged); return; }
        if (_watch is not null) _watch.Need = SystemView.Need(_module.Settings) & ~SensorNeed.Disks;
        if (_sampler is not null) Show(SystemView.Opening(_sampler.Latest, _module.PageSnapshot));
    }

    private void OnUpdated(SystemSnapshot snapshot) => Dispatcher.BeginInvoke(() => { if (_sampler is not null) Show(snapshot); });

    internal void Show(SystemSnapshot s)
    {
        var (loads, rest, _) = _present.Plan(s);
        string key = string.Join(",", loads) + "|" + string.Join(",", rest);
        if (key != _key)
        {
            _key = key;
            Layout(loads, rest);
        }
        foreach (var widget in Widgets)
            _present.Fill(widget, s);
    }

    private void Layout(List<Detail> loads, List<Detail> rest)
    {
        foreach (var row in _grid.Children.OfType<Grid>()) row.Children.Clear();
        _grid.Children.Clear();
        bool wide = rest is [Detail.Network];
        if (_widgets.TryGetValue(Detail.Network, out var old) && old is NetworkWidget network && network.Wide != wide)
            _widgets.Remove(Detail.Network);
        var widgets = new List<Widget>();
        foreach (var kinds in new[] { loads, rest })
        {
            if (kinds.Count == 0)
                continue;
            var row = new Grid { Height = kinds == rest && wide ? WideNetworkHeight : RowHeight, Margin = new Thickness(0, _grid.Children.Count > 0 ? 8 : 0, 0, 0) };
            for (int c = 0; c < kinds.Count; c++)
            {
                if (c > 0) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var kind = kinds[c];
                if (!_widgets.TryGetValue(kind, out var widget))
                    _widgets[kind] = widget = kind == Detail.Network ? new NetworkWidget(Look.Settings, null, wide) : _present.NewReading(kind, Look.Settings, null);
                Grid.SetColumn(widget.Root, c * 2);
                row.Children.Add(widget.Root);
                widgets.Add(widget);
            }
            _grid.Children.Add(row);
        }
        Widgets = widgets;
        Visibility = widgets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
