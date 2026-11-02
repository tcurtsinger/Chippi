// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using System.Windows.Controls;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

public sealed class QuickActionsTests
{
    private sealed class SettingsStore : IModuleSettings
    {
        public QuickActionsSettings Value = new();
        public int Saves;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class { Value = (QuickActionsSettings)(object)value; Saves++; }
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Tile;
        public Func<string?, object>? Page;
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Tile = spec; return new IslandTileContribution(QuickActionsModule.Id, spec); }
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
    }

    private sealed class Hotkey : IMicHotkey
    {
        public bool Available = true;
        public Action? Pressed;
        public bool Register(MicShortcut shortcut, Action pressed)
        {
            if (!Available) return false;
            Pressed = pressed;
            return true;
        }
        public void Dispose() => Pressed = null;
    }

    private sealed class Fixture
    {
        public readonly FakePc Pc = new();
        public readonly SettingsStore Settings = new();
        public readonly Ui Ui = new();
        public readonly List<Hotkey> Hotkeys = [];
        public readonly List<string> Opened = [];
        public bool HotkeysAvailable = true;
        public bool IslandShows = true;
        public readonly QuickActionsModule Module;

        public Fixture() => Module = new QuickActionsModule(() => Pc, () =>
        {
            var hotkey = new Hotkey { Available = HotkeysAvailable };
            Hotkeys.Add(hotkey);
            return hotkey;
        });

        public Task Start() => Module.StartAsync(new ModuleContext(QuickActionsModule.Id, Settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), Ui)
        {
            TryOpenIslandPage = key =>
            {
                if (IslandShows)
                    Opened.Add(key);
                return IslandShows;
            },
            IslandPasteTarget = () => new PasteTarget(1, 42),
        }, default);
    }

    private static readonly MicShortcut Keys = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x44);

    [Fact]
    public async Task The_home_button_is_Actions_with_no_status()
    {
        var f = new Fixture();
        await f.Start();
        Assert.Equal(("quick-actions", "Actions"), (f.Ui.Tile!.Key, f.Ui.Tile.Name));
        Assert.Null(f.Ui.Tile.Toggle);
        Assert.Null(f.Module.Settings.Shown); // the page opens on the picker
    }

    [Fact]
    public async Task Done_saves_the_pick_in_the_pages_order()
    {
        var f = new Fixture();
        await f.Start();
        f.Module.Pick([QuickActionId.Power, QuickActionId.Dark, QuickActionId.Lock]);
        Assert.Equal(["dark", "lock", "power"], f.Settings.Value.Shown);
        Assert.Equal([QuickActionId.Dark, QuickActionId.Lock, QuickActionId.Power], f.Module.Shown.Select(a => a.Id));
    }

    [Fact]
    public async Task What_the_PC_cant_do_is_left_out_of_the_pick_and_the_page()
    {
        var f = new Fixture();
        f.Pc.State = QuickState.Empty with { Unsupported = new HashSet<QuickActionId> { QuickActionId.Bluetooth, QuickActionId.Extend } };
        f.Settings.Value = new() { Shown = ["bluetooth", "wifi"] };
        await f.Start();
        Assert.DoesNotContain(f.Module.Offered, a => a.Id is QuickActionId.Bluetooth or QuickActionId.Extend);
        Assert.Equal(16, f.Module.Offered.Count);
        Assert.Equal([QuickActionId.WiFi], f.Module.Shown.Select(a => a.Id));
    }

    [Fact]
    public async Task Settings_switches_one_action_on_and_off()
    {
        var f = new Fixture();
        await f.Start();
        f.Module.SetShown(QuickActionId.WiFi, true);
        Assert.Equal(["wifi"], f.Settings.Value.Shown);
        f.Module.SetShown(QuickActionId.Dark, true);
        Assert.Equal(["dark", "wifi"], f.Settings.Value.Shown);
        f.Module.SetShown(QuickActionId.WiFi, false);
        Assert.Equal(["dark"], f.Settings.Value.Shown);
    }

    [Fact]
    public async Task The_app_in_front_is_the_one_before_the_island_opened()
    {
        var f = new Fixture();
        await f.Start();
        Assert.Equal(42u, f.Pc.Fronts.Last());
    }

    [Fact]
    public async Task Quit_all_never_closes_a_kept_app_and_all_but_keeps_the_front_one()
    {
        var f = new Fixture();
        f.Pc.State = QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Spotify, FakePc.Discord] };
        f.Settings.Value = new() { Kept = [FakePc.Spotify.Path.ToUpperInvariant()] };
        await f.Start();
        Assert.Equal([FakePc.Chrome, FakePc.Discord], f.Module.Quitting(keepFront: false));
        await f.Module.QuitAllAsync(keepFront: true);
        Assert.Equal(["quit Discord"], f.Pc.Calls);
        await f.Module.QuitAllAsync(keepFront: false);
        Assert.Equal(["quit Discord", "quit Google Chrome"], f.Pc.Calls);
    }

    [Fact]
    public async Task A_shortcut_flips_a_toggle_and_runs_an_action_as_its_button_does()
    {
        var f = new Fixture();
        await f.Start();
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Dark, Keys));
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Lock, Keys with { Key = 0x4C }));
        f.Hotkeys[0].Pressed!();
        f.Hotkeys[1].Pressed!();
        Assert.Equal(["set Dark True", "run Lock"], f.Pc.Calls);
        Assert.Empty(f.Opened);
        Assert.Equal("Ctrl+Alt+D", f.Settings.Value.Shortcuts["dark"].ToString());
    }

    [Theory]
    [InlineData(QuickActionId.Recycle)]
    [InlineData(QuickActionId.Power)]
    [InlineData(QuickActionId.Quit)]
    public async Task A_shortcut_that_asks_or_needs_a_choice_opens_the_island_on_it(QuickActionId id)
    {
        var f = new Fixture();
        await f.Start();
        Assert.True(f.Module.ChangeShortcut(id, Keys));
        f.Hotkeys[0].Pressed!();
        Assert.Empty(f.Pc.Calls);
        Assert.Equal(["quick-actions"], f.Opened);
        Assert.Equal(id, f.Module.Pending);
    }

    [Fact]
    public async Task A_shortcut_that_needs_the_island_does_nothing_while_it_cant_show()
    {
        var f = new Fixture { IslandShows = false }; // off, or a fullscreen app in front
        await f.Start();
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Frozen, Keys));
        f.Hotkeys[0].Pressed!();
        Assert.Empty(f.Opened);
        Assert.Null(f.Module.Pending); // nothing pops up the next time Actions opens
        Assert.Empty(f.Pc.Calls);
    }

    [Fact]
    public async Task Quit_all_closes_the_rest_when_one_app_refuses_and_says_it_failed()
    {
        var f = new Fixture();
        var admin = new OpenApp(@"C:\Windows\System32\Taskmgr.exe", "Task Manager", false);
        f.Pc.State = QuickState.Empty with { Apps = [FakePc.Chrome, admin, FakePc.Spotify, FakePc.Discord] };
        f.Pc.Refuse.Add("Task Manager");
        await f.Start();
        await Assert.ThrowsAsync<AggregateException>(() => f.Module.QuitAllAsync(keepFront: false));
        Assert.Equal(["quit Google Chrome", "quit Spotify", "quit Discord"], f.Pc.Calls);
    }

    [Fact]
    public async Task A_kept_app_stays_kept_when_an_update_moves_it_to_a_new_folder()
    {
        var f = new Fixture();
        f.Pc.State = QuickState.Empty with { Apps = [FakePc.Discord] };
        f.Settings.Value = new() { Kept = [@"C:\Users\me\AppData\Local\Discord\app-0.9\Discord.exe"] };
        await f.Start();
        Assert.Empty(f.Module.Quitting(keepFront: false));
        f.Module.Keep(FakePc.Discord.Path); // already kept: not added twice
        Assert.Single(f.Settings.Value.Kept);
    }

    [Fact]
    public async Task Ejects_shortcut_ejects_the_only_drive_at_once()
    {
        var f = new Fixture();
        f.Pc.State = QuickState.Empty with { Drives = [new("e", "SanDisk (F:)", 62_000_000_000)] };
        await f.Start();
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Eject, Keys));
        f.Hotkeys[0].Pressed!();
        Assert.Equal(["eject SanDisk (F:)"], f.Pc.Calls);
    }

    [Fact]
    public async Task A_shortcut_while_Settings_records_one_does_nothing()
    {
        var f = new Fixture();
        await f.Start();
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Dark, Keys));
        f.Module.CaptureShortcut(true);
        f.Hotkeys[0].Pressed!();
        Assert.Empty(f.Pc.Calls);
    }

    [Fact]
    public async Task A_clashing_shortcut_says_so_and_isnt_saved()
    {
        var f = new Fixture { HotkeysAvailable = false };
        await f.Start();
        Assert.False(f.Module.ChangeShortcut(QuickActionId.Dark, Keys));
        Assert.Equal("Shortcut in use", f.Module.ShortcutErrors[QuickActionId.Dark]);
        Assert.Empty(f.Settings.Value.Shortcuts);
    }

    [Fact]
    public async Task Removing_a_shortcut_forgets_it()
    {
        var f = new Fixture();
        await f.Start();
        Assert.True(f.Module.ChangeShortcut(QuickActionId.Dark, Keys));
        f.Module.RemoveShortcut(QuickActionId.Dark);
        Assert.Empty(f.Settings.Value.Shortcuts);
        Assert.Null(f.Hotkeys[0].Pressed);
    }

    [Fact]
    public void Settings_keep_their_shortcuts_through_settings_json()
    {
        var settings = new QuickActionsSettings { Shown = ["dark"], Kept = ["a.exe"], Shortcuts = new() { ["dark"] = Keys } };
        var back = JsonSerializer.Deserialize<QuickActionsSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(["dark"], back.Shown);
        Assert.Equal(["a.exe"], back.Kept);
        Assert.Equal(Keys, back.ShortcutOf(QuickActionId.Dark));
    }

    [Fact]
    public void Each_button_says_what_there_is_to_act_on()
    {
        var empty = QuickState.Empty;
        (string, bool, bool) Of(QuickActionId id, QuickState state) => ActionsPage.Describe(QuickAction.Of(id), state);
        Assert.Equal(("No drives", false, false), Of(QuickActionId.Eject, empty));
        Assert.Equal(("Samsung T7", true, false), Of(QuickActionId.Eject, empty with { Drives = [new("e", "Samsung T7 (E:)", 1)] }));
        Assert.Equal(("2 drives", true, true), Of(QuickActionId.Eject, empty with { Drives = [new("e", "A (E:)", 1), new("f", "B (F:)", 1)] }));
        Assert.Equal(("None", false, false), Of(QuickActionId.Quit, empty));
        Assert.Equal(("3 open", true, true), Of(QuickActionId.Quit, empty with { Apps = [FakePc.Chrome, FakePc.Spotify, FakePc.Discord] }));
        Assert.Equal(("None", false, false), Of(QuickActionId.Frozen, empty));
        Assert.Equal(("2 apps", true, false), Of(QuickActionId.Frozen, empty with { Frozen = 2 }));
        Assert.Equal(("Empty", false, false), Of(QuickActionId.Recycle, empty));
        Assert.Equal(("", true, false), Of(QuickActionId.Recycle, empty with { RecycleBinItems = 12 }));
        Assert.Equal(("Balanced", true, true), Of(QuickActionId.Plan, empty with { Plans = [new(Guid.NewGuid(), "Balanced", true), new(Guid.NewGuid(), "Power saver", false)] }));
        Assert.Equal(("", true, true), Of(QuickActionId.Power, empty));
        Assert.Equal(("", true, false), Of(QuickActionId.Lock, empty));
    }

    [Theory]
    [InlineData("Zoom Workplace", "ZW")]
    [InlineData("Spotify", "Sp")]
    [InlineData("007 First Light", "0F")]
    [InlineData("🎧 Studio Pro", "SP")]
    [InlineData("", "?")]
    public void An_app_without_an_icon_shows_its_letters(string name, string letters) => Assert.Equal(letters, Views.Ui.Initials(name));

    [Theory]
    [InlineData(6, 44)]
    [InlineData(15, 44)]
    [InlineData(16, 34)]
    [InlineData(18, 34)]
    public void Four_rows_take_shorter_buttons_so_they_fit(int count, double height) => Assert.Equal(height, ActionsPage.ButtonHeight(count));

    [Theory]
    [InlineData(1_000_204_886_016, "931 GB")]
    [InlineData(62_008_590_336, "57.7 GB")]
    [InlineData(4_000_787_030_016, "3.63 TB")]
    [InlineData(512, "512 bytes")]
    public void Drive_sizes_read_as_Explorer_shows_them(long bytes, string text) => Assert.Equal(text, ActionsPage.Size(bytes));

    [Fact]
    public async Task The_page_opens_on_the_picker_then_shows_the_pick()
    {
        var f = new Fixture();
        await f.Start();
        WpfTestThread.Run(() =>
        {
            var page = new ActionsPage(f.Module);
            Assert.Equal("picker", page.Showing);
            f.Module.Pick([QuickActionId.Dark, QuickActionId.Quit]);
            page.Render();
            Assert.Equal("grid", page.Showing);
            Assert.Equal("Actions", ((TextBlock)page.Header).Text);
        });
    }

    [Fact]
    public async Task A_list_steps_into_the_page_and_Back_returns_to_the_buttons()
    {
        var f = new Fixture();
        f.Pc.State = QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Discord] };
        f.Settings.Value = new() { Shown = ["quit", "dark"] };
        await f.Start();
        WpfTestThread.Run(() =>
        {
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            Assert.Equal("quit", page.Showing);
            Assert.Equal("Quit apps", ((TextBlock)page.Header).Text);
            Assert.True(page.Escape());
            Assert.Equal("grid", page.Showing);
            Assert.False(page.Back()); // the island's own Back takes it home
        });
    }

    [Fact]
    public async Task A_toggle_ignores_a_press_while_its_last_one_is_applied()
    {
        var f = new Fixture();
        f.Settings.Value = new() { Shown = ["extend"] };
        await f.Start();
        var hold = new TaskCompletionSource();
        f.Pc.Hold = hold;
        WpfTestThread.Run(() =>
        {
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Extend);
            page.Press(QuickActionId.Extend); // a double click
            Assert.Equal(["set Extend True"], f.Pc.Calls);
            hold.SetResult();
        });
    }

    [Fact]
    public async Task A_failure_in_a_list_says_so_at_its_foot()
    {
        var f = new Fixture();
        f.Pc.State = QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Discord] };
        f.Pc.Refuse.Add("Discord");
        f.Settings.Value = new() { Shown = ["quit"] };
        await f.Start();
        WpfTestThread.Run(() =>
        {
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            var quit = Find<System.Windows.Controls.Button>(page).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Quit Discord");
            quit.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("quit", page.Showing);
            Assert.Equal("Quit failed", page.ListFailure);
        });
    }

    private static IEnumerable<T> Find<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
            foreach (var found in Find<T>(child))
                yield return found;
    }

    [Fact]
    public async Task A_toggles_button_turns_it_and_a_failure_says_so()
    {
        var f = new Fixture();
        f.Settings.Value = new() { Shown = ["dark"] };
        await f.Start();
        WpfTestThread.Run(() =>
        {
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Dark);
            Assert.Equal(["set Dark True"], f.Pc.Calls);
            Assert.True(f.Module.State.On[QuickActionId.Dark]);
            f.Pc.Fail = true;
            page.Press(QuickActionId.Dark);
            Assert.Equal("Dark, Failed", System.Windows.Automation.AutomationProperties.GetName((System.Windows.DependencyObject)page.InitialFocus!));
        });
    }
}
