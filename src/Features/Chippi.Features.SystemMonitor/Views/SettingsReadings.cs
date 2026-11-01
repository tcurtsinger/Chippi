// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The top of Settings › System: the island's widgets (but Drives, which has its own section), 4 to a
/// row, live while Settings shows them. Settings' cards and the accent meter; nothing to click.
/// </summary>
internal sealed class SettingsReadings : UserControl
{
    private const int PerRow = 4;
    private readonly SystemMonitorModule _module;
    private readonly Present _present;
    private readonly Grid _grid = new();
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
        var kinds = loads.Concat(rest).ToList();
        string key = string.Join(",", kinds);
        if (key != _key)
        {
            _key = key;
            Layout(kinds);
        }
        foreach (var widget in Widgets)
            _present.Fill(widget, s);
    }

    private void Layout(List<Detail> kinds)
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        for (int c = 0; c < PerRow; c++)
        {
            if (c > 0) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        var widgets = new List<Widget>();
        for (int i = 0; i < kinds.Count; i++)
        {
            int row = i / PerRow, column = i % PerRow;
            if (column == 0)
            {
                if (row > 0) _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var kind = kinds[i];
            if (!_widgets.TryGetValue(kind, out var widget))
                _widgets[kind] = widget = kind == Detail.Network ? new NetworkWidget(Look.Settings, null, wide: false) : _present.NewReading(kind, Look.Settings, null);
            Grid.SetRow(widget.Root, row * 2);
            Grid.SetColumn(widget.Root, column * 2);
            _grid.Children.Add(widget.Root);
            widgets.Add(widget);
        }
        Widgets = widgets;
        Visibility = widgets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
