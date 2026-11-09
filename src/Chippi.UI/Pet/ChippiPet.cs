// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chippi.Core.Config;
using Chippi.UI.Themes;

namespace Chippi.UI.Pet;

public enum PetRecordingState { None, Recording, Paused }

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

    /// <summary>
    /// The celebrate hop: squished at take-off (where the celebrating face and the sparks come in), stretched on the way
    /// up, the top of the jump, touch down, squished landing, settled.
    /// </summary>
    private static readonly TimeSpan SquishTrough = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HopStretch = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HopApex = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan HopTouchdown = TimeSpan.FromMilliseconds(360);
    private static readonly TimeSpan HopLanding = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan HopDuration = TimeSpan.FromMilliseconds(500);
    internal const double HopHeight = 12; // in the 120 drawing: 4px on the 40 puck

    private static readonly TimeSpan SparkIn = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SparkBurst = TimeSpan.FromMilliseconds(220);
    private const double SparkBurstFrom = 0.6;
    private static readonly TimeSpan SparkHold = TimeSpan.FromMilliseconds(2100);
    private static readonly TimeSpan EyesShut = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan MouthShut = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan EyesOpen = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan ZFade = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan HeadphonesOn = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HeadphonesOff = TimeSpan.FromMilliseconds(120);
    private const double HeadphonesDrop = 18; // in the 120 drawing: 6px on the 40 puck

    /// <summary>Perking up for Keep awake: a stretch tall at its peak, settled; the steam rises this far into place.</summary>
    private static readonly TimeSpan PerkPeak = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PerkDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan SteamOff = TimeSpan.FromMilliseconds(240);
    private const double SteamRise = 4;

    /// <summary>A glance toward the bar: the pupils go this far, hold, and come back.</summary>
    private static readonly TimeSpan GlanceStart = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GlanceThere = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan GlanceBack = TimeSpan.FromMilliseconds(1100);
    private static readonly TimeSpan GlanceEnd = TimeSpan.FromMilliseconds(1300);
    internal const double GlanceShift = 3.5;

    /// <summary>A look toward a point (the Radial menu's pointed slice): the pupils turn this far from the whites' middle, over this long.</summary>
    internal const double LookReach = 3.5;
    private static readonly TimeSpan LookTurn = TimeSpan.FromMilliseconds(120);

    /// <summary>The yawn: eyes squeezed and the mouth opening wide while he stretches, then the face that follows.</summary>
    private static readonly TimeSpan YawnWide = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan YawnStretch = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan YawnClosing = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan YawnSwap = TimeSpan.FromMilliseconds(1100);
    internal static readonly TimeSpan YawnDuration = TimeSpan.FromMilliseconds(1250);

    /// <summary>A short squish and back: a nod, a boop, a landing, a beat.</summary>
    private static readonly TimeSpan NodPeak = TimeSpan.FromMilliseconds(110), NodEnd = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan BoopHold = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan LandPeak = TimeSpan.FromMilliseconds(80), LandEnd = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan BeatPeak = TimeSpan.FromMilliseconds(60), BeatEnd = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// The breathing loop runs whenever the puck is asleep, all day, so it asks WPF for few frames: 10 a second in the
    /// puck form, where a breath moves about a pixel (at 30 it cost about 5% of a core, measured 2026-11-01), and 30 above.
    /// </summary>
    internal const int BreathFrameRatePuck = 10, BreathFrameRate = 30;

    // Stroke widths in the puck form, then above it (DESIGN.md §7, Sizes).
    internal const double SleepyEyeStrokePuck = 4.6, SleepyEyeStroke = 3.8;
    internal const double HappyEyeStrokePuck = 4.8, HappyEyeStroke = 4.2;
    internal const double SparkStrokePuck = 4.5, SparkStroke = 3.5;
    internal const double LidStrokePuck = 4.4, LidStroke = 3.6;
    internal const double SteamStrokePuck = 4.4, SteamStroke = 3.4;

    /// <summary>
    /// His body's middle is (60, 68.5), below the drawing's, to leave room above him for the headphones, z's and sparks.
    /// The drawing is lifted by the difference, so whatever box holds him, his body sits in its middle.
    /// </summary>
    internal const double BodyLift = 8.5;

    // Each skin is decoded once on first use. All the pets wearing it share the frozen material.
    private static readonly ConcurrentDictionary<PetColor, ImageBrush> Skins = new();

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(PetMood), typeof(ChippiPet), new PropertyMetadata(PetMood.Idle, OnMoodChanged));

    // A size that isn't a number, is negative or is endless is refused here, not deep in layout.
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(ChippiPet), new PropertyMetadata(28.0, OnSizeChanged), v => v is double d && double.IsFinite(d) && d >= 0);

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

    public static readonly DependencyProperty ShowsSteamProperty = DependencyProperty.Register(
        nameof(ShowsSteam), typeof(bool), typeof(ChippiPet), new PropertyMetadata(true, (d, _) => ((ChippiPet)d).ApplyForm()));

    public static readonly DependencyProperty IsHeldProperty = DependencyProperty.Register(
        nameof(IsHeld), typeof(bool), typeof(ChippiPet), new PropertyMetadata(false, (d, e) => ((ChippiPet)d).ShowHeld((bool)e.NewValue)));

    private readonly Path _shadow;
    private readonly Path _body;
    private readonly ScaleTransform _breath = new(1, 1, 60, 106);
    private readonly ScaleTransform _squish = new(1, 1, 60, 106);
    private readonly TranslateTransform _hop = new();
    private readonly ScaleTransform _shadowSquish = new(1, 1, 60, 108);
    private readonly ScaleTransform _sparkBurst = new(1, 1, 60, 30);
    private readonly TranslateTransform _steamRise = new();
    private readonly ScaleTransform _bandPulse = new(1, 1, 60, 45);
    private readonly Face _idle;
    private readonly Face _wideAwake;
    private readonly Face _celebrating;
    private readonly Face _sleepy;
    private readonly Face _small;
    private readonly Face _yawn;
    private readonly Face _held;
    private readonly Path _happyEyes;
    private readonly Path _sleepyEyes;
    private readonly Canvas _headphones;
    private readonly Canvas _camcorder = new() { Width = 120, Height = 120, Visibility = Visibility.Collapsed };
    private readonly Ellipse _cameraTally = new() { Width = 3.45, Height = 3.45, Fill = new SolidColorBrush(Color.FromRgb(255, 84, 91)) };
    public static readonly DependencyProperty RecordingProperty = DependencyProperty.Register(nameof(Recording), typeof(PetRecordingState), typeof(ChippiPet),
        new PropertyMetadata(PetRecordingState.None, (d, _) => ((ChippiPet)d).UpdateCamcorder(true)));
    public PetRecordingState Recording { get => (PetRecordingState)GetValue(RecordingProperty); set => SetValue(RecordingProperty, value); }
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
    private bool _celebratedAwake; // the celebration began wide awake (a timed Keep awake ran out), so he yawns after it

    public ChippiPet()
    {
        Focusable = false;
        IsTabStop = false;
        SnapsToDevicePixels = false;

        // A soft contact shadow that follows the squish across, so it stays under him.
        _shadow = Filled(Ellipse(60, 108, 34, 5.5), "PetShadowBrush");
        _shadow.RenderTransform = _shadowSquish;
        _body = new Path { Data = Shapes.Body };
        RenderOptions.SetBitmapScalingMode(_body, BitmapScalingMode.HighQuality);

        _happyEyes = Seam(Shapes.HappyEyes, HappyEyeStroke);
        _sleepyEyes = Seam(Shapes.SleepyEyes, SleepyEyeStroke);
        // The approved iris carries its pupil and catchlight. Each eye has its own molded ivory socket.
        _idle = new Face(canBlink: true,
            eyes: PaintedPair(Shapes.EyeWhites, Vinyl.Ivory, Vinyl.SocketRim),
            mouth: [Painted(Shapes.OpenSmile, Vinyl.Smile)],
            lids: Seam(Shapes.IdleLids, LidStroke),
            pupils: PaintedPair(Shapes.IdleEyes, Vinyl.Iris));
        _wideAwake = new Face(canBlink: true,
            eyes: PaintedPair(Shapes.WideEyeWhites, Vinyl.Ivory, Vinyl.SocketRim),
            mouth: [Painted(Shapes.WideMouth, Vinyl.Mouth)],
            lids: Seam(Shapes.WideLids, LidStroke),
            pupils: PaintedPair(Shapes.WidePupils, Vinyl.Iris));
        // Picked up: Wide awake's round eyes over a small "o".
        _held = new Face(canBlink: false,
            eyes: [.. PaintedPair(Shapes.WideEyeWhites, Vinyl.Ivory, Vinyl.SocketRim), .. PaintedPair(Shapes.WidePupils, Vinyl.Iris)],
            mouth: [Painted(Shapes.HeldMouth, Vinyl.Mouth)]);
        // A yawn: eyes squeezed shut and a tall open mouth with the tongue at its floor.
        _yawn = new Face(canBlink: false,
            eyes: [Seam(Shapes.SqueezedEyes, HappyEyeStroke)],
            mouth: [Painted(Shapes.YawnMouth, Vinyl.Mouth), Painted(Shapes.YawnTongue, Vinyl.Tongue)]);
        _celebrating = new Face(canBlink: false,
            eyes: [_happyEyes],
            mouth: [Painted(Shapes.OpenSmile, Vinyl.Smile)]);
        _sleepy = new Face(canBlink: false,
            eyes: [_sleepyEyes],
            mouth: [Painted(Shapes.SleepMouth, Vinyl.Mouth)]);
        // At 16 the enlarged eyes and their ivory surrounds carry the face alone.
        _small = new Face(canBlink: true,
            eyes: [.. PaintedPair(Shapes.SmallEyeWhites, Vinyl.Ivory), .. PaintedPair(Shapes.SmallEyes, Vinyl.Iris)],
            mouth: [],
            eyeY: 70);

        // A satin graphite headband, molded cups and softly padded inserts, lit from the upper left as the body is.
        _headphones = new Canvas { Width = 120, Height = 120 };
        _headphones.Children.Add(Band(Vinyl.Trim, 9, _bandPulse));
        _headphones.Children.Add(Band(Vinyl.Graphite, 6, _bandPulse));
        foreach (var cup in PaintedPair(Shapes.HeadphoneCups, Vinyl.Graphite, Vinyl.Trim))
            _headphones.Children.Add(cup);
        foreach (var pad in PaintedPair(Shapes.HeadphonePads, Vinyl.Cushion))
            _headphones.Children.Add(pad);

        _sparks = Stroked(Shapes.Sparks, "PetSparkBrush", SparkStroke);
        _sparks.RenderTransform = _sparkBurst;

        // Wide awake's steam, where the z's sit, so every mood has its sign in that corner. A host hides it while its own
        // badge takes the corner (the puck's Keep awake badge, while it's lifted).
        _steam = Stroked(Shapes.Steam, "IslandTextSecondaryBrush", SteamStroke);
        _steam.Opacity = 0;
        _steam.RenderTransform = _steamRise;

        // The z's: the outer layer fades with the mood, the glyphs inside drift while the pet breathes.
        _zGlyphs = new Path { RenderTransform = _zDrift };
        _zGlyphs.SetResourceReference(Shape.FillProperty, "IslandTextSecondaryBrush");
        _zs = new Canvas { Width = 120, Height = 120 };
        _zs.Children.Add(_zGlyphs);

        var figure = new Canvas { Width = 120, Height = 120, RenderTransform = new TransformGroup { Children = { _breath, _squish, _hop } } };
        figure.Children.Add(_body);
        figure.Children.Add(_headphones);
        foreach (var face in Faces)
            figure.Children.Add(face.Root);
        var camera = new Image { Width = 60, Height = 51.875, Source = new BitmapImage(new Uri("pack://application:,,,/Chippi.UI;component/Pet/Assets/HandCamcorder.png")), IsHitTestVisible = false };
        Canvas.SetLeft(camera, -15); Canvas.SetTop(camera, 75); _camcorder.Children.Add(camera);
        Canvas.SetLeft(_cameraTally, 10.95); Canvas.SetTop(_cameraTally, 85.5); _camcorder.Children.Add(_cameraTally);
        figure.Children.Add(_camcorder);

        var canvas = new Canvas { Width = 120, Height = 120, RenderTransform = new TranslateTransform(0, -BodyLift) };
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
            Motion.Changed -= OnMotionChanged; // WPF can raise Loaded twice: one handler, or the static event holds the pet
            Motion.Changed += OnMotionChanged;
            UpdateBlinkTimer();
            UpdateBreathing();
            UpdateCamcorder(false);
        };
        Unloaded += (_, _) =>
        {
            Motion.Changed -= OnMotionChanged;
            _blinkTimer.Stop();
            SetBreathing(false);
            _cameraTally.BeginAnimation(OpacityProperty, null);
        };
        IsVisibleChanged += (_, _) =>
        {
            UpdateCamcorder(false);
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

    /// <summary>False while a host's badge takes the corner Wide awake's steam rises in (the lifted puck's Keep awake badge).</summary>
    public bool ShowsSteam
    {
        get => (bool)GetValue(ShowsSteamProperty);
        set => SetValue(ShowsSteamProperty, value);
    }

    private bool IsSmall => Size <= SmallFormMaxSize;

    private static bool IsPuckForm(double size) => size > SmallFormMaxSize && size <= PuckFormMaxSize;

    private IEnumerable<Face> Faces => [_idle, _wideAwake, _celebrating, _sleepy, _small, _yawn, _held];

    /// <summary>True while the asleep loop runs: the body and face breathing, the z's drifting.</summary>
    internal bool IsBreathing => _breathing;

    /// <summary>True while the blink timer runs (idle, wide awake and the small form, shown, with motion).</summary>
    internal bool IsBlinking => _blinkTimer.IsEnabled;

    /// <summary>For tests and sheets: the face layer showing now, and the z glyphs (one in the puck form, two above it).</summary>
    internal Canvas ShownFace => (IsSmall ? _small : FaceFor(_shown)).Root;

    internal Path ZGlyphs => _zGlyphs;

    internal ScaleTransform Breath => _breath;

    internal ScaleTransform Squish => _squish;

    internal TranslateTransform Hop => _hop;

    internal Path Body => _body;

    internal Path Sparks => _sparks;

    internal Path Steam => _steam;

    internal UIElement Headphones => _headphones;

    internal Path Shadow => _shadow;

    internal TranslateTransform SteamDrift => _steamRise;

    internal ScaleTransform BandPulse => _bandPulse;

    internal Canvas YawnFace => _yawn.Root;

    internal ScaleTransform YawnMouth => _yawn.MouthScale;

    internal Canvas HeldFace => _held.Root;

    /// <summary>The celebrating face, which a grin shows.</summary>
    internal Canvas GrinFace => _celebrating.Root;

    /// <summary>How many faces show now: one, whatever overlaps (a boop, a yawn, being picked up mid-change).</summary>
    internal int FacesShown => Faces.Count(f => f.Root.Opacity > 0);

    /// <summary>The glance's shift on the open-eyed faces' pupils (idle, wide awake).</summary>
    internal TranslateTransform? Pupils => FaceFor(_shown).Pupils;

    /// <summary>The breathing's frame rate at this size: few frames on the puck, where it runs all day.</summary>
    internal int BreathRate => Size <= PuckFormMaxSize ? BreathFrameRatePuck : BreathFrameRate;

    /// <summary>
    /// True while the host holds him up (the puck being dragged): his eyes go round over a small "o", whatever the mood,
    /// and set down he lands with a squish.
    /// </summary>
    public bool IsHeld
    {
        get => (bool)GetValue(IsHeldProperty);
        set => SetValue(IsHeldProperty, value);
    }

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
            pet._celebratedAwake = old == PetMood.WideAwake;
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
        _body.Fill = Skins.GetOrAdd(Enum.IsDefined(PetColor) ? PetColor : PetColor.Synth, static color => Vinyl.Artwork(color.ToString()));
    }

    /// <summary>
    /// Small form drops the mouth and extras; the puck form draws heavier strokes and one big z;
    /// the ground shadow shows from 64px.
    /// </summary>
    private void ApplyForm()
    {
        bool small = IsSmall, puck = IsPuckForm(Size);
        _shadow.Visibility = !small && Size >= GroundShadowMinSize ? Visibility.Visible : Visibility.Collapsed;
        _sparks.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        _zs.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        _steam.Visibility = small || !ShowsSteam ? Visibility.Collapsed : Visibility.Visible;
        _steam.StrokeThickness = puck ? SteamStrokePuck : SteamStroke;
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

        // A glance, a beat's pulse or a yawn still running stops where the new mood begins, and snaps to rest when
        // reduced motion comes on (OnMotionChanged applies the mood again).
        foreach (var face in Faces)
            face.Pupils?.BeginAnimation(TranslateTransform.XProperty, null);
        _bandPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _bandPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _yawn.Reset();
        _yawnEnds = 0;

        animate = animate && IsLoaded && !Motion.IsReduced && !small && from is not null && (from != mood || replay);
        // Picked up, the round eyes stay over whatever the mood is until he's set down.
        var target = small ? _small : IsHeld ? _held : FaceFor(mood);
        // When Keep awake ends he yawns before he sleeps: from wide awake, or after the celebration a timed one ends with.
        bool yawning = animate && !IsHeld && mood == PetMood.Sleepy && (from == PetMood.WideAwake || (from == PetMood.Celebrating && _celebratedAwake));
        bool fallingAsleep = animate && !yawning && mood == PetMood.Sleepy && from is PetMood.Idle or PetMood.WideAwake;
        bool waking = animate && from == PetMood.Sleepy && mood is PetMood.Idle or PetMood.WideAwake;
        bool celebrating = animate && mood == PetMood.Celebrating && from != PetMood.Celebrating;
        bool perking = animate && mood == PetMood.WideAwake && from != PetMood.WideAwake;
        // Falling asleep, the open face closes first. Celebrating, it holds to the squish's deepest point and swaps there
        // at once, so the two faces never show together; perking up, the same at the stretch's peak. Yawning, the old
        // face goes at once as the yawn comes in, and the sleeping face takes over at once as it ends.
        var swapAt = yawning ? YawnSwap : fallingAsleep ? EyesShut : celebrating ? SquishTrough : perking ? PerkPeak : TimeSpan.Zero;
        var fade = animate && !celebrating && !perking && !yawning ? MoodFade : TimeSpan.Zero;
        var outAt = yawning ? TimeSpan.Zero : swapAt;
        var outFade = fade;

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
                Fade(face.Root, 0, outFade, outAt);
            }
        }
        if (yawning)
            PlayYawn();

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
        // Wide awake's steam rises 4 into place as it fades in; when he leaves wide awake it drifts on up as it fades.
        bool steam = mood == PetMood.WideAwake, steamWas = from == PetMood.WideAwake;
        _steamRise.BeginAnimation(TranslateTransform.YProperty, null);
        if (steam)
        {
            Fade(_steam, 1, animate ? PerkDuration : TimeSpan.Zero, swapAt);
            if (animate && !steamWas)
                _steamRise.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(SteamRise, 0, new Duration(PerkDuration)) { BeginTime = swapAt, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        else
        {
            bool drift = animate && steamWas;
            Fade(_steam, 0, drift ? SteamOff : fade, TimeSpan.Zero);
            if (drift)
                _steamRise.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -SteamRise, new Duration(SteamOff)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.Stop });
        }

        // The celebrate hop, from the bottom center (60, 106): squished (1.10, 0.90) at take-off, stretched (0.94, 1.08) on
        // the way up, 12 up at the top, down by 360ms, squished (1.06, 0.94) landing and settled by 500ms. The shadow spreads
        // with the squishes, and shrinks and fades while he's up. Perking up for Keep awake he stretches tall and settles;
        // yawning he stretches slower.
        _squish.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _squish.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _hop.BeginAnimation(TranslateTransform.YProperty, null);
        _shadowSquish.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _shadow.BeginAnimation(OpacityProperty, null);
        _hopEnds = 0;
        if (animate && mood == PetMood.Celebrating)
        {
            _squish.BeginAnimation(ScaleTransform.ScaleXProperty, HopScale(1.10, 0.94, 1.06));
            _squish.BeginAnimation(ScaleTransform.ScaleYProperty, HopScale(0.90, 1.08, 0.94));
            _hop.BeginAnimation(TranslateTransform.YProperty, Leap(0, -HopHeight));
            _shadowSquish.BeginAnimation(ScaleTransform.ScaleXProperty, HopScale(1.10, 0.9, 1.06, apex: 0.8));
            _shadow.BeginAnimation(OpacityProperty, Leap(1, 0.55));
            _hopEnds = Environment.TickCount64 + (long)HopDuration.TotalMilliseconds;
        }
        else if (perking)
            Squash(0.96, 1.06, PerkPeak, PerkDuration);
        else if (yawning)
            Squash(0.97, 1.05, YawnStretch, YawnDuration - MoodFade);

        UpdateBlinkTimer();
        UpdateBreathing(settle: animate);
    }

    // The hop owns his squish until it lands: a nod, a boop or a beat meanwhile would cut it short. A yawn owns his face
    // and its stretch until the next face comes in: a glance or a beat waits for it; a boop or being picked up ends it.
    private long _hopEnds, _yawnEnds;

    private bool Hopping => Environment.TickCount64 < _hopEnds;

    private bool Yawning => Environment.TickCount64 < _yawnEnds;

    /// <summary>Picked up, the round eyes take his face; set down, his face comes back and he lands with a squish.</summary>
    private void ShowHeld(bool held)
    {
        if (IsSmall)
            return;
        bool animate = IsLoaded && !Motion.IsReduced;
        var face = held ? _held : FaceFor(_shown);
        if (!held)
            face.Reset();
        // At once, both ways, and every other face with it: a cross-fade, a face still closing as he falls asleep, a grin
        // or a yawn would show two faces at the same time.
        _yawnEnds = 0;
        foreach (var other in Faces)
            Fade(other.Root, other == face ? 1 : 0, TimeSpan.Zero, TimeSpan.Zero);
        if (!held && animate && !Hopping)
            Squash(1.06, 0.94, LandPeak, LandEnd);
        UpdateBlinkTimer();
    }

    /// <summary>
    /// He looks toward something new beside him (the bar unfolding, or something new arriving on it): -1 to his left,
    /// 1 to his right. Open eyes glance there for about a second and come back; closed ones (listening) nod instead.
    /// </summary>
    public void Glance(int direction)
    {
        if (!IsLoaded || Motion.IsReduced || IsSmall || IsHeld || Yawning || direction == 0)
            return;
        var face = FaceFor(_shown);
        if (face.Pupils is { } pupils)
        {
            double shift = direction < 0 ? -face.GlanceLeft : face.GlanceRight;
            var look = new DoubleAnimationUsingKeyFrames { Duration = new Duration(GlanceEnd), FillBehavior = FillBehavior.Stop };
            look.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(GlanceStart)));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(shift, KeyTime.FromTimeSpan(GlanceThere), new CubicEase { EasingMode = EasingMode.EaseOut }));
            look.KeyFrames.Add(new LinearDoubleKeyFrame(shift, KeyTime.FromTimeSpan(GlanceBack)));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(GlanceEnd), new SineEase { EasingMode = EasingMode.EaseInOut }));
            pupils.BeginAnimation(TranslateTransform.XProperty, look);
        }
        else if (_shown != PetMood.Sleepy && !Hopping)
            Squash(1.03, 0.95, NodPeak, NodEnd);
    }

    /// <summary>
    /// He looks toward <paramref name="direction"/> (screen x and y, up to 1 long), or back ahead for null: the Radial
    /// menu's hub turns his pupils toward the pointed slice. They turn over 120ms, at once under reduced motion, and stay
    /// until the next look. Faces with open eyes only (idle, wide awake).
    /// </summary>
    public void Look(Vector? direction)
    {
        var face = FaceFor(_shown);
        if (face.Pupils is not { } pupils)
            return;
        // From the irises' photographed rest position to the sockets' center plus the look.
        var to = direction is { } d ? LookReach * d - face.Rest : new Vector();
        if (Motion.IsReduced || !IsLoaded)
        {
            pupils.BeginAnimation(TranslateTransform.XProperty, null);
            pupils.BeginAnimation(TranslateTransform.YProperty, null);
            pupils.X = to.X;
            pupils.Y = to.Y;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        pupils.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to.X, new Duration(LookTurn)) { EasingFunction = ease });
        pupils.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to.Y, new Duration(LookTurn)) { EasingFunction = ease });
    }

    /// <summary>
    /// The celebrating face at once in place of his own, with no hop, squish or sparks, for <paramref name="hold"/>, then
    /// his face again: the Radial menu's hub as a slice runs and the wheel fades.
    /// </summary>
    public void Grin(TimeSpan hold)
    {
        if (IsSmall || IsHeld)
            return;
        var shown = FaceFor(_shown);
        foreach (var face in Faces)
        {
            if (face == _celebrating)
                face.Root.BeginAnimation(OpacityProperty, shown == _celebrating ? Then(1, TimeSpan.Zero) : Swap(1, 0, TimeSpan.Zero, hold));
            else if (face == shown)
                face.Root.BeginAnimation(OpacityProperty, Swap(0, 1, TimeSpan.Zero, hold));
            else if (face.Root.Opacity > 0 || face.Root.HasAnimatedProperties)
                face.Root.BeginAnimation(OpacityProperty, Then(0, TimeSpan.Zero));
        }
    }

    /// <summary>
    /// A yawn and a stretch over whatever he shows, then that face again: the first unlock of the day. Falling asleep
    /// when Keep awake ends, he yawns on his own.
    /// </summary>
    public void Yawn()
    {
        if (!IsLoaded || Motion.IsReduced || IsSmall || IsHeld)
            return;
        // His face goes as the yawn comes in and is back as it ends; any other still showing (a boop's grin, a face
        // still closing as he fell asleep) goes with it.
        var face = FaceFor(_shown);
        foreach (var other in Faces)
            if (other != face && other != _yawn)
                Fade(other.Root, 0, TimeSpan.Zero, TimeSpan.Zero);
        face.Root.BeginAnimation(OpacityProperty, Swap(0, 1, TimeSpan.Zero, YawnSwap));
        if (_shown == PetMood.Sleepy)
        {
            // After a quiet minute the sleeping face returns at the end of this yawn. Its z follows it.
            Fade(_zs, 0, TimeSpan.Zero, TimeSpan.Zero);
            Fade(_zs, 1, ZFade, YawnSwap);
        }
        PlayYawn();
        if (!Hopping)
            Squash(0.97, 1.05, YawnStretch, YawnDuration - MoodFade);
    }

    /// <summary>
    /// A face's opacity <paramref name="during"/> from <paramref name="from"/> to <paramref name="until"/>, then
    /// <paramref name="after"/>, held: swapped at once each way, so two faces never show together.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Swap(double during, double after, TimeSpan from, TimeSpan until)
    {
        var swap = new DoubleAnimationUsingKeyFrames();
        swap.KeyFrames.Add(new DiscreteDoubleKeyFrame(during, KeyTime.FromTimeSpan(from)));
        swap.KeyFrames.Add(new DiscreteDoubleKeyFrame(after, KeyTime.FromTimeSpan(until)));
        return swap;
    }

    /// <summary>A face's opacity as it is until <paramref name="at"/>, then <paramref name="to"/>, held.</summary>
    private static DoubleAnimationUsingKeyFrames Then(double to, TimeSpan at) =>
        new() { KeyFrames = { new DiscreteDoubleKeyFrame(to, KeyTime.FromTimeSpan(at)) } };

    /// <summary>The yawn's face: in at once, the mouth opening wide by 450ms and closing by 1.1s, out at once as the next face comes in.</summary>
    private void PlayYawn()
    {
        _yawn.Reset();
        _yawnEnds = Environment.TickCount64 + (long)YawnSwap.TotalMilliseconds;
        _yawn.Root.BeginAnimation(OpacityProperty, Swap(1, 0, TimeSpan.Zero, YawnSwap));
        var open = new DoubleAnimationUsingKeyFrames { Duration = new Duration(YawnDuration), FillBehavior = FillBehavior.Stop };
        open.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        open.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(YawnWide), new CubicEase { EasingMode = EasingMode.EaseOut }));
        open.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(YawnClosing)));
        open.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(YawnSwap), new SineEase { EasingMode = EasingMode.EaseInOut }));
        _yawn.MouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, open);
    }

    /// <summary>A boop (the footer's Chippi clicked): a squish and a grin for a moment, then his face again.</summary>
    public void Boop()
    {
        if (IsSmall || IsHeld)
            return;
        // The grin comes in at the squish's deepest point and goes at once, as the celebration's face does. Whatever shows
        // until then (his face, a yawn, a face still closing) goes as it comes, so one face shows at a time.
        var face = FaceFor(_shown);
        var at = Motion.IsReduced ? TimeSpan.Zero : NodPeak;
        foreach (var other in Faces)
        {
            if (other == _celebrating)
                other.Root.BeginAnimation(OpacityProperty, face == _celebrating ? Then(1, at) : Swap(1, 0, at, BoopHold));
            else if (other == face)
                other.Root.BeginAnimation(OpacityProperty, Swap(0, 1, at, BoopHold));
            else if (other.Root.Opacity > 0 || other.Root.HasAnimatedProperties)
                other.Root.BeginAnimation(OpacityProperty, Then(0, at));
        }
        _yawnEnds = 0;
        if (!Motion.IsReduced && !Hopping)
            Squash(1.08, 0.92, NodPeak, TimeSpan.FromMilliseconds(360));
    }

    /// <summary>
    /// A beat in the music he's listening to, <paramref name="strength"/> 0 to 1: a dip from the bottom center and a pulse
    /// of the headphones' band. Only while listening, with motion, and never over the hop or a yawn.
    /// </summary>
    public void Beat(double strength)
    {
        if (!IsLoaded || Motion.IsReduced || IsSmall || IsHeld || _shown != PetMood.Listening || Hopping || Yawning || !double.IsFinite(strength))
            return;
        double s = Math.Clamp(strength, 0, 1);
        Squash(1 + 0.025 * s, 1 - 0.04 * s, BeatPeak, BeatEnd);
        var pulse = Pulse(1 + 0.04 * s, BeatPeak, BeatEnd);
        _bandPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        _bandPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    /// <summary>A short squish from the bottom center: to (<paramref name="x"/>, <paramref name="y"/>) at its peak, round again by its end.</summary>
    private void Squash(double x, double y, TimeSpan peak, TimeSpan end)
    {
        _squish.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse(x, peak, end));
        _squish.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(y, peak, end));
    }

    /// <summary>To <paramref name="to"/> by <paramref name="peak"/>, quick then easing, and back to 1 by <paramref name="end"/>.</summary>
    internal static DoubleAnimationUsingKeyFrames Pulse(double to, TimeSpan peak, TimeSpan end)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(end), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(peak), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(end), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return animation;
    }

    /// <summary>
    /// Celebrating's sparks. With motion they come in with the celebrating face as he takes off, bursting out from 60% over
    /// 220ms, hold to 2.1s and fade over the celebration's last 400ms; without, they show for the whole of it.
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
        burst.KeyFrames.Add(new DiscreteDoubleKeyFrame(SparkBurstFrom, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        burst.KeyFrames.Add(new DiscreteDoubleKeyFrame(SparkBurstFrom, KeyTime.FromTimeSpan(at)));
        burst.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(at + SparkBurst), new CubicEase { EasingMode = EasingMode.EaseOut }));
        _sparkBurst.BeginAnimation(ScaleTransform.ScaleXProperty, burst);
        _sparkBurst.BeginAnimation(ScaleTransform.ScaleYProperty, burst);
    }

    // Shown, with motion, and seen: locked or with the display off, nobody watches him breathe all night.
    private bool CanBreathe => IsLoaded && IsVisible && !Motion.IsReduced && !Motion.IsUnseen && (_shown == PetMood.Sleepy || Recording == PetRecordingState.Recording);

    private void UpdateCamcorder(bool animate)
    {
        bool on = Recording != PetRecordingState.None;
        bool entering = on && _camcorder.Visibility != Visibility.Visible;
        _camcorder.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _cameraTally.BeginAnimation(OpacityProperty, null);
        _cameraTally.Fill = new SolidColorBrush(Recording == PetRecordingState.Paused ? Color.FromRgb(244, 187, 94) : Color.FromRgb(255, 84, 91));
        _camcorder.RenderTransform = new TranslateTransform();
        if (on && IsLoaded && IsVisible && !Motion.IsReduced && !Motion.IsUnseen && Recording != PetRecordingState.Paused)
        {
            var pulse = new DoubleAnimation(.45, 1, TimeSpan.FromSeconds(1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(pulse, 10); _cameraTally.BeginAnimation(OpacityProperty, pulse);
            if (entering && animate) ((TranslateTransform)_camcorder.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)));
        }
        UpdateBreathing();
    }

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
            int rate = BreathRate;
            Breathe(_breath, ScaleTransform.ScaleXProperty, 1, 1.022, rate);
            Breathe(_breath, ScaleTransform.ScaleYProperty, 1, 1.04, rate);
            Breathe(_zDrift, TranslateTransform.YProperty, 2, -3, rate);
            Breathe(_zGlyphs, OpacityProperty, 0.45, 1, rate);
        }
        else
        {
            Rest(_breath, ScaleTransform.ScaleXProperty, 1, settle);
            Rest(_breath, ScaleTransform.ScaleYProperty, 1, settle);
            Rest(_zDrift, TranslateTransform.YProperty, 0, settle);
            Rest(_zGlyphs, OpacityProperty, 1, settle);
        }
    }

    private static void Breathe(IAnimatable target, DependencyProperty property, double from, double to, int frameRate)
    {
        var loop = new DoubleAnimation(from, to, new Duration(BreathCycle / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(loop, frameRate);
        target.BeginAnimation(property, loop, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Rest(IAnimatable target, DependencyProperty property, double rest, bool settle) =>
        target.BeginAnimation(property,
            settle ? new DoubleAnimation(rest, new Duration(MoodFade)) { FillBehavior = FillBehavior.Stop } : null,
            HandoffBehavior.SnapshotAndReplace);

    /// <summary>
    /// A scale through the hop: <paramref name="takeOff"/> at the trough, <paramref name="stretch"/> on the way up,
    /// <paramref name="apex"/> at the top, 1 by touch down, <paramref name="landing"/> as he lands, then 1.
    /// </summary>
    internal static DoubleAnimationUsingKeyFrames HopScale(double takeOff, double stretch, double landing, double apex = 1)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var impact = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(HopDuration), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(takeOff, KeyTime.FromTimeSpan(SquishTrough), impact));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(stretch, KeyTime.FromTimeSpan(HopStretch), ease));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(apex, KeyTime.FromTimeSpan(HopApex), ease));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(HopTouchdown), ease));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(landing, KeyTime.FromTimeSpan(HopLanding), impact));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(HopDuration), ease));
        return animation;
    }

    /// <summary>
    /// <paramref name="ground"/> until take-off, then to <paramref name="top"/> at the apex, slowing as he rises, and back
    /// by touch down, quickening as he falls.
    /// </summary>
    internal static DoubleAnimationUsingKeyFrames Leap(double ground, double top)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(HopDuration), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(ground, KeyTime.FromTimeSpan(SquishTrough)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(top, KeyTime.FromTimeSpan(HopApex), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(ground, KeyTime.FromTimeSpan(HopTouchdown), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
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
            UpdateCamcorder(false);
        });

    // The small form has one face for every mood, and blinks only in the moods whose eyes are open.
    private bool CanBlink => IsLoaded && IsVisible && !Motion.IsReduced && !Motion.IsUnseen && (IsSmall ? _shown is PetMood.Idle or PetMood.WideAwake : FaceFor(_shown).CanBlink);

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

    private static Path Painted(Geometry data, Brush material, Brush? rim = null)
    {
        var path = new Path { Data = data, Fill = material, Stroke = rim, StrokeThickness = 0.65 };
        if (material is ImageBrush)
            RenderOptions.SetBitmapScalingMode(path, BitmapScalingMode.HighQuality);
        return path;
    }

    // Separate paths keep the material's light local to each eye or cup.
    private static Path[] PaintedPair(GeometryGroup pair, Brush material, Brush? rim = null) =>
        [.. pair.Children.Select(shape => Painted(shape, material, rim))];

    private static class Vinyl
    {
        internal static readonly Brush Iris = Artwork("Eye"), Smile = Artwork("Smile");
        internal static readonly Brush Ivory = Molded(0xFFFCF2, 0xF6E4DC, 0xC4A7B9);
        internal static readonly Brush Mouth = Molded(0x67314C, 0x491E3A, 0x2A1228);
        internal static readonly Brush Tongue = Molded(0xD878B6, 0xB25398, 0x782F66);
        internal static readonly Brush Graphite = Molded(0x776C86, 0x3F354F, 0x21182D);
        internal static readonly Brush Cushion = Molded(0xF1E5F7, 0xB8A5C8, 0x79688B);
        internal static readonly Brush Trim = Molded(0xF4EBF8, 0xAE9AC3, 0x6E5D83);
        internal static readonly Brush SocketRim = Molded(0xB49AA9, 0xBA9AAE, 0xD4BAD3);

        private static Brush Molded(int light, int mid, int edge)
        {
            static Color Rgb(int value) => Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.45, 0.44), GradientOrigin = new Point(0.23, 0.18), RadiusX = 0.70, RadiusY = 0.70,
                GradientStops = { new GradientStop(Rgb(light), 0.04), new GradientStop(Rgb(mid), 0.63), new GradientStop(Rgb(edge), 1) },
            };
            brush.Freeze();
            return brush;
        }

        internal static ImageBrush Artwork(string name)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri($"pack://application:,,,/Chippi.UI;component/Pet/Assets/Vinyl{name}.png");
            image.EndInit();
            image.Freeze();
            var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }
    }

    private static Path Band(Brush stroke, double thickness, Transform pulse) => new()
    {
        Data = Shapes.Headphones,
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        RenderTransform = pulse,
    };

    private static Path Stroked(Geometry data, string brushKey, double thickness)
    {
        var path = Stroke(data, thickness);
        path.SetResourceReference(Shape.StrokeProperty, brushKey);
        return path;
    }

    private static Path Seam(Geometry data, double thickness)
    {
        var path = Stroke(data, thickness);
        path.Stroke = Vinyl.Mouth;
        return path;
    }

    private static Path Stroke(Geometry data, double thickness) => new()
    {
        Data = data, StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
    };

    /// <summary>
    /// One mood's eyes and mouth (and the lids it blinks with), cross-faded as a layer. The eyes scale about their
    /// middle (y 66, the small form's y 70) to blink or close; the mouth scales about (60, 86) to close.
    /// </summary>
    private sealed class Face
    {
        /// <param name="pupils">The open eyes' pupils and highlights, which glance together; the whites stay put.</param>
        public Face(bool canBlink, Path[] eyes, IEnumerable<UIElement> mouth, Path? lids = null, double eyeY = 66,
            Path[]? pupils = null)
        {
            CanBlink = canBlink;
            EyeScale = new ScaleTransform(1, 1, 60, eyeY);
            var eyeLayer = new Canvas { RenderTransform = EyeScale };
            foreach (var part in eyes)
                eyeLayer.Children.Add(part);
            if (pupils is not null)
            {
                Pupils = new TranslateTransform();
                // How far they can go each way and stay half a unit inside the whites they sit in.
                var sockets = new GeometryGroup { Children = new GeometryCollection(eyes.Select(p => p.Data)) };
                sockets.Freeze();
                var clipped = new Canvas { Width = 120, Height = 120, Clip = sockets };
                var whites = sockets.Bounds;
                var ink = Rect.Union(pupils[0].Data.Bounds, pupils[1].Data.Bounds);
                Rest = new Point(ink.X + ink.Width / 2, ink.Y + ink.Height / 2) - new Point(whites.X + whites.Width / 2, whites.Y + whites.Height / 2);
                for (int i = 0; i < pupils.Length; i++)
                {
                    pupils[i].RenderTransform = Pupils;
                    clipped.Children.Add(pupils[i]);
                    var iris = pupils[i].Data.Bounds;
                    var white = eyes[i].Data.Bounds;
                    GlanceLeft = Math.Min(GlanceLeft, iris.Left - white.Left - 0.5);
                    GlanceRight = Math.Min(GlanceRight, white.Right - iris.Right - 0.5);
                }
                eyeLayer.Children.Add(clipped);
            }
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

        /// <summary>The glance's shift, for a face with open eyes.</summary>
        public TranslateTransform? Pupils { get; }

        public double GlanceLeft { get; } = GlanceShift;

        public double GlanceRight { get; } = GlanceShift;

        /// <summary>Where the pupils rest from the whites' middle, for a look.</summary>
        public Vector Rest { get; }

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
        // The skin carries its own silhouette and soft edge inside these bounds.
        public static Geometry Body { get; } = Freeze(new RectangleGeometry(new Rect(12, 29.5, 96, 78)));

        private static GeometryGroup Pair(Geometry left, Geometry right)
        {
            var group = new GeometryGroup { Children = { left, right } };
            group.Freeze();
            return group;
        }

        private static Geometry Patch(double x, double y, double width, double height) =>
            Freeze(new RectangleGeometry(new Rect(x, y, width, height)));

        public static GeometryGroup EyeWhites { get; } = Pair(
            Parse("M42 55.6 C47.7 55.1 52.5 60.3 52.8 67 C53.2 73.5 49.3 78.3 43.5 78.5 C36.9 78.9 31.2 74.7 31.5 67 C31.6 60.9 36.3 55.9 42 55.6 Z"),
            Parse("M71.2 51.6 C77.2 51.3 82.3 57.1 82.5 63.2 C83 69.8 79.4 74.9 73.1 75.1 C66.2 75.3 61.4 70.5 61.4 63.4 C61.4 57.1 65.5 52.1 71.2 51.6 Z"));

        /// <summary>The idle eyes shut, for the blink: a line across each eye's middle.</summary>
        public static Geometry IdleLids { get; } = Parse("M33.5 66 Q42 67.5 51 66 M63 65.6 Q71 66.6 81 65.5");
        public static GeometryGroup IdleEyes { get; } = Pair(Patch(37.5, 58.2, 14.3, 17.25), Patch(65.75, 54.6, 14.3, 17.25));
        public static Geometry SleepMouth { get; } = Ellipse(60, 86, 4, 3.4);
        public static Geometry Headphones { get; } = Parse("M19 70 C19 19 101 19 101 70");

        /// <summary>Headphone cups 15 × 26, radius 6, at x 10 and 95, y 57.</summary>
        public static GeometryGroup HeadphoneCups { get; } = Pair(Rounded(10, 57, 15, 26, 6), Rounded(95, 57, 15, 26, 6));

        /// <summary>Pads 6 × 16, radius 3, 4.5 in from each cup's left edge, at y 62.</summary>
        public static GeometryGroup HeadphonePads { get; } = Pair(Rounded(14.5, 62, 6, 16, 3), Rounded(99.5, 62, 6, 16, 3));

        /// <summary>Wide awake exposes more ivory around the same iris; it sits one unit low in the socket.</summary>
        public static GeometryGroup WideEyeWhites { get; } = Pair(Ellipse(42.5, 66.5, 12, 14), Ellipse(72, 62.5, 12, 14));
        public static GeometryGroup WidePupils { get; } = Pair(Patch(37, 60.75, 11, 13.5), Patch(66.5, 56.75, 11, 13.5));
        public static Geometry WideMouth { get; } = Ellipse(60, 89, 4.4, 5);
        public static Geometry WideLids { get; } = Parse("M32 66 H53 M61.5 65.5 H82.5");

        /// <summary>Wide awake's two wisps of steam, where the z's sit when he's asleep.</summary>
        public static Geometry Steam { get; } = Parse("M95 36 C91 31 99 27 95 21 M105 33 C101 28 109 24 105 18");

        public static Geometry HappyEyes { get; } = Parse("M34.5 69.6 Q41.6 61 50.3 68.5 M64.2 65.8 Q71.2 57.1 80 64.7");

        public static Geometry OpenSmile { get; } = Patch(49.6, 77.05, 20.3, 14.05);

        public static Geometry Sparks { get; } = Parse("M14 30 L6 24 M12 44 H3 M24 18 L20 9 M106 30 L114 24 M108 44 H117 M96 18 L100 9");

        public static Geometry SleepyEyes { get; } = Parse("M34.6 67.6 Q42 73 50.4 66.6 M64 64.1 Q71.5 69.6 80.3 63");

        /// <summary>Picked up: a small "o", 3.2 × 3.8 at (60, 88), under Wide awake's round eyes.</summary>
        public static Geometry HeldMouth { get; } = Ellipse(60, 88, 3.2, 3.8);

        /// <summary>The yawn: eyes squeezed shut as two chevrons, a tall open mouth 6.5 × 9 at (60, 88), the tongue at its floor.</summary>
        public static Geometry SqueezedEyes { get; } = Parse("M40 62 L52 67 L40 72 M80 62 L68 67 L80 72");
        public static Geometry YawnMouth { get; } = Ellipse(60, 88, 6.5, 9);
        public static Geometry YawnTongue { get; } = Ellipse(60, 93.5, 4, 2.4);

        /// <summary>Small form: enlarged sockets and irises keep their light and dark shapes distinct at 16.</summary>
        public static GeometryGroup SmallEyeWhites { get; } = Pair(Ellipse(44, 70, 12, 13.5), Ellipse(74, 67, 12, 13.5));
        public static GeometryGroup SmallEyes { get; } = Pair(Patch(38, 62.5, 15, 17), Patch(67.5, 59.5, 15, 17));

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
