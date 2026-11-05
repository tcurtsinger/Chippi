// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Debounce;

/// <summary>A key with a window of its own, in milliseconds.</summary>
public sealed record KeyWindow(uint Vk, int Ms);

/// <summary>Both switches start off, even right after install (task 226).</summary>
public sealed record DebounceSettings
{
    public const int ClickDefaultMs = 25, ClickMinMs = 5, ClickMaxMs = 100;
    public const int KeyDefaultMs = 5, KeyMinMs = 0, KeyMaxMs = 200;

    public bool ClickEnabled { get; init; }

    public int ClickMs { get; init; } = ClickDefaultMs;

    public bool KeyEnabled { get; init; }

    public int KeyMs { get; init; } = KeyDefaultMs;

    public KeyWindow[] Keys { get; init; } = [];
}

/// <summary>
/// Click and key debounce: two filters in the input hub, each in only while its switch is on. The hub pauses them in
/// a fullscreen app (they say <c>Pauses</c>) and keeps a dropped press's release. The counts are memory only.
/// </summary>
public sealed class DebounceModule : IFeatureModule
{
    public const string Id = "debounce";
    public const string MouseSection = "mouse", KeyboardSection = "keyboard";

    private readonly object _gate = new();
    private readonly bool _includeInjected;
    private ModuleContext? _context;
    private ClickDebounceFilter? _click;
    private KeyDebounceFilter? _key;
    private IInputRegistration? _clickRegistration, _keyRegistration;
    private int _clickBase, _keyBase;

    /// <param name="includeInjected">Only the Sandbox probe: it sends input with <c>SendInput</c>, which Windows marks injected.</param>
    public DebounceModule(bool includeInjected = false) => _includeInjected = includeInjected;

    public DebounceSettings Settings { get; private set; } = new();

    /// <summary>"Chippi cannot watch the mouse right now." while the hook is down, else null.</summary>
    public string? MouseError { get; private set; }

    public string? KeyboardError { get; private set; }

    public event Action? Changed;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Click and key debounce",
        "Ignores the double clicks and key chatter of worn switches", FeatureGroup.Windows)
    { Icon = nameof(LucideIcons.Keyboard) };

    /// <summary>Presses ignored since the switch was turned on.</summary>
    public int ClickIgnored => (_click?.Ignored ?? 0) - _clickBase;

    public int KeyIgnored => (_key?.Ignored ?? 0) - _keyBase;

    /// <summary>The input hub, for the key picker.</summary>
    internal IInputHub? Hub => _context?.Input;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            _context = context;
            Settings = Normalize(context.Settings.Get<DebounceSettings>());
            context.Ui.SetSettingsPage(section => new DebounceRows(this, section == KeyboardSection ? KeyboardSection : MouseSection));
            Apply();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _clickRegistration?.Dispose();
            _keyRegistration?.Dispose();
            _clickRegistration = _keyRegistration = null;
            _click = null;
            _key = null;
            _context = null;
        }
        return Task.CompletedTask;
    }

    public void Update(Func<DebounceSettings, DebounceSettings> change)
    {
        lock (_gate)
        {
            if (_context is null)
                return;
            var next = Normalize(change(Settings));
            _context.Settings.Save(next);
            Settings = next;
            Apply();
        }
        Changed?.Invoke();
    }

    private static DebounceSettings Normalize(DebounceSettings s) => s with
    {
        ClickMs = Math.Clamp(s.ClickMs, DebounceSettings.ClickMinMs, DebounceSettings.ClickMaxMs),
        KeyMs = Math.Clamp(s.KeyMs, DebounceSettings.KeyMinMs, DebounceSettings.KeyMaxMs),
        Keys = [.. (s.Keys ?? []).Where(k => k.Vk is > 0 and < 256)
            .Select(k => k with { Ms = Math.Clamp(k.Ms, DebounceSettings.KeyMinMs, DebounceSettings.KeyMaxMs) })
            .GroupBy(k => k.Vk).Select(g => g.Last())],
    };

    private void Apply()
    {
        var hub = _context?.Input;
        if (hub is null)
            return;
        if (Settings.ClickEnabled)
        {
            if (_clickRegistration is null)
            {
                _click = new ClickDebounceFilter { IncludeInjected = _includeInjected };
                _clickBase = 0;
                MouseError = Try(() => _clickRegistration = hub.Add(_click), "mouse");
                if (MouseError is not null)
                    _click = null;
            }
            if (_click is not null)
                _click.WindowMs = Settings.ClickMs;
        }
        else if (_clickRegistration is not null || _click is not null)
        {
            _clickRegistration?.Dispose();
            _clickRegistration = null;
            _click = null;
            MouseError = null;
        }
        else
            MouseError = null;
        if (Settings.KeyEnabled)
        {
            if (_keyRegistration is null)
            {
                _key = new KeyDebounceFilter { IncludeInjected = _includeInjected };
                _keyBase = 0;
                KeyboardError = Try(() => _keyRegistration = hub.Add(_key), "keyboard");
                if (KeyboardError is not null)
                    _key = null;
            }
            if (_key is not null)
            {
                _key.WindowMs = Settings.KeyMs;
                _key.SetOwn(Settings.Keys.Select(k => (k.Vk, k.Ms)));
            }
        }
        else if (_keyRegistration is not null || _key is not null)
        {
            _keyRegistration?.Dispose();
            _keyRegistration = null;
            _key = null;
            KeyboardError = null;
        }
        else
            KeyboardError = null;
    }

    private static string? Try(Action add, string device)
    {
        try
        {
            add();
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Debounce: the {device} hook failed", ex);
            return $"Chippi cannot watch the {device} right now.";
        }
    }
}
