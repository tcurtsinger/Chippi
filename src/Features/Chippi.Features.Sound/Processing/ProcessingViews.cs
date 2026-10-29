// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Features.Sound.Routing;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Processing;

/// <summary>A horizontal peak meter: an accent fill over the slider's track, 0 to 1.</summary>
internal sealed class LevelBar : Grid
{
    private readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };

    /// <param name="island">In the island's colors, like its slider.</param>
    public LevelBar(bool island = false)
    {
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, island ? "IslandTrackBrush" : "TrackRemainderBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, island ? "IslandAccentBrush" : "AccentBrush");
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(track);
        Children.Add(_fill);
        SizeChanged += (_, _) => Level = _level;
    }

    private double _level;

    public double Level
    {
        get => _level;
        set
        {
            _level = Math.Clamp(value, 0, 1);
            _fill.Width = ActualWidth * _level;
        }
    }
}

/// <summary>Shared pieces of the processing views.</summary>
internal static class ProcessingUi
{
    public static string Db(double db) => (db > 0 ? "+" : db < 0 ? "−" : "") + Math.Abs(Math.Round(db)).ToString(CultureInfo.CurrentCulture) + " dB";

    /// <summary>A peak (0 to 1) in dBFS for the meter's label; silence reads as "−∞ dB".</summary>
    public static string PeakDb(float peak) => peak <= 0.00001f ? "−∞ dB" : Db(MicSettings.ToDecibels(peak));

    /// <summary>Meters drawn on a dB scale from −60 to 0, as level meters usually are.</summary>
    public static double MeterFill(float peak) => peak <= 0 ? 0 : Math.Clamp((MicSettings.ToDecibels(peak) + 60) / 60, 0, 1);

    public static Slider Slider(string name, (double Min, double Max) range, double value, Action<double> changed, out TextBlock label)
    {
        var slider = new Slider { Minimum = range.Min, Maximum = range.Max, Value = value, Width = 240, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 0.5 };
        slider.SetResourceReference(FrameworkElement.StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(slider, name);
        var text = RoutingUi.Text(Db(value), "NumberTextStyle", "TextSecondaryBrush");
        text.MinWidth = 52;
        text.TextAlignment = TextAlignment.Right;
        text.Margin = new Thickness(16, 0, 0, 0);
        slider.ValueChanged += (_, e) => { text.Text = Db(e.NewValue); changed(e.NewValue); };
        label = text;
        return slider;
    }

    public static FrameworkElement WithLabel(FrameworkElement control, TextBlock label)
    {
        var grid = RoutingUi.Columns(GridLength.Auto, GridLength.Auto);
        RoutingUi.Add(grid, control, 0);
        RoutingUi.Add(grid, label, 1);
        return grid;
    }

    /// <summary>Watches meters while <paramref name="view"/> is shown, and only then (architecture §2).</summary>
    public static void Meter(FrameworkElement view, MicProcessingModule module, Action<MeterReadings> show)
    {
        IDisposable? watch = null;
        void OnMeters(object? sender, MeterReadings readings)
        {
            if (view.Dispatcher.CheckAccess())
                show(readings);
            else
                view.Dispatcher.BeginInvoke(() => show(readings));
        }
        view.Loaded += (_, _) =>
        {
            try { watch = module.WatchMeters(); }
            catch (InvalidOperationException) { return; } // audio isn't running
            module.Meters += OnMeters;
        };
        view.Unloaded += (_, _) =>
        {
            module.Meters -= OnMeters;
            watch?.Dispose();
            watch = null;
        };
    }

    public static async void Run(Task change, Action refresh)
    {
        try { await change; }
        catch { /* the module shows the error */ }
        refresh();
    }
}

/// <summary>
/// The Mic processing section of Settings › Microphone (revised 2026-10-28): the switch, Default device,
/// the levels while it runs, then each stage, Noise gate and Auto gain folding their tuning. Switches
/// apply at once; levels are heard at once and saved after a pause. The mic is the one in use, picked
/// above; a mic chosen here before shows until it is set back to that.
/// </summary>
public sealed class ProcessingPage : UserControl
{
    private readonly MicProcessingModule _module;
    private readonly StackPanel _body = new();
    private readonly LevelBar _in = new() { Width = 220 };
    private readonly LevelBar _out = new() { Width = 220 };
    private readonly TextBlock _inDb = RoutingUi.Text("", "NumberTextStyle", "TextSecondaryBrush");
    private readonly TextBlock _outDb = RoutingUi.Text("", "NumberTextStyle", "TextSecondaryBrush");
    private string _shape = "";
    private bool _gateOpen, _agcOpen; // a rebuild keeps a stage folded or open

    public ProcessingPage(MicProcessingModule module)
    {
        _module = module;
        Content = _body;
        ProcessingUi.Meter(this, module, readings =>
        {
            _in.Level = ProcessingUi.MeterFill(readings.ProcessingInput);
            _out.Level = ProcessingUi.MeterFill(readings.ProcessingOutput);
            _inDb.Text = ProcessingUi.PeakDb(readings.ProcessingInput);
            _outDb.Text = ProcessingUi.PeakDb(readings.ProcessingOutput);
        });
        Loaded += (_, _) => { _module.Changed += Refresh; Render(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Render();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        // Rebuilding under a dragged slider would drop the drag: only rebuild when what's shown changes shape.
        var shape = Shape();
        if (shape != _shape)
            Render();
    }

    private string Shape()
    {
        var s = _module.Settings;
        return string.Join("|", s.Enabled, s.Suppression, s.Gate, s.Agc, s.Source, s.SetDefault, _module.Status.State, _module.StatusText, _module.SourceName, _module.Error,
            string.Join(",", _module.Sources.Select(e => e.Id + e.Name)));
    }

    private void Render()
    {
        _shape = Shape();
        _body.Children.Clear();
        var s = _module.Settings;

        var cleanup = SettingsLayout.Card(
            SettingsLayout.Row("Use mic processing", RoutingUi.Switch("Use mic processing", s.Enabled, on => Run(_module.SetEnabledAsync(on)))),
            SettingsLayout.Row("Default device", RoutingUi.Switch("Default device", s.SetDefault, on => Run(_module.SetDefaultMicAsync(on))), "Makes CABLE Output the default mic"));
        // The meter only means something while processing runs.
        if (_module.Status.State == ProcessingState.Live)
            cleanup.Children.Add(SettingsLayout.Row("Level", Meters()));
        cleanup.Children.Add(SettingsLayout.Row("Noise suppression", RoutingUi.Switch("Noise suppression", s.Suppression, on => Stages(x => x with { Suppression = on }))));
        cleanup.Children.Add(Fold("Noise gate", RoutingUi.Switch("Noise gate", s.Gate, on => Stages(x => x with { Gate = on })), _gateOpen, open => _gateOpen = open,
            Level("Threshold", ProcessingSettings.GateRange, s.GateDb, db => Stages(x => x with { GateDb = db }), s.Gate)));
        cleanup.Children.Add(Fold("Auto gain", RoutingUi.Switch("Auto gain", s.Agc, on => Stages(x => x with { Agc = on })), _agcOpen, open => _agcOpen = open,
            Level("Target level", ProcessingSettings.TargetRange, s.TargetDb, db => Stages(x => x with { TargetDb = db }), s.Agc),
            Level("Max gain", ProcessingSettings.MaxGainRange, s.MaxGainDb, db => Stages(x => x with { MaxGainDb = db }), s.Agc)));
        if (s.Source != ProcessingSettings.PinnedSource)
            cleanup.Children.Add(SettingsLayout.Row("Input device", SourcePicker()));
        _body.Children.Add(cleanup);
        // Only what needs attention: a failure, or processing that stopped.
        if ((_module.Error ?? (_module.Status.State == ProcessingState.Stopped ? _module.StatusText : null)) is { } problem)
        {
            var text = RoutingUi.Text(problem, "CaptionTextStyle", "WarningTextBrush");
            text.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            _body.Children.Add(text);
        }
    }

    private FrameworkElement Meters()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new());
        foreach (var (row, label, bar, db) in new[] { (0, "In", _in, _inDb), (1, "Out", _out, _outDb) })
        {
            var name = RoutingUi.Text(label, "CaptionTextStyle", "TextSecondaryBrush");
            name.Margin = new Thickness(0, 4, 16, 4);
            Detach(bar);
            Detach(db);
            db.MinWidth = 56;
            db.TextAlignment = TextAlignment.Right;
            db.Margin = new Thickness(16, 0, 0, 0);
            foreach (var (child, column) in new (UIElement, int)[] { (name, 0), (bar, 1), (db, 2) })
            {
                Grid.SetRow(child, row);
                Grid.SetColumn(child, column);
                grid.Children.Add(child);
            }
        }
        AutomationProperties.SetName(grid, "Input and output level");
        return grid;
    }

    private static void Detach(FrameworkElement element) => (element.Parent as Panel)?.Children.Remove(element);

    /// <summary>A stage with its switch in the header and its tuning folded inside, open or closed as it was.</summary>
    private static ExpanderRow Fold(string name, ToggleButton toggle, bool open, Action<bool> remember, params UIElement[] tuning)
    {
        var row = SettingsLayout.Expander(name, null, toggle, tuning);
        row.IsExpanded = open;
        row.Expanded += (_, _) => remember(true);
        row.Collapsed += (_, _) => remember(false);
        return row;
    }

    /// <summary>A stage's tuning, dimmed while the stage is off.</summary>
    private static SettingRow Level(string name, (double Min, double Max) range, double value, Action<double> changed, bool enabled)
    {
        var slider = ProcessingUi.Slider(name, range, value, changed, out var label);
        return new SettingRow { Header = name, Content = ProcessingUi.WithLabel(slider, label), IsEnabled = enabled, Opacity = enabled ? 1 : 0.45 };
    }

    private ComboBox SourcePicker()
    {
        var items = new List<KeyValuePair<string, string>> { new(ProcessingSettings.PinnedSource, "Highest priority") };
        items.AddRange(_module.Sources.Select(e => new KeyValuePair<string, string>(e.Id, e.Name)));
        if (_module.Settings.Source != ProcessingSettings.PinnedSource && items.All(i => i.Key != _module.Settings.Source))
            items.Add(new(_module.Settings.Source, "Not connected"));
        var combo = new ComboBox { MinWidth = 180, ItemsSource = items, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = _module.Settings.Source };
        combo.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        AutomationProperties.SetName(combo, "Input device");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedValue is string id && id != _module.Settings.Source)
                Run(_module.SetSourceAsync(id));
        };
        return combo;
    }

    private void Stages(Func<ProcessingSettings, ProcessingSettings> edit) => Run(_module.SetStagesAsync(edit));

    private void Run(Task change) => ProcessingUi.Run(change, Refresh);
}

/// <summary>
/// Mic processing's card on the Sound page (DESIGN.md §5 Island, revised 2026-10-28): one 44 row with
/// the switch, and while it runs a meter of the processed mic after the label. A warning shows below
/// the card only when it failed or stopped.
/// </summary>
public sealed class ProcessingQuickView : UserControl
{
    private readonly MicProcessingModule _module;
    private readonly ToggleButton _switch = new();
    private readonly TextBlock _warning = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private readonly LevelBar _meter = new(island: true) { Margin = new Thickness(10, 0, 10, 0) };
    private bool _refreshing;

    public ProcessingQuickView(MicProcessingModule module)
    {
        _module = module;
        _switch.SetResourceReference(StyleProperty, "SwitchStyle");
        _switch.LayoutTransform = new System.Windows.Media.ScaleTransform(34 / 40d, 18 / 20d); // the island's 34 x 18 switch, from the 40 x 20 template
        AutomationProperties.SetName(_switch, "Mic processing");
        _switch.Checked += (_, _) => Flip(true);
        _switch.Unchecked += (_, _) => Flip(false);
        var label = RoutingUi.Text("Mic processing");
        label.FontSize = 12;
        DockPanel.SetDock(label, Dock.Left);
        var glyph = new LucideIcon { Data = LucideIcons.AudioWaveform };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandAccentBrush");
        var card = RoutingUi.IslandCard(glyph, new DockPanel { Children = { label, _meter } }, _switch);
        _warning.Margin = new Thickness(10, 4, 0, 0);
        AutomationProperties.SetLiveSetting(_warning, AutomationLiveSetting.Polite);
        Content = new StackPanel { Children = { card, _warning } };
        ProcessingUi.Meter(this, module, readings => _meter.Level = ProcessingUi.MeterFill(readings.ProcessingOutput));
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void Flip(bool on)
    {
        if (!_refreshing)
            ProcessingUi.Run(_module.SetEnabledAsync(on), Refresh);
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            _switch.IsChecked = _module.Settings.Enabled;
            _warning.Text = _module.Error ?? (_module.Status.State == ProcessingState.Stopped ? _module.StatusText : "");
            _warning.Visibility = _warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            SoundColumn.SetWarning(this, _warning.Text.Length > 0);
            _meter.Visibility = _module.Status.State == ProcessingState.Live ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _refreshing = false; }
    }
}
