// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Input;

/// <summary>
/// The one place Chippi hears the keyboard and mouse (docs/phase-6/architecture.md §2). Owned by the shell: one
/// low-level keyboard hook and one mouse hook on a thread of their own, each in only while a filter needs it. Every feature
/// that needs raw input adds a filter; the hub calls them in <see cref="InputOrder"/>, on its thread.
/// </summary>
public interface IInputHub
{
    /// <summary>
    /// Adds <paramref name="filter"/> and puts in the hooks its <see cref="IInputFilter.Devices"/> need. Throws when Windows
    /// refuses a hook. Dispose the registration to take the filter out; the hooks come off once no filter needs them and
    /// every key and button a filter kept is up.
    /// </summary>
    IInputRegistration Add(IInputFilter filter);

    /// <summary>
    /// While true, filters that pause (<see cref="IInputFilter.Pauses"/>) hear nothing new: the window in front counts as
    /// fullscreen and Always on is off (<see cref="PauseRules"/>). The ups of keys and buttons they kept before still come to
    /// them. A pause beginning lets go of every modifier Chippi holds down, and a hook no unpaused filter needs comes off.
    /// </summary>
    bool Paused { get; }

    /// <summary>
    /// The window in front is a fullscreen app or game, or an app on Pause in these apps, whatever Always on says: the Radial
    /// menu stays closed then while its Open over fullscreen apps is off.
    /// </summary>
    bool FullscreenInFront { get; }

    /// <summary>
    /// <see cref="Paused"/> or <see cref="FullscreenInFront"/> changed. Raised on the hub's thread: hand off to the UI
    /// thread before doing anything slow. Features outside the hooks (Focus follows mouse) follow the pause from here.
    /// </summary>
    event Action? PauseChanged;
}

/// <summary>A filter in the hub. Dispose to take it out.</summary>
public interface IInputRegistration : IDisposable
{
    /// <summary>The filter's <see cref="IInputFilter.Devices"/> changed: the hub puts in or takes off hooks to match.</summary>
    void Refresh();

    /// <summary>
    /// Sends <paramref name="modifiers"/> down, marked as Chippi's, and holds them for the key <paramref name="heldBy"/>.
    /// The hub sends their ups, as one batch, when that key comes up, the filter goes, the hub stops, the hook is found
    /// gone, the session locks or the desktop switches, a pause begins, or no key event arrives for 5 seconds.
    /// </summary>
    void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers);

    /// <summary>
    /// Sends one unassigned key (VK 0xE8) down and up, so the Alt or Win up that follows a chord the filter kept opens no
    /// menu bar and no Start.
    /// </summary>
    void Mask();
}

[Flags]
public enum InputDevices
{
    None = 0,
    Keyboard = 1,
    Mouse = 2,
    Both = Keyboard | Mouse,
}

/// <summary>Where a filter sits in the hub (architecture §2). Lower goes first; filters at the same place go in the order added.</summary>
public enum InputOrder
{
    CleaningLock = 10,
    Debounce = 20,
    CloseProtection = 30,

    /// <summary>Shortcuts' hook triggers and the Radial menu's gesture, which share buttons.</summary>
    Triggers = 40,
    SmoothScrolling = 50,
    Snippets = 60,
    AltTab = 70,

    /// <summary>Settings recording a shortcut: it sees what every filter above passed.</summary>
    Recorder = 80,
}

/// <summary>
/// One feature's say over raw input. Called on the hub's thread, one event at a time: answer from memory in microseconds
/// (no I/O, no locks the UI thread holds for long, no COM, no process lookups) and act after returning, on the UI thread or a
/// worker. Input Chippi sends itself never reaches a filter.
/// </summary>
public interface IInputFilter
{
    InputOrder Order { get; }

    /// <summary>The hooks this filter needs now. Read by the hub on <see cref="IInputHub.Add"/> and each <see cref="IInputRegistration.Refresh"/>.</summary>
    InputDevices Devices { get; }

    /// <summary>Hears nothing new while <see cref="IInputHub.Paused"/>.</summary>
    bool Pauses { get; }

    /// <summary>
    /// A key went down or up. True keeps it from the apps and Windows. Keeping a down keeps its up: that up comes only to
    /// this filter, whatever it answers. An up whose down nobody kept always passes on.
    /// </summary>
    bool Key(in KeyInput key);

    /// <summary>A mouse move, button or wheel. True keeps it; a kept button down keeps its up, as for keys.</summary>
    bool Mouse(in MouseInput mouse);
}

/// <param name="Vk">The virtual-key code.</param>
/// <param name="Down">Going down (or repeating); false for coming up.</param>
/// <param name="WasDown">For a down: Windows' own state already had it down (a repeat, or held from before the hook).</param>
/// <param name="Injected">Another program sent it (Remote Desktop, a remapper). It still counts as a hand's.</param>
/// <param name="Timestamp">The hub's clock (<see cref="System.Diagnostics.Stopwatch"/> ticks) as the hook was called.</param>
public readonly record struct KeyInput(uint Vk, uint Scan, bool Down, bool WasDown, bool Injected, bool Extended, long Timestamp);

public enum MouseKind { Move, Down, Up, Wheel, HorizontalWheel }

public enum MouseButton { None, Left, Right, Middle, Back, Forward }

/// <param name="X">The pointer, in physical pixels.</param>
/// <param name="Delta">For a wheel: its delta (120 a notch).</param>
/// <param name="Timestamp">The hub's clock (<see cref="System.Diagnostics.Stopwatch"/> ticks) as the hook was called.</param>
public readonly record struct MouseInput(MouseKind Kind, MouseButton Button, int X, int Y, int Delta, bool Injected, long Timestamp);
