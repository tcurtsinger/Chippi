// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Input;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using RadialButton = Chippi.Features.RadialMenu.Input.MouseButton;

namespace Chippi.Features.RadialMenu.Tests;

/// <summary>Settings › Radial menu › Open over fullscreen apps (docs/phase-6/input-layer.md): on by default; off, the game keeps its buttons and keys.</summary>
public sealed class FullscreenTests
{
    [Fact]
    public void It_is_on_out_of_the_box_and_a_settings_file_without_it_reads_as_on()
    {
        Assert.True(new RadialMenuSettings().OpenOverFullscreen);
        var read = System.Text.Json.JsonSerializer.Deserialize<RadialMenuSettings>("""{"HoldMiddle":true}""")!;
        Assert.True(read.OpenOverFullscreen);
    }

    [Fact]
    public void Off_the_shortcut_steps_aside_while_a_fullscreen_app_is_in_front_and_comes_back_after() => WpfTestThread.Run(() =>
    {
        var f = new Fixture();
        f.Settings.Value = f.Settings.Value with { Shortcut = new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x52) };
        f.StartAsync().GetAwaiter().GetResult();
        Assert.True(f.Input.OpenOverFullscreen);
        Assert.NotNull(f.Hotkey.Pressed);

        // On (the default): a game in front changes nothing.
        f.InputHub.FullscreenInFront = true;
        f.InputHub.Raise();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.NotNull(f.Hotkey.Pressed);

        f.Module.SetOpenOverFullscreen(false);
        Assert.False(f.Settings.Value.OpenOverFullscreen);
        Assert.False(f.Input.OpenOverFullscreen);
        Assert.Null(f.Hotkey.Pressed); // the game gets Ctrl+Alt+R

        f.InputHub.FullscreenInFront = false;
        f.InputHub.Raise();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.NotNull(f.Hotkey.Pressed);
        Assert.Null(f.Module.ShortcutError);
    });

    private sealed class Hub : IInputHub
    {
        public bool Paused => false;

        public bool FullscreenInFront { get; set; }

        public event Action? PauseChanged { add { } remove { } }

        public IInputRegistration Add(IInputFilter filter) => new Registration();

        private sealed class Registration : IInputRegistration
        {
            public void Refresh() { }

            public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { }

            public void Mask() { }

            public void Dispose() { }
        }
    }

    private sealed class Sink : IWheelInputSink
    {
        public void Opened(PixelPoint at, RadialButton button) { }

        public void Moved(PixelPoint at) { }

        public void Released(PixelPoint at) { }

        public void Clicked(PixelPoint at) { }

        public void Dismissed() { }
    }

    private static MouseInput MiddleDown => new(MouseKind.Down, Core.Input.MouseButton.Middle, 100, 100, 0, false, 0);

    [Fact]
    public void Off_a_press_over_a_fullscreen_app_is_the_games_and_on_it_is_kept_for_the_hold()
    {
        var hub = new Hub();
        // A press that's kept starts the hold's timer; each hook is disposed before it fires, so nothing is replayed or opened.
        using (var off = new WheelHook(new Sink(), hub, Dispatcher.CurrentDispatcher))
        {
            off.Watch(middle: true, back: false, forward: false);
            off.OpenOverFullscreen = false;
            hub.FullscreenInFront = true;
            Assert.False(off.Mouse(MiddleDown));
        }
        using (var on = new WheelHook(new Sink(), hub, Dispatcher.CurrentDispatcher))
        {
            on.Watch(middle: true, back: false, forward: false);
            Assert.True(on.Mouse(MiddleDown));
        }
        hub.FullscreenInFront = false;
        using (var offButWindowed = new WheelHook(new Sink(), hub, Dispatcher.CurrentDispatcher))
        {
            offButWindowed.Watch(middle: true, back: false, forward: false);
            offButWindowed.OpenOverFullscreen = false;
            Assert.True(offButWindowed.Mouse(MiddleDown));
        }
    }
}
