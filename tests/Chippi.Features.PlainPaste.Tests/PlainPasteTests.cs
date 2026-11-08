// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Core.Shortcuts;
using Chippi.Clipboard.Tests;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.PlainPaste.Tests;

public sealed class PlainPasteTests
{
    public sealed class SettingsStore : IModuleSettings
    {
        public PlainPasteSettings Value = new(); public int Saves; public bool Reject;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (Reject) throw new System.IO.IOException("fixture");
            Value = (PlainPasteSettings)(object)value; Saves++;
        }
    }
    public sealed class Ui : IModuleUi
    {
        public Func<string?, object> Page = null!;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Plain paste must not add a tile");
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
    }
    public sealed class Hotkey : IHotkey
    {
        public bool Available = true, Disposed; public KeyShortcut? Registered; public Action? Pressed;
        public bool Register(KeyShortcut shortcut, Action pressed)
        {
            if (!Available) return false;
            Registered = shortcut; Pressed = pressed; return true;
        }
        public void Unregister() { Registered = null; Pressed = null; }
        public void Dispose() { Disposed = true; Pressed = null; }
    }
    public sealed class Hub : IClipboardHub
    {
        public int Calls, Beeps; public Func<Task<PasteOutcome>> Paste = () => Task.FromResult(PasteOutcome.Sent);
        public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) { Calls++; return Paste(); }
        public void Beep() => Beeps++;
        public IDisposable Listen(Action<ClipCopy> copied, Func<string,bool>? skip = null) => throw new InvalidOperationException("No listener");
        public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => throw new InvalidOperationException("No preview/read");
        public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default) => throw new InvalidOperationException("The hub owns writes");
        public PasteTarget? CaptureTarget() => throw new InvalidOperationException("The hub owns the shortcut target");
        public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new InvalidOperationException("Use plain paste");
    }
    public sealed class Fixture : IAsyncDisposable
    {
        public readonly SettingsStore Settings = new(); public readonly Ui Ui = new(); public readonly Hub Hub = new();
        public readonly Hotkey Hotkey = new(); public Hotkey? Replacement;
        public readonly ManualTime Clock = new(); public readonly IslandActivityHub Island;
        public PlainPasteModule Module; public int Creates;
        public readonly List<string> ShortcutEdits = [];
        public static readonly KeyShortcut Shortcut = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x56);
        public Fixture()
        {
            Island = new IslandActivityHub(Clock);
            Module = new PlainPasteModule(() => ++Creates == 1 ? Hotkey : Replacement = new Hotkey { Available = Hotkey.Available });
        }
        public Task Start() => Module.StartAsync(new ModuleContext(PlainPasteModule.Id, Settings, NullLogger.Instance, Island.CreatePublisher(PlainPasteModule.Id), Ui) { Clipboard = Hub, EditShortcut = ShortcutEdits.Add }, default);
        public void Configure() => Assert.True(Module.ChangeShortcut(Shortcut));
        public ValueTask DisposeAsync() => new(Module.StopAsync(default));
    }
    [Fact]
    public async Task Default_reserves_nothing_and_does_not_read_or_listen()
    {
        await using var f = new Fixture(); await f.Start();
        // Listed in Settings › Shortcuts, unset: no keys held.
        Assert.Null(f.Module.Settings.Shortcut); Assert.Equal(1, f.Creates); Assert.Null(f.Hotkey.Registered);
        await f.Module.InvokeAsync(); Assert.Equal(0, f.Hub.Calls); Assert.Empty(f.Island.Activities); Assert.Equal(0, f.Settings.Saves);
    }
    [Theory]
    [InlineData(PasteOutcome.Copied, "Plain text copied", false)]
    [InlineData(PasteOutcome.NoText, "No text copied", true)]
    [InlineData(PasteOutcome.Busy, "Clipboard busy", true)]
    public async Task Failure_beeps_and_publishes_only_a_six_second_glyph(PasteOutcome outcome, string label, bool caution)
    {
        await using var f = new Fixture(); await f.Start(); f.Configure(); f.Hub.Paste = () => Task.FromResult(outcome);
        await f.Module.InvokeAsync(); Assert.Equal(1, f.Hub.Calls); Assert.Equal(1, f.Hub.Beeps);
        var feedback = Assert.Single(f.Island.Activities); Assert.Equal(label, feedback.Title); Assert.Equal(caution, feedback.NeedsAttention); Assert.Null(feedback.TileKey);
        f.Clock.Advance(TimeSpan.FromMilliseconds(5999)); Assert.Single(f.Island.Activities);
        f.Clock.Advance(TimeSpan.FromMilliseconds(1)); Assert.Empty(f.Island.Activities);
    }
    [Fact]
    public async Task A_sent_paste_is_silent_and_clears_an_earlier_failure()
    {
        await using var f = new Fixture(); await f.Start(); f.Configure();
        f.Hub.Paste = () => Task.FromResult(PasteOutcome.NoText); await f.Module.InvokeAsync();
        f.Hub.Paste = () => Task.FromResult(PasteOutcome.Sent); await f.Module.InvokeAsync();
        Assert.Equal(1, f.Hub.Beeps); Assert.Empty(f.Island.Activities);
    }
    [Fact]
    public async Task Repeated_feedback_restarts_its_six_seconds()
    {
        await using var f = new Fixture(); await f.Start(); f.Configure(); f.Hub.Paste = () => Task.FromResult(PasteOutcome.Copied);
        await f.Module.InvokeAsync(); f.Clock.Advance(TimeSpan.FromSeconds(4)); await f.Module.InvokeAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.Single(f.Island.Activities);
        f.Clock.Advance(TimeSpan.FromSeconds(4)); Assert.Empty(f.Island.Activities);
    }
    [Fact]
    public async Task Shortcut_conflict_preserves_the_old_keys_and_remove_unregisters()
    {
        await using var f = new Fixture(); await f.Start(); f.Configure();
        f.Hotkey.Available = false;
        Assert.False(f.Module.ChangeShortcut(new KeyShortcut(ShortcutModifiers.Control, 0x42)));
        Assert.Equal(Fixture.Shortcut, f.Module.Settings.Shortcut); Assert.Equal(Fixture.Shortcut, f.Hotkey.Registered); Assert.Equal("Shortcut in use", f.Module.ShortcutError);
        f.Module.RemoveShortcut(); Assert.Null(f.Hotkey.Registered); Assert.False(f.Hotkey.Disposed); Assert.Null(f.Module.Settings.Shortcut); Assert.Null(f.Module.ShortcutError);
        await f.Module.InvokeAsync(); Assert.Equal(0, f.Hub.Calls);
    }
    [Fact]
    public async Task Rejected_changes_and_removals_keep_the_saved_shortcut_registered()
    {
        await using var f = new Fixture();
        await f.Start();
        f.Configure();
        f.Settings.Reject = true;
        var alternate = new KeyShortcut(ShortcutModifiers.Control, 0x42);
        Assert.False(f.Module.ChangeShortcut(alternate));
        Assert.Null(f.Replacement); // one listed shortcut, changed in place
        Assert.Equal(Fixture.Shortcut, f.Hotkey.Registered); // the saved keys taken back
        Assert.False(f.Hotkey.Disposed);
        Assert.Equal("Save failed", f.Module.ShortcutError);
        f.Module.RemoveShortcut();
        Assert.False(f.Hotkey.Disposed);
        Assert.Equal(Fixture.Shortcut, f.Module.Settings.Shortcut);
        Assert.Equal(f.Module.Settings, f.Settings.Value);
        f.Settings.Reject = false;
        Assert.True(f.Module.ChangeShortcut(alternate));
        Assert.False(f.Hotkey.Disposed);
        Assert.Equal(alternate, f.Hotkey.Registered);
        Assert.Null(f.Module.ShortcutError);
        Assert.Equal(alternate, f.Settings.Value.Shortcut);
    }

    [Fact]
    public async Task Shift_only_is_unavailable_and_keeps_the_old_shortcut()
    {
        await using var f = new Fixture(); await f.Start(); f.Configure();
        foreach (uint key in new uint[] { 0x56, 0x31 })
        {
            Assert.False(f.Module.ChangeShortcut(new KeyShortcut(ShortcutModifiers.Shift, key)));
            Assert.Equal("Shortcut unavailable", f.Module.ShortcutError);
            Assert.Equal(Fixture.Shortcut, f.Module.Settings.Shortcut);
            Assert.Equal(Fixture.Shortcut, f.Hotkey.Registered);
        }
    }
    [Fact]
    public async Task Capturing_suppresses_the_active_shortcut_without_losing_it()
    {
        await using var f = new Fixture(); await f.Start(); f.Configure();
        f.Module.CaptureShortcut(true); f.Hotkey.Pressed!(); Assert.Equal(0, f.Hub.Calls);
        f.Module.CaptureShortcut(false); f.Hotkey.Pressed!(); Assert.Equal(1, f.Hub.Calls);
    }
    [Fact]
    public async Task Saved_conflict_keeps_the_keys_visible_and_invalid_saved_keys_are_not_reserved()
    {
        await using var f = new Fixture(); f.Settings.Value = new() { Shortcut = Fixture.Shortcut }; f.Hotkey.Available = false;
        await f.Start(); Assert.Equal("Shortcut in use", f.Module.ShortcutError); Assert.Equal(Fixture.Shortcut, f.Module.Settings.Shortcut);
        await f.Module.StopAsync(default);
        f.Settings.Value = new() { Shortcut = new KeyShortcut(ShortcutModifiers.Shift, 0x56) };
        await f.Start(); Assert.Null(f.Replacement!.Registered); Assert.Null(f.Module.Settings.Shortcut);
    }
    [Fact]
    public async Task Stop_waits_for_the_inflight_paste_and_blocks_late_feedback_and_new_presses()
    {
        await using var f = new Fixture(); await f.Start(); f.Configure();
        var result = new TaskCompletionSource<PasteOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Hub.Paste = () => result.Task;
        var paste = f.Module.InvokeAsync(); await f.Module.InvokeAsync(); Assert.Equal(1, f.Hub.Calls);
        var stop = f.Module.StopAsync(default); Assert.False(stop.IsCompleted); Assert.True(f.Hotkey.Disposed);
        result.SetResult(PasteOutcome.Copied); await paste; await stop;
        f.Clock.Advance(TimeSpan.FromSeconds(10)); Assert.Empty(f.Island.Activities); Assert.Equal(0, f.Hub.Beeps);
        await f.Module.InvokeAsync(); Assert.Equal(1, f.Hub.Calls);
    }
}
