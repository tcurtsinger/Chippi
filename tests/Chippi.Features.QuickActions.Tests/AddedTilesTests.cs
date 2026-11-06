// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

/// <summary>Tiles other features add to Actions (docs/phase-6/architecture.md §3), the Input menu first.</summary>
public sealed class AddedTilesTests
{
    // A layout saved on main before the regroup: all 19 tiles in his own order, with one gap of his own at slot 3.
    private static readonly string?[] SavedOnMain =
    [
        "power", "quit", "lock", null, "dark", "frozen", "wifi", "displays", "plan", "hidden",
        "icons", "screensaver", "auto-hide", "extend", "bluetooth", "camera", "recycle", "eject", "explorer", "cleaning",
    ];

    [Fact]
    public void A_new_tile_goes_after_the_last_tile_and_keeps_its_position_while_its_feature_is_out() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(new() { Shown = [.. SavedOnMain.OfType<string>()], Layout = [.. SavedOnMain] });
        string?[] closed = ["power", "quit", null, "dark", "wifi", "plan", "hidden", "icons", "auto-hide", "extend", "bluetooth", "camera", "recycle", "eject", "explorer", "cleaning"];

        // Never installed: no gap and no position kept for it.
        Assert.Equal(closed, f.Module.Layout.Take(closed.Length));
        Assert.DoesNotContain("input", f.Module.Layout);

        f.Installed = true;
        f.Page.Render();
        Assert.Equal([.. closed, "input"], f.Module.Layout.Take(closed.Length + 1));
        Assert.Null(f.Module.Layout[2]); // his own gap stays a gap
        Assert.Contains("input", f.Store.Value.Shown!);
        Assert.Equal(["input"], f.Store.Value.Added);
        Assert.Equal("Off", Status(f.Page, "input"));

        // Out: the tile goes and its position waits for it.
        f.Installed = false;
        f.Page.Render();
        Assert.DoesNotContain(Tiles(f.Page), b => Equals(b.Tag, "input"));
        Assert.Equal("input", f.Module.Layout[closed.Length]);

        // Moving it in the editor, then reinstalling, keeps his position; it isn't appended again.
        f.Installed = true;
        f.Page.Render();
        f.Page.BeginEdit();
        f.Page.MoveEditor(closed.Length, 2);
        f.Page.FinishEdit();
        f.Installed = false;
        f.Page.Render();
        f.Installed = true;
        f.Page.Render();
        Assert.Equal("input", f.Module.Layout[2]);
        Assert.Single(f.Module.Layout, k => k == "input");
    });

    [Fact]
    public void Input_steps_into_its_menu_whose_switches_run_their_commands_and_show_how_many_are_on() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(installed: true);
        f.Page.Render();
        Assert.Equal("Off", Status(f.Page, "input"));
        f.Page.Press("input");
        Assert.Equal(("menu", "Input", "input"), (f.Page.Showing, f.Page.Title, f.Page.Menu));
        var switches = Switches(f.Page);
        Assert.Equal(["Click debounce", "Always on in games"], switches.Select(AutomationProperties.GetName));
        Assert.All(switches, s => Assert.False(s.IsChecked));

        Toggle(switches[0]);
        Assert.Equal(1, f.Click.Runs);
        Assert.True(Switches(f.Page)[0].IsChecked);
        Toggle(Switches(f.Page)[1]);
        Assert.True(Switches(f.Page)[1].IsChecked);

        Assert.True(f.Page.Back());
        Assert.Equal(("grid", "Actions"), (f.Page.Showing, f.Page.Title));
        Assert.Equal("2 on", Status(f.Page, "input"));

        // A switch whose feature stops while the menu shows returns to the buttons.
        f.Page.Press("input");
        f.Installed = false;
        f.Page.Render();
        Assert.Equal(("grid", null), (f.Page.Showing, f.Page.Menu));
    });

    [Fact]
    public void A_failed_switch_says_so_on_its_row_until_it_succeeds() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(installed: true);
        f.Page.Render();
        f.Page.Press("input");
        f.Click.Fail = true;
        Toggle(Switches(f.Page)[0]);
        var row = Switches(f.Page)[0];
        Assert.False(row.IsChecked);
        Assert.Equal("Click debounce failed", AutomationProperties.GetItemStatus(row));
        Assert.Contains(Find<TextBlock>(f.Page), t => t.Text == "Click debounce failed");

        f.Click.Fail = false;
        Toggle(Switches(f.Page)[0]);
        Assert.True(Switches(f.Page)[0].IsChecked);
        Assert.Equal("", AutomationProperties.GetItemStatus(Switches(f.Page)[0]));
    });

    [Fact]
    public void The_editor_hides_and_shows_an_added_tile_and_Settings_switches_keep_its_choice() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(installed: true);
        f.Page.Render();
        f.Page.BeginEdit();
        Assert.Contains("input", f.Page.EditorSelection);
        var pick = Find<ToggleButton>(f.Page).Single(b => Equals(b.Tag, "input"));
        Assert.Equal("Input", AutomationProperties.GetName(pick));
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(pick)).Toggle();
        f.Page.FinishEdit();
        Assert.DoesNotContain("input", f.Store.Value.Shown!);
        Assert.DoesNotContain(Tiles(f.Page), b => Equals(b.Tag, "input"));
        Assert.Equal(["input"], f.Store.Value.Added); // hidden, never added again

        f.Page.BeginEdit();
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(Find<ToggleButton>(f.Page).Single(b => Equals(b.Tag, "input")))).Toggle();
        f.Page.FinishEdit();
        Assert.Contains("input", f.Store.Value.Shown!);
        f.Module.SetShown(QuickActionId.Explorer, false);
        Assert.Contains("input", f.Store.Value.Shown!);
        f.Module.SaveLayout([QuickActionId.Dark], f.Module.Layout);
        Assert.Contains("input", f.Store.Value.Shown!);
    });

    [Fact]
    public void A_tile_of_one_switch_flips_it_in_place() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(installed: true);
        f.Extra = new FakeSwitch("app-switcher/open", "App switcher") { Tile = new ActionsTile("switcher", "Switcher", "AppWindow") };
        f.Page.Render();
        var tile = Find<ToggleButton>(f.Page).Single(b => Equals(b.Tag, "switcher"));
        Assert.Equal(("App switcher", "Off"), (AutomationProperties.GetName(tile), AutomationProperties.GetItemStatus(tile)));
        Toggle(tile);
        Assert.Equal(1, f.Extra.Runs);
        tile = Find<ToggleButton>(f.Page).Single(b => Equals(b.Tag, "switcher"));
        Assert.Equal((true, "On"), (tile.IsChecked, AutomationProperties.GetItemStatus(tile)));
    });

    /// <summary>A menu view with a page of its own, as Snap's Arrangements is.</summary>
    private sealed class PagedView : StackPanel, IActionsMenuView
    {
        public readonly Button Side = new() { Content = "Arrangements" };
        public bool Inner;
        public string? Title => Inner ? "Arrangements" : null;
        public object? Aside => Inner ? null : Side;
        public event Action? HeaderChanged;
        public PagedView() => Children.Add(new Button { Content = "First" });
        public void Step(bool inner) { Inner = inner; HeaderChanged?.Invoke(); }
        public bool Back()
        {
            if (!Inner) return false;
            Step(false);
            return true;
        }
    }

    [Fact]
    public void A_menu_view_with_pages_sets_the_header_takes_Back_first_and_can_ask_to_open() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(installed: true);
        PagedView? view = null;
        bool ask = false;
        var tile = new ActionsTile("snap", "Snap", "LayoutGrid") { Menu = true, View = () => view = new PagedView(), OpensNow = () => { bool a = ask; ask = false; return a; } };
        f.Extra = new FakeSwitch("snap/x", "Snap") { Tile = tile };
        f.Page.Render();
        f.Page.Press("snap");
        Assert.Equal(("menu", "Snap"), (f.Page.Showing, f.Page.Title));
        Assert.Contains(view!.Side, Find<Button>(f.Page.Header));

        // Its own page: its title, nothing at the right; Back returns to its first page, then leaves the menu.
        view.Step(true);
        Assert.Equal("Arrangements", f.Page.Title);
        Assert.DoesNotContain(view.Side, Find<Button>(f.Page.Header));
        Assert.True(f.Page.Back());
        Assert.Equal(("menu", "Snap"), (f.Page.Showing, f.Page.Title));
        Assert.True(f.Page.Back());
        Assert.Equal(("grid", "Actions"), (f.Page.Showing, f.Page.Title));
        Assert.DoesNotContain(view.Side, Find<Button>(f.Page.Header));

        // Asked once, Actions opens on the menu; the next opening is the usual one.
        ask = true;
        f.Page.OpenAsked();
        Assert.Equal(("menu", "snap"), (f.Page.Showing, f.Page.Menu));
        f.Page.Back();
        f.Page.OpenAsked();
        Assert.Equal("grid", f.Page.Showing);
    });

    private static string Status(ActionsPage page, string key) => AutomationProperties.GetItemStatus(Tiles(page).Single(b => Equals(b.Tag, key)));

    private static IEnumerable<ButtonBase> Tiles(ActionsPage page) => Find<ButtonBase>(page).Where(b => b.Tag is string key && !key.Contains(':'));

    private static ToggleButton[] Switches(ActionsPage page) => [.. Find<ToggleButton>(page).Where(b => b.Tag is string key && key.StartsWith("command:", StringComparison.Ordinal))];

    private static void Toggle(ToggleButton button) => ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(button)).Toggle();

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class FakeSwitch(string key, string name)
    {
        public bool On, Fail;
        public int Runs;
        public ActionsTile Tile { get; init; } = ActionsTile.Input;

        public ChippiCommand Command => new(key, name, "Mouse", () =>
        {
            Runs++;
            if (Fail) throw new InvalidOperationException("Fixture hook failure");
            On = !On;
            return Task.CompletedTask;
        })
        { IsOn = On, Tile = Tile };
    }

    private sealed class Fixture : IDisposable
    {
        internal FakePc Pc { get; } = new();
        internal Store Store { get; } = new();
        internal FakeSwitch Click { get; } = new("debounce/click", "Click debounce");
        internal FakeSwitch AlwaysOn { get; } = new("input/always-on", "Always on in games");
        internal FakeSwitch? Extra { get; set; }
        internal bool Installed { get; set; }
        internal QuickActionsModule Module { get; }
        internal ActionsPage Page { get; }

        internal Fixture(QuickActionsSettings? saved = null, bool installed = false)
        {
            Store.Value = saved ?? new() { Shown = ["dark", "explorer"] };
            Installed = installed;
            Module = new QuickActionsModule(() => Pc, () => new Hotkey());
            Module.StartAsync(new ModuleContext(QuickActionsModule.Id, Store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new Pages())
            {
                Commands = () => Installed ? [Click.Command, AlwaysOn.Command, .. Extra is null ? [] : new[] { Extra.Command }] : [],
            }, default).GetAwaiter().GetResult();
            Page = new ActionsPage(Module);
        }

        public void Dispose() => Module.StopAsync(default).GetAwaiter().GetResult();
    }

    private sealed class Store : IModuleSettings
    {
        internal QuickActionsSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (QuickActionsSettings)(object)value;
    }

    private sealed class Pages : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }

    private sealed class Hotkey : IHotkey
    {
        public bool Register(KeyShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
