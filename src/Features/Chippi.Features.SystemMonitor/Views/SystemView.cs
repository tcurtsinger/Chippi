// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The island's System page: the overview of widgets, and the detail page a widget opens. Back and Esc
/// on a detail page return to the overview, and the island always reopens on it. On top of what the
/// module reads all the time, the page reads the drives and the GPU's sensors while it's on screen, and
/// a detail page adds what it needs (each processor, the processes).
/// </summary>
internal sealed class SystemView : UserControl
{
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(180), FadeTime = TimeSpan.FromMilliseconds(120);
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly SystemMonitorModule _module;
    private readonly Present _present;
    private readonly OverviewPage _overview;
    private readonly Dictionary<Detail, DetailPage> _details = [];
    private readonly Grid _host = new() { Width = IslandPage.BodyWidth, Height = IslandPage.BodyHeight, ClipToBounds = false };
    private readonly TextBlock _title = new() { Text = "System", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _picker;
    private readonly TextBlock _pickerText;
    private readonly Popup _menu = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private SensorWatch? _watch;
    private SensorSampler? _sampler;
    private SystemSnapshot? _shown;
    private DetailPage? _detail;

    public SystemView(SystemMonitorModule module)
    {
        _module = module;
        _present = new Present(module);
        _overview = new OverviewPage(module, _present, kind => Open(kind));
        (_picker, _pickerText) = Picker();
        DockPanel.SetDock(_picker, Dock.Right);
        Header = new DockPanel { LastChildFill = true, Children = { _picker, _title } };
        _host.Children.Add(_overview);
        Content = new Grid { Children = { _host, _menu } };
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>The page's header: its title, and on a GPU page with more than one GPU, which one it shows.</summary>
    public DockPanel Header { get; }

    internal OverviewPage Overview => _overview;

    /// <summary>The detail page showing; null on the overview.</summary>
    internal DetailPage? Current => _detail;

    internal string Title => _title.Text;

    internal Button GpuPicker => _picker;

    /// <summary>
    /// What a page opens with: the latest of every reading the module keeps, and the last page's drives
    /// (and anything else the latest didn't read), so it opens whole and its first update changes values
    /// in place, not the layout.
    /// </summary>
    internal static SystemSnapshot Opening(SystemSnapshot latest, SystemSnapshot? page) => page is null ? latest : latest with
    {
        Cpu = Newer(latest.Cpu, page.Cpu),
        Memory = Newer(latest.Memory, page.Memory),
        Gpus = Newer(latest.Gpus, page.Gpus),
        Network = Newer(latest.Network, page.Network),
        Disks = Newer(latest.Disks, page.Disks),
        Battery = Newer(latest.Battery, page.Battery),
        ThermalZones = Newer(latest.ThermalZones, page.ThermalZones),
    };

    private static Sample<T> Newer<T>(Sample<T> latest, Sample<T> page) where T : class => latest.Status == ReadingStatus.NotRead ? page : latest;

    /// <summary>What the overview reads: every reading that's on.</summary>
    internal static SensorNeed Need(SystemMonitorSettings s)
    {
        var need = SensorNeed.None;
        if (s.IsOn(Reading.Cpu)) need |= SensorNeed.Cpu;
        if (s.IsOn(Reading.Memory)) need |= SensorNeed.Memory;
        if (s.IsOn(Reading.Gpu)) need |= SensorNeed.GpuSensors | SensorNeed.GpuLoad;
        if (s.IsOn(Reading.Network)) need |= SensorNeed.Network;
        if (s.IsOn(Reading.Disks)) need |= SensorNeed.Disks;
        if (s.IsOn(Reading.Battery)) need |= SensorNeed.Battery;
        if (s.IsOn(Reading.ThermalZone)) need |= SensorNeed.ThermalZones;
        return need;
    }

    private SensorNeed Needed => Need(_module.Settings) | (_detail?.Need ?? SensorNeed.None);

    /// <summary>Opens a widget's detail page, sliding in from the right.</summary>
    internal void Open(Detail kind)
    {
        if (!_details.TryGetValue(kind, out var page))
            _details[kind] = page = new DetailPage(kind, _module, _present);
        _detail = page;
        if (_watch is not null) _watch.Need = Needed;
        if (_shown is not null) page.Update(_shown);
        Swap(page, fromRight: true, animate: IsLoaded);
        Header.Visibility = Visibility.Visible;
        RefreshHeader();
    }

    /// <summary>Back and Esc: from a detail page to the overview. False on the overview, where they leave the page.</summary>
    internal bool Back() => Back(animate: true);

    private bool Back(bool animate)
    {
        _menu.IsOpen = false;
        if (_detail is null)
            return false;
        _detail = null;
        if (_watch is not null) _watch.Need = Needed;
        if (_shown is not null) _overview.Update(_shown);
        Swap(_overview, fromRight: false, animate: animate && IsLoaded);
        RefreshHeader();
        return true;
    }

    private void Start()
    {
        if (_module.Sampler is not { } sampler) return;
        _sampler = sampler;
        _module.Changed += OnSettingsChanged;
        sampler.Updated += OnUpdated;
        _watch = sampler.Watch(Needed);
        Show(Opening(sampler.Latest, _module.PageSnapshot));
    }

    private void Stop()
    {
        _module.Changed -= OnSettingsChanged;
        if (_sampler is not null) _sampler.Updated -= OnUpdated;
        _watch?.Dispose(); _watch = null; _sampler = null; _menu.IsOpen = false;
        Back(animate: false); // the island reopens on the overview
    }

    private void OnSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnSettingsChanged); return; }
        if (_watch is not null) _watch.Need = Needed;
        if (_shown is not null) Show(_shown);
    }

    private void OnUpdated(SystemSnapshot snapshot) => Dispatcher.BeginInvoke(() =>
    {
        if (_sampler is null) return;
        if (snapshot.Gpus.Status != ReadingStatus.NotRead || snapshot.Network.Status != ReadingStatus.NotRead || snapshot.Disks.Status != ReadingStatus.NotRead)
            _module.PageSnapshot = snapshot with { Processes = Sample<IReadOnlyList<ProcessReading>>.NotRead(snapshot.At) };
        Show(snapshot);
    });

    internal void Show(SystemSnapshot s)
    {
        _shown = s;
        _overview.Update(s);
        // A reading switched off (or gone) under its detail page: back to the overview.
        if (_detail is not null && !_overview.Widgets.Any(w => w.Kind == _detail.Kind))
            Back(animate: false);
        _detail?.Update(s);
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        _title.Text = _detail?.Title ?? "System";
        var gpus = _shown is null ? [] : _present.Gpus(_shown);
        bool pick = _detail?.Kind == Detail.Gpu && gpus.Count > 1;
        _picker.Visibility = pick ? Visibility.Visible : Visibility.Collapsed;
        if (pick && _present.Gpu(_shown!) is { } gpu)
        {
            _pickerText.Text = _present.GpuName(_shown!, gpu);
            AutomationProperties.SetName(_picker, "GPU, " + gpu.Name);
            _picker.ToolTip = gpu.Name;
        }
    }

    // The new page comes in from its side as it fades in, the island's page slide; reduced motion only fades.
    private void Swap(FrameworkElement page, bool fromRight, bool animate)
    {
        _host.Children.Clear();
        _host.Children.Add(page);
        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 1;
        page.RenderTransform = Transform.Identity;
        if (!animate)
            return;
        bool reduced = Motion.IsReduced;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, reduced ? FadeTime : SlideTime) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        if (reduced)
            return;
        var shift = new TranslateTransform();
        page.RenderTransform = shift;
        var slide = new DoubleAnimation(fromRight ? 12 : -12, 0, SlideTime) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
        slide.Completed += (_, _) => { if (page.RenderTransform == shift) page.RenderTransform = Transform.Identity; };
        shift.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    // On a GPU page with more than one GPU: the shown GPU's name and a chevron; a menu of every GPU.
    private (Button, TextBlock) Picker()
    {
        var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var chevron = Parts.Glyph(LucideIcons.ChevronDown, 12, "IslandTextTertiaryBrush");
        chevron.Margin = new Thickness(4, 0, 0, 0);
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0), Children = { text, chevron } },
            Height = 24, Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, -6, 0),
        };
        button.SetResourceReference(StyleProperty, "IslandBareButtonStyle");
        button.Click += (_, _) =>
        {
            if (_shown is null) return;
            var current = _present.Gpu(_shown);
            var list = new StackPanel { Margin = new Thickness(4) };
            foreach (var gpu in _present.Gpus(_shown))
            {
                var item = new Button { Content = gpu.Name };
                item.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
                if (gpu.Id == current?.Id) item.FontWeight = FontWeights.SemiBold;
                string id = gpu.Id;
                item.Click += (_, _) =>
                {
                    _menu.IsOpen = false;
                    _module.Save(_module.Settings with { GpuId = id });
                };
                list.Children.Add(item);
            }
            var menu = new Border { MinWidth = 160, MaxWidth = 320, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
            menu.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
            menu.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
            _menu.PlacementTarget = button;
            _menu.Child = menu;
            _menu.IsOpen = true;
        };
        return (button, text);
    }
}
