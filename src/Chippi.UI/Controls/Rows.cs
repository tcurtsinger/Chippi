// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Chippi.UI.Controls;

/// <summary>
/// App volume row (DESIGN.md §6): monogram or icon, name over output device, mono value and a mute
/// button spanning both rows, then the gradient slider. Visual only in Phase 1.
/// </summary>
public class AppVolumeRow : Control
{
    static AppVolumeRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppVolumeRow), new FrameworkPropertyMetadata(typeof(AppVolumeRow)));
        FocusableProperty.OverrideMetadata(typeof(AppVolumeRow), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty AppNameProperty = DependencyProperty.Register(
        nameof(AppName), typeof(string), typeof(AppVolumeRow), new PropertyMetadata("", (d, _) => ((AppVolumeRow)d).CoerceValue(MonogramProperty)));

    public string AppName
    {
        get => (string)GetValue(AppNameProperty);
        set => SetValue(AppNameProperty, value);
    }

    public static readonly DependencyProperty DeviceNameProperty = DependencyProperty.Register(
        nameof(DeviceName), typeof(string), typeof(AppVolumeRow), new PropertyMetadata(""));

    public string DeviceName
    {
        get => (string)GetValue(DeviceNameProperty);
        set => SetValue(DeviceNameProperty, value);
    }

    public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
        nameof(Volume), typeof(double), typeof(AppVolumeRow),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>0 to 100.</summary>
    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted), typeof(bool), typeof(AppVolumeRow),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public static readonly DependencyProperty AppIconProperty = DependencyProperty.Register(
        nameof(AppIcon), typeof(ImageSource), typeof(AppVolumeRow), new PropertyMetadata(null));

    /// <summary>The app's real icon. Without one the row shows a monogram (logos are never redrawn).</summary>
    public ImageSource? AppIcon
    {
        get => (ImageSource?)GetValue(AppIconProperty);
        set => SetValue(AppIconProperty, value);
    }

    public static readonly DependencyProperty MonogramProperty = DependencyProperty.Register(
        nameof(Monogram), typeof(string), typeof(AppVolumeRow),
        new PropertyMetadata(null, null, (d, value) => value is string s && s.Length > 0 ? s : FirstLetter(((AppVolumeRow)d).AppName)));

    public string Monogram
    {
        get => (string)GetValue(MonogramProperty);
        set => SetValue(MonogramProperty, value);
    }

    private static string FirstLetter(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
}

/// <summary>
/// Stash row (DESIGN.md §6): file name over destination, mono age, undo. A held file gets an amber
/// 45% border and shows its reason in Signal Amber instead of a destination; it has no undo.
/// </summary>
public class StashRow : Control
{
    static StashRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StashRow), new FrameworkPropertyMetadata(typeof(StashRow)));
        FocusableProperty.OverrideMetadata(typeof(StashRow), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(
        nameof(FileName), typeof(string), typeof(StashRow), new PropertyMetadata(""));

    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public static readonly DependencyProperty DestinationProperty = DependencyProperty.Register(
        nameof(Destination), typeof(string), typeof(StashRow), new PropertyMetadata(""));

    public string Destination
    {
        get => (string)GetValue(DestinationProperty);
        set => SetValue(DestinationProperty, value);
    }

    public static readonly DependencyProperty AgeProperty = DependencyProperty.Register(
        nameof(Age), typeof(string), typeof(StashRow), new PropertyMetadata(""));

    /// <summary>Preformatted, e.g. "2m".</summary>
    public string Age
    {
        get => (string)GetValue(AgeProperty);
        set => SetValue(AgeProperty, value);
    }

    public static readonly DependencyProperty IsHeldProperty = DependencyProperty.Register(
        nameof(IsHeld), typeof(bool), typeof(StashRow), new PropertyMetadata(false));

    public bool IsHeld
    {
        get => (bool)GetValue(IsHeldProperty);
        set => SetValue(IsHeldProperty, value);
    }

    public static readonly DependencyProperty HeldReasonProperty = DependencyProperty.Register(
        nameof(HeldReason), typeof(string), typeof(StashRow), new PropertyMetadata(""));

    /// <summary>State first, detail second: "Held · remote-access installer".</summary>
    public string HeldReason
    {
        get => (string)GetValue(HeldReasonProperty);
        set => SetValue(HeldReasonProperty, value);
    }

    public static readonly DependencyProperty UndoCommandProperty = DependencyProperty.Register(
        nameof(UndoCommand), typeof(ICommand), typeof(StashRow), new PropertyMetadata(null));

    public ICommand? UndoCommand
    {
        get => (ICommand?)GetValue(UndoCommandProperty);
        set => SetValue(UndoCommandProperty, value);
    }
}

/// <summary>
/// Stat tile (DESIGN.md §6): Label eyebrow, mono readout with a mono caption, and a 2px azure
/// sparkline 30px tall drawn from <see cref="Samples"/> (any numbers; scaled to their own range).
/// </summary>
[TemplatePart(Name = SparklinePart, Type = typeof(Polyline))]
[TemplatePart(Name = SparklineHostPart, Type = typeof(FrameworkElement))]
public class StatTile : Control
{
    private const string SparklinePart = "PART_Sparkline";
    private const string SparklineHostPart = "PART_SparklineHost";
    private Polyline? _sparkline;
    private FrameworkElement? _host;

    static StatTile()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatTile), new FrameworkPropertyMetadata(typeof(StatTile)));
        FocusableProperty.OverrideMetadata(typeof(StatTile), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    /// <summary>Preformatted readout, e.g. "23%".</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IEnumerable), typeof(StatTile), new PropertyMetadata(null, (d, _) => ((StatTile)d).UpdateSparkline()));

    public IEnumerable? Samples
    {
        get => (IEnumerable?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_host is not null)
            _host.SizeChanged -= OnSparklineSizeChanged;
        base.OnApplyTemplate();
        _sparkline = GetTemplateChild(SparklinePart) as Polyline;
        _host = GetTemplateChild(SparklineHostPart) as FrameworkElement;
        if (_host is not null)
            _host.SizeChanged += OnSparklineSizeChanged;
        UpdateSparkline();
    }

    private void OnSparklineSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSparkline();

    private void UpdateSparkline()
    {
        if (_sparkline is null || _host is null)
            return;
        var values = Samples?.Cast<object>().Select(v => Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)).ToList() ?? [];
        double width = _host.ActualWidth, height = _host.ActualHeight;
        var points = new PointCollection();
        if (values.Count >= 2 && width > 0 && height > 0)
        {
            double min = values.Min(), max = values.Max();
            double range = max - min < 1e-9 ? 1 : max - min;
            const double inset = 1; // keep the 2px stroke inside the box
            for (int i = 0; i < values.Count; i++)
            {
                double x = i * width / (values.Count - 1);
                double y = inset + (height - 2 * inset) * (1 - (values[i] - min) / range);
                points.Add(new Point(x, y));
            }
        }
        points.Freeze();
        _sparkline.Points = points;
    }
}
