// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;
using Chippi.Island.Tests;

namespace Chippi.Features.CleaningMode.Tests;

/// <summary>The lock as the input hub's first filter, fed by hand: no test hooks the owner's keyboard.</summary>
public sealed class KeyboardLockTests
{
    private sealed class FakeHub : IInputHub
    {
        public IInputFilter? Filter { get; private set; }

        public bool Removed { get; private set; }

        public bool Paused { get; set; }

        public IInputRegistration Add(IInputFilter filter)
        {
            Filter = filter;
            return new Registration(this);
        }

        private sealed class Registration(FakeHub hub) : IInputRegistration
        {
            public void Refresh() { }

            public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { }

            public void Mask() { }

            public void Dispose() => hub.Removed = true;
        }
    }

    private static KeyInput Key(uint vk, bool down, bool wasDown = false, long ms = 0) =>
        new(vk, 0, down, wasDown, false, false, ms * Stopwatch.Frequency / 1000);

    [Fact]
    public void It_is_the_hubs_first_filter_needs_only_the_keyboard_and_never_pauses()
    {
        var hub = new FakeHub();
        var heard = new List<LockedKey>();
        IDisposable? lease = null;
        WpfTestThread.Run(() => lease = new KeyboardLock(() => hub).Lock(heard.Add));

        Assert.Equal(InputOrder.CleaningLock, hub.Filter!.Order);
        Assert.Equal(InputDevices.Keyboard, hub.Filter.Devices);
        Assert.False(hub.Filter.Pauses);

        lease!.Dispose();
        Assert.True(hub.Removed);
    }

    [Fact]
    public void Every_key_is_taken_and_heard_with_its_time_on_the_hubs_clock_but_a_key_held_from_before_gets_its_up_through()
    {
        var hub = new FakeHub();
        var heard = new List<LockedKey>();
        WpfTestThread.Run(() =>
        {
            new KeyboardLock(() => hub).Lock(heard.Add);
            var filter = hub.Filter!;
            Assert.True(filter.Key(Key(0x1B, down: true, ms: 10_000)));
            Assert.True(filter.Key(Key(0x1B, down: false, ms: 10_200)));
            Assert.True(filter.Key(Key(0xA2, down: true, wasDown: true))); // Ctrl from the shortcut, repeating
            Assert.False(filter.Key(Key(0xA2, down: false)));
            Assert.False(filter.Mouse(new MouseInput(MouseKind.Down, MouseButton.Left, 0, 0, 0, false, 0)));
        });
        WpfTestThread.Run(() => { });

        Assert.Equal([new LockedKey(0x1B, true, 10_000), new LockedKey(0x1B, false, 10_200), new LockedKey(0xA2, true, 0)], heard);
    }
}
