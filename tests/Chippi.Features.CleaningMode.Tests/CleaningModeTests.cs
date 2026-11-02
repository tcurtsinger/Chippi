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
    private const uint Esc = UnlockCount.Escape, A = 0x41, Shift = 0x10;

    /// <summary>A clock a test moves by hand.</summary>
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    /// <summary>A keyboard lock that only records: no test takes the owner's keyboard.</summary>
    private sealed class FakeLock : IKeyboardLock
    {
        public Action<LockedKey>? Key;
        public int Locks, Releases;

        public IDisposable Lock(Action<LockedKey> key)
        {
            Locks++;
            Key = key;
            return new Release(this);
        }

        public void Press(uint vk)
        {
            Key!(new LockedKey(vk, true));
            Key?.Invoke(new LockedKey(vk, false));
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
    private sealed class FakeScreens : ICleaningScreens
    {
        public readonly List<string> Calls = [];
        public Action? Gone;
        public event Action? UnlockClicked;
        public event Action? Interrupted;
        public void Show() => Calls.Add("show");
        public void Count(int presses, bool counted) => Calls.Add($"count {presses}{(counted ? " squish" : "")}");
        public void Unlock(Action gone) { Calls.Add("unlock"); Gone = gone; }
        public void Close() => Calls.Add("close");
        public void ClickUnlock() => UnlockClicked?.Invoke();
        public void Interrupt() => Interrupted?.Invoke();
    }

    private sealed class Fixture
    {
        public readonly Clock Time = new();
        public readonly FakeLock Keyboard = new();
        public readonly List<FakeScreens> Screens = [];
        public readonly CleaningSession Session;

        public Fixture() => Session = new CleaningSession(Keyboard, () =>
        {
            var screens = new FakeScreens();
            Screens.Add(screens);
            return screens;
        }, Time);

        public FakeScreens Last => Screens[^1];
    }

    [Fact]
    public void Three_Escs_each_within_a_second_unlock()
    {
        var time = new Clock();
        var count = new UnlockCount(time);
        Assert.Equal(1, count.Key(Esc, true));
        count.Key(Esc, false);
        time.Advance(TimeSpan.FromMilliseconds(900));
        Assert.Equal(2, count.Key(Esc, true));
        count.Key(Esc, false);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, count.Key(Esc, true));
        Assert.True(count.Unlocked);
    }

    [Fact]
    public void A_wait_over_a_second_starts_the_count_over()
    {
        var time = new Clock();
        var count = new UnlockCount(time);
        count.Key(Esc, true);
        count.Key(Esc, false);
        time.Advance(TimeSpan.FromMilliseconds(1001));
        Assert.Equal(1, count.Key(Esc, true));
    }

    [Theory]
    [InlineData(A)]
    [InlineData(Shift)]
    public void Any_other_key_starts_the_count_over(uint other)
    {
        var count = new UnlockCount(new Clock());
        count.Key(Esc, true);
        count.Key(Esc, false);
        Assert.Equal(0, count.Key(other, true));
        count.Key(other, false);
        Assert.Equal(1, count.Key(Esc, true));
    }

    [Fact]
    public void A_held_Escs_repeat_starts_the_count_over()
    {
        var count = new UnlockCount(new Clock());
        count.Key(Esc, true);
        Assert.Equal(0, count.Key(Esc, true)); // the repeat, no key-up between
        count.Key(Esc, false);
        Assert.Equal(1, count.Key(Esc, true));
    }

    [Fact]
    public void A_key_held_before_the_lock_still_comes_up()
    {
        var filter = new KeyFilter();
        Assert.False(filter.Swallow(0x11, down: false)); // the shortcut's Ctrl, pressed before the lock
        Assert.True(filter.Swallow(A, down: true));
        Assert.True(filter.Swallow(A, down: false));
        Assert.False(filter.Swallow(A, down: false)); // already up
    }

    [Fact]
    public void Starting_locks_the_keyboard_and_shows_the_screens_once()
    {
        var f = new Fixture();
        f.Session.Start();
        f.Session.Start();
        Assert.True(f.Session.Active);
        Assert.Equal(1, f.Keyboard.Locks);
        Assert.Single(f.Screens);
        Assert.Equal(["show"], f.Last.Calls);
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
        var keyboard = new FakeLock();
        var hotkey = new Hotkey();
        var settings = new Settings();
        var module = new CleaningModeModule(keyboard, () => new FakeScreens(), () => hotkey, new Clock());
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
