// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Shortcuts;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.RadialMenu.Views;
using Chippi.Features.RadialMenu.Wheel;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Actions;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.Features.RadialMenu;

/// <summary>
/// The Radial menu (docs/phase-5.5/radial-menu.md): hold the middle, Back or Forward button, or press its shortcut, and a
/// wheel of up to 8 slices opens at the pointer; flick toward one and let go, or click one. It never takes focus, so keys,
/// text and window actions land in the app in front. Set up in Settings › Radial menu; no home button.
/// </summary>
public sealed class RadialMenuModule : IFeatureModule, IWheelInputSink
{
    public const string Id = "radial-menu";

    /// <summary>A failure stays on the bar this long, as the Output shortcut's does.</summary>

    /// <summary>Chippi grins this long as a slice runs, past the wheel's fade.</summary>
    private static readonly TimeSpan GrinHold = TimeSpan.FromMilliseconds(400);

    private const string ErrorKey = "radial-menu-failed";

    private readonly Func<IRadialPc> _createPc;
    private readonly Func<IWheelInputSink, IWheelInput> _createInput;
    private readonly Func<IHotkey>? _createHotkey;
    private readonly Func<bool> _cleaning;
    private readonly Func<bool> _keepAwake;
    private readonly Func<WebEngine> _engine;
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private IRadialPc? _pc;
    private IWheelInput? _input;
    private IHotkey? _hotkey;
    private ModuleContext? _context;
    private bool _capturing;

    // The open wheel.
    private WheelState? _state;
    private WheelWindow? _window;
    private Screen _screen;
    private nint _windowUnder;
    private IReadOnlyList<ChippiCommand> _commands = [];
    private int _shownDepth;

    /// <param name="cleaning">Whether Cleaning mode is on: the wheel stays closed then.</param>
    /// <param name="keepAwake">Whether Keep awake runs: the hub's Chippi is wide awake then.</param>
    /// <param name="engine">The Command bar's web search, for Selected text's Search the web.</param>
    public RadialMenuModule(Func<bool> cleaning, Func<bool> keepAwake, Func<WebEngine> engine)
        : this(null, null, null, null, cleaning, keepAwake, engine)
    {
    }

    /// <param name="pc">The PC; null for the real one, whose actions run on the shell's (<see cref="ModuleContext.Actions"/>).</param>
    /// <param name="input">The mouse and Esc; null for the real ones, a filter in the shell's input hub.</param>
    /// <param name="recorder">Records a Key shortcut slice; null for the real one, a filter in the input hub.</param>
    public RadialMenuModule(Func<IRadialPc>? pc, Func<IWheelInputSink, IWheelInput>? input, Func<IHotkey>? hotkey, IKeyRecorder? recorder,
        Func<bool>? cleaning = null, Func<bool>? keepAwake = null, Func<WebEngine>? engine = null)
    {
        _createPc = pc ?? (() => new WindowsRadialPc(Actions()));
        _createInput = input ?? (sink => new WheelHook(sink, Hub(), Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher));
        _createHotkey = hotkey;
        Recorder = recorder ?? new KeyRecorder(Hub);
        _cleaning = cleaning ?? (() => false);
        _keepAwake = keepAwake ?? (() => false);
        _engine = engine ?? (() => WebEngine.Google);
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Radial menu", "Hold a mouse button for a ring of actions under the pointer", FeatureGroup.Windows)
    {
        Icon = nameof(LucideIcons.Zap),
        Requires = [Capability.InputHooks],
        Presets = PresetMembership.PowerUser,
    };

    public RadialMenuSettings Settings { get; private set; } = new();

    /// <summary>Why the shortcut didn't register ("Shortcut in use"), or null.</summary>
    public string? ShortcutError { get; private set; }

    /// <summary>"No trigger" while nothing opens the wheel (Add features shows it in place of the description).</summary>
    public string? Status => _context is not null && !Settings.HoldsAButton && Settings.Shortcut is null ? "No trigger" : null;

    /// <summary>The settings changed, or the Start menu's apps were read. Raised on the UI thread.</summary>
    public event Action? Changed;

    internal IRadialPc? Pc => _pc;

    internal ActionRunner? Runner { get; private set; }

    /// <summary>Whether a slice's target is gone (<see cref="ActionRunner.IsMissing"/>); a submenu's never is.</summary>
    internal bool IsMissing(Slice slice, IReadOnlyList<ChippiCommand> known) =>
        slice.Kind != SliceKind.Submenu && Runner?.IsMissing(slice.ToAction(), known) == true;

    internal IKeyRecorder Recorder { get; }

    private Core.Input.IInputHub Hub() => _context?.Input ?? throw new InvalidOperationException("The Radial menu needs the input hub");

    private IActionPc Actions() => _context?.Actions ?? throw new InvalidOperationException("The Radial menu needs the shell's actions");

    /// <summary>Chippi's colour, as Settings › Island &amp; pet picked it.</summary>
    internal Core.Config.PetColor ChippiColor => _context?.ChippiColor() ?? Core.Config.PetColor.Synth;

    /// <summary>The wheel at the pointer while it shows (tests and shots read it).</summary>
    internal WheelState? Open => _state;

    internal WheelView? View => _window?.View;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _hotkey ??= CreateHotkey(); // listed in Settings › Shortcuts while it runs, set or not
        _capturing = false;
        ShortcutError = null;
        Settings = context.Settings.Get<RadialMenuSettings>().Checked();
        _pc = _createPc();
        _pc.AppsChanged += OnAppsChanged;
        Runner = new ActionRunner(_pc, context.Commands, () => context.Clipboard, text => _engine().SearchUrl(text), () => context.IslandPasteTarget()?.Window ?? 0);
        _input = _createInput(this);
        _input.Paused = _cleaning;
        _input.OpenOverFullscreen = Settings.OpenOverFullscreen;
        if (context.Input is { } hub)
            hub.PauseChanged += OnPauseChanged;
        try { _input.Watch(Settings.HoldMiddle, Settings.HoldBack, Settings.HoldForward); }
        catch (Exception ex)
        {
            Log.Warn("Radial menu: hooking the mouse failed", ex);
            throw;
        }
        if (Settings.Shortcut is { } shortcut && !ShortcutStandsAside)
            TryRegister(shortcut);
        context.Ui.SetSettingsPage(_ => new RadialMenuSettingsPage(this));
        _pc.RefreshApps();
        WarmIcons();
        Prepare();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Makes the wheel's window while the app is idle, once something can open it: made at the first hold instead, it
    /// costs about 65ms there (measured 2026-11-02); made ahead, an opening takes about 5.
    /// </summary>
    private void Prepare()
    {
        if (_window is not null || (!Settings.HoldsAButton && Settings.Shortcut is null))
            return;
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_context is not null && _window is null)
                _window = WheelWindow.Made(ChippiColor);
        });
    }

    public Task StopAsync(CancellationToken ct)
    {
        void Stop()
        {
            if (_state is not null)
                Finish();
            _window?.Close();
            _window = null;
            if (_context?.Input is { } hub)
                hub.PauseChanged -= OnPauseChanged;
            _input?.Dispose();
            _input = null;
            _hotkey?.Dispose();
            _hotkey = null;
            if (_pc is not null)
            {
                _pc.AppsChanged -= OnAppsChanged;
                _pc.Dispose();
            }
            _pc = null;
            Runner = null;
            _context = null;
            _icons.Clear();
        }
        // Its window and hooks are the UI thread's; the registry may stop it from another.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(Stop);
        else
            Stop();
        return Task.CompletedTask;
    }

    private void OnAppsChanged() => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        WarmIcons();
        Changed?.Invoke();
    });

    // ---- Settings › Radial menu ----

    public void SetHold(MouseButton button, bool on)
    {
        var settings = button switch
        {
            MouseButton.Middle => Settings with { HoldMiddle = on },
            MouseButton.Back => Settings with { HoldBack = on },
            MouseButton.Forward => Settings with { HoldForward = on },
            _ => Settings,
        };
        Save(settings);
        try { _input?.Watch(settings.HoldMiddle, settings.HoldBack, settings.HoldForward); }
        catch (Exception ex) { Log.Warn("Radial menu: hooking the mouse failed", ex); }
        Prepare();
    }

    /// <summary>Settings › Radial menu › Open over fullscreen apps.</summary>
    public void SetOpenOverFullscreen(bool on)
    {
        Save(Settings with { OpenOverFullscreen = on });
        if (_input is not null)
            _input.OpenOverFullscreen = on;
        ApplyShortcutGate();
    }

    /// <summary>Off over fullscreen apps while one is in front: the shortcut is let go, so the game gets its keys.</summary>
    private bool ShortcutStandsAside => !Settings.OpenOverFullscreen && _context?.Input?.FullscreenInFront == true;

    // The hub raises it on its own thread.
    private void OnPauseChanged() => Application.Current?.Dispatcher.BeginInvoke(ApplyShortcutGate);

    private void ApplyShortcutGate()
    {
        if (_context is null || Settings.Shortcut is not { } shortcut)
            return;
        if (ShortcutStandsAside)
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        else if (_hotkey is null)
        {
            TryRegister(shortcut);
            Changed?.Invoke();
        }
    }

    /// <summary>The wheel's slices, as the editor leaves them.</summary>
    public void SetSlices(List<Slice> slices)
    {
        Save((Settings with { Slices = slices }).Checked());
        WarmIcons();
    }

    private void Save(RadialMenuSettings settings)
    {
        _context?.Settings.Save(settings);
        Settings = settings;
        Changed?.Invoke();
    }

    /// <summary>The app's or file's own icon, 24 at the wheel's scale, once read; null until then or when there's none.</summary>
    internal ImageSource? IconOf(Slice slice) =>
        slice.Glyph is null && slice.Kind is SliceKind.App or SliceKind.File && _icons.TryGetValue(slice.Value, out var icon) ? icon : null;

    // Reads every app's and file's icon ahead, so the wheel opens with them.
    private void WarmIcons()
    {
        if (_pc is not { } pc)
            return;
        foreach (var slice in All(Settings.Slices).Where(s => s.Glyph is null && s.Kind is SliceKind.App or SliceKind.File && s.Value.Length > 0))
        {
            if (_icons.TryGetValue(slice.Value, out var known) && known is not null)
                continue;
            string value = slice.Value;
            _ = Read();

            async Task Read()
            {
                try
                {
                    var icon = await pc.IconAsync(value, 48); // 24 at 200%
                    _icons[value] = icon;
                    if (icon is not null)
                        Changed?.Invoke();
                }
                catch (Exception ex) { Log.Warn("Radial menu: reading an icon failed", ex); }
            }
        }

        static IEnumerable<Slice> All(IEnumerable<Slice> slices) => slices.SelectMany(s => s.Kind == SliceKind.Submenu ? All(s.Slices) : [s]);
    }

    // ---- The shortcut: none by default, recorded in Settings › Radial menu or Settings › Shortcuts ----

    private void OnShortcut()
    {
        if (_capturing || _context is null)
            return;
        // Tapped again, it closes the wheel it opened.
        if (_state is not null)
        {
            Dismissed();
            return;
        }
        var (at, shown) = _pc!.Pointer();
        OpenAt(at, shown, holding: false);
    }

    public bool ChangeShortcut(KeyShortcut shortcut)
    {
        if (_context is null)
            return false;
        if (!shortcut.IsBindable)
        {
            ShortcutError = "Shortcut unavailable";
            Changed?.Invoke();
            return false;
        }
        if (!TryRegister(shortcut))
        {
            Changed?.Invoke();
            return false;
        }
        Save(Settings with { Shortcut = shortcut });
        Prepare();
        return true;
    }

    /// <summary>Settings › Shortcuts with this shortcut's editor open in its row: it's recorded only there.</summary>
    public void EditShortcut() => _context?.EditShortcut(Descriptor.Name);

    public void RemoveShortcut()
    {
        if (_context is null)
            return;
        _hotkey?.Unregister(); // still listed in Settings › Shortcuts, unset
        ShortcutError = null;
        Save(Settings with { Shortcut = null });
    }

    /// <summary>While Settings records a shortcut, pressing ours doesn't open the wheel.</summary>
    public void CaptureShortcut(bool capture) => _capturing = capture;

    /// <summary>The shortcut in the shell's list, set or not, from start to stop.</summary>
    private IHotkey CreateHotkey() => _createHotkey?.Invoke() ?? (_context ?? throw new InvalidOperationException("Not started")).Shortcuts.Add(Id, Descriptor.Name, new(() => Settings.Shortcut, k => ChangeShortcut(k) ? null : ShortcutError, () => RemoveShortcut()) { Icon = Descriptor.Icon });

    private bool TryRegister(KeyShortcut shortcut)
    {
        try
        {
            _hotkey ??= CreateHotkey();
            ShortcutError = _hotkey.Register(shortcut, OnShortcut) ? null : _hotkey.Error ?? ShortcutService.InUse;
        }
        catch (Exception ex)
        {
            Log.Warn("Radial menu: registering the shortcut failed", ex);
            ShortcutError = "Shortcut unavailable";
        }
        return ShortcutError is null;
    }

    // ---- The wheel at the pointer ----

    void IWheelInputSink.Opened(PixelPoint at, MouseButton button)
    {
        var (_, shown) = _pc?.Pointer() ?? (at, true);
        OpenAt(at, shown, holding: true);
    }

    private void OpenAt(PixelPoint at, bool shown, bool holding)
    {
        if (_context is null || _pc is null || Runner is null || _input is null || _state is not null || _cleaning())
        {
            if (holding)
                _input?.Closed();
            return;
        }
        _screen = _pc.ScreenAt(at);
        // In a game that hides the pointer, it opens in the middle of the screen and follows the mouse from there.
        if (!shown)
            at = new PixelPoint(_screen.Left + _screen.Width / 2, _screen.Top + _screen.Height / 2);
        _windowUnder = _pc.WindowAt(at);
        _commands = Runner.ChippiCommands();
        var commands = _commands;
        _state = new WheelState(new Size(_screen.Width / _screen.Scale, _screen.Height / _screen.Scale), ToWheel(at), Settings.Slices, holding,
            slice => IsMissing(slice, commands));
        if (holding)
        {
            if (!shown)
                _input.UseVirtualPointer(at);
        }
        else
            _input.OpenedByShortcut(!shown, at);
        _window ??= new WheelWindow(ChippiColor);
        _shownDepth = 0;
        ShowTop();
        _window.View.Pet.Mood = _keepAwake() ? PetMood.WideAwake : PetMood.Idle;
        _window.View.Pet.Look(null);
        _window.Present(_screen, _state.Levels, CrumbGlyph);
        _shownDepth = _state.Levels.Count;
    }

    private Point ToWheel(PixelPoint at) => new((at.X - _screen.Left) / _screen.Scale, (at.Y - _screen.Top) / _screen.Scale);

    void IWheelInputSink.Moved(PixelPoint at)
    {
        if (_state is not null)
            Apply(_state.Move(ToWheel(at)));
    }

    void IWheelInputSink.Released(PixelPoint at)
    {
        if (_state is not null)
            Apply(_state.Release(ToWheel(at)));
    }

    void IWheelInputSink.Clicked(PixelPoint at)
    {
        if (_state is not null)
            Apply(_state.Click(ToWheel(at)));
    }

    public void Dismissed()
    {
        if (_state is null || _window is null)
            return;
        Close(WheelWindow.FadeOnClose);
    }

    private void Apply(WheelOutcome outcome)
    {
        if (_state is null || _window is null)
            return;
        switch (outcome)
        {
            case WheelOutcome.Changed:
                if (_state.Levels.Count != _shownDepth)
                {
                    ShowTop();
                    _window.Rearrange(_state.Levels, CrumbGlyph);
                    _shownDepth = _state.Levels.Count;
                }
                else
                    Point();
                break;
            case WheelOutcome.Run:
                var slice = _state.PickedSlice!;
                Point();
                _window.View.Pet.Grin(GrinHold);
                nint under = _windowUnder;
                Close(WheelWindow.FadeOnPick);
                // The action runs as the fade starts, so nothing waits on it.
                RunAsync(slice, under);
                break;
            case WheelOutcome.Close:
                Close(WheelWindow.FadeOnClose);
                break;
        }
    }

    private void Close(TimeSpan fade)
    {
        var window = _window!;
        Finish();
        window.FadeOut(fade);
    }

    private void Finish()
    {
        _state = null;
        _input?.Closed();
    }

    /// <summary>The top wheel's slots, its pointed slice, the hub's line and where Chippi looks.</summary>
    private void ShowTop()
    {
        var top = _state!.Top;
        var slots = top.Items.Select(item => item is null
            ? new WheelSlot(LucideIcons.ArrowLeft, "Back") { IsBack = true }
            : new WheelSlot(SliceCatalog.GlyphOf(item, CommandIcon), WheelState.NameOf(item))
            {
                Image = IconOf(item),
                IsSubmenu = item.Kind == SliceKind.Submenu,
                IsMissing = _state.IsMissing(item),
            }).ToList();
        _window!.View.Show(slots, top.First);
        Point();
    }

    private void Point()
    {
        var state = _state!;
        var view = _window!.View;
        view.SetLooks(state.Picked ?? state.Pointed, state.Picked is null ? SlotLook.Pointed : SlotLook.Picked);
        var (text, tone) = state.Hub;
        view.SetHub(text, tone);
        view.Pet.Look(state.Look);
    }

    private string? CommandIcon(string key) => _commands.FirstOrDefault(c => c.Key == key)?.Icon;

    private Geometry CrumbGlyph(WheelLevel level) => level.Submenu is { } submenu ? SliceCatalog.GlyphOf(submenu, CommandIcon) : LucideIcons.LayoutGrid;

    private async void RunAsync(Slice slice, nint window)
    {
        if (Runner is not { } runner)
            return;
        try { await runner.RunAsync(slice.ToAction(), window); }
        catch (ActionFailure failure) { Report(slice, failure.Message); }
        catch (Exception ex)
        {
            Log.Warn($"Radial menu: running {slice.Kind} failed", ex);
            Report(slice, "Error");
        }
    }

    /// <summary>"… failed" on the bar for the island notice time, over a song: the wheel has gone by then.</summary>
    private void Report(Slice slice, string reason)
    {
        if (_context is not { } context)
            return;
        context.Island.Publish(new IslandActivity(ErrorKey, IslandActivityKind.Status, $"{WheelState.NameOf(slice)} failed", reason)
        {
            NeedsAttention = true,
            IsFeedback = true,
        });
    }
}
