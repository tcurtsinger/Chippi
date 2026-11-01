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

namespace Chippi.UI.Pet;

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

    /// <summary>Above the small form and up to this size the puck form is drawn: heavier strokes and one z.</summary>
    public const double PuckFormMaxSize = 44;

    /// <summary>From this size up the ground shadow is drawn.</summary>
    public const double GroundShadowMinSize = 64;

    internal static readonly TimeSpan CelebrationHold = TimeSpan.FromSeconds(2.5);

    /// <summary>One breath while asleep: out to (1.022, 1.04) for half of it and back for the other half.</summary>
    internal static readonly TimeSpan BreathCycle = TimeSpan.FromSeconds(4.2);

    private static readonly TimeSpan MoodFade = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SquishDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>The squish's deepest point, where the celebrating face and the sparks come in.</summary>
    private static readonly TimeSpan SquishTrough = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan SparkIn = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SparkBurst = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan SparkHold = TimeSpan.FromMilliseconds(2100);
    private static readonly TimeSpan EyesShut = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan MouthShut = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan EyesOpen = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan ZFade = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan HeadphonesOn = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HeadphonesOff = TimeSpan.FromMilliseconds(120);
    private const double HeadphonesDrop = 18; // in the 120 drawing: 6px on the 40 puck

    /// <summary>
    /// The breathing loop runs whenever the puck is asleep, so it asks WPF for 30 frames a second, not 60.
    /// It moves well under a pixel a frame at any size.
    /// </summary>
    private const int BreathFrameRate = 30;

    // Stroke widths in the puck form, then above it (DESIGN.md §7, Sizes).
    internal const double SleepyEyeStrokePuck = 4.6, SleepyEyeStroke = 3.8;
    internal const double HappyEyeStrokePuck = 4.8, HappyEyeStroke = 4.2;
    internal const double SparkStrokePuck = 4.5, SparkStroke = 3.5;
    internal const double LidStrokePuck = 4.4, LidStroke = 3.6;
    internal const double SteamStroke = 3.4;

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(PetMood), typeof(ChippiPet), new PropertyMetadata(PetMood.Idle, OnMoodChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(ChippiPet), new PropertyMetadata(28.0, OnSizeChanged));

    /// <summary>
    /// The pet's color. Inherited: set on a host (the island's panel), it reaches every pet inside, including one on a
    /// feature's page that never hears about the setting.
    /// </summary>
    public static readonly DependencyProperty PetColorProperty = DependencyProperty.RegisterAttached(
        nameof(PetColor), typeof(PetColor), typeof(ChippiPet), new FrameworkPropertyMetadata(PetColor.Synth, FrameworkPropertyMetadataOptions.Inherits, OnPetColorChanged));

    public static PetColor GetPetColor(DependencyObject element) => (PetColor)element.GetValue(PetColorProperty);

    public static void SetPetColor(DependencyObject element, PetColor value) => element.SetValue(PetColorProperty, value);

    public static readonly DependencyProperty IsDecorativeProperty = DependencyProperty.Register(
        nameof(IsDecorative), typeof(bool), typeof(ChippiPet), new PropertyMetadata(false));

    private readonly Path _shadow;
    private readonly Path _body;
    private readonly ScaleTransform _breath = new(1, 1, 60, 106);
    private readonly ScaleTransform _squish = new(1, 1, 60, 106);
    private readonly ScaleTransform _shadowSquish = new(1, 1, 60, 108);
    private readonly ScaleTransform _sparkBurst = new(1, 1, 60, 30);
    private readonly Face _idle;
    private readonly Face _wideAwake;
    private readonly Face _celebrating;
    private readonly Face _sleepy;
    private readonly Face _small;
    private readonly Path _happyEyes;
    private readonly Path _sleepyEyes;
    private readonly Canvas _headphones;
    private readonly Path _sparks;
    private readonly Path _steam;
    private readonly Canvas _zs;
    private readonly Path _zGlyphs;
    private readonly TranslateTransform _zDrift = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _celebrationTimer;

    private PetMood _shown;
    private bool _hasShown;
    private bool _breathing;
    private PetMood _returnMood = PetMood.Idle;

    public ChippiPet()
    {
        Focusable = false;
        IsTabStop = false;
        SnapsToDevicePixels = false;

        // A soft contact shadow that follows the squish across, so it stays under him.
        _shadow = Filled(Ellipse(60, 108, 34, 5.5), "PetShadowBrush");
        _shadow.RenderTransform = _shadowSquish;
        _body = new Path { Data = Shapes.Body };

        _happyEyes = Stroked(Shapes.HappyEyes, "PetInkBrush", HappyEyeStroke);
        _sleepyEyes = Stroked(Shapes.SleepyEyes, "PetInkBrush", SleepyEyeStroke);
        _idle = new Face(canBlink: true,
            eyes: [Whites(Shapes.EyeWhites), Filled(Shapes.IdleEyes, "PetInkBrush"), Filled(Shapes.IdleHighlights, "PetEyeHighlightBrush")],
            mouth: [Filled(Shapes.OpenSmile, "PetInkBrush"), new Path { Data = Shapes.Tongue, Fill = Frozen(0xFF7FA8) }],
            lids: Stroked(Shapes.IdleLids, "PetInkBrush", LidStroke));
        _wideAwake = new Face(canBlink: true,
            eyes: [Whites(Shapes.WideEyeWhites), Filled(Shapes.WidePupils, "PetInkBrush"), Filled(Shapes.WideHighlights, "PetEyeHighlightBrush")],
            mouth: [Filled(Shapes.WideMouth, "PetInkBrush")],
            lids: Stroked(Shapes.WideLids, "PetInkBrush", LidStroke));
        _celebrating = new Face(canBlink: false,
            eyes: [_happyEyes],
            mouth: [Filled(Shapes.OpenSmile, "PetInkBrush"), new Path { Data = Shapes.Tongue, Fill = Frozen(0xFF7FA8) }]);
        _sleepy = new Face(canBlink: false,
            eyes: [_sleepyEyes],
            mouth: [Filled(Shapes.SleepMouth, "PetInkBrush")]);
        // The small form's eyes are the tray icon's: whites and big pupils, which read at 16 where dots alone don't.
        _small = new Face(canBlink: true,
            eyes: [Whites(Shapes.SmallEyeWhites), Filled(Shapes.SmallEyes, "PetInkBrush")],
            mouth: [],
            eyeY: 70);

        // Headphones that read on the dark puck: a light rim under an ink band, ink cups rimmed the same, lilac pads.
        var headphoneInk = Frozen(0x1B1530);
        var headphoneRim = Frozen(0xE6DCFF);
        _headphones = new Canvas { Width = 120, Height = 120 };
        _headphones.Children.Add(Band(headphoneRim, 10));
        _headphones.Children.Add(Band(headphoneInk, 5.5));
        _headphones.Children.Add(new Path { Data = Shapes.HeadphoneCups, Fill = headphoneInk, Stroke = headphoneRim, StrokeThickness = 2.2 });
        _headphones.Children.Add(new Path { Data = Shapes.HeadphonePads, Fill = Frozen(0xD2BEFF) });

        _sparks = Stroked(Shapes.Sparks, "PetSparkBrush", SparkStroke);
        _sparks.RenderTransform = _sparkBurst;

        // Wide awake's steam, where the z's sit; above the puck form only, since the puck's Keep awake badge takes that corner.
        _steam = Stroked(Shapes.Steam, "IslandTextSecondaryBrush", SteamStroke);
        _steam.Opacity = 0;

        // The z's: the outer layer fades with the mood, the glyphs inside drift while the pet breathes.
        _zGlyphs = new Path { RenderTransform = _zDrift };
        _zGlyphs.SetResourceReference(Shape.FillProperty, "IslandTextSecondaryBrush");
        _zs = new Canvas { Width = 120, Height = 120 };
        _zs.Children.Add(_zGlyphs);

        var figure = new Canvas { Width = 120, Height = 120, RenderTransform = new TransformGroup { Children = { _breath, _squish } } };
        figure.Children.Add(_body);
        figure.Children.Add(_headphones);
        foreach (var face in Faces)
            figure.Children.Add(face.Root);

        var canvas = new Canvas { Width = 120, Height = 120 };
        canvas.Children.Add(_shadow);
        canvas.Children.Add(figure);
        canvas.Children.Add(_sparks);
        canvas.Children.Add(_steam);
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
            UpdateBreathing();
        };
        Unloaded += (_, _) =>
        {
            Motion.Changed -= OnMotionChanged;
            _blinkTimer.Stop();
            SetBreathing(false);
        };
        IsVisibleChanged += (_, _) =>
        {
            UpdateBlinkTimer();
            UpdateBreathing();
        };
    }

    public PetMood Mood
    {
        get => (PetMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>Rendered edge in DIPs. At 22 and below the small form is drawn, from 23 to 44 the puck form (DESIGN.md §7, Sizes).</summary>
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

    private static bool IsPuckForm(double size) => size > SmallFormMaxSize && size <= PuckFormMaxSize;

    private IEnumerable<Face> Faces => [_idle, _wideAwake, _celebrating, _sleepy, _small];

    /// <summary>True while the asleep loop runs: the body and face breathing, the z's drifting.</summary>
    internal bool IsBreathing => _breathing;

    /// <summary>True while the blink timer runs (idle, wide awake and the small form, shown, with motion).</summary>
    internal bool IsBlinking => _blinkTimer.IsEnabled;

    /// <summary>For tests and sheets: the face layer showing now, and the z glyphs (one in the puck form, two above it).</summary>
    internal Canvas ShownFace => (IsSmall ? _small : FaceFor(_shown)).Root;

    internal Path ZGlyphs => _zGlyphs;

    internal ScaleTransform Breath => _breath;

    internal ScaleTransform Squish => _squish;

    internal Path Sparks => _sparks;

    internal Path Steam => _steam;

    internal UIElement Headphones => _headphones;

    internal Path Shadow => _shadow;

    /// <summary>The mood as words, for the accessible name: "idle", "celebrating", "asleep", "wide awake", "listening".</summary>
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
    public void ShowMood(PetMood mood)
    {
        if (Mood == PetMood.Celebrating && _celebrationTimer.IsEnabled && mood != PetMood.Celebrating)
            _returnMood = mood;
        else
            SetCurrentValue(MoodProperty, mood);
    }

    /// <summary>Celebrates for about 2.5s, then returns to the current mood. Celebrating again restarts it.</summary>
    public void Celebrate()
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
        double old = (double)e.OldValue, size = (double)e.NewValue;
        pet.Width = pet.Height = size;
        if ((old <= SmallFormMaxSize) != pet.IsSmall
            || IsPuckForm(old) != IsPuckForm(size)
            || (old >= GroundShadowMinSize) != (size >= GroundShadowMinSize))
        {
            pet.ApplyForm();
            pet.ApplyMood(pet._shown, animate: false);
        }
    }

    // Inherited, so every element under a host hears it; only pets redraw.
    private static void OnPetColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChippiPet pet)
            pet.UpdateBodyBrush();
    }

    /// <summary>The body color for <paramref name="color"/> (DESIGN.md §7, Color option). The one place the six live.</summary>
    public static Color ColorOf(PetColor color) => color switch
    {
        PetColor.Iris => Color.FromRgb(0xCB, 0xB2, 0xFF),
        PetColor.Azure => Color.FromRgb(0x8E, 0xC0, 0xFF),
        PetColor.Signal => Color.FromRgb(0x8E, 0xE6, 0xC0),
        PetColor.Ember => Color.FromRgb(0xFF, 0xC5, 0x8F),
        PetColor.Frost => Color.FromRgb(0xE8, 0xEC, 0xF6),
        _ => Color.FromRgb(0xB7, 0x8F, 0xFF),
    };

    private void UpdateBodyBrush()
    {
        var baseColor = ColorOf(PetColor);
        var shade = Color.FromRgb((byte)(baseColor.R * .88), (byte)(baseColor.G * .88), (byte)(baseColor.B * .88));
        var brush = new LinearGradientBrush(baseColor, shade, 90);
        brush.Freeze();
        _body.Fill = brush;
    }

    /// <summary>
    /// Small form drops the mouth, highlights and extras; the puck form draws heavier strokes and one big z;
    /// the ground shadow and Wide awake's steam show above the puck form, the shadow from 64px.
    /// </summary>
    private void ApplyForm()
    {
        bool small = IsSmall, puck = IsPuckForm(Size);
        _shadow.Visibility = !small && Size >= GroundShadowMinSize ? Visibility.Visible : Visibility.Collapsed;
        _sparks.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        _zs.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        _steam.Visibility = small || puck ? Visibility.Collapsed : Visibility.Visible;
        _happyEyes.StrokeThickness = puck ? HappyEyeStrokePuck : HappyEyeStroke;
        _sleepyEyes.StrokeThickness = puck ? SleepyEyeStrokePuck : SleepyEyeStroke;
        _sparks.StrokeThickness = puck ? SparkStrokePuck : SparkStroke;
        foreach (var face in Faces)
            if (face.Lids is { } lids)
                lids.StrokeThickness = puck ? LidStrokePuck : LidStroke;
        _zGlyphs.Data = puck ? Shapes.PuckZ() : Shapes.Zs();
    }

    private bool _headphonesOn;

    /// <summary>
    /// The headphones go on when a song starts: they drop 18 onto Chippi (6px on the 40 puck) as they fade in over
    /// 180ms, and come off lifting the same as they fade over 120ms. Under reduced motion only the fades.
    /// </summary>
    private void ShowHeadphones(bool on, bool animate)
    {
        if (on == _headphonesOn && animate)
            return; // already on their way
        _headphonesOn = on;
        if (_headphones.RenderTransform is not TranslateTransform drop || drop.IsFrozen)
            _headphones.RenderTransform = drop = new TranslateTransform();
        if (!animate)
        {
            _headphones.BeginAnimation(OpacityProperty, null);
            drop.BeginAnimation(TranslateTransform.YProperty, null);
            _headphones.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        var ease = new CubicEase { EasingMode = on ? EasingMode.EaseOut : EasingMode.EaseIn };
        var time = on ? HeadphonesOn : HeadphonesOff;
        // From off they start above and clear; turning back midway, they go on from where they are.
        bool fromOff = _headphones.Visibility != Visibility.Visible;
        _headphones.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(on ? 1 : 0, time);
        if (fromOff)
            fade.From = 0;
        fade.Completed += (_, _) =>
        {
            if (_headphonesOn != on)
                return; // turned again meanwhile
            if (!on)
                _headphones.Visibility = Visibility.Collapsed;
            _headphones.BeginAnimation(OpacityProperty, null);
            drop.BeginAnimation(TranslateTransform.YProperty, null);
        };
        _headphones.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        if (Motion.IsReduced)
            return;
        var move = new DoubleAnimation(on ? 0 : -HeadphonesDrop, time) { EasingFunction = ease };
        if (fromOff)
            move.From = -HeadphonesDrop;
        drop.BeginAnimation(TranslateTransform.YProperty, move, HandoffBehavior.SnapshotAndReplace);
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
        bool small = IsSmall;
        // A celebration during a song keeps the headphones on, since he goes back to listening after it.
        bool headphones = !small && (mood == PetMood.Listening || (mood == PetMood.Celebrating && _returnMood == PetMood.Listening));
        ShowHeadphones(headphones, animate && IsLoaded && from is not null && from != mood);
        _shown = mood;
        _hasShown = true;

        animate = animate && IsLoaded && !Motion.IsReduced && !small && from is not null && (from != mood || replay);
        var target = small ? _small : FaceFor(mood);
        bool fallingAsleep = animate && mood == PetMood.Sleepy && from is PetMood.Idle or PetMood.WideAwake;
        bool waking = animate && from == PetMood.Sleepy && mood is PetMood.Idle or PetMood.WideAwake;
        bool celebrating = animate && mood == PetMood.Celebrating && from != PetMood.Celebrating;
        // Falling asleep, the open face closes first. Celebrating, it holds to the squish's deepest point and swaps there
        // at once, so the two faces never show together.
        var swapAt = fallingAsleep ? EyesShut : celebrating ? SquishTrough : TimeSpan.Zero;
        var fade = animate && !celebrating ? MoodFade : TimeSpan.Zero;

        if (fallingAsleep)
        {
            // The eyes ease shut while the grin closes toward the sleeping mouth, so he never smirks on the way.
            var closing = FaceFor(from!.Value);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            closing.EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.1, new Duration(EyesShut)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            closing.MouthScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.35, new Duration(MouthShut)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            closing.MouthScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.35, new Duration(MouthShut)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            // The last of it the lids draw in, so the eyes end shut, not as a sliver, when the sleeping face takes over.
            if (closing.Lids is { } lids)
            {
                var shut = new DoubleAnimationUsingKeyFrames();
                shut.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
                shut.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(EyesShut)));
                lids.BeginAnimation(OpacityProperty, shut, HandoffBehavior.SnapshotAndReplace);
            }
        }

        foreach (var face in Faces)
        {
            if (face == target)
            {
                if (!(replay && face == _celebrating))
                    face.Reset();
                Fade(face.Root, 1, fade, swapAt);
            }
            else
            {
                Fade(face.Root, 0, fade, swapAt);
            }
        }

        // Waking, the eyes open from shut a little past round and settle: falling asleep the other way.
        if (waking)
        {
            var open = new DoubleAnimationUsingKeyFrames { Duration = new Duration(EyesOpen), FillBehavior = FillBehavior.Stop };
            open.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            open.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            open.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(EyesOpen), new SineEase { EasingMode = EasingMode.EaseInOut }));
            target.EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, open);
        }

        ShowSparks(mood == PetMood.Celebrating && !small, animate, swapAt);

        // z's: they fade in after the eyes have closed. Wide awake's steam comes and goes with its face.
        if (mood == PetMood.Sleepy)
            Fade(_zs, 1, animate ? ZFade : TimeSpan.Zero, swapAt + fade);
        else
            Fade(_zs, 0, fade, TimeSpan.Zero);
        bool steam = mood == PetMood.WideAwake;
        Fade(_steam, steam ? 1 : 0, fade, steam ? swapAt : TimeSpan.Zero);

        // The celebrate squish: (1.10, 0.90) from the bottom center (60, 106), springing back in about 400ms; the
        // shadow spreads with it.
        _squish.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _squish.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _shadowSquish.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (animate && mood == PetMood.Celebrating)
        {
            _squish.BeginAnimation(ScaleTransform.ScaleXProperty, Spring(1.10, 0.97));
            _squish.BeginAnimation(ScaleTransform.ScaleYProperty, Spring(0.90, 1.03));
            _shadowSquish.BeginAnimation(ScaleTransform.ScaleXProperty, Spring(1.10, 0.97));
        }

        UpdateBlinkTimer();
        UpdateBreathing(settle: animate);
    }

    /// <summary>
    /// Celebrating's sparks. With motion they come in with the celebrating face, bursting out from 75% over 220ms, hold
    /// to 2.1s and fade over the celebration's last 400ms; without, they show for the whole of it.
    /// </summary>
    private void ShowSparks(bool on, bool animate, TimeSpan at)
    {
        _sparks.BeginAnimation(OpacityProperty, null);
        _sparkBurst.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _sparkBurst.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _sparks.Opacity = on ? 1 : 0;
        if (!on || !animate)
            return;
        var show = new DoubleAnimationUsingKeyFrames();
        show.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        show.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(at)));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(at + SparkIn)));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(SparkHold)));
        show.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(CelebrationHold), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        _sparks.BeginAnimation(OpacityProperty, show);
        var burst = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        burst.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        burst.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(at)));
        burst.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(at + SparkBurst), new CubicEase { EasingMode = EasingMode.EaseOut }));
        _sparkBurst.BeginAnimation(ScaleTransform.ScaleXProperty, burst);
        _sparkBurst.BeginAnimation(ScaleTransform.ScaleYProperty, burst);
    }

    private bool CanBreathe => IsLoaded && IsVisible && !Motion.IsReduced && _shown == PetMood.Sleepy;

    private void UpdateBreathing(bool settle = false) => SetBreathing(CanBreathe, settle);

    /// <summary>
    /// Asleep, the body and face scale to (1.022, 1.04) about the bottom center (60, 106) and back, while
    /// the z's drift from 2 below to 3 above and from 45% to full opacity, one 4.2s cycle, eased, repeating.
    /// Stopping settles over the mood fade when <paramref name="settle"/>, else snaps to rest at once.
    /// </summary>
    private void SetBreathing(bool on, bool settle = false)
    {
        if (on == _breathing)
            return;
        _breathing = on;
        if (on)
        {
            Breathe(_breath, ScaleTransform.ScaleXProperty, 1, 1.022);
            Breathe(_breath, ScaleTransform.ScaleYProperty, 1, 1.04);
            Breathe(_zDrift, TranslateTransform.YProperty, 2, -3);
            Breathe(_zGlyphs, OpacityProperty, 0.45, 1);
        }
        else
        {
            Rest(_breath, ScaleTransform.ScaleXProperty, 1, settle);
            Rest(_breath, ScaleTransform.ScaleYProperty, 1, settle);
            Rest(_zDrift, TranslateTransform.YProperty, 0, settle);
            Rest(_zGlyphs, OpacityProperty, 1, settle);
        }
    }

    private static void Breathe(IAnimatable target, DependencyProperty property, double from, double to)
    {
        var loop = new DoubleAnimation(from, to, new Duration(BreathCycle / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(loop, BreathFrameRate);
        target.BeginAnimation(property, loop, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Rest(IAnimatable target, DependencyProperty property, double rest, bool settle) =>
        target.BeginAnimation(property,
            settle ? new DoubleAnimation(rest, new Duration(MoodFade)) { FillBehavior = FillBehavior.Stop } : null,
            HandoffBehavior.SnapshotAndReplace);

    private static DoubleAnimationUsingKeyFrames Spring(double squish, double rebound)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(SquishDuration), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(squish, KeyTime.FromTimeSpan(SquishTrough), ease));
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
                ApplyMood(_shown, animate: false); // snap any running tween to its end state, and stop breathing
            UpdateBlinkTimer();
            UpdateBreathing();
        });

    // The small form has one face for every mood, and blinks only in the moods whose eyes are open.
    private bool CanBlink => IsLoaded && IsVisible && !Motion.IsReduced && (IsSmall ? _shown is PetMood.Idle or PetMood.WideAwake : FaceFor(_shown).CanBlink);

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

    /// <summary>
    /// The eyes scale to 10% height and back within 120ms. While they're shut the lids show as an ink line over them,
    /// so the blink reads as closed eyes rather than a squashed white sliver.
    /// </summary>
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
        if (face.Lids is { } lids)
        {
            var shut = new DoubleAnimationUsingKeyFrames { Duration = new Duration(BlinkDuration), FillBehavior = FillBehavior.Stop };
            shut.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(35))));
            shut.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
            shut.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
            shut.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85))));
            lids.BeginAnimation(OpacityProperty, shut);
        }
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

    /// <summary>Eye whites with a faint ink rim, so they hold their edge on a pale body (Frost).</summary>
    private static Path Whites(Geometry data)
    {
        var path = Filled(data, "PetEyeHighlightBrush");
        path.SetResourceReference(Shape.StrokeProperty, "PetEyeRimBrush");
        path.StrokeThickness = 1.5;
        return path;
    }

    private static SolidColorBrush Frozen(int rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    private static Path Band(Brush stroke, double thickness) => new()
    {
        Data = Shapes.Headphones,
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

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

    /// <summary>
    /// One mood's eyes and mouth (and the lids it blinks with), cross-faded as a layer. The eyes scale about their
    /// middle (y 66, the small form's y 70) to blink or close; the mouth scales about (60, 86) to close.
    /// </summary>
    private sealed class Face
    {
        public Face(bool canBlink, IEnumerable<UIElement> eyes, IEnumerable<UIElement> mouth, Path? lids = null, double eyeY = 66)
        {
            CanBlink = canBlink;
            EyeScale = new ScaleTransform(1, 1, 60, eyeY);
            var eyeLayer = new Canvas { RenderTransform = EyeScale };
            foreach (var part in eyes)
                eyeLayer.Children.Add(part);
            Root.Children.Add(eyeLayer);
            var mouthLayer = new Canvas { RenderTransform = MouthScale };
            foreach (var part in mouth)
                mouthLayer.Children.Add(part);
            Root.Children.Add(mouthLayer);
            if (lids is not null)
            {
                lids.Opacity = 0;
                Root.Children.Add(lids);
                Lids = lids;
            }
        }

        public Canvas Root { get; } = new() { Width = 120, Height = 120, Opacity = 0 };

        public ScaleTransform EyeScale { get; }

        public ScaleTransform MouthScale { get; } = new(1, 1, 60, 86);

        public Path? Lids { get; }

        public bool CanBlink { get; }

        /// <summary>Open eyes, a whole mouth and no lids: where a blink, falling asleep or waking left off.</summary>
        public void Reset()
        {
            EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EyeScale.ScaleY = 1;
            MouthScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            MouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            MouthScale.ScaleX = MouthScale.ScaleY = 1;
            if (Lids is not null)
            {
                Lids.BeginAnimation(OpacityProperty, null);
                Lids.Opacity = 0;
            }
        }
    }

    /// <summary>DESIGN.md §7 geometry on the 120×120 viewBox, verbatim.</summary>
    internal static class Shapes
    {
        public static Geometry Body { get; } = Parse("M17 76 C17 45 36 31 60 31 C84 31 103 45 103 76 C103 98 87 106 60 106 C33 106 17 98 17 76 Z");

        public static Geometry EyeWhites { get; } = Freeze(new GeometryGroup { Children = { Ellipse(47, 66, 10.5, 12.5), Ellipse(73, 66, 10.5, 12.5) } });

        /// <summary>The idle eyes shut, for the blink: a line across each eye's middle.</summary>
        public static Geometry IdleLids { get; } = Parse("M38 66 H56 M64 66 H82");
        public static Geometry IdleEyes { get; } = Freeze(new GeometryGroup { Children = { Ellipse(49, 68, 6, 6), Ellipse(75, 68, 6, 6) } });
        public static Geometry Tongue { get; } = Parse("M55 87 Q60 91 65 87 Z");
        public static Geometry SleepMouth { get; } = Ellipse(60, 86, 4, 3.4);
        public static Geometry Headphones { get; } = Parse("M19 70 C19 19 101 19 101 70");

        /// <summary>Headphone cups 15 × 26, radius 6, at x 10 and 95, y 57.</summary>
        public static Geometry HeadphoneCups { get; } = Freeze(new GeometryGroup { Children = { Rounded(10, 57, 15, 26, 6), Rounded(95, 57, 15, 26, 6) } });

        /// <summary>Pads 6 × 16, radius 3, 4.5 in from each cup's left edge, at y 62.</summary>
        public static Geometry HeadphonePads { get; } = Freeze(new GeometryGroup { Children = { Rounded(14.5, 62, 6, 16, 3), Rounded(99.5, 62, 6, 16, 3) } });

        /// <summary>White highlights r 2 at (50.6, 65.6) and (76.6, 65.6), up and right in each pupil.</summary>
        public static Geometry IdleHighlights { get; } = Freeze(new GeometryGroup { Children = { Ellipse(50.6, 65.6, 2, 2), Ellipse(76.6, 65.6, 2, 2) } });

        /// <summary>Wide awake: eye whites 12 × 14 at (46, 65) and (74, 65), pupils r 4.4 a little low, small highlights, an "o" mouth.</summary>
        public static Geometry WideEyeWhites { get; } = Freeze(new GeometryGroup { Children = { Ellipse(46, 65, 12, 14), Ellipse(74, 65, 12, 14) } });
        public static Geometry WidePupils { get; } = Freeze(new GeometryGroup { Children = { Ellipse(46, 66, 4.4, 4.4), Ellipse(74, 66, 4.4, 4.4) } });
        public static Geometry WideHighlights { get; } = Freeze(new GeometryGroup { Children = { Ellipse(47.7, 63.8, 1.6, 1.6), Ellipse(75.7, 63.8, 1.6, 1.6) } });
        public static Geometry WideMouth { get; } = Ellipse(60, 89, 4.4, 5);
        public static Geometry WideLids { get; } = Parse("M35 65 H57 M63 65 H85");

        /// <summary>Wide awake's two wisps of steam, where the z's sit when he's asleep.</summary>
        public static Geometry Steam { get; } = Parse("M95 36 C91 31 99 27 95 21 M105 33 C101 28 109 24 105 18");

        public static Geometry HappyEyes { get; } = Parse("M39 70 Q46 63 53 70 M67 70 Q74 63 81 70");

        public static Geometry OpenSmile { get; } = Parse("M50 82 Q60 94 70 82 Z");

        public static Geometry Sparks { get; } = Parse("M14 30 L6 24 M12 44 H3 M24 18 L20 9 M106 30 L114 24 M108 44 H117 M96 18 L100 9");

        public static Geometry SleepyEyes { get; } = Parse("M39 67 Q46 72 53 67 M67 67 Q74 72 81 67");

        /// <summary>Small form: the tray icon's eyes, whites 12 × 13.5 at (46, 70) and (74, 70) with pupils r 7.5 a little in and down.</summary>
        public static Geometry SmallEyeWhites { get; } = Freeze(new GeometryGroup { Children = { Ellipse(46, 70, 12, 13.5), Ellipse(74, 70, 12, 13.5) } });
        public static Geometry SmallEyes { get; } = Freeze(new GeometryGroup { Children = { Ellipse(47, 71, 7.5, 7.5), Ellipse(75, 71, 7.5, 7.5) } });

        private static Geometry? _zs;
        private static Geometry? _puckZ;

        /// <summary>
        /// The z's above the puck form: "z" in 22px Segoe UI Variable Display SemiBold at (93, 32) and in 14px at
        /// (106, 16), the points read as SVG text origins (left edge, baseline). Built from the font as outlines.
        /// </summary>
        public static Geometry Zs() => _zs ??= Glyphs((22, 93, 32), (14, 106, 16));

        /// <summary>The puck form's one z: 30px SemiBold at (92, 34), big enough to read at 40.</summary>
        public static Geometry PuckZ() => _puckZ ??= Glyphs((30, 92, 34));

        private static Geometry Glyphs(params (double Size, double X, double Y)[] zs)
        {
            var family = Application.Current?.TryFindResource("DisplayFont") as FontFamily ?? new FontFamily("Segoe UI");
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var group = new GeometryGroup();
            foreach (var (size, x, y) in zs)
            {
                var text = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1.0);
                group.Children.Add(text.BuildGeometry(new Point(x, y - text.Baseline)));
            }
            group.Freeze();
            return group;
        }

        private static Geometry Rounded(double x, double y, double width, double height, double radius) =>
            Freeze(new RectangleGeometry(new Rect(x, y, width, height), radius, radius));

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
