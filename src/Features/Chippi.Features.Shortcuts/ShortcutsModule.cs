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
    public const string RadialText = "That button already opens the Radial menu. Pick another one, or free it there first.";
    public const string ButtonTakenText = "That button or direction is already on the list.";
    public const string UnavailableText = "Unavailable";

    private sealed class Slot(ShortcutBinding binding)
    {
        public ShortcutBinding Binding = binding;
        public IHotkey? Hotkey;
        public BindingStatus Status = new(BindingState.Active);
    }

    private readonly object _gate = new();
    private readonly Func<IActionHost?> _actions;
    private readonly Func<MouseTrigger, bool> _radialHolds;
    private readonly List<Slot> _slots = [];
    private ModuleContext? _context;
    private SynchronizationContext? _ui;
    private MouseTriggerFilter? _mouse;
    private IInputRegistration? _mouseRegistration;
    private Action? _pauseListener;

    /// <param name="actions">The shared action runner; null while there is none (every binding then shows Unavailable).</param>
    /// <param name="radialHolds">Whether the Radial menu opens from a mouse button now, read from its settings. A memory read: the hub calls it.</param>
    public ShortcutsModule(Func<IActionHost?>? actions = null, Func<MouseTrigger, bool>? radialHolds = null)
    {
        _actions = actions ?? (() => null);
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

    private string? Check(ShortcutBinding binding, string? except)
    {
        if (binding.Door == BindingDoor.Key)
        {
            if (binding.Keys is not { IsBindable: true })
                return "Pick a shortcut with Ctrl, Alt or the Windows key.";
        }
        else if (!Enum.IsDefined(binding.Button))
            return "Pick a button.";
        if (Settings.Bindings.FirstOrDefault(b => b.Id != except && b.SameDoor(binding)) is { } other)
            return binding.Door == BindingDoor.Key ? $"{other.Label} already uses this." : ButtonTakenText;
        if (binding.Door == BindingDoor.Mouse && binding.Button <= MouseTrigger.Forward && _radialHolds(binding.Button))
            return RadialText;
        return null;
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
        Bindings = [.. (s.Bindings ?? []).Where(b => b is not null)
            .Where(b => b.Door == BindingDoor.Key ? b.Keys is not null : Enum.IsDefined(b.Button))
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
        var host = _actions();
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
                continue;
            if (slot.Hotkey.Error is { } error && error != ShortcutService.InUse)
                slot.Status = new BindingStatus(BindingState.UsedTwice, error); // another Chippi feature: never taken from it
            else
                slot.Status = new BindingStatus(BindingState.InUseByApp, "In use by another app");
        }
        ApplyMouse(host);
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
                            RunBinding(slot.Binding.Id, _actions(), WindowTarget.UnderPointer);
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
        _ = Task.Run(async () =>
        {
            try { await host.RunAsync(binding.Action, target).ConfigureAwait(false); }
            catch (Exception ex)
            {
                string reason = ex is ActionFailure ? ex.Message : "Failed";
                if (ex is not ActionFailure)
                    Log.Warn($"Shortcuts: {binding.Label} failed", ex);
                Post(() => ActionFailed?.Invoke(binding, reason));
            }
        });
    }
}
