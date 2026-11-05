// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Actions;
using Chippi.Core.Input;

namespace Chippi.Features.Shortcuts;

/// <summary>The Caps door's press state belongs to the hub thread.</summary>
internal sealed class CapsTriggerFilter : IInputFilter
{
    private sealed record Options(uint HeldKey, ushort[] Modifiers, HashSet<uint> Bound, HashSet<string> Ignored);
    private Options _options = new(0, [], [], []);
    private uint _held;
    private long _since;
    private bool _used;
    private readonly HashSet<uint> _kept = [];
    public InputOrder Order => InputOrder.Triggers;
    public InputDevices Devices => InputDevices.Keyboard;
    public bool Pauses => true;
    public Action<uint> Fired { get; init; } = _ => { };
    public Action Tapped { get; init; } = () => { };
    public Action<uint, ushort[]> Hold { get; set; } = (_, _) => { };
    public Func<string?> ForegroundApp { get; init; } = () => null;
    public Func<bool> Blocked { get; init; } = () => false;

    public void Set(uint heldKey, KeyModifiers modifiers, IEnumerable<uint> bound, IEnumerable<string> ignored) =>
        Volatile.Write(ref _options, new Options(heldKey,
            [.. new KeyCombo(modifiers, 0).ModifierKeys.Select(k => (ushort)k)], [.. bound],
            new HashSet<string>(ignored.Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase)));

    public bool Key(in KeyInput key)
    {
        // Disabling still keeps repeats and ups of keys whose down was swallowed.
        if (_kept.Contains(key.Vk))
        {
            if (!key.Down) _kept.Remove(key.Vk);
            return true;
        }
        var options = Volatile.Read(ref _options);
        if (_held != 0 && key.Vk == _held)
        {
            if (!key.Down)
            {
                _held = 0;
                if (!_used && options.HeldKey != 0 && !Blocked() && key.Timestamp - _since <= Stopwatch.Frequency / 2)
                    Tapped();
            }
            return true;
        }
        bool ignored = ForegroundApp() is { } app && options.Ignored.Contains(Path.GetFileName(app));
        if (options.HeldKey == 0 || Blocked() || ignored) return false;
        if (key.Vk == options.HeldKey)
        {
            if (!key.Down || key.WasDown) return false;
            _held = key.Vk;
            _since = key.Timestamp;
            _used = false;
            return true;
        }
        if (_held == 0 || !key.Down) return false;
        _used = true;
        if (key.WasDown) return false;
        if (options.Bound.Contains(key.Vk))
        {
            _kept.Add(key.Vk);
            Fired(key.Vk);
            return true;
        }
        if (KeyCombo.ModifierOf(key.Vk) == KeyModifiers.None) Hold(key.Vk, options.Modifiers);
        return false;
    }
    public bool Mouse(in MouseInput mouse) => false;
    public void InputReset(InputDevices devices)
    {
        if ((devices & InputDevices.Keyboard) == 0) return;
        _held = 0;
        _kept.Clear();
    }
}
