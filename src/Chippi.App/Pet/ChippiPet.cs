// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chippi.Core.Config;
using Chippi.UI.Themes;

namespace Chippi.App.Pet;

public enum PetMood
{
    Idle,
    Celebrating,
    Sleepy,
    WideAwake,
    Listening,
}

/// <summary>Chippi, the rice-cake blob from the approved shell mock. Motion and colors are shared by every host.</summary>
public class ChippiPet : UserControl
{
    /// <summary>At this size and below the small form is drawn.</summary>
    public const double SmallFormMaxSize = 22;

    /// <summary>From this size up the ground shadow is drawn.</summary>
    public const double GroundShadowMinSize = 64;

    internal static readonly TimeSpan CelebrationHold = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan MoodFade = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SquishDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SparkFade = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan EyesShut = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ZFade = TimeSpan.FromMilliseconds(400);

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(PetMood), typeof(ChippiPet), new PropertyMetadata(PetMood.Idle, OnMoodChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(ChippiPet), new PropertyMetadata(28.0, OnSizeChanged));

    public static readonly DependencyProperty PetColorProperty = DependencyProperty.Register(
        nameof(PetColor), typeof(PetColor), typeof(ChippiPet), new PropertyMetadata(PetColor.Synth, OnPetColorChanged));

    public static readonly DependencyProperty IsDecorativeProperty = DependencyProperty.Register(
        nameof(IsDecorative), typeof(bool), typeof(ChippiPet), new PropertyMetadata(false));

    private readonly Path _shadow;
    private readonly Path _body;
    private readonly Path _shine;
    private readonly ScaleTransform _squish = new(1, 1, 60, 108);
    private readonly Face _idle;
    private readonly Face _wideAwake;
    private readonly Face _celebrating;
    private readonly Face _sleepy;
    private readonly Face _small;
    private readonly Canvas _headphones;
    private readonly Path _sparks;
    private readonly Path _zs;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _celebrationTimer;

    private PetMood _shown;
    private bool _hasShown;
    private PetMood _returnMood = PetMood.Idle;

    public ChippiPet()
    {
        Focusable = false;
        IsTabStop = false;
        SnapsToDevicePixels = false;

        _shadow = Filled(Ellipse(60, 113, 30, 4), "PetShadowBrush");
        _body = new Path { Data = Shapes.Body };
        _shine = Filled(Shapes.Shine, "PetShineBrush");

        _idle = new Face(canBlink: true,
            eyes: [Filled(Shapes.EyeWhites, "PetEyeHighlightBrush"), Filled(Shapes.IdleEyes, "PetInkBrush"), Filled(Shapes.IdleHighlights, "PetEyeHighlightBrush")],
            rest: [Filled(Shapes.OpenSmile, "PetInkBrush"), new Path { Data = Shapes.Tongue, Fill = new SolidColorBrush(Color.FromRgb(255, 127, 168)) }]);
        _wideAwake = new Face(canBlink: true,
            eyes: [Stroked(Shapes.HappyEyes, "PetInkBrush", 3.2)],
            rest: [Filled(Shapes.OpenSmile, "PetInkBrush"), new Path { Data = Shapes.Tongue, Fill = new SolidColorBrush(Color.FromRgb(255, 127, 168)) }]);
        _celebrating = new Face(canBlink: false,
            eyes: [Stroked(Shapes.HappyEyes, "PetInkBrush", 4.2)],
            rest: [Filled(Shapes.OpenSmile, "PetInkBrush"), new Path { Data = Shapes.Tongue, Fill = new SolidColorBrush(Color.FromRgb(255, 127, 168)) }]);
        _sleepy = new Face(canBlink: false,
            eyes: [Stroked(Shapes.SleepyEyes, "PetInkBrush", SleepyEyeStroke)],
            rest: [Filled(Shapes.SleepMouth, "PetInkBrush")]);
        _small = new Face(canBlink: true,
            eyes: [Filled(Shapes.SmallEyes, "PetInkBrush")],
            rest: []);

        _headphones = new Canvas { Width = 120, Height = 120 };
        var headphoneInk = new SolidColorBrush(Color.FromRgb(42, 36, 64));
        var headphonePad = new SolidColorBrush(Color.FromRgb(210, 190, 255));
        _headphones.Children.Add(new Path
        {
            Data = Shapes.Headphones, Stroke = headphoneInk, StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        foreach (var x in new[] { 12.0, 95.0 })
        {
            var cup = new System.Windows.Shapes.Rectangle { Width = 13, Height = 23, RadiusX = 5.5, RadiusY = 5.5, Fill = headphoneInk };
            Canvas.SetLeft(cup, x);
            Canvas.SetTop(cup, 59);
            _headphones.Children.Add(cup);
            var pad = new System.Windows.Shapes.Rectangle { Width = 5, Height = 15, RadiusX = 2.5, RadiusY = 2.5, Fill = headphonePad };
            Canvas.SetLeft(pad, x == 12 ? 15 : 102);
            Canvas.SetTop(pad, 63);
            _headphones.Children.Add(pad);
        }
        _sparks = Stroked(Shapes.Sparks, "PetSparkBrush", 3.5);
        _zs = Filled(Shapes.Zs(), "PetZBrush");

        var figure = new Canvas { Width = 120, Height = 120, RenderTransform = _squish };
        figure.Children.Add(_body);
        figure.Children.Add(_shine);
        figure.Children.Add(_headphones);
        foreach (var face in Faces)
            figure.Children.Add(face.Root);

        var canvas = new Canvas { Width = 120, Height = 120 };
        canvas.Children.Add(_shadow);
        canvas.Children.Add(figure);
        canvas.Children.Add(_sparks);
        canvas.Children.Add(_zs);

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
        Width = Height = Size;
        UpdateBodyBrush();

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = NextBlinkInterval() };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkTimer.Interval = NextBlinkInterval();
            Blink();
        };
        _celebrationTimer = new DispatcherTimer { Interval = CelebrationHold };
        _celebrationTimer.Tick += (_, _) =>
        {
            _celebrationTimer.Stop();
            if (Mood == PetMood.Celebrating)
                SetCurrentValue(MoodProperty, _returnMood);
        };

        ApplyForm();
        ApplyMood(Mood, animate: false);

        Loaded += (_, _) =>
        {
            Motion.Changed += OnMotionChanged;
            UpdateBlinkTimer();
        };
        Unloaded += (_, _) =>
        {
            Motion.Changed -= OnMotionChanged;
            _blinkTimer.Stop();
        };
        IsVisibleChanged += (_, _) => UpdateBlinkTimer();
    }

    /// <summary>Stroke width of the sleepy closed-eye arcs. DESIGN.md §7 gives none; this sits between the mouth (2.8) and the happy arcs (4.2).</summary>
    internal const double SleepyEyeStroke = 3.4;

    public PetMood Mood
    {
        get => (PetMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>Rendered edge in DIPs. At 22 and below the small form is drawn (DESIGN.md §7, Sizes).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public PetColor PetColor
    {
        get => (PetColor)GetValue(PetColorProperty);
        set => SetValue(PetColorProperty, value);
    }

    /// <summary>
    /// True where the text beside the pet carries the state (island, Settings titlebar): screen readers
    /// skip it. This is WPF's stand-in for <c>AccessibilityView="Raw"</c>, which WPF does not have;
    /// the pet's peer leaves the control and content views. False (the default) reads it as
    /// "Chippi, &lt;mood&gt;", for where it stands alone (the Settings preview).
    /// </summary>
    public bool IsDecorative
    {
        get => (bool)GetValue(IsDecorativeProperty);
        set => SetValue(IsDecorativeProperty, value);
    }

    private bool IsSmall => Size <= SmallFormMaxSize;

    private IEnumerable<Face> Faces => [_idle, _wideAwake, _celebrating, _sleepy, _small];

    /// <summary>The mood as words, for the accessible name: "idle", "celebrating", "sleepy", "wide awake".</summary>
    public static string Describe(PetMood mood) => mood switch
    {
        PetMood.Celebrating => "celebrating",
        PetMood.Sleepy => "asleep",
        PetMood.WideAwake => "wide awake",
        PetMood.Listening => "listening",
        _ => "idle",
    };

    /// <summary>
    /// Shows <paramref name="mood"/>, unless a celebration is playing: then it becomes the mood the
    /// pet returns to when the celebration ends. For hosts (the island) whose own state changes
    /// while the pet celebrates.
    /// </summary>
    internal void ShowMood(PetMood mood)
    {
        if (Mood == PetMood.Celebrating && _celebrationTimer.IsEnabled && mood != PetMood.Celebrating)
            _returnMood = mood;
        else
            SetCurrentValue(MoodProperty, mood);
    }

    /// <summary>Celebrates for about 2.5s, then returns to the current mood. Celebrating again restarts it.</summary>
    internal void Celebrate()
    {
        if (Mood == PetMood.Celebrating)
        {
            _celebrationTimer.Stop();
            _celebrationTimer.Start();
            ApplyMood(PetMood.Celebrating, animate: true, replay: true);
            return;
        }
        SetCurrentValue(MoodProperty, PetMood.Celebrating);
    }

    private static void OnMoodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pet = (ChippiPet)d;
        var mood = (PetMood)e.NewValue;
        var old = (PetMood)e.OldValue;
        if (mood == PetMood.Celebrating)
        {
            pet._returnMood = old == PetMood.Celebrating ? PetMood.Idle : old;
            pet._celebrationTimer.Stop();
            pet._celebrationTimer.Start();
        }
        else
        {
            pet._celebrationTimer.Stop();
        }
        pet.ApplyMood(mood, animate: true);

        if (UIElementAutomationPeer.FromElement(pet) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, $"Chippi, {Describe(old)}", $"Chippi, {Describe(mood)}");
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pet = (ChippiPet)d;
        pet.Width = pet.Height = (double)e.NewValue;
        bool wasSmall = (double)e.OldValue <= SmallFormMaxSize;
        bool hadShadow = (double)e.OldValue >= GroundShadowMinSize;
        if (wasSmall != pet.IsSmall || hadShadow != ((double)e.NewValue >= GroundShadowMinSize))
        {
            pet.ApplyForm();
            pet.ApplyMood(pet._shown, animate: false);
        }
    }

    private static void OnPetColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ChippiPet)d).UpdateBodyBrush();

    private void UpdateBodyBrush()
    {
        var color = PetColor switch
        {
            PetColor.Iris => "#CBB2FF", PetColor.Azure => "#8EC0FF",
            PetColor.Signal => "#8EE6C0", PetColor.Ember => "#FFC58F",
            PetColor.Frost => "#E8ECF6", _ => "#B78FFF",
        };
        var baseColor = (Color)ColorConverter.ConvertFromString(color);
        var shade = Color.FromRgb((byte)(baseColor.R * .88), (byte)(baseColor.G * .88), (byte)(baseColor.B * .88));
        var brush = new LinearGradientBrush(baseColor, shade, 90);
        brush.Freeze();
        _body.Fill = brush;
        _shine.Visibility = Visibility.Collapsed;
    }

    /// <summary>Small form drops the shine, mouth, highlights and extras; the ground shadow shows from 64px.</summary>
    private void ApplyForm()
    {
        bool small = IsSmall;
        _shine.Visibility = Visibility.Collapsed;
        _shadow.Visibility = !small && Size >= GroundShadowMinSize ? Visibility.Visible : Visibility.Collapsed;
        _sparks.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        _zs.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
    }

    private Face FaceFor(PetMood mood) => mood switch
    {
        PetMood.Celebrating => _celebrating,
        PetMood.Sleepy => _sleepy,
        PetMood.WideAwake => _wideAwake,
        PetMood.Listening => _celebrating,
        _ => _idle,
    };

    private void ApplyMood(PetMood mood, bool animate, bool replay = false)
    {
        PetMood? from = _hasShown ? _shown : null;
        _headphones.Visibility = mood == PetMood.Listening ? Visibility.Visible : Visibility.Collapsed;
        _shown = mood;
        _hasShown = true;

        bool small = IsSmall;
        animate = animate && IsLoaded && !Motion.IsReduced && !small && from is not null && (from != mood || replay);
        var target = small ? _small : FaceFor(mood);
        bool fallingAsleep = animate && mood == PetMood.Sleepy && from is PetMood.Idle or PetMood.WideAwake;
        var swapAt = fallingAsleep ? EyesShut : TimeSpan.Zero;
        var fade = animate ? MoodFade : TimeSpan.Zero;

        if (fallingAsleep)
        {
            // The open eyes ease shut first; only then does the sleepy face take over.
            var closing = FaceFor(from!.Value);
            closing.EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.1, new Duration(EyesShut)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } },
                HandoffBehavior.SnapshotAndReplace);
        }

        foreach (var face in Faces)
        {
            if (face == target)
            {
                if (!(replay && face == _celebrating))
                    face.ResetEyes();
                Fade(face.Root, 1, fade, swapAt);
            }
            else
            {
                Fade(face.Root, 0, fade, swapAt);
            }
        }

        // Sparks: on while celebrating; with motion they fade out over 1.2s.
        _sparks.BeginAnimation(OpacityProperty, null);
        if (mood == PetMood.Celebrating && !small)
        {
            _sparks.Opacity = 1;
            if (animate)
                _sparks.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, new Duration(SparkFade)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }
        else
        {
            _sparks.Opacity = 0;
        }

        // z's: they fade in after the eyes have closed.
        if (mood == PetMood.Sleepy)
            Fade(_zs, 1, animate ? ZFade : TimeSpan.Zero, swapAt + fade);
        else
            Fade(_zs, 0, fade, TimeSpan.Zero);

        // The celebrate squish: (1.06, 0.94) from the bottom center (60, 108), springing back in about 400ms.
        _squish.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _squish.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (animate && mood == PetMood.Celebrating)
        {
            _squish.BeginAnimation(ScaleTransform.ScaleXProperty, Spring(1.06, 0.985));
            _squish.BeginAnimation(ScaleTransform.ScaleYProperty, Spring(0.94, 1.02));
        }

        UpdateBlinkTimer();
    }

    private static DoubleAnimationUsingKeyFrames Spring(double squish, double rebound)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(SquishDuration), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(squish, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), ease));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(rebound, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(SquishDuration), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return animation;
    }

    private static void Fade(UIElement element, double to, TimeSpan duration, TimeSpan begin)
    {
        if (duration == TimeSpan.Zero && begin == TimeSpan.Zero)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, new Duration(duration == TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : duration)) { BeginTime = begin },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnMotionChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (Motion.IsReduced)
                ApplyMood(_shown, animate: false); // snap any running tween to its end state
            UpdateBlinkTimer();
        });

    private bool CanBlink => IsLoaded && IsVisible && !Motion.IsReduced && (IsSmall ? _small : FaceFor(_shown)).CanBlink;

    private void UpdateBlinkTimer()
    {
        if (CanBlink)
        {
            if (!_blinkTimer.IsEnabled)
            {
                _blinkTimer.Interval = NextBlinkInterval();
                _blinkTimer.Start();
            }
        }
        else
        {
            _blinkTimer.Stop();
        }
    }

    private static TimeSpan NextBlinkInterval() => TimeSpan.FromMilliseconds(Random.Shared.Next(4000, 7001));

#if DEBUG
    /// <summary>Debug builds: blink now, for the motion sheet.</summary>
    internal void BlinkNow() => Blink();
#endif

    /// <summary>The eyes scale to 10% height and back within 120ms.</summary>
    private void Blink()
    {
        if (!CanBlink)
            return;
        var face = IsSmall ? _small : FaceFor(_shown);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var blink = new DoubleAnimationUsingKeyFrames { Duration = new Duration(BlinkDuration), FillBehavior = FillBehavior.Stop };
        blink.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50)), ease));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(BlinkDuration), ease));
        face.EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChippiPetAutomationPeer(this);

    private static Geometry Ellipse(double cx, double cy, double rx, double ry) => Freeze(new EllipseGeometry(new Point(cx, cy), rx, ry));

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    private static Path Filled(Geometry data, string brushKey)
    {
        var path = new Path { Data = data };
        path.SetResourceReference(Shape.FillProperty, brushKey);
        return path;
    }

    private static Path Stroked(Geometry data, string brushKey, double thickness)
    {
        var path = new Path
        {
            Data = data,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, brushKey);
        return path;
    }

    /// <summary>One mood's eyes and mouth (plus its extras), cross-faded as a layer. The eyes scale about y = 74 to blink or close.</summary>
    private sealed class Face
    {
        public Face(bool canBlink, IEnumerable<UIElement> eyes, IEnumerable<UIElement> rest)
        {
            CanBlink = canBlink;
            var eyeLayer = new Canvas { RenderTransform = EyeScale };
            foreach (var part in eyes)
                eyeLayer.Children.Add(part);
            Root.Children.Add(eyeLayer);
            foreach (var part in rest)
                Root.Children.Add(part);
        }

        public Canvas Root { get; } = new() { Width = 120, Height = 120, Opacity = 0 };

        public ScaleTransform EyeScale { get; } = new(1, 1, 60, 66);

        public bool CanBlink { get; }

        public void ResetEyes()
        {
            EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EyeScale.ScaleY = 1;
        }
    }

    /// <summary>DESIGN.md §7 geometry on the 120×120 viewBox, verbatim.</summary>
    internal static class Shapes
    {
        public static Geometry Body { get; } = Parse("M17 76 C17 45 36 31 60 31 C84 31 103 45 103 76 C103 98 87 106 60 106 C33 106 17 98 17 76 Z");

        /// <summary>Ellipse centered (46, 46), radii 9 × 5.5, rotated −28°.</summary>
        public static Geometry Shine { get; } = Freeze(new EllipseGeometry(new Point(46, 46), 9, 5.5) { Transform = new RotateTransform(-28, 46, 46) });

        public static Geometry EyeWhites { get; } = Freeze(new GeometryGroup { Children = { Ellipse(47, 66, 10.5, 12.5), Ellipse(73, 66, 10.5, 12.5) } });
        public static Geometry IdleEyes { get; } = Freeze(new GeometryGroup { Children = { Ellipse(49, 68, 6, 6), Ellipse(75, 68, 6, 6) } });
        public static Geometry Tongue { get; } = Parse("M55 87 Q60 91 65 87 Z");
        public static Geometry SleepMouth { get; } = Ellipse(60, 85, 3.5, 3);
        public static Geometry Headphones { get; } = Parse("M19 70 C19 19 101 19 101 70");

        /// <summary>White highlights r 1.7 at (+1.7, −2.3) from each eye center.</summary>
        public static Geometry IdleHighlights { get; } = Freeze(new GeometryGroup { Children = { Ellipse(50.6, 65.6, 2, 2), Ellipse(76.6, 65.6, 2, 2) } });


        public static Geometry HappyEyes { get; } = Parse("M39 70 Q46 63 53 70 M67 70 Q74 63 81 70");

        public static Geometry OpenSmile { get; } = Parse("M50 82 Q60 94 70 82 Z");


        public static Geometry Sparks { get; } = Parse("M14 30 L6 24 M12 44 H3 M24 18 L20 9 M106 30 L114 24 M108 44 H117 M96 18 L100 9");

        public static Geometry SleepyEyes { get; } = Parse("M39 67 Q46 72 53 67 M67 67 Q74 72 81 67");





        /// <summary>Small form: the eyes enlarged to 6 × 7.4.</summary>
        public static Geometry SmallEyes { get; } = Pair(6, 7.4);

        private static Geometry? _zs;

        /// <summary>
        /// The sleepy z's: "z" in 20px Segoe UI Variable Display Bold at (94, 30) and in 13px at (106, 15), the points
        /// read as SVG text origins (left edge, baseline). Built from the font as outlines.
        /// </summary>
        public static Geometry Zs()
        {
            if (_zs is not null)
                return _zs;
            var family = Application.Current?.TryFindResource("DisplayFont") as FontFamily ?? new FontFamily("Segoe UI");
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var group = new GeometryGroup();
            foreach (var (size, x, y) in new[] { (20.0, 94.0, 30.0), (13.0, 106.0, 15.0) })
            {
                var text = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1.0);
                group.Children.Add(text.BuildGeometry(new Point(x, y - text.Baseline)));
            }
            group.Freeze();
            return _zs = group;
        }

        private static Geometry Pair(double rx, double ry) =>
            Freeze(new GeometryGroup { Children = { Ellipse(47, 74, rx, ry), Ellipse(73, 74, rx, ry) } });

        private static Geometry Highlights(double r) =>
            Freeze(new GeometryGroup { Children = { Ellipse(47 + 1.7, 74 - 2.3, r, r), Ellipse(73 + 1.7, 74 - 2.3, r, r) } });

        private static Geometry Parse(string data) => Freeze(Geometry.Parse(data));
    }

    private sealed class ChippiPetAutomationPeer(ChippiPet owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ChippiPet);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrWhiteSpace(name) ? $"Chippi, {Describe(((ChippiPet)Owner).Mood)}" : name;
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override bool IsControlElementCore() => !((ChippiPet)Owner).IsDecorative;

        protected override bool IsContentElementCore() => !((ChippiPet)Owner).IsDecorative;
    }
}
