// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32.Foundation;

namespace Chippi.App.Island;

/// <summary>
/// The open island (DESIGN.md §5 Island): the header, the live activity card and the installed
/// features' island views, beside the tab. <see cref="IslandWindow"/> opens, places and closes it.
/// </summary>
public partial class IslandPanelWindow : Window
{
    private const double Width328 = 328;
    private const double SlideDistance = 12;
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(180);

    private readonly DispatcherTimer _clock = new();
    private readonly Dictionary<FlyoutPanelContribution, FrameworkElement> _views = [];
    private IReadOnlyList<FlyoutPanelContribution> _panels = [];
    private HWND _hwnd;
    private IslandEdge _edge;
    private Rect _tab;
    private MonitorArea _area;

    public IslandPanelWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => UpdateClock();
        PlayPauseButton.Click += (_, _) => TogglePlayGlyph();
        // Content that grows or shrinks (a module's view refreshing) keeps the island centered on the tab.
        Card.SizeChanged += (_, _) =>
        {
            if (IsOpen)
                Place();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (IsOpen)
                Place();
        });
        Render(null);
    }

    public bool IsOpen { get; private set; }

    internal HWND Handle => _hwnd;

    public void Initialize() => _hwnd = IslandNative.MakeNoActivate(this);

    public void SetPetColor(PetColor color) => Pet.PetColor = color;

    public void Celebrate()
    {
        if (IsOpen)
            Pet.Celebrate();
    }

    /// <summary>The live card shows the highest activity, except Keep awake's status, which its own island view shows.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities, PetMood mood)
    {
        Pet.ShowMood(mood == PetMood.WideAwake ? PetMood.WideAwake : PetMood.Idle);
        Render(activities.FirstOrDefault(a => !(a.Kind == IslandActivityKind.Status && a.ModuleId == IslandHost.KeepAwakeModuleId)));
    }

    /// <summary>The running features' panels that have an island view, in sidebar order.</summary>
    public void SetPanels(IReadOnlyList<FlyoutPanelContribution> panels)
    {
        if (panels.SequenceEqual(_panels))
            return;
        _panels = panels;
        foreach (var gone in _views.Keys.Where(p => !panels.Contains(p)).ToList())
            _views.Remove(gone);
        if (IsOpen)
            AttachPanels();
    }

    /// <summary>Opens beside <paramref name="tab"/> (physical pixels) on <paramref name="edge"/>, or moves there when already open.</summary>
    internal void Open(IslandEdge edge, Rect tab, MonitorArea area)
    {
        _edge = edge;
        _tab = tab;
        _area = area;
        if (IsOpen)
        {
            Place();
            return;
        }
        IsOpen = true;
        // The island's local Dark.xaml carries the default accent; module views ask for AccentBrush,
        // so it is pointed at the island accent (the dark shade of the user's choice) on every open.
        if (Application.Current?.TryFindResource("IslandAccentBrush") is { } accent)
            Resources["AccentBrush"] = accent;
        AttachPanels();
        UpdateClock();
        Show();
        Place();
        SlideIn();
    }

    public void CloseIsland()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        _clock.Stop();
        Hide();
        // Unparented views get Unloaded and stop listening until the island opens again.
        Panels.Children.Clear();
    }

    private void Place()
    {
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double s = _area.Scale;
        var room = Surface.Margin;
        double height = Surface.DesiredSize.Height - room.Top - room.Bottom;
        var origin = IslandGeometry.PanelOrigin(_edge, _tab, new Size(Width328 * s, height * s), _area.Work, s);
        IslandNative.Move(_hwnd, new Rect(origin.X - room.Left * s, origin.Y - room.Top * s,
            (Width328 + room.Left + room.Right) * s, (height + room.Top + room.Bottom) * s));
    }

    /// <summary>Slides 12px in from the tab's side over 180ms; under reduced motion it just appears.</summary>
    private void SlideIn()
    {
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        Surface.BeginAnimation(OpacityProperty, null);
        Slide.X = Slide.Y = 0;
        Surface.Opacity = 1;
        if (Motion.IsReduced)
            return;
        var (x, y) = _edge switch
        {
            IslandEdge.Left => (-SlideDistance, 0.0),
            IslandEdge.Right => (SlideDistance, 0.0),
            _ => (0.0, -SlideDistance),
        };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(x, 0, SlideTime) { EasingFunction = ease });
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(y, 0, SlideTime) { EasingFunction = ease });
        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SlideTime) { EasingFunction = ease });
    }

    private void AttachPanels()
    {
        Panels.Children.Clear();
        foreach (var panel in _panels)
        {
            if (View(panel) is not { } view)
                continue;
            view.Margin = new Thickness(0, 8, 0, 0);
            Panels.Children.Add(view);
        }
    }

    private FrameworkElement? View(FlyoutPanelContribution panel)
    {
        if (_views.TryGetValue(panel, out var view))
            return view;
        try
        {
            if (panel.CreateIslandView?.Invoke() is FrameworkElement created)
                return _views[panel] = created;
            Log.Warn($"Island: {panel.ModuleId} returned a {panel.Name} island view that is not a FrameworkElement");
        }
        catch (Exception ex)
        {
            Log.Error($"Island: {panel.ModuleId} failed to build its {panel.Name} island view", ex);
        }
        return null;
    }

    private void Render(IslandActivity? live)
    {
        LiveCard.Visibility = Shown(live is not null);
        if (live is null)
            return;
        MediaLayout.Visibility = Shown(live.Kind == IslandActivityKind.Media);
        TimerLayout.Visibility = Shown(live.Kind == IslandActivityKind.Timer);
        OtherLayout.Visibility = Shown(live.Kind is not (IslandActivityKind.Media or IslandActivityKind.Timer));
        switch (live.Kind)
        {
            case IslandActivityKind.Media:
                MediaTitle.Text = live.Title;
                MediaSource.Text = live.Source;
                string initial = string.IsNullOrWhiteSpace(live.Source) ? live.Title : live.Source;
                MediaMonogram.Text = initial.Length > 0 ? initial[..1].ToUpper(CultureInfo.CurrentCulture) : "";
                break;
            case IslandActivityKind.Timer:
                TimerLabel.Text = live.Title;
                TimerValue.Text = live.Source;
                break;
            default:
                OtherTitle.Text = live.Title;
                OtherSource.Text = live.Source;
                if (live.NeedsAttention)
                    OtherSource.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
                else
                    OtherSource.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
                break;
        }
        Progress.Visibility = Shown(live.Progress is not null);
        double value = Math.Clamp(live.Progress ?? 0, 0, 1);
        Progress.ColumnDefinitions[0].Width = new GridLength(value, GridUnitType.Star);
        Progress.ColumnDefinitions[1].Width = new GridLength(1 - value, GridUnitType.Star);
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString("t", CultureInfo.CurrentCulture);
        _clock.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        if (IsOpen)
            _clock.Start();
    }

    /// <summary>No media session is behind the island yet, so play/pause only flips its own glyph.</summary>
    private void TogglePlayGlyph()
    {
        bool showingPause = PlayPauseIcon.Data == LucideIcons.Pause;
        PlayPauseIcon.Data = showingPause ? LucideIcons.Play : LucideIcons.Pause;
        System.Windows.Automation.AutomationProperties.SetName(PlayPauseButton, showingPause ? "Play" : "Pause");
    }

#if DEBUG
    /// <summary>Debug builds: the window's own pixels, for <see cref="IslandWindow.SaveScene"/>.</summary>
    internal FrameworkElement Visual => Root;
#endif

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        base.OnClosed(e);
    }
}
