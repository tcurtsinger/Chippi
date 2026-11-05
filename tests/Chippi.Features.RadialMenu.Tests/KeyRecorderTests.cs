// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Island.Tests;
using RadialKeyRecorder = Chippi.Features.RadialMenu.Input.KeyRecorder;

namespace Chippi.Features.RadialMenu.Tests;

/// <summary>The key recorder as the input hub's last filter, fed by hand: no test hooks the owner's keyboard.</summary>
public sealed class KeyRecorderTests
{
    private sealed class FakeHub : IInputHub
    {
        public IInputFilter? Filter { get; private set; }

        public int Removed { get; private set; }

        public bool Paused { get; set; }

        public bool FullscreenInFront { get; set; }

        public event Action? PauseChanged { add { } remove { } }

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

            public void Dispose() => hub.Removed++;
        }
    }

    private static KeyInput Key(uint vk, bool down, bool wasDown = false) => new(vk, 0, down, wasDown, false, false, 0);

    private static void Drain() =>
        WpfTestThread.Run(() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle));

    [Fact]
    public void It_keeps_the_keys_records_the_first_combo_and_leaves_the_hub_at_once()
    {
        var hub = new FakeHub();
        var recorded = new List<KeyCombo?>();
        WpfTestThread.Run(() =>
        {
            new RadialKeyRecorder(() => hub).Record(recorded.Add);
            var filter = hub.Filter!;
            Assert.Equal(InputOrder.Recorder, filter.Order);
            Assert.False(filter.Pauses);
            Assert.False(filter.Key(Key(0x41, down: true, wasDown: true))); // held from before: the app's
            Assert.True(filter.Key(Key(0xA2, down: true)));
            Assert.True(filter.Key(Key(0xA0, down: true)));
            Assert.True(filter.Key(Key(0xA0, down: false)));
            Assert.True(filter.Key(Key(0x53, down: true)));
            Assert.Equal(1, hub.Removed);
        });
        Drain(); // the posted callbacks, which an Invoke at Send priority would overtake

        Assert.Equal([new KeyCombo(KeyModifiers.Ctrl, 0x53)], recorded);
    }

    [Fact]
    public void Esc_alone_records_nothing()
    {
        var hub = new FakeHub();
        var recorded = new List<KeyCombo?>();
        WpfTestThread.Run(() =>
        {
            new RadialKeyRecorder(() => hub).Record(recorded.Add);
            Assert.True(hub.Filter!.Key(Key(0x1B, down: true)));
        });
        Drain(); // the posted callbacks, which an Invoke at Send priority would overtake

        Assert.Equal([null], recorded);
    }

    [Fact]
    public void Stopping_early_leaves_the_hub()
    {
        var hub = new FakeHub();
        WpfTestThread.Run(() => new RadialKeyRecorder(() => hub).Record(_ => { }).Dispose());

        Assert.Equal(1, hub.Removed);
    }
}
