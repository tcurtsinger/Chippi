// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CleaningMode.Screens;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.CleaningMode.Tests;

public sealed class CleaningModeTests
{
    private const uint Esc = UnlockCount.Escape, A = 0x41, Shift = 0x10, Ctrl = 0xA2;

    /// <summary>A keyboard lock that only records: no test takes the owner's keyboard. Each press is 100ms after the last.</summary>
    private sealed class FakeLock(List<string> order) : IKeyboardLock
    {
        public Action<LockedKey>? Key;
        public int Locks, Releases;
        public uint Time = 5000;

        public IDisposable Lock(Action<LockedKey> key)
        {
            order.Add("lock");
            Locks++;
            Key = key;
            return new Release(this);
        }

        public void Press(uint vk, uint after = 100)
        {
            Time += after;
            Key!(new LockedKey(vk, true, Time));
            Key?.Invoke(new LockedKey(vk, false, Time + 30));
        }

        private sealed class Release(FakeLock owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Releases++;
                owner.Key = null;
            }
        }
    }

    /// <summary>Screens that only record: nothing goes black on the owner's PC.</summary>
    private sealed class FakeScreens(List<string> order) : ICleaningScreens
    {
        public readonly List<string> Calls = [];
        public Action? Gone;
        public bool FailUnlock, FailShow;
        public event Action? UnlockClicked;
        public event Action? Interrupted;

        public void Show()
        {
            order.Add("show");
            if (FailShow)
                throw new InvalidOperationException("no screens");
            Calls.Add("show");
        }

        public void Count(int presses, bool counted) => Calls.Add($"count {presses}{(counted ? " squish" : "")}");

        public void Unlock(Action gone)
        {
            Calls.Add("unlock");
            if (FailUnlock)
                throw new InvalidOperationException("the celebration broke");
            Gone = gone;
        }

        public void Close() => Calls.Add("close");
        public void ClickUnlock() => UnlockClicked?.Invoke();
        public void Interrupt() => Interrupted?.Invoke();
    }

    private sealed class Fixture
    {
        public readonly List<string> Order = [];
        public readonly FakeLock Keyboard;
        public readonly List<FakeScreens> Screens = [];
        public readonly CleaningSession Session;
        public Action<FakeScreens>? Arrange;

        public Fixture()
        {
            Keyboard = new FakeLock(Order);
            Session = new CleaningSession(Keyboard, () =>
            {
                var screens = new FakeScreens(Order);
                Arrange?.Invoke(screens);
                Screens.Add(screens);
                return screens;
            });
        }

        public FakeScreens Last => Screens[^1];
    }

    [Fact]
    public void Three_Escs_each_within_a_second_of_the_last_unlock()
    {
        var count = new UnlockCount();
        Assert.Equal(1, count.Key(Esc, true, 1000));
        count.Key(Esc, false, 1050);
        Assert.Equal(2, count.Key(Esc, true, 1900));
        count.Key(Esc, false, 1950);
        Assert.Equal(3, count.Key(Esc, true, 2900));
        Assert.True(count.Unlocked);
    }

    [Fact]
    public void A_wait_over_a_second_starts_the_count_over()
    {
        var count = new UnlockCount();
        count.Key(Esc, true, 1000);
        count.Key(Esc, false, 1050);
        Assert.Equal(1, count.Key(Esc, true, 2001));
    }

    [Fact]
    public void The_wait_counts_across_the_tick_wrapping()
    {
        var count = new UnlockCount();
        count.Key(Esc, true, uint.MaxValue - 100);
        count.Key(Esc, false, uint.MaxValue - 50);
        Assert.Equal(2, count.Key(Esc, true, 300)); // 400ms later
    }

    [Theory]
    [InlineData(A)]
    [InlineData(Shift)]
    public void Any_other_key_starts_the_count_over(uint other)
    {
        var count = new UnlockCount();
        count.Key(Esc, true, 1000);
        count.Key(Esc, false, 1050);
        Assert.Equal(0, count.Key(other, true, 1100));
        count.Key(other, false, 1150);
        Assert.Equal(1, count.Key(Esc, true, 1200));
    }

    [Fact]
    public void A_held_Escs_repeat_starts_the_count_over()
    {
        var count = new UnlockCount();
        count.Key(Esc, true, 1000);
        Assert.Equal(0, count.Key(Esc, true, 1500)); // the repeat, no key-up between
        count.Key(Esc, false, 1600);
        Assert.Equal(1, count.Key(Esc, true, 1700));
    }

    [Fact]
    public void A_key_held_before_the_lock_still_comes_up_after_its_repeats()
    {
        var filter = new KeyFilter();
        Assert.True(filter.Swallow(Ctrl, down: true, heldBefore: true)); // the shortcut's Ctrl, repeating: Windows has it down
        Assert.True(filter.Swallow(Ctrl, down: true, heldBefore: true));
        Assert.False(filter.Swallow(Ctrl, down: false)); // its key-up reaches Windows
        Assert.True(filter.Swallow(A, down: true));
        Assert.True(filter.Swallow(A, down: true)); // a key pressed during the lock, repeating: Windows never saw it
        Assert.True(filter.Swallow(A, down: false));
        Assert.False(filter.Swallow(A, down: false)); // already up
    }

    [Fact]
    public void Starting_shows_the_black_before_it_locks_the_keyboard_and_only_once()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Session.Start();
        Assert.True(f.Session.Active);
        Assert.Equal(["show", "lock"], f.Order);
        Assert.Single(f.Screens);
    }

    [Fact]
    public void A_start_that_fails_leaves_nothing_locked_or_shown()
    {
        var f = new Fixture { Arrange = s => s.FailShow = true };
        Assert.Throws<InvalidOperationException>(f.Session.Start);
        Assert.False(f.Session.Active);
        Assert.Equal(0, f.Keyboard.Locks);
        Assert.Equal(["close"], f.Last.Calls);
    }

    [Fact]
    public void Each_Esc_fills_a_dot_and_squishes_Chippi_and_another_key_empties_them()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Keyboard.Press(Esc);
        f.Keyboard.Press(Esc);
        f.Keyboard.Press(A);
        Assert.Equal(["show", "count 1 squish", "count 2 squish", "count 0"], f.Last.Calls);
        Assert.Equal(0, f.Keyboard.Releases);
    }

    [Fact]
    public void Escs_more_than_a_second_apart_start_over()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Keyboard.Press(Esc);
        f.Keyboard.Press(Esc, after: 1100);
        Assert.Equal(["show", "count 1 squish", "count 1 squish"], f.Last.Calls);
    }

    [Fact]
    public void The_third_Esc_gives_the_keyboard_back_at_once_and_the_black_goes_after()
    {
        var f = new Fixture();
        f.Session.Start();
        for (int i = 0; i < 3; i++)
            f.Keyboard.Press(Esc);
        Assert.Equal(1, f.Keyboard.Releases);
        Assert.Equal("unlock", f.Last.Calls[^1]);
        Assert.True(f.Session.Active); // Chippi celebrates, then the black fades
        f.Last.Gone!();
        Assert.False(f.Session.Active);
    }

    [Fact]
    public void Unlock_clicked_unlocks()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Last.ClickUnlock();
        Assert.Equal(1, f.Keyboard.Releases);
        Assert.Equal(["show", "unlock"], f.Last.Calls);
    }

    [Fact]
    public void An_unlock_that_fails_still_takes_the_black_away()
    {
        var f = new Fixture { Arrange = s => s.FailUnlock = true };
        f.Session.Start();
        f.Last.ClickUnlock();
        Assert.False(f.Session.Active);
        Assert.Equal(1, f.Keyboard.Releases);
        Assert.Equal(["show", "unlock", "close"], f.Last.Calls);
    }

    [Fact]
    public void Windows_taking_over_ends_it_at_once()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Last.Interrupt(); // the PC locked or slept, or Ctrl+Alt+Del
        Assert.False(f.Session.Active);
        Assert.Equal(1, f.Keyboard.Releases);
        Assert.Equal(["show", "close"], f.Last.Calls);
        f.Session.Start(); // and it can start again
        Assert.Equal(2, f.Screens.Count);
    }

    [Fact]
    public void A_late_gone_from_an_earlier_unlock_doesnt_end_the_next_one()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Last.ClickUnlock();
        var first = f.Last.Gone!;
        f.Session.End();
        f.Session.Start();
        first();
        Assert.True(f.Session.Active);
    }

    private sealed class Settings : IModuleSettings
    {
        public CleaningModeSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (CleaningModeSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Cleaning mode has no home button");
        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class Hotkey : IMicHotkey
    {
        public Action? Pressed;
        public bool Register(MicShortcut shortcut, Action pressed) { Pressed = pressed; return true; }
        public void Dispose() => Pressed = null;
    }

    [Fact]
    public async Task Its_shortcut_starts_it_but_not_while_Settings_records_one()
    {
        var order = new List<string>();
        var keyboard = new FakeLock(order);
        var hotkey = new Hotkey();
        var settings = new Settings();
        var module = new CleaningModeModule(keyboard, () => new FakeScreens(order), () => hotkey);
        Assert.False(module.Installed);
        module.Start(); // not installed: nothing
        Assert.Equal(0, keyboard.Locks);
        await module.StartAsync(new ModuleContext(CleaningModeModule.Id, settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(CleaningModeModule.Id), new Ui()), default);
        Assert.True(module.Installed);
        Assert.Null(settings.Value.Shortcut); // none by default
        Assert.True(module.ChangeShortcut(new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x43)));
        module.CaptureShortcut(true);
        hotkey.Pressed!();
        Assert.Equal(0, keyboard.Locks);
        module.CaptureShortcut(false);
        hotkey.Pressed!();
        Assert.True(module.IsOn);
        await module.StopAsync(default); // uninstalling ends it
        Assert.False(module.IsOn);
        Assert.Equal(1, keyboard.Releases);
    }

    [Fact]
    public async Task A_failed_start_throws_to_who_started_it_but_not_from_the_shortcut()
    {
        var order = new List<string>();
        var hotkey = new Hotkey();
        var module = new CleaningModeModule(new FakeLock(order), () => new FakeScreens(order) { FailShow = true }, () => hotkey);
        await module.StartAsync(new ModuleContext(CleaningModeModule.Id, new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher(CleaningModeModule.Id), new Ui()), default);
        Assert.Throws<InvalidOperationException>(module.Start); // Settings and the Actions page say "… failed"
        Assert.True(module.ChangeShortcut(new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x43)));
        hotkey.Pressed!(); // logged, nothing thrown into the hotkey's window
        Assert.False(module.IsOn);
    }

    [Fact]
    public void Chippi_wears_the_colour_picked_for_him() => WpfTestThread.Run(() =>
    {
        Assert.Equal(Core.Config.PetColor.Synth, new CleaningFace().Pet.PetColor);
        Assert.Equal(Core.Config.PetColor.Azure, new CleaningFace(Core.Config.PetColor.Azure).Pet.PetColor);
    });

    [Fact]
    public void The_dots_fill_as_Esc_is_pressed_and_all_three_on_unlock() => WpfTestThread.Run(() =>
    {
        var face = new CleaningFace();
        Assert.Equal(0, face.Filled);
        face.Count(2, squish: true);
        Assert.Equal(2, face.Filled);
        face.Count(0, squish: false);
        Assert.Equal(0, face.Filled);
        face.Celebrate();
        Assert.Equal(3, face.Filled);
    });
}
