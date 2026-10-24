// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
/// features' island views, in one shape that grows out of the tab. <see cref="IslandWindow"/>
/// opens, places and closes it.
/// </summary>
public partial class IslandPanelWindow : Window
{
    private const double Width328 = 328;
    private const double OpenRadius = 20;
    private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan ShrinkTime = TimeSpan.FromMilliseconds(160);

    private readonly DispatcherTimer _clock = new();
    private readonly Dictionary<FlyoutPanelContribution, FrameworkElement> _views = [];
    private IReadOnlyList<FlyoutPanelContribution> _panels = [];
    private HWND _hwnd;
    private IslandEdge _edge;
    private Rect _tab;
    private MonitorArea _area;
    private Rect _bounds; // the open island, in physical pixels
    private Rect _from; // the tab, in the island's own DIPs
    private Rect _to; // the whole island, likewise
    private double _grown; // 0 is the tab, 1 the whole island
    private long _lastFrame;
    private EventHandler? _frame;

    public IslandPanelWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => UpdateClock();
        PlayPauseButton.Click += (_, _) => TogglePlayGlyph();
        // The island grows over the tab: a press where the tab sits is the tab's (click or drag).
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_from.Contains(e.GetPosition(Surface)))
                TabPressed?.Invoke(this, e);
        };
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

    /// <summary>A left press inside the tab's footprint, which the island covers while it is visible.</summary>
    internal event MouseButtonEventHandler? TabPressed;

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

    /// <summary>Grows out of <paramref name="tab"/> (physical pixels) on <paramref name="edge"/>, or moves there when already open.</summary>
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
        if (!IsVisible) // reopened while still shrinking, it keeps its views
            AttachPanels();
        UpdateClock();
        Place();
        Show();
        Animate();
    }

    /// <summary>Shrinks back into the tab, then hides; <paramref name="immediate"/> hides at once.</summary>
    public void CloseIsland(bool immediate = false)
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        _clock.Stop();
        if (immediate)
            Collapse();
        else
            Animate();
    }

    /// <summary>Whether <paramref name="physical"/> is on the open island's shape, whether or not the window has seen the pointer yet.</summary>
    internal bool Covers(Point physical) => IsOpen && _bounds.Contains(physical);

    /// <summary>Sizes the window to the island and its shadow, flat side on the tab's edge, and redraws the shape there.</summary>
    private void Place()
    {
        double s = _area.Scale;
        var room = Room(_edge);
        Surface.Margin = room;
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double height = Surface.DesiredSize.Height - room.Top - room.Bottom;
        var origin = IslandGeometry.PanelOrigin(_edge, _tab, new Size(Width328 * s, height * s), _area.Work, s);
        _bounds = new Rect(origin.X, origin.Y, Width328 * s, height * s);
        IslandNative.Move(_hwnd, new Rect(origin.X - room.Left * s, origin.Y - room.Top * s,
            (Width328 + room.Left + room.Right) * s, (height + room.Top + room.Bottom) * s));
        _from = new Rect((_tab.X - origin.X) / s, (_tab.Y - origin.Y) / s, _tab.Width / s, _tab.Height / s);
        _to = new Rect(0, 0, Width328, height);
        ShapeFill.BorderThickness = _edge switch
        {
            IslandEdge.Left => new Thickness(0, 1, 1, 1),
            IslandEdge.Right => new Thickness(1, 1, 0, 1),
            _ => new Thickness(1, 0, 1, 1),
        };
        Draw();
    }

    /// <summary>Room around the island for its shadow, none on the edge it grows from.</summary>
    private static Thickness Room(IslandEdge edge) => edge switch
    {
        IslandEdge.Left => new Thickness(0, 24, 40, 56),
        IslandEdge.Right => new Thickness(40, 24, 0, 56),
        _ => new Thickness(40, 0, 40, 56),
    };

    /// <summary>
    /// Grows toward the whole island while open and shrinks toward the tab once closed, over 220ms
    /// and 160ms; one running animation turns around when that flips. Under reduced motion it
    /// appears and hides at once.
    /// </summary>
    private void Animate()
    {
        if (Motion.IsReduced)
        {
            StopFrames();
            if (!IsOpen)
            {
                Collapse();
                return;
            }
            _grown = 1;
            Draw();
            return;
        }
        if (_frame is not null)
            return;
        _lastFrame = 0;
        _frame = (_, _) =>
        {
            // The clock starts on the first frame, so a slow first show doesn't skip the start.
            long now = Stopwatch.GetTimestamp();
            var step = _lastFrame == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_lastFrame, now);
            _lastFrame = now;
            _grown = IsOpen ? Math.Min(1, _grown + step / GrowTime) : Math.Max(0, _grown - step / ShrinkTime);
            Draw();
            if (!IsOpen && _grown <= 0)
                Collapse();
            else if (IsOpen && _grown >= 1)
                StopFrames();
        };
        System.Windows.Media.CompositionTarget.Rendering += _frame;
    }

    private void StopFrames()
    {
        if (_frame is null)
            return;
        System.Windows.Media.CompositionTarget.Rendering -= _frame;
        _frame = null;
    }

    /// <summary>Back in the tab: hidden, with the views let go.</summary>
    private void Collapse()
    {
        StopFrames();
        _grown = 0;
        Hide();
        // Unparented views get Unloaded and stop listening until the island opens again.
        Panels.Children.Clear();
    }

    /// <summary>
    /// The shape at <see cref="_grown"/>: from the tab's half-oval to the whole island with 20px
    /// corners away from the edge, easing out as it grows (and in as it shrinks). The content fades
    /// in over the second half, once the shape is nearly full.
    /// </summary>
    private void Draw()
    {
        double t = 1 - Math.Pow(1 - _grown, 3);
        Canvas.SetLeft(Shape, Lerp(_from.X, _to.X, t));
        Canvas.SetTop(Shape, Lerp(_from.Y, _to.Y, t));
        Shape.Width = Lerp(_from.Width, _to.Width, t);
        Shape.Height = Lerp(_from.Height, _to.Height, t);
        // The tab's corners are as round as it is deep.
        double tab = _edge == IslandEdge.Top ? Math.Min(_from.Height, _from.Width / 2) : Math.Min(_from.Width, _from.Height / 2);
        double r = Lerp(tab, OpenRadius, t);
        ShapeShadow.CornerRadius = ShapeFill.CornerRadius = _edge switch
        {
            IslandEdge.Left => new CornerRadius(0, r, r, 0),
            IslandEdge.Right => new CornerRadius(r, 0, 0, r),
            _ => new CornerRadius(0, 0, r, r),
        };
        Card.Opacity = Math.Clamp(_grown * 2 - 1, 0, 1);
        Card.IsHitTestVisible = _grown >= 1; // nothing half-faded takes a click
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

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
        StopFrames();
        base.OnClosed(e);
    }
}
