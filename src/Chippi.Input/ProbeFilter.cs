// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using Chippi.Core.Input;

namespace Chippi.Input;

/// <summary>
/// Debug builds only, with <c>CHIPPI_INPUT_PROBE_FILTER</c> set: a filter that pauses, as Phase 6 features do, and keeps F24
/// (which no keyboard has), so the Sandbox's pause check can see the pause before any real feature uses it.
/// </summary>
public sealed class ProbeFilter : IInputFilter
{
    public const uint Key = 0x87; // F24

    public InputOrder Order => InputOrder.Debounce;

    public InputDevices Devices => InputDevices.Keyboard;

    public bool Pauses => true;

    bool IInputFilter.Key(in KeyInput key) => key.Vk == Key;

    public bool Mouse(in MouseInput mouse) => false;
}
#endif
