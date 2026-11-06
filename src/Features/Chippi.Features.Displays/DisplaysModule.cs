// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using Chippi.Core.Diagnostics;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.Displays.Views;
using Chippi.UI.Controls;
using Chippi.UI.Displays;

namespace Chippi.Features.Displays;

/// <summary>
/// Displays and brightness (docs/phase-7/displays.md): reads and sets each monitor's brightness and contrast, the brightness
/// keys, Settings › Displays and the Command bar's rows. The Displays tile and panel on the island are Lumen's and join it
/// with the Displays design.
/// </summary>
public sealed class DisplaysModule : IFeatureModule, IDisplaysPageModel
{
    public const string Id = "displays";

    private readonly Func<IMonitorBus> _createBus;
    private readonly Func<IReadOnlyList<DisplayInfo>> _displays;
    private readonly Func<nint?> _underPointer;
    private readonly Func<long>? _nowMs;
    private readonly Action<TimeSpan>? _sleep;
    private readonly Func<string, IHotkey>? _createHotkey;
    private readonly object _gate = new();
    private readonly Dictionary<string, IHotkey> _hotkeys = [];
    private readonly Dictionary<string, string?> _errors = [];
    private ModuleContext? _context;
    private DisplayBrightness? _engine;
    private Action? _changedHandler;

    /// <param name="createBus">The monitors' bus; the app passes the real one (DDC/CI and WMI), tests a fake.</param>
    public DisplaysModule(Func<IMonitorBus>? createBus = null, Func<IReadOnlyList<DisplayInfo>>? displays = null, Func<nint?>? underPointer = null,
        Func<long>? nowMs = null, Action<TimeSpan>? sleep = null, Func<string, IHotkey>? createHotkey = null)
    {
        _createBus = createBus ?? (() => new WindowsMonitorBus());
        _displays = displays ?? DisplayCatalog.Connected;
        _underPointer = underPointer ?? WindowsMonitorBus.UnderPointer;
        _nowMs = nowMs;
        _sleep = sleep;
        _createHotkey = createHotkey;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Displays and brightness", "Brightness for every monitor, external ones included", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Sun),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
    };

    public DisplaysSettings Settings { get; private set; } = new();

    public string? SettingsError { get; private set; }

    public event Action? Changed;

    public IReadOnlyList<DisplayStatus> Displays => _engine?.Statuses ?? [];

    /// <summary>The tile's tooltip ("OMEN 34c 54% · Samsung 60%").</summary>
    public string Tooltip => _engine?.Tooltip ?? "";

    internal DisplayBrightness? Engine => _engine;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Settings = Validated(context.Settings.Get<DisplaysSettings>());
        var engine = new DisplayBrightness(_createBus(), _displays, _underPointer, _nowMs, _sleep);
        engine.Changed += OnEngineChanged;
        engine.ControlFailed += OnControlFailed;
        lock (_gate)
        {
            _context = context;
            _engine = engine;
        }
        context.Ui.SetSettingsPage(_ => new DisplaysSettingsPage(this));
        context.Ui.AddCommands(Commands);
        context.Ui.AddCommandSearch(Values);
        AddSlot(context, DisplaysShortcuts.BrightnessUp, () => Settings.BrightnessUp, OnBrightness(+1));
        AddSlot(context, DisplaysShortcuts.BrightnessDown, () => Settings.BrightnessDown, OnBrightness(-1));
        AddSlot(context, DisplaysShortcuts.OpenDisplays, () => Settings.OpenDisplays, () => context.TryOpenIslandPage(Id));
        // The panel opens with the last levels read, and these land under it; never on a timer.
        _changedHandler = () => engine.Refresh(reopen: true);
        DisplayChanges.Changed += _changedHandler;
        engine.Refresh();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        DisplayBrightness? engine;
        List<IHotkey> hotkeys;
        lock (_gate)
        {
            engine = _engine;
            _engine = null;
            _context = null;
            hotkeys = [.. _hotkeys.Values];
            _hotkeys.Clear();
        }
        if (_changedHandler is not null) DisplayChanges.Changed -= _changedHandler;
        _changedHandler = null;
        foreach (var hotkey in hotkeys) hotkey.Dispose();
        if (engine is not null)
        {
            engine.Changed -= OnEngineChanged;
            engine.ControlFailed -= OnControlFailed;
            // Waits for the read or write in flight on each monitor (at most one) and drops the queued ones.
            engine.Dispose();
        }
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task UninstallAsync(CancellationToken ct) => Task.CompletedTask;

    private static DisplaysSettings Validated(DisplaysSettings s) =>
        s.Step is 5 or 10 or 20 ? s : s with { Step = 10 };

    // Shortcuts. A slot is listed from start to stop, set or not; Settings › Shortcuts is the only place it's recorded.

    private void AddSlot(ModuleContext context, string name, Func<KeyShortcut?> saved, Action pressed)
    {
        var hotkey = _createHotkey?.Invoke(name) ?? context.Shortcuts.Add(Id, name,
            new(saved, keys => Change(name, keys, pressed), () => Remove(name)) { Icon = Descriptor.Icon });
        lock (_gate) _hotkeys[name] = hotkey;
        if (saved() is { IsBindable: true } keys)
            _errors[name] = hotkey.Register(keys, pressed) ? null : hotkey.Error ?? ShortcutService.InUse;
    }

    private string? Change(string name, KeyShortcut keys, Action pressed)
    {
        IHotkey? hotkey;
        lock (_gate) _hotkeys.TryGetValue(name, out hotkey);
        if (hotkey is null || !keys.IsBindable) return "Shortcut unavailable";
        if (!hotkey.Register(keys, pressed)) return _errors[name] = hotkey.Error ?? ShortcutService.InUse;
        _errors[name] = null;
        return Save(With(name, keys)) ? null : "Save failed";
    }

    private void Remove(string name)
    {
        IHotkey? hotkey;
        lock (_gate) _hotkeys.TryGetValue(name, out hotkey);
        hotkey?.Unregister();
        _errors[name] = null;
        Save(With(name, null));
    }

    private DisplaysSettings With(string name, KeyShortcut? keys) => name switch
    {
        DisplaysShortcuts.BrightnessUp => Settings with { BrightnessUp = keys },
        DisplaysShortcuts.BrightnessDown => Settings with { BrightnessDown = keys },
        _ => Settings with { OpenDisplays = keys },
    };

    // The hotkey handler runs on the UI thread: it hands the press to the monitors' workers and returns.
    private Action OnBrightness(int direction) => () => _engine?.Step(direction * Settings.Step, Settings.ActOn);

    public (KeyShortcut? Keys, string? Error) Shortcut(string name) =>
        (name switch
        {
            DisplaysShortcuts.BrightnessUp => Settings.BrightnessUp,
            DisplaysShortcuts.BrightnessDown => Settings.BrightnessDown,
            _ => Settings.OpenDisplays,
        }, _errors.GetValueOrDefault(name));

    public void EditShortcut(string name) => _context?.EditShortcut(name);

    // Settings.

    private bool Save(DisplaysSettings next)
    {
        var context = _context;
        if (context is null) return false;
        try
        {
            context.Settings.Save(next);
            Settings = next;
            SettingsError = null;
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Displays: save failed", ex);
            SettingsError = null;
            Changed?.Invoke();
            return false;
        }
    }

    public bool SetActOn(BrightnessTarget target) => Save(Settings with { ActOn = target });

    public bool SetStep(int percent) => percent is 5 or 10 or 20 && Save(Settings with { Step = percent });

    public void TryAgain(string displayId) => _engine?.TryAgain(displayId);

    private void OnEngineChanged() => Changed?.Invoke();

    private void OnControlFailed(string displayId, bool fromKey)
    {
        // Only a key press that couldn't change the display shows on the bar; a slider shows it on its card.
        if (!fromKey) return;
        _context?.Island.Publish(new IslandActivity("brightness-failed", IslandActivityKind.Status, "Brightness control failed", "Displays")
        {
            IsFeedback = true,
            NeedsAttention = true,
            Icon = nameof(LucideIcons.Sun),
        });
    }

    // The Command bar and the Radial menu: Brightness up and down, and values typed in.

    private IReadOnlyList<ChippiCommand> Commands() =>
    [
        new("displays/brightness-up", "Brightness up", nameof(LucideIcons.Sun), () => Run(Settings.Step))
            { Words = "brighter display monitor screen increase", Detail = "Displays" },
        new("displays/brightness-down", "Brightness down", nameof(LucideIcons.Sun), () => Run(-Settings.Step))
            { Words = "dimmer darker display monitor screen decrease", Detail = "Displays" },
    ];

    private Task Run(int delta)
    {
        _engine?.Step(delta, Settings.ActOn);
        return Task.CompletedTask;
    }

    /// <summary>"brightness 40" → Brightness to 40%, then a row per display; "brightness" alone gives each display's level.</summary>
    private IReadOnlyList<ChippiCommand> Values(string text)
    {
        var engine = _engine;
        if (engine is null) return [];
        var shown = engine.Statuses.Where(s => !s.Failed && s.Brightness is not null).ToList();
        if (DisplayBrightness.TryParseLevel(text, out int percent))
        {
            var rows = new List<ChippiCommand>
            {
                new("displays/brightness-to", $"Brightness to {percent}%", nameof(LucideIcons.Sun), () => SetTarget(percent))
                    { Kind = ChippiCommandKind.Value, Level = percent / 100.0, Detail = "Displays" },
            };
            rows.AddRange(shown.Select(s => new ChippiCommand($"displays/brightness-to/{s.Id}", $"{s.Name} to {percent}%", nameof(LucideIcons.Sun),
                () =>
                {
                    engine.Set(s.Id, Vcp.Brightness, percent);
                    return Task.CompletedTask;
                }) { Kind = ChippiCommandKind.Value, Level = percent / 100.0, Detail = "Displays" }));
            return rows;
        }
        if (!text.Trim().Equals("brightness", StringComparison.OrdinalIgnoreCase)) return [];
        return [.. shown.Select(s => new ChippiCommand($"displays/level/{s.Id}", $"{s.Name} · {s.Brightness!.Value.ToString(CultureInfo.InvariantCulture)}%",
            nameof(LucideIcons.Sun), () => Task.CompletedTask) { Kind = ChippiCommandKind.Value, Level = s.Brightness!.Value / 100.0, Detail = "Displays" })];
    }

    // Acts on the keys' target: the display under the pointer, or every display.
    private Task SetTarget(int percent)
    {
        var engine = _engine;
        if (engine is null) return Task.CompletedTask;
        var statuses = engine.Statuses.Where(s => !s.Failed && s.Brightness is not null).ToList();
        if (Settings.ActOn == BrightnessTarget.AllDisplays)
            foreach (var s in statuses) engine.Set(s.Id, Vcp.Brightness, percent);
        else if (engine.UnderPointerId(statuses) is { } id)
            engine.Set(id, Vcp.Brightness, percent);
        return Task.CompletedTask;
    }
}
