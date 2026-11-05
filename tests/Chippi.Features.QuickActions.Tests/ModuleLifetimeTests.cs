// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.IO;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

/// <summary>Controlled tasks exercise module lifetime without a window, native input, sleeps or Windows actions.</summary>
public sealed class ModuleLifetimeTests
{
    [Fact]
    public async Task Stopping_drops_a_pending_read_and_rejects_late_actions()
    {
        using var f = new Fixture();
        await f.Start();
        var lookup = new TaskCompletionSource<QuickState>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pc.NextRead = () => lookup.Task;
        var poll = f.Module.PollAsync();
        int changed = 0;
        f.Module.Changed += () => changed++;
        await f.Module.StopAsync(default);
        await poll;
        lookup.SetResult(QuickState.Empty with { Frozen = 4 });
        await f.Module.PollAsync();
        Assert.Equal(0, changed);
        Assert.Equal(0, f.Module.State.Frozen);
        Assert.Equal(2, f.Pc.Reads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Module.RunAsync(QuickActionId.Lock));
        Assert.Empty(f.Pc.Inner.Calls);
    }

    [Fact]
    public async Task Restart_uses_a_new_system_and_never_reuses_the_old_lookup()
    {
        var first = new ControlledPc();
        var second = new ControlledPc();
        second.Inner.State = QuickState.Empty with { Frozen = 7 };
        var systems = new Queue<ControlledPc>([first, second]);
        var module = new QuickActionsModule(() => systems.Dequeue(), () => new Hotkey());
        var store = new Store();
        await module.StartAsync(Context(store), default);
        var lookup = new TaskCompletionSource<QuickState>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.NextRead = () => lookup.Task;
        var oldPoll = module.PollAsync();
        await module.StopAsync(default);
        await oldPoll;
        await module.StartAsync(Context(store), default);
        lookup.SetResult(QuickState.Empty with { Frozen = 99 });
        await module.RefreshAsync();
        Assert.Equal(7, module.State.Frozen);
        Assert.Equal(2, first.Reads);
        Assert.Equal(2, second.Reads);
        await module.StopAsync(default);
    }

    [Fact]
    public async Task A_shortcut_waiting_for_state_does_nothing_after_stop()
    {
        using var f = new Fixture();
        await f.Start();
        var lookup = new TaskCompletionSource<QuickState>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pc.NextRead = () => lookup.Task;
        var shortcut = f.Module.PressedAsync(QuickActionId.Lock);
        await f.Module.StopAsync(default);
        await shortcut;
        lookup.SetResult(QuickState.Empty);
        Assert.Empty(f.Pc.Inner.Calls);
    }

    [Fact]
    public async Task An_action_refresh_replaces_a_canceled_older_lookup_instead_of_publishing_its_stale_state()
    {
        using var f = new Fixture();
        await f.Start();
        var lookup = new TaskCompletionSource<QuickState>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pc.NextRead = () => lookup.Task;
        using var cancellation = new CancellationTokenSource();
        var read = f.Module.RefreshAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        var observed = new List<bool>();
        f.Module.Changed += () => observed.Add(f.Module.State.On.GetValueOrDefault(QuickActionId.Dark));
        var change = f.Module.SetAsync(QuickActionId.Dark, true);
        Assert.Equal(["set Dark True"], f.Pc.Inner.Calls);
        lookup.SetResult(QuickState.Empty); // the outstanding read began before the switch
        await change;
        Assert.True(f.Module.State.On[QuickActionId.Dark]);
        Assert.Equal([true], observed);
        Assert.Equal(3, f.Pc.Reads); // initial read, joined older lookup, exactly one fresh replacement
    }

    [Fact]
    public async Task Repeated_shortcut_while_the_first_switch_is_pending_does_not_dispatch_again()
    {
        using var f = new Fixture();
        await f.Start();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pc.Inner.Hold = hold;
        var first = f.Module.PressedAsync(QuickActionId.Dark);
        await f.Module.PressedAsync(QuickActionId.Dark);
        Assert.Equal(["set Dark True"], f.Pc.Inner.Calls);
        hold.SetResult();
        await first;
        Assert.Equal(3, f.Pc.Reads); // startup plus the first shortcut's before/after
        Assert.True(f.Module.State.On[QuickActionId.Dark]);
    }

    [Fact]
    public async Task Repeated_polls_share_one_slow_lookup()
    {
        using var f = new Fixture();
        await f.Start();
        var lookup = new TaskCompletionSource<QuickState>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pc.NextRead = () => lookup.Task;
        var pending = f.Module.PollAsync();
        for (int i = 0; i < 10; i++) await f.Module.PollAsync();
        Assert.Equal(2, f.Pc.Reads);
        Assert.Equal(1, f.Pc.ActiveReads);
        lookup.SetResult(QuickState.Empty);
        await pending;
        Assert.Equal(1, f.Pc.MaxActiveReads);
    }

    [Fact]
    public async Task Stopping_bulk_quit_does_not_close_the_next_app()
    {
        using var f = new Fixture();
        await f.Start();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pc.NextQuit = async app => { await hold.Task; await f.Pc.Inner.QuitAsync(app); };
        var quit = f.Module.QuitAllAsync([FakePc.Chrome, FakePc.Discord]);
        await f.Module.StopAsync(default);
        hold.SetResult();
        await quit;
        Assert.Equal(["quit Google Chrome"], f.Pc.Inner.Calls);
    }

    [Fact]
    public async Task Failed_layout_save_leaves_memory_and_storage_unchanged_and_can_retry()
    {
        using var f = new Fixture();
        await f.Start();
        f.Module.Pick([QuickActionId.Dark]);
        var saved = f.Module.Settings;
        var layout = f.Module.Layout.ToArray();
        (layout[0], layout[19]) = (layout[19], layout[0]);
        f.Settings.Fail = true;
        Assert.Throws<IOException>(() => f.Module.SaveLayout([QuickActionId.Dark, QuickActionId.Explorer], layout));
        Assert.Same(saved, f.Module.Settings);
        Assert.Same(saved, f.Settings.Value);
        f.Settings.Fail = false;
        f.Module.SaveLayout([QuickActionId.Dark, QuickActionId.Explorer], layout);
        Assert.Null(f.Module.Layout[0]);
        Assert.Equal("dark", f.Module.Layout[19]);
        Assert.True(f.Module.Settings.Shows(QuickActionId.Explorer));
    }

    [Fact]
    public async Task Shortcut_save_failure_restores_the_registered_shortcut_and_remove_failure_keeps_it()
    {
        using var f = new Fixture();
        await f.Start();
        var original = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x44);
        var replacement = original with { Key = 0x45 };
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Dark, original));
        f.Settings.Fail = true;
        Assert.False(f.Module.ChangeShortcut(QuickActionId.Dark, replacement));
        Assert.Equal(original, f.Hotkey.Current);
        Assert.Equal(original, f.Module.Settings.ShortcutOf(QuickActionId.Dark));
        Assert.Equal("Save failed", f.Module.ShortcutErrors[QuickActionId.Dark]);
        f.Module.RemoveShortcut(QuickActionId.Dark);
        Assert.Equal(original, f.Hotkey.Current);
        Assert.Equal(original, f.Module.Settings.ShortcutOf(QuickActionId.Dark));
        f.Settings.Fail = false;
        f.Module.RemoveShortcut(QuickActionId.Dark);
        Assert.Null(f.Hotkey.Current);
        Assert.Null(f.Module.Settings.ShortcutOf(QuickActionId.Dark));
        Assert.Empty(f.Module.ShortcutErrors);
    }

    [Fact]
    public async Task A_failed_shortcut_rollback_disposes_the_unsaved_new_registration()
    {
        using var f = new Fixture();
        await f.Start();
        var original = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x44);
        var replacement = original with { Key = 0x45 };
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Dark, original));
        f.Settings.Fail = true;
        f.Hotkey.CanRegister = shortcut => shortcut != original; // another app takes the released old chord
        Assert.False(f.Module.ChangeShortcut(QuickActionId.Dark, replacement));
        Assert.Null(f.Hotkey.Current);
        Assert.Equal(original, f.Module.Settings.ShortcutOf(QuickActionId.Dark));
        Assert.Equal("Save failed", f.Module.ShortcutErrors[QuickActionId.Dark]);
    }

    private sealed class Fixture : IDisposable
    {
        public ControlledPc Pc { get; } = new();
        public Store Settings { get; } = new();
        public Hotkey Hotkey { get; } = new();
        public QuickActionsModule Module { get; }
        public Fixture() => Module = new QuickActionsModule(() => Pc, () => Hotkey);
        public Task Start() => Module.StartAsync(Context(Settings), default);
        public void Dispose() => Module.StopAsync(default).GetAwaiter().GetResult();
    }
    private static ModuleContext Context(Store store) => new(QuickActionsModule.Id, store, NullLogger.Instance,
        new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new Ui());
    private sealed class Store : IModuleSettings
    {
        public QuickActionsSettings Value = new();
        public bool Fail;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (Fail) throw new IOException("Settings read-only");
            Value = (QuickActionsSettings)(object)value;
        }
    }
    private sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }
    private sealed class Hotkey : IHotkey
    {
        public KeyShortcut? Current;
        public Func<KeyShortcut, bool>? CanRegister;
        public bool Register(KeyShortcut shortcut, Action pressed)
        {
            if (CanRegister?.Invoke(shortcut) == false) return false;
            Current = shortcut;
            return true;
        }
        public void Unregister() => Current = null;
        public void Dispose() => Current = null;
    }
    private sealed class ControlledPc : IQuickSystem
    {
        public FakePc Inner { get; } = new();
        public int Reads, ActiveReads, MaxActiveReads;
        public Func<Task<QuickState>>? NextRead;
        public Func<OpenApp, Task>? NextQuit;
        public async Task<QuickState> ReadAsync(uint frontProcessId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Reads);
            int active = Interlocked.Increment(ref ActiveReads);
            MaxActiveReads = Math.Max(MaxActiveReads, active);
            try { return await (Interlocked.Exchange(ref NextRead, null)?.Invoke() ?? Inner.ReadAsync(frontProcessId, ct)); }
            finally { Interlocked.Decrement(ref ActiveReads); }
        }
        public Task SetAsync(QuickActionId id, bool on) => Inner.SetAsync(id, on);
        public Task SetPowerPlanAsync(Guid plan) => Inner.SetPowerPlanAsync(plan);
        public Task EjectAsync(RemovableDrive drive) => Inner.EjectAsync(drive);
        public Task QuitAsync(OpenApp app) => Interlocked.Exchange(ref NextQuit, null)?.Invoke(app) ?? Inner.QuitAsync(app);
        public Task EndAsync(OpenApp app) => Inner.EndAsync(app);
        public Task RunAsync(QuickActionId id) => Inner.RunAsync(id);
        public Task PowerAsync(PowerChoice choice) => Inner.PowerAsync(choice);
    }
}
