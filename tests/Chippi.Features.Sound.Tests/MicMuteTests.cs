// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Audio.Tests;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Features.Sound.Tests;

public sealed class MuteFixture : IAsyncDisposable
{
    public FakeDeviceSystem System { get; } = new();
    public AudioService Audio { get; }
    public FakeHotkey Hotkey { get; } = new();
    public MemorySettings<MicMuteSettings> Settings { get; } = new();
    public List<bool> Tones { get; } = [];
    public IslandActivityHub Hub { get; } = new();
    public RoutingUiFake Ui { get; } = new();
    public MicMuteModule Module { get; }
    public MuteFixture()
    {
        Audio = new(() => System, null, options: new() { Debounce = TimeSpan.FromMilliseconds(10) });
        Module = new(Audio, () => Hotkey, Tones.Add);
    }
    public Task Start() => Module.StartAsync(new(MicMuteModule.Id, Settings, NullLogger.Instance, Hub.CreatePublisher(MicMuteModule.Id), Ui), CancellationToken.None);
    public ValueTask DisposeAsync() => new(Module.StopAsync(CancellationToken.None));
}

public sealed class FakeHotkey : IMicHotkey
{
    public bool Available { get; set; } = true;
    public bool Disposed { get; private set; }
    public MicShortcut? Current { get; private set; }
    public Action? Pressed { get; private set; }
    public bool Register(MicShortcut shortcut, Action pressed)
    {
        if (!Available) return false;
        Current = shortcut;
        Pressed = pressed;
        return true;
    }
    public void Dispose() { Disposed = true; Pressed = null; }
}

public sealed class MicMuteTests
{
    [Fact]
    public async Task Startup_registers_shortcut_without_changing_mute_and_conflicts_do_not_disable_the_button()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"));
        f.Hotkey.Available = false;
        await f.Start();
        Assert.Equal("Shortcut in use", f.Module.ShortcutError);
        Assert.False(f.System.GetLevel("A").Muted);
        await f.Module.ToggleAsync();
        Assert.True(f.System.GetLevel("A").Muted);
        Assert.Equal("Mic muted", Assert.Single(f.Ui.Tiles).Status);
        var activity = Assert.Single(f.Hub.Activities);
        Assert.True(activity.NeedsAttention);
        Assert.Equal(SoundTile.Key, activity.TileKey);
        Assert.Equal("MicOff", f.Module.Descriptor.Icon);
    }

    [Fact]
    public async Task Shared_tile_preserves_processing_failure_unless_microphones_are_muted()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"), Mic("B"));
        await f.Start();
        var mute = Assert.Single(f.Ui.Tiles);
        var processing = new IslandTileContribution("mic-processing", SoundTile.Section(30, SoundTile.Microphone, () => new object()) with { StatusPriority = 10 });
        processing.SetStatus("Clean-up stopped");
        var group = Assert.Single(IslandTileContribution.Group([processing, mute]));

        Assert.Equal("Clean-up stopped", group.Status);
        await f.Module.ToggleAsync();
        Assert.Equal("Mic muted", group.Status);
        f.System.ChangeLevel("B", new(.5f, false));
        await f.Audio.RefreshAsync();
        Assert.Equal("Some microphones muted", group.Status);
        f.System.ChangeLevel("A", new(.5f, false));
        await f.Audio.RefreshAsync();
        Assert.Equal("Clean-up stopped", group.Status);
        Assert.Equal("", mute.Status);

        f.System.Remove("A", "B");
        await f.Audio.RefreshAsync();
        Assert.Equal("Clean-up stopped", group.Status);
    }
    [Fact]
    public async Task Shortcut_changes_persist_only_on_success_and_reject_OS_shortcuts()
    {
        await using var f = new MuteFixture();
        await f.Start();
        var original = f.Module.Settings.Shortcut;
        f.Hotkey.Available = false;
        Assert.False(f.Module.ChangeShortcut(new(ShortcutModifiers.Control, 0x4B)));
        Assert.Equal(original, f.Module.Settings.Shortcut);
        Assert.False(f.Module.ChangeShortcut(new(ShortcutModifiers.Windows | ShortcutModifiers.Alt, 0x4B)));
        f.Hotkey.Available = true;
        var replacement = new MicShortcut(ShortcutModifiers.Alt | ShortcutModifiers.Shift, 0x4E);
        Assert.True(f.Module.ChangeShortcut(replacement));
        Assert.Equal(replacement, f.Settings.Value.Shortcut);
        Assert.Null(f.Module.ShortcutError);
    }

    [Fact]
    public async Task Optional_tone_is_fake_and_only_follows_a_successful_user_toggle()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"));
        await f.Start();
        await f.Module.ToggleAsync();
        Assert.Empty(f.Tones);
        f.Module.SetPlaySound(true);
        await f.Module.ToggleAsync();
        await f.Module.ToggleAsync();
        Assert.Equal([false, true], f.Tones);
        f.System.ChangeLevel("A", new(.5f, false));
        await f.Audio.RefreshAsync();
        Assert.Equal(2, f.Tones.Count);
        Assert.Empty(f.Hub.Activities);
    }

    [Fact]
    public async Task Stop_disposes_hotkey_and_restores_only_owned_mics_while_other_audio_stays_running()
    {
        await using var f = new MuteFixture();
        var other = f.Audio.Acquire();
        f.System.With(Mic("A"), Mic("Already"));
        f.System.ChangeLevel("Already", new(.5f, true));
        await f.Start();
        await f.Module.ToggleAsync();
        await f.Module.StopAsync(CancellationToken.None);
        Assert.True(f.Hotkey.Disposed);
        Assert.False(f.System.GetLevel("A").Muted);
        Assert.True(f.System.GetLevel("Already").Muted);
        Assert.Empty(f.Hub.Activities);
        Assert.True(f.Audio.IsRunning);
        await other.DisposeAsync();
    }

    [Fact]
    public async Task Recording_a_shortcut_does_not_trigger_mute()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"));
        await f.Start();
        f.Module.CaptureShortcut(true);
        f.Hotkey.Pressed!();
        await f.Audio.RefreshAsync();
        Assert.False(f.System.GetLevel("A").Muted);
        f.Module.CaptureShortcut(false);
        f.Hotkey.Pressed!();
        await RoutingFixture.Until(() => f.Module.Status.State == MicrophoneMuteState.Muted);
    }

    [Fact]
    public async Task Fake_shortcut_callback_reaches_the_same_toggle_path()
    {
        await using var f = new MuteFixture();
        f.System.With(Mic("A"));
        await f.Start();
        f.Hotkey.Pressed!();
        await RoutingFixture.Until(() => f.Module.Status.State == MicrophoneMuteState.Muted);
        Assert.True(f.System.GetLevel("A").Muted);
    }
}