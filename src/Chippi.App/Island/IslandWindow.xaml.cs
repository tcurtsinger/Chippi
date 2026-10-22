// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Island;

/// <summary>
/// The island window (DESIGN.md §5 Island, §9). Never activates (WS_EX_NOACTIVATE plus
/// WM_MOUSEACTIVATE), never shows in Alt+Tab or the taskbar (WS_EX_TOOLWINDOW), and is placed in
/// physical pixels top-center on the monitor it is given, 8 DIPs below the top of its work area.
/// <see cref="IslandHost"/> feeds it activities, the mood, the monitor and whether to hide.
/// </summary>
public partial class IslandWindow : Window
{
    internal enum IslandState
    {
        Resting,
        Live,
        Expanded,
    }

    // DESIGN.md §5: resting 84×34, live up to 292×38, expanded 328 wide.
    private const double RestingWidth = 84;
    private const double RestingHeight = 34;
    private const double LiveHeight = 38;
    private const double LiveMaxWidth = 292;
    private const double ExpandedWidth = 328;
    private const double ExpandedRadius = 24;
    private const double TopGap = 8;

    // Room for the island lift (0 8px 24px pills, 0 16px 40px expanded) around the island.
    private const double LiftSide = 44;
    private const double LiftBelow = 60;

    // Live pill geometry inside the 1px hairline: 6px left padding (pet), 8px to the text, 14px right padding.
    private const double LiveTextInset = 43;
    private const double LiveRightInset = 13;
    private const double EqualizerWidth = 10 + 25;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly TimeSpan Morph = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan ContentFadeIn = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ContentFadeOut = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan LeaveCheck = TimeSpan.FromMilliseconds(180);

    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _leaveTimer;
    private readonly DispatcherTimer _clockTimer;
    private IReadOnlyList<IslandActivity> _activities = [];
    private IslandState _state = IslandState.Resting;
    private bool _expandRequested;
    private PetMood _baseMood = PetMood.Sleepy;
    private bool _enabled = true;
    private bool _suppressed;
    private bool _equalizerRunning;
    private int _morphGeneration;
    private Size _windowSize;
    private HWND _hwnd;
    private HMONITOR _monitor;

    public IslandWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer { Interval = HoverDelay };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (PointerOverIsland())
                Expand();
        };
        // Resizing a layered window under the pointer can produce a spurious mouse-leave before the new
        // frame is on screen, so a leave only collapses once the pointer is really outside the island.
        _leaveTimer = new DispatcherTimer { Interval = LeaveCheck };
        _leaveTimer.Tick += (_, _) =>
        {
            if (!_expandRequested || PointerOverIsland() || DebugMenu.IsOpen)
            {
                if (!_expandRequested)
                    _leaveTimer.Stop();
                return; // keep checking while the pointer is over it (WPF may not report the next leave)
            }
            _leaveTimer.Stop();
            _expandRequested = false;
            Refresh();
        };
        _clockTimer = new DispatcherTimer();
        _clockTimer.Tick += (_, _) => UpdateClock();

        Island.MouseEnter += (_, _) =>
        {
            if (!_expandRequested)
                _hoverTimer.Start();
        };
        Island.MouseLeave += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_expandRequested)
                _leaveTimer.Start();
        };
        Island.MouseLeftButtonUp += (_, e) =>
        {
            if (!_expandRequested)
            {
                _hoverTimer.Stop();
                Expand();
                e.Handled = true;
            }
        };
        Island.MouseRightButtonUp += (_, e) =>
        {
            if (DebugItems.Children.Count > 0)
            {
                DebugMenu.IsOpen = true;
                e.Handled = true;
            }
        };
        DebugMenu.Closed += (_, _) =>
        {
            if (_expandRequested)
                _leaveTimer.Start();
        };
        Island.SizeChanged += (_, _) => UpdateCornerRadius();
        PlayPauseButton.Click += (_, _) => TogglePlayGlyph();
        Motion.Changed += OnMotionChanged;
        Closed += (_, _) => Motion.Changed -= OnMotionChanged;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, Place);

        _windowSize = WindowSizeFor(RestingWidth, RestingHeight);
        Width = _windowSize.Width;
        Height = _windowSize.Height;
        Render();
        SnapTo(IslandState.Resting);
    }

    /// <summary>Raised when the display layout or work area changed (WM_DISPLAYCHANGE, WM_SETTINGCHANGE).</summary>
    public event EventHandler? DisplayChanged;

    internal HWND Handle => _hwnd;

    internal IslandState State => _state;

    /// <summary>Creates the window handle with the no-activate tool-window styles, before anything is shown.</summary>
    public void Initialize()
    {
        var helper = new WindowInteropHelper(this);
        _hwnd = (HWND)helper.EnsureHandle();
        int ex = PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        _ = PInvoke.SetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    /// <summary>The monitor the island sits on (the one holding the foreground window).</summary>
    internal void SetMonitor(HMONITOR monitor)
    {
        if (monitor == default || monitor == _monitor)
            return;
        _monitor = monitor;
        Place();
    }

    /// <summary>True while a fullscreen app, game or presentation is in front: the island hides.</summary>
    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        ApplyVisibility();
    }

    /// <summary>AppSettings.IslandEnabled.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ApplyVisibility();
    }

    public void SetPetColor(PetColor color) => Pet.PetColor = color;

    /// <summary>The activities in display priority order (<see cref="IslandActivityHub.Activities"/>) and the pet's mood for them.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities, PetMood mood)
    {
        _activities = activities;
        _baseMood = mood;
        Render();
        Refresh();
    }

    /// <summary>A module reported success: Gumdrop celebrates for about 2.5s.</summary>
    public void Celebrate() => Pet.Celebrate();

    /// <summary>Debug menu entries (null is a separator). Right-clicking the island opens them.</summary>
    public void SetDebugMenu(IEnumerable<(string Label, Action Action)?> items)
    {
        DebugItems.Children.Clear();
        foreach (var item in items)
        {
            if (item is not { } entry)
            {
                DebugItems.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4), Background = (Brush)FindResource("IslandBorderBrush") });
                continue;
            }
            var button = new Button { Content = entry.Label, Style = (Style)FindResource("IslandMenuItemStyle") };
            button.Click += (_, _) =>
            {
                DebugMenu.IsOpen = false;
                entry.Action();
            };
            DebugItems.Children.Add(button);
        }
    }

    // ----- Visibility and placement -----

    private void ApplyVisibility()
    {
        bool visible = _enabled && !_suppressed && _monitor != default && _hwnd != default;
        if (visible && !IsVisible)
        {
            Show();
            Place();
            Refresh();
        }
        else if (!visible && IsVisible)
        {
            _hoverTimer.Stop();
            _leaveTimer.Stop();
            DebugMenu.IsOpen = false;
            _expandRequested = false;
            Hide();
            Refresh();
        }
    }

    /// <summary>Sizes and positions the window in the target monitor's physical pixels, top-center, and re-asserts topmost.</summary>
    private void Place()
    {
        if (_hwnd == default || _monitor == default)
            return;
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(_monitor, ref info))
            return;
        double scale = OperatingSystem.IsWindowsVersionAtLeast(10)
            && PInvoke.GetDpiForMonitor(_monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpi, out _).Succeeded ? dpi / 96.0 : 1.0;
        int width = (int)Math.Ceiling(_windowSize.Width * scale);
        int height = (int)Math.Ceiling(_windowSize.Height * scale);
        var monitor = info.rcMonitor;
        int x = monitor.left + (monitor.right - monitor.left - width) / 2;
        int y = info.rcWork.top;
        PInvoke.SetWindowPos(_hwnd, (HWND)(nint)(-1) /* HWND_TOPMOST */, x, y, width, height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    private static Size WindowSizeFor(double islandWidth, double islandHeight) =>
        new(Math.Ceiling(islandWidth + 2 * LiftSide), Math.Ceiling(TopGap + islandHeight + LiftBelow));

    private void SetWindowSize(Size size)
    {
        if (size == _windowSize)
            return;
        _windowSize = size;
        Place();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case PInvoke.WM_MOUSEACTIVATE:
                // Clicks never activate the island: the previously active window keeps focus.
                handled = true;
                return (nint)PInvoke.MA_NOACTIVATE;
            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_SETTINGCHANGE:
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => DisplayChanged?.Invoke(this, EventArgs.Empty));
                break;
        }
        return 0;
    }

    // ----- States -----

    private IslandState TargetState =>
        _expandRequested && IsVisible ? IslandState.Expanded
        : _activities.Count > 0 ? IslandState.Live
        : IslandState.Resting;

    /// <summary>Whether the pointer is inside the island's current bounds, read from the system cursor (physical pixels).</summary>
    private bool PointerOverIsland()
    {
        if (!IsVisible || PresentationSource.FromVisual(Island) is null || !PInvoke.GetCursorPos(out var cursor))
            return false;
        var topLeft = Island.PointToScreen(new Point(0, 0));
        var bottomRight = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
        return cursor.X >= topLeft.X && cursor.X < bottomRight.X && cursor.Y >= topLeft.Y && cursor.Y < bottomRight.Y;
    }

    private void Expand()
    {
        if (_expandRequested || !IsVisible)
            return;
        _expandRequested = true;
        Refresh();
    }

    /// <summary>Moves to the state the activities and pointer ask for, morphing unless motion is reduced or the window is hidden.</summary>
    private void Refresh()
    {
        var target = TargetState;
        UpdatePetMood(target);
        bool animate = IsVisible && !Motion.IsReduced;
        if (target == _state)
        {
            // Same state, new content: re-fit the live pill's width or the expanded height.
            var size = IslandSizeFor(target);
            if (Math.Abs(size.Width - Island.Width) > 0.5 || Math.Abs(size.Height - Island.Height) > 0.5)
                MorphTo(target, animate);
            UpdateEqualizer();
            return;
        }
        MorphTo(target, animate);
    }

    private void SnapTo(IslandState state) => MorphTo(state, animate: false);

    private void MorphTo(IslandState target, bool animate)
    {
        _state = target;
        int generation = ++_morphGeneration;
        var size = IslandSizeFor(target);
        var (petLeft, petTop, petSize) = PetSlot(target);

        Lift.Effect = (Effect)FindResource(target == IslandState.Expanded ? "IslandExpandedShadow" : "IslandShadow");
        LiveLayer.IsHitTestVisible = target == IslandState.Live;
        ExpandedLayer.IsHitTestVisible = target == IslandState.Expanded;
        double liveOpacity = target == IslandState.Live ? 1 : 0;
        double expandedOpacity = target == IslandState.Expanded ? 1 : 0;
        // Inactive layers end up Hidden (not Collapsed, so they still measure): screen readers skip them.
        if (liveOpacity > 0)
            LiveLayer.Visibility = Visibility.Visible;
        if (expandedOpacity > 0)
            ExpandedLayer.Visibility = Visibility.Visible;

        if (target == IslandState.Expanded)
            StartClock();
        else
            _clockTimer.Stop();

        if (!animate)
        {
            SetWindowSize(WindowSizeFor(size.Width, size.Height));
            Set(Island, WidthProperty, size.Width);
            Set(Island, HeightProperty, size.Height);
            Set(Pet, Canvas.LeftProperty, petLeft);
            Set(Pet, Canvas.TopProperty, petTop);
            Set(Pet, Gumdrop.SizeProperty, petSize);
            Set(LiveLayer, OpacityProperty, liveOpacity);
            Set(ExpandedLayer, OpacityProperty, expandedOpacity);
            if (liveOpacity == 0)
                LiveLayer.Visibility = Visibility.Hidden;
            if (expandedOpacity == 0)
                ExpandedLayer.Visibility = Visibility.Hidden;
            UpdateEqualizer();
            return;
        }

        // Grow the window first so the morph is never clipped; shrink it once the morph is done.
        double fromWidth = Island.ActualWidth > 0 ? Island.ActualWidth : Island.Width;
        double fromHeight = Island.ActualHeight > 0 ? Island.ActualHeight : Island.Height;
        SetWindowSize(WindowSizeFor(Math.Max(fromWidth, size.Width), Math.Max(fromHeight, size.Height)));

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var width = new DoubleAnimation(size.Width, new Duration(Morph)) { EasingFunction = ease };
        width.Completed += (_, _) =>
        {
            if (generation == _morphGeneration)
                SetWindowSize(WindowSizeFor(size.Width, size.Height));
        };
        Island.BeginAnimation(WidthProperty, width, HandoffBehavior.SnapshotAndReplace);
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(size.Height, new Duration(Morph)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        Pet.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(petLeft, new Duration(Morph)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        Pet.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(petTop, new Duration(Morph)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        Pet.BeginAnimation(Gumdrop.SizeProperty, new DoubleAnimation(petSize, new Duration(Morph)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        FadeLayer(LiveLayer, liveOpacity, generation);
        FadeLayer(ExpandedLayer, expandedOpacity, generation);
        UpdateEqualizer();
    }

    /// <summary>Outgoing content fades quickly, then hides; incoming content fades in once the surface has mostly reached its size.</summary>
    private void FadeLayer(UIElement layer, double to, int generation)
    {
        var animation = to > 0
            ? new DoubleAnimation(to, new Duration(ContentFadeIn)) { BeginTime = TimeSpan.FromMilliseconds(Morph.TotalMilliseconds * 0.55) }
            : new DoubleAnimation(to, new Duration(ContentFadeOut));
        if (to == 0)
        {
            animation.Completed += (_, _) =>
            {
                if (generation == _morphGeneration)
                    layer.Visibility = Visibility.Hidden;
            };
        }
        layer.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Set(DependencyObject target, DependencyProperty property, double value)
    {
        if (target is UIElement element)
            element.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    private Size IslandSizeFor(IslandState state)
    {
        switch (state)
        {
            case IslandState.Live:
                LiveText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // +2: layout rounding can leave the star column a fraction short, and trimming would then cut a word.
                double text = Math.Ceiling(LiveText.DesiredSize.Width) + 2;
                double content = LiveTextInset + text + (Equalizer.Visibility == Visibility.Visible ? EqualizerWidth : 0) + LiveRightInset + 2;
                return new Size(Math.Clamp(Math.Ceiling(content), RestingWidth, LiveMaxWidth), LiveHeight);
            case IslandState.Expanded:
                ExpandedLayer.Measure(new Size(ExpandedWidth - 2, double.PositiveInfinity));
                return new Size(ExpandedWidth, Math.Ceiling(ExpandedLayer.DesiredSize.Height + 2));
            default:
                return new Size(RestingWidth, RestingHeight);
        }
    }

    /// <summary>Gumdrop's slot inside the hairline: centered at 28 resting, 30 at the live pill's left, 28 in the expanded header.</summary>
    private static (double Left, double Top, double Size) PetSlot(IslandState state) => state switch
    {
        IslandState.Live => (5, (LiveHeight - 2 - 30) / 2, 30),
        IslandState.Expanded => (13, 11, 28),
        _ => ((RestingWidth - 2 - 28) / 2, (RestingHeight - 2 - 28) / 2, 28),
    };

    /// <summary>Pills are fully rounded (half the height); the expanded island has a 24px radius.</summary>
    private void UpdateCornerRadius()
    {
        double radius = Math.Min(Island.ActualHeight / 2, Math.Max(ExpandedRadius, Math.Min(Island.ActualHeight, LiveHeight) / 2));
        Lift.CornerRadius = new CornerRadius(radius);
        Frame.CornerRadius = new CornerRadius(radius);
    }

    /// <summary>Resting is sleepy; the expanded header shows the pet awake (idle) even when nothing is live.</summary>
    private void UpdatePetMood(IslandState target) =>
        Pet.ShowMood(target == IslandState.Expanded && _baseMood == PetMood.Sleepy ? PetMood.Idle : _baseMood);

    // ----- Content -----

    private void Render()
    {
        var current = _activities.Count > 0 ? _activities[0] : null;

        // Live pill: title in Body 700, then the tertiary source (mono where it is a number).
        LiveText.Inlines.Clear();
        if (current is not null)
        {
            LiveText.Inlines.Add(new Run(current.Title) { FontWeight = FontWeights.Bold });
            if (!string.IsNullOrWhiteSpace(current.Source))
            {
                LiveText.Inlines.Add(new Run(" · ") { Foreground = (Brush)FindResource("IslandTextTertiaryBrush") });
                LiveText.Inlines.Add(SourceRun(current.Source));
            }
        }
        Equalizer.Visibility = current?.Kind == IslandActivityKind.Media ? Visibility.Visible : Visibility.Collapsed;

        // Expanded: only the sections that have data.
        var media = First(IslandActivityKind.Media);
        MediaSection.Visibility = TransportSection.Visibility = Shown(media is not null);
        if (media is not null)
        {
            MediaTitle.Text = media.Title;
            MediaSource.Text = media.Source;
            string monogramSource = string.IsNullOrWhiteSpace(media.Source) ? media.Title : media.Source;
            MediaMonogram.Text = monogramSource.Length > 0 ? monogramSource[..1].ToUpper(CultureInfo.CurrentCulture) : "";
            SetProgress(MediaProgress, media.Progress);
        }

        var timer = First(IslandActivityKind.Timer);
        var stash = First(IslandActivityKind.Stash);
        var download = stash is null ? First(IslandActivityKind.Download) : null;
        TimerTile.Visibility = Shown(timer is not null);
        if (timer is not null)
        {
            TimerLabel.Text = timer.Title;
            TimerValue.Text = timer.Source;
        }
        var held = stash ?? download;
        StashTile.Visibility = Shown(held is not null);
        if (held is not null)
        {
            StashLabel.Text = stash is not null ? "Just stashed" : "Downloading";
            StashValue.Text = held.Title;
        }
        TileSection.Visibility = Shown(timer is not null || held is not null);
        Grid.SetColumnSpan(TimerTile, held is null ? 3 : 1);
        Grid.SetColumn(StashTile, timer is null ? 0 : 2);
        Grid.SetColumnSpan(StashTile, timer is null ? 3 : 1);

        var agent = First(IslandActivityKind.Agent);
        AgentSection.Visibility = Shown(agent is not null);
        if (agent is not null)
        {
            AgentName.Text = agent.Title;
            AgentUsage.Text = agent.Source;
            SetProgress(AgentProgress, agent.Progress);
        }

        StatusSection.Children.Clear();
        foreach (var status in _activities.Where(a => a.Kind == IslandActivityKind.Status))
            StatusSection.Children.Add(StatusRow(status));

        EmptyText.Visibility = Shown(_activities.Count == 0);
    }

    private IslandActivity? First(IslandActivityKind kind) => _activities.FirstOrDefault(a => a.Kind == kind);

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private static bool IsMeasurement(string text) => text.Any(char.IsDigit);

    /// <summary>The Readout Rule: a source that carries a number (a countdown, a time) is set in mono.</summary>
    private Run SourceRun(string source)
    {
        var run = new Run(source) { Foreground = (Brush)FindResource("IslandTextTertiaryBrush") };
        if (IsMeasurement(source))
        {
            run.FontFamily = (FontFamily)FindResource("MonoFont");
            run.FontWeight = (FontWeight)FindResource("MonoSemiBold");
            run.FontSize = 11.5;
        }
        return run;
    }

    private Border StatusRow(IslandActivity status)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = status.Title,
            Style = (Style)FindResource("BodyTextStyle"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("IslandTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock
        {
            Text = status.Source,
            Style = (Style)FindResource(IsMeasurement(status.Source) ? "MonoTextStyle" : "CaptionTextStyle"),
            Foreground = (Brush)FindResource("IslandTextTertiaryBrush"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(name);
        grid.Children.Add(value);
        return new Border { Style = (Style)FindResource("IslandTileStyle"), Margin = new Thickness(0, 10, 0, 0), Child = grid };
    }

    private static void SetProgress(Grid bar, double? progress)
    {
        bar.Visibility = Shown(progress is not null);
        double value = Math.Clamp(progress ?? 0, 0, 1);
        bar.ColumnDefinitions[0].Width = new GridLength(value, GridUnitType.Star);
        bar.ColumnDefinitions[1].Width = new GridLength(1 - value, GridUnitType.Star);
    }

    private void StartClock()
    {
        UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString("t", CultureInfo.CurrentCulture);
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
    }

    /// <summary>Phase 1 has no media session behind the island, so play/pause only flips its own glyph.</summary>
    private void TogglePlayGlyph()
    {
        bool showingPause = PlayPauseIcon.Data == LucideIcons.Pause;
        PlayPauseIcon.Data = showingPause ? LucideIcons.Play : LucideIcons.Pause;
        System.Windows.Automation.AutomationProperties.SetName(PlayPauseButton, showingPause ? "Play" : "Pause");
    }

    // ----- Equalizer -----

    private static readonly double[] RestHeights = [0.45, 0.8, 1, 0.62, 0.35];
    private static readonly double[] BarPeriods = [0.82, 0.64, 0.96, 0.72, 0.88];

    /// <summary>Animates on the live media pill; static under reduced motion and whenever it is not on screen.</summary>
    private void UpdateEqualizer()
    {
        bool run = IsVisible && _state == IslandState.Live && Equalizer.Visibility == Visibility.Visible && !Motion.IsReduced;
        if (run == _equalizerRunning)
            return;
        _equalizerRunning = run;
        for (int i = 0; i < Equalizer.Children.Count; i++)
        {
            var scale = (ScaleTransform)((Rectangle)Equalizer.Children[i]).RenderTransform;
            if (!run)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = RestHeights[i];
                continue;
            }
            var bounce = new DoubleAnimation(0.28, 1, new Duration(TimeSpan.FromSeconds(BarPeriods[i] / 2)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(-130 * i),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(bounce, 30);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }
    }

    private void OnMotionChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (Motion.IsReduced)
            SnapTo(_state); // finish any running morph at its end state
        UpdateEqualizer();
    });

#if DEBUG
    /// <summary>Debug builds: hold the island expanded (or release it) without a pointer, for snapshots.</summary>
    internal void ForceExpanded(bool expanded)
    {
        _expandRequested = expanded && IsVisible;
        Refresh();
    }

    /// <summary>Debug builds: renders the window's own pixels (island, lift, transparent surround) over a backdrop color, at 1x.</summary>
    internal void SaveSnapshot(string path, Color backdrop)
    {
        double width = Root.ActualWidth, height = Root.ActualHeight;
        var content = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        content.Render(Root);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(backdrop), null, new Rect(0, 0, content.PixelWidth, content.PixelHeight));
            dc.DrawImage(content, new Rect(0, 0, content.PixelWidth, content.PixelHeight));
        }
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap(content.PixelWidth, content.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
#endif

    protected override void OnClosed(EventArgs e)
    {
        _hoverTimer.Stop();
        _leaveTimer.Stop();
        _clockTimer.Stop();
        base.OnClosed(e);
    }
}
