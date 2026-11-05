// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Shortcuts;

/// <summary>What fires a binding's action: the shared runner (docs/phase-6/architecture.md section 3), from the shell.</summary>
public interface IActionHost
{
    Task RunAsync(ChippiAction action, WindowTarget target);

    /// <summary>The action's target is gone: an app removed, a command whose feature isn't running.</summary>
    bool IsMissing(ChippiAction action);
}

/// <summary>
/// Shortcuts: any action on a key, or a mouse button (the Caps Lock combo comes next). A key shortcut is one of the
/// shortcut service's, so Settings lists it with Chippi's own and Windows is asked for the keys. A mouse button is a filter in the hub, in only while the section's
/// switch is on and a binding uses a button. Both pause in a fullscreen app (keys are freed, so the game gets them).
/// </summary>
public sealed class ShortcutsModule : IFeatureModule
{
    public const string Id = "shortcuts";
    public const string RadialText = "The Radial menu uses this";
    public const string WindowsText = "Windows uses this";
    public const string AnotherAppText = "Another app uses this";
    public const string UnavailableText = "Unavailable";

    private sealed class Slot(ShortcutBinding binding)
    {
        public ShortcutBinding Binding = binding;
        public IHotkey? Hotkey;

        /// <summary>Windows gave Chippi the keys: false while paused, refused or reserved.</summary>
        public bool Held;
        public BindingStatus Status = new(BindingState.Active);
    }

    private readonly object _gate = new();
    private readonly Func<IActionHost?> _actionsOverride;
    private readonly Func<string, string> _searchUrl;
    private IActionHost? _shellHost;
    private readonly Func<MouseTrigger, bool> _radialHolds;
    private readonly List<Slot> _slots = [];
    private ModuleContext? _context;
    private SynchronizationContext? _ui;
    private CapsTriggerFilter? _caps;
    private IInputRegistration? _capsRegistration;
    public string? CapsError { get; private set; }
    private MouseTriggerFilter? _mouse;
    private IInputRegistration? _mouseRegistration;
    private Action? _pauseListener;

    /// <param name="actions">The shared action runner; null while there is none (every binding then shows Unavailable).</param>
    /// <param name="radialHolds">Whether the Radial menu opens from a mouse button now, read from its settings. A memory read: the hub calls it.</param>
    /// <param name="searchUrl">The web search's address for a text (the Command bar's engine), for a Text action that searches.</param>
    public ShortcutsModule(Func<IActionHost?>? actions = null, Func<MouseTrigger, bool>? radialHolds = null, Func<string, string>? searchUrl = null)
    {
        _actionsOverride = actions ?? (() => null);
        _searchUrl = searchUrl ?? (text => "https://www.google.com/search?q=" + Uri.EscapeDataString(text));
        _radialHolds = radialHolds ?? (_ => false);
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Shortcuts", "Binds actions to keys and mouse buttons, with clashes flagged", FeatureGroup.Windows)
    { Icon = "Keyboard", Requires = [Capability.InputHooks], PausesInFullscreen = true };

    public ShortcutsSettings Settings { get; private set; } = new();

    /// <summary>"Watching the mouse failed" while the hook is down, else null.</summary>
    public string? MouseError { get; private set; }

    /// <summary>A binding's action failed ("Snap left failed", "No window"). Raised on the UI thread.</summary>
    public event Action<ShortcutBinding, string>? ActionFailed;

    public event Action? Changed;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            _context = context;
            _ui = SynchronizationContext.Current;
            _shellHost = context.Actions is { } pc
                ? new RunnerHost(new ActionRunner(pc, context.Commands, () => context.Clipboard, _searchUrl, () => context.IslandPasteTarget()?.Window ?? 0))
                : null;
            Settings = Normalize(context.Settings.Get<ShortcutsSettings>());
            _pauseListener = () => Post(() => { lock (_gate) Rebuild(); });
            if (context.Input is { } hub)
                hub.PauseChanged += _pauseListener;
            Rebuild();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_context?.Input is { } hub && _pauseListener is not null)
                hub.PauseChanged -= _pauseListener;
            _pauseListener = null;
            Clear();
            StopCaps();
            _mouseRegistration?.Dispose();
            _mouseRegistration = null;
            _mouse = null;
            _context = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>How a binding stands now, for its row.</summary>
    public BindingStatus StatusOf(string id)
    {
        lock (_gate)
            return _slots.FirstOrDefault(s => s.Binding.Id == id)?.Status ?? new BindingStatus(BindingState.Unavailable, UnavailableText);
    }

    /// <summary>Adds a binding. Refused, with the page's words, when its door is already on the list or the Radial menu holds the button.</summary>
    public BindingResult Add(ShortcutBinding binding)
    {
        lock (_gate)
        {
            if (Check(binding, except: null) is { } error)
                return new BindingResult(error);
            Save(Settings with { Bindings = [.. Settings.Bindings, binding] });
        }
        return new BindingResult(null);
    }

    /// <summary>Changes a binding's door or action, keeping its place in the list.</summary>
    public BindingResult Change(ShortcutBinding binding)
    {
        lock (_gate)
        {
            int at = Settings.Bindings.FindIndex(b => b.Id == binding.Id);
            if (at < 0)
                return new BindingResult("Not found");
            if (Check(binding, except: binding.Id) is { } error)
                return new BindingResult(error);
            var list = Settings.Bindings.ToList();
            list[at] = binding;
            Save(Settings with { Bindings = list });
        }
        return new BindingResult(null);
    }

    public void Remove(string id)
    {
        lock (_gate)
            Save(Settings with { Bindings = [.. Settings.Bindings.Where(b => b.Id != id)] });
    }

    public void SetMouseEnabled(bool on)
    {
        lock (_gate)
            Save(Settings with { MouseEnabled = on });
    }

    public void SetMouseIgnoredApps(IEnumerable<string> apps)
    {
        lock (_gate)
            Save(Settings with { MouseIgnoredApps = [.. apps.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase)] });
    }

    /// <summary>
    /// Why a binding can't be added or changed to this, in the words the editor shows by Save (Travis, 2026-11-05: a shortcut
    /// in use is refused there, not saved with a warning); null when it can.
    /// </summary>
    public void ConfigureCaps(bool enabled, uint heldKey, KeyModifiers modifiers, CapsTap tap, IEnumerable<string> ignoredApps)
    {
        if (heldKey is not (0x14 or 0xA3 or 0xA1 or 0x5C)) throw new ArgumentOutOfRangeException(nameof(heldKey));
        if (((uint)modifiers & ~15u) != 0 || !Enum.IsDefined(tap)) throw new ArgumentOutOfRangeException(nameof(modifiers));
        lock (_gate)
            Save(Settings with { CapsEnabled = enabled, CapsHeldKey = heldKey, CapsModifiers = modifiers, CapsTap = tap,
                CapsIgnoredApps = [.. ignoredApps.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase)] });
    }
    public string? Check(ShortcutBinding binding, string? except)
    {
        if (binding.Door == BindingDoor.Key)
        {
            if (binding.Keys is not { IsBindable: true })
                return "Pick a shortcut with Ctrl, Alt or the Windows key.";
        }
        else if (binding.Door == BindingDoor.Caps)
        {
            if (binding.CapsKey is < 1 or > 254 || binding.CapsKey == Settings.CapsHeldKey || KeyCombo.ModifierOf(binding.CapsKey) != KeyModifiers.None)
                return "Pick a key.";
        }
        else if (!Enum.IsDefined(binding.Button))
            return "Pick a button.";
        if (Settings.Bindings.FirstOrDefault(b => b.Id != except && b.SameDoor(binding)) is { } other)
            return $"{other.Label} already uses this";
        if (binding.Door == BindingDoor.Mouse)
            return binding.Button <= MouseTrigger.Forward && _radialHolds(binding.Button) ? RadialText : null;
        if (binding.Door == BindingDoor.Caps) return null;
        var keys = binding.Keys!;
        if (WindowsKeys.IsReserved(keys))
            return WindowsText;
        // Its own keys, unchanged, and held by Chippi right now: nothing to ask. A row dimmed because another app or
        // Windows took the keys, or paused in a game, isn't held, so recording them again runs the check and says who has them.
        if (except is not null && _slots.FirstOrDefault(s => s.Binding.Id == except) is { Binding: { Door: BindingDoor.Key } own, Held: true }
            && own.Keys == keys)
            return null;
        // Windows says only whether the keys were free: ask it, and let go at once.
        if (_context is null)
            return null;
        using var probe = _context.Shortcuts.Add(Id, "Shortcut check");
        if (probe.Register(keys, () => { }))
            return null;
        return probe.Error is { } error && error != ShortcutService.InUse && error.StartsWith("Used by ", StringComparison.Ordinal)
            ? error["Used by ".Length..] + " already uses this"
            : AnotherAppText;
    }

    /// <summary>A test's or the probe's host, else the shell's runner; null while there is none.</summary>
    private IActionHost? Host() => _actionsOverride() ?? _shellHost;

    private sealed class RunnerHost(ActionRunner runner) : IActionHost
    {
        public Task RunAsync(ChippiAction action, WindowTarget target) => runner.RunAsync(action, target);

        public bool IsMissing(ChippiAction action) => runner.IsMissing(action);
    }

    private void Save(ShortcutsSettings next)
    {
        if (_context is null)
            return;
        next = Normalize(next);
        _context.Settings.Save(next);
        Settings = next;
        Rebuild();
        Post(() => Changed?.Invoke());
    }

    private static ShortcutsSettings Normalize(ShortcutsSettings s) => s with
    {
        MouseIgnoredApps = s.MouseIgnoredApps ?? [],
        CapsIgnoredApps = s.CapsIgnoredApps ?? [],
        CapsHeldKey = s.CapsHeldKey is 0x14 or 0xA3 or 0xA1 or 0x5C ? s.CapsHeldKey : 0x14,
        CapsModifiers = s.CapsModifiers & (KeyModifiers)15,
        Bindings = [.. (s.Bindings ?? []).Where(b => b is not null)
            .Where(b => b.Door == BindingDoor.Key ? b.Keys is not null : b.Door == BindingDoor.Caps ? b.CapsKey is > 0 and < 255 : b.Door == BindingDoor.Mouse && Enum.IsDefined(b.Button))
            .GroupBy(b => b.Id).Select(g => g.First())],
    };

    /// <summary>Tests run what would go to the UI thread right where it is.</summary>
    internal bool InlinePosts { get; init; }

    private void Post(Action action)
    {
        if (_ui is null || InlinePosts)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    /// <summary>Frees every key and drops every slot's hotkey; they stay on the list.</summary>
    private void Clear()
    {
        foreach (var slot in _slots)
            slot.Hotkey?.Dispose();
        _slots.Clear();
    }

    private void Rebuild()
    {
        if (_context is null)
            return;
        Clear();
        bool paused = _context.Input?.Paused == true;
        var host = Host();
        foreach (var binding in Settings.Bindings)
        {
            var slot = new Slot(binding);
            _slots.Add(slot);
            if (host is null || host.IsMissing(binding.Action))
            {
                slot.Status = new BindingStatus(BindingState.Unavailable, UnavailableText);
                continue;
            }
            if (binding.Door == BindingDoor.Mouse)
            {
                if (!Settings.MouseEnabled)
                    slot.Status = new BindingStatus(BindingState.Inactive);
                else if (binding.Button <= MouseTrigger.Forward && _radialHolds(binding.Button))
                    slot.Status = new BindingStatus(BindingState.HeldByRadial, "This button opens the Radial menu now, so the shortcut waits.");
                continue;
            }
            if (binding.Door == BindingDoor.Caps)
            {
                if (!Settings.CapsEnabled) slot.Status = new BindingStatus(BindingState.Inactive);
                continue;
            }
            var keys = binding.Keys!;
            if (!keys.IsBindable)
            {
                slot.Status = new BindingStatus(BindingState.Unavailable, UnavailableText); // a hand-edited settings file
                continue;
            }
            if (WindowsKeys.IsReserved(keys))
            {
                slot.Status = new BindingStatus(BindingState.InUseByWindows, WindowsKeys.Text);
                continue;
            }
            // Always on the service's list, so Settings shows it; a pause frees the keys but keeps the row.
            slot.Hotkey = _context.Shortcuts.Add(Id, binding.Label);
            if (paused)
                continue;
            if (slot.Hotkey.Register(keys, () => RunBinding(binding.Id, host, WindowTarget.InFront)))
            {
                slot.Held = true;
                continue;
            }
            if (slot.Hotkey.Error is { } error && error != ShortcutService.InUse)
                slot.Status = new BindingStatus(BindingState.UsedTwice, error); // another Chippi feature: never taken from it
            else
                slot.Status = new BindingStatus(BindingState.InUseByApp, "In use by another app");
        }
        ApplyMouse(host);
        ApplyCaps(host);
    }

    private void StopCaps()
    {
        _caps?.Set(0, KeyModifiers.None, [], []);
        _capsRegistration?.Dispose();
        _capsRegistration = null;
        _caps = null;
    }

    private void ApplyCaps(IActionHost? host)
    {
        StopCaps(); // replacing the registration releases every modifier through the hub
        CapsError = null;
        var hub = _context?.Input;
        if (!Settings.CapsEnabled || hub is null || host is null || hub.Paused) return;
        var filter = new CapsTriggerFilter
        {
            ForegroundApp = () => hub.ForegroundApp,
            Blocked = () => hub.ForegroundElevated,
            Fired = key => Post(() =>
            {
                lock (_gate)
                    if (_slots.FirstOrDefault(s => s.Binding.Door == BindingDoor.Caps && s.Binding.CapsKey == key && s.Status.Acts) is { } slot)
                        RunBinding(slot.Binding.Id, Host(), WindowTarget.InFront);
            }),
            Tapped = () => Post(() =>
            {
                string? keys = Settings.CapsTap switch { CapsTap.Capitals => "Caps Lock", CapsTap.Escape => "Esc", CapsTap.InputLanguage => "Win+Space", _ => null };
                if (keys is not null)
                    _ = RunTapAsync(host, keys);
            }),
        };
        try
        {
            var registration = hub.Add(filter);
            filter.Hold = (key, modifiers) => registration.HoldModifiers(key, modifiers);
            filter.Set(Settings.CapsHeldKey, Settings.CapsModifiers,
                _slots.Where(s => s.Binding.Door == BindingDoor.Caps && s.Status.Acts).Select(s => s.Binding.CapsKey), Settings.CapsIgnoredApps);
            _capsRegistration = registration;
            _caps = filter;
        }
        catch (Exception ex)
        {
            CapsError = "Watching the keyboard failed";
            Log.Warn("Shortcuts: the keyboard hook failed", ex);
        }
    }

    private static async Task RunTapAsync(IActionHost host, string keys)
    {
        try { await host.RunAsync(new ChippiAction { Kind = ActionKind.Keys, Value = keys }, WindowTarget.InFront); }
        catch (Exception ex) { Log.Warn("Shortcuts: tap failed", ex); }
    }
    private void ApplyMouse(IActionHost? host)
    {
        var hub = _context?.Input;
        var active = _slots.Where(s => s.Binding.Door == BindingDoor.Mouse && s.Status.Acts).Select(s => s.Binding.Button).ToList();
        if (active.Count == 0 || hub is null || host is null)
        {
            _mouseRegistration?.Dispose();
            _mouseRegistration = null;
            _mouse = null;
            MouseError = null;
            return;
        }
        if (_mouseRegistration is null)
        {
            var filter = new MouseTriggerFilter
            {
                HeldByRadial = t => t <= MouseTrigger.Forward && _radialHolds(t),
                ForegroundApp = () => hub.ForegroundApp,
                Fired = trigger => Post(() =>
                {
                    lock (_gate)
                        if (_slots.FirstOrDefault(s => s.Binding.Door == BindingDoor.Mouse && s.Binding.Button == trigger && s.Status.Acts) is { } slot)
                            RunBinding(slot.Binding.Id, Host(), WindowTarget.UnderPointer);
                }),
            };
            try
            {
                _mouseRegistration = hub.Add(filter);
                _mouse = filter;
                MouseError = null;
            }
            catch (Exception ex)
            {
                Log.Warn("Shortcuts: the mouse hook failed", ex);
                MouseError = "Watching the mouse failed";
                return;
            }
        }
        _mouse!.Set(active, Settings.MouseIgnoredApps);
    }

    private void RunBinding(string id, IActionHost? host, WindowTarget target)
    {
        var slot = _slots.FirstOrDefault(s => s.Binding.Id == id);
        if (slot is null || host is null)
            return;
        var binding = slot.Binding;
        _ = RunAsync();

        // Started where the press landed, the UI thread: Chippi's commands and the PC's window calls are UI-thread APIs.
        async Task RunAsync()
        {
            try { await host.RunAsync(binding.Action, target); }
            catch (Exception ex)
            {
                string reason = ex is ActionFailure ? ex.Message : "Failed";
                if (ex is not ActionFailure)
                    Log.Warn($"Shortcuts: {binding.Label} failed", ex);
                Post(() => ActionFailed?.Invoke(binding, reason));
            }
        }
    }
}
