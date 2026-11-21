// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Uninstaller;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Uninstaller.Tests;

public sealed class ModuleTests : IDisposable
{
    private readonly Fixture _f = new();
    public void Dispose() => _f.Dispose();

    private sealed class Ui : IModuleUi
    {
        public List<Func<IReadOnlyList<ChippiCommand>>> Commands = [];
        public List<Func<string, IReadOnlyList<ChippiCommand>>> Searches = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("uninstaller", spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands.Add(commands);
        public void AddCommandSearch(Func<string, IReadOnlyList<ChippiCommand>> search) => Searches.Add(search);
    }

    private sealed class Settings : IModuleSettings
    {
        public object? Stored;
        public T Get<T>() where T : class, new() => Stored as T ?? new();
        public void Save<T>(T value) where T : class => Stored = value;
    }

    private sealed class Island : IIslandPublisher
    {
        public List<IslandActivity> Published = [];
        public List<string> Cleared = [];
        public void Publish(IslandActivity activity) => Published.Add(activity);
        public void Clear(string key) => Cleared.Add(key);
        public void ReportSuccess(string? message = null) { }
    }

    private sealed class Rig
    {
        public readonly Ui Ui = new();
        public readonly Settings Settings = new();
        public readonly Island Island = new();
        public readonly List<string?> Opened = [];
        public readonly UninstallerModule Module;
        public readonly ModuleContext Context;

        public Rig(Fixture f)
        {
            Module = new UninstallerModule(() => f.Sys, () => f.Roots, new FixedTime(f));
            Context = new ModuleContext(UninstallerModule.Id, Settings, NullLogger.Instance, Island, Ui)
            {
                OpenSettings = Opened.Add, Admin = f.Admin, DataDirectory = Path.Combine(f.Base, "data"),
            };
            Assert.True(Module.StartAsync(Context, default).IsCompletedSuccessfully);
        }

        public IReadOnlyList<ChippiCommand> Commands => Ui.Commands.Single()();
        public IReadOnlyList<ChippiCommand> Search(string text) => Ui.Searches.Single()(text);
    }

    private void Installed()
    {
        string exe = _f.WriteFile(_f.ProgramFiles("Notepad++", "uninstall.exe"));
        _f.Registry.Add(AppSource.Machine64, "Notepad++", "Notepad++", "Notepad++ Team", "8.7.1", _f.ProgramFiles("Notepad++"), "\"" + exe + "\"");
        _f.Registry.Add(AppSource.User, "Notion", "Notion", "Notion Labs", "4.2", null, "\"" + exe + "\"");
        _f.Registry.Add(AppSource.Machine64, "dn", "Microsoft .NET Runtime 8.0.10", "Microsoft Corporation", "8.0.10", null, "\"" + exe + "\"");
        _f.Registry.Add(AppSource.User, "Chippi", "Chippi", "Chippi", "0.1", null, "\"" + exe + "\"");
    }

    [Fact]
    public void The_descriptor_is_in_Apps_needs_admin_and_carries_the_badge()
    {
        var d = new UninstallerModule().Descriptor;
        Assert.Equal(("uninstaller", "Uninstaller", FeatureGroup.Apps), (d.Id, d.Name, d.Group));
        Assert.Contains(Capability.Admin, d.Requires);
        Assert.Equal("Admin for some apps", d.Badges.Single().Text);
    }

    [Fact]
    public async Task Start_adds_the_commands_the_Apps_menu_row_and_two_unassigned_shortcuts_and_stop_removes_them()
    {
        var rig = new Rig(_f);
        Assert.Equal(["Uninstaller", "Uninstall app", "Scan for leftovers", "Uninstall"], rig.Commands.Select(c => c.Name));
        Assert.Equal(ChippiCommandKind.Page, rig.Commands[0].Kind);
        // Search "uninstall": Uninstall app · Settings · Open, Scan for leftovers · Uninstaller, Uninstaller · Settings.
        Assert.Equal([("Uninstaller", "Settings", ChippiCommandKind.Page), ("Uninstall app", "Settings", ChippiCommandKind.Page), ("Scan for leftovers", "Uninstaller", ChippiCommandKind.Run)],
            rig.Commands.Take(3).Select(c => (c.Name, c.Detail, c.Kind)));
        var row = rig.Commands[3];
        Assert.Equal("apps", row.Tile!.Key);
        Assert.True(row.Tile.Menu);
        Assert.True(row.MenuOnly);
        Assert.Equal(UninstallerModule.ShortcutNames, rig.Context.Shortcuts.List.Select(s => s.Name));
        Assert.All(rig.Context.Shortcuts.List, s => { Assert.Null(s.Shortcut); Assert.True(s.Editable); });
        await rig.Module.StopAsync(default);
        Assert.Empty(rig.Context.Shortcuts.List);
        Assert.Null(rig.Module.Engine);
    }

    [Fact]
    public async Task The_verbs_open_Settings_with_the_search_focused_or_the_recent_list_and_never_uninstall_anything()
    {
        var rig = new Rig(_f);
        await rig.Commands[1].Run();
        await rig.Commands[3].Run();
        await rig.Commands[2].Run();
        Assert.Equal(["search", "search", "uninstalled"], rig.Opened);
        bool picked = false;
        rig.Module.Pick = () => { picked = true; return Task.CompletedTask; };
        await rig.Commands[2].Run();
        Assert.True(picked);
        Assert.Equal(["search", "search", "uninstalled"], rig.Opened);
        Assert.Empty(_f.Admin.Calls);
    }

    [Fact]
    public async Task Search_uninstall_notepad_finds_the_app_and_Run_asks_first()
    {
        Installed();
        var rig = new Rig(_f);
        await rig.Module.Engine!.RefreshAsync();
        var rows = rig.Search("uninstall note");
        var row = Assert.Single(rows);
        Assert.Equal("Uninstall Notepad++", row.Name);
        Assert.Equal(("Run", true, ""), (row.Verb, row.NeedsAdmin, row.Detail));   // no size in the entry: the second line is empty
        Assert.Equal(ChippiCommandKind.Value, row.Kind);

        await row.Run();   // the island is off: Settings opens at the app
        Assert.Equal(["search:Notepad++"], rig.Opened);
        Assert.Empty(_f.Admin.Calls);

        IReadOnlyList<AppEntry>? asked = null;
        rig.Module.Request = apps => { asked = apps; return Task.CompletedTask; };
        await row.Run();
        Assert.Equal("Uninstall Notepad++?", UninstallerText.Question(asked!));
        Assert.Empty(_f.Admin.Calls);   // asking uninstalls nothing
        Assert.Empty(_f.User.Calls);
    }

    [Fact]
    public async Task Search_leaves_out_components_and_Chippi_and_anything_that_isnt_an_uninstall_query()
    {
        Installed();
        var rig = new Rig(_f);
        await rig.Module.Engine!.RefreshAsync();
        Assert.Empty(rig.Search("uninstall runtime"));
        Assert.Empty(rig.Search("uninstall chippi"));
        Assert.Empty(rig.Search("uninstall"));
        Assert.Empty(rig.Search("uninstall   "));
        Assert.Empty(rig.Search("notepad"));
        Assert.Empty(rig.Search("uninstall zzz"));
        Assert.Equal(["Uninstall Notion"], rig.Search("UNINSTALL not").Select(r => r.Name).Where(n => n.Contains("Notion", StringComparison.Ordinal)));
        Assert.False(rig.Search("uninstall notion").Single().NeedsAdmin);   // a user app needs no helper
    }

    [Fact]
    public async Task The_bar_says_Uninstalling_while_it_runs_then_the_result()
    {
        Installed();
        var rig = new Rig(_f);
        var engine = rig.Module.Engine!;
        await engine.RefreshAsync();
        var notion = engine.Apps.Single(a => a.Name == "Notion");
        _f.User.Run = (_, _) => { _f.Registry.Remove(AppSource.User, "Notion"); return 0; };
        await engine.UninstallAsync([notion]);
        Assert.Contains(rig.Island.Published, a => a is { Title: "Uninstalling", Source: "Notion", Icon: "AppWindow" });   // a wizard-only uninstaller: the window glyph
        var done = rig.Island.Published.Last();
        Assert.Equal(("Uninstalled", "Notion", false), (done.Title, done.Source, done.NeedsAttention));
        Assert.Contains("uninstaller/run", rig.Island.Cleared);

        _f.Registry.Add(AppSource.User, "Notion", "Notion", "Notion Labs", "4.2", null, "\"" + _f.WriteFile(_f.ProgramFiles("x", "u.exe")) + "\"");
        _f.User.Run = (_, _) => 0;   // leaves the key: failed
        await engine.RefreshAsync();
        await engine.UninstallAsync([engine.Apps.Single(a => a.Name == "Notion")]);
        var failed = rig.Island.Published.Last();
        Assert.Equal(("Uninstall failed", "Notion", true), (failed.Title, failed.Source, failed.NeedsAttention));
    }

    [Fact]
    public async Task The_quit_guard_asks_through_the_screen_when_one_is_wired()
    {
        var rig = new Rig(_f);
        Assert.Null(await rig.Module.AskQuitAsync());
        rig.Module.QuitAsk = () => Task.FromResult<bool?>(true);
        Assert.True(await rig.Module.AskQuitAsync());
    }

    [Fact]
    public async Task Stop_ends_a_helper_session_a_leftover_step_left_open()
    {
        var rig = new Rig(_f);
        await rig.Module.StopAsync(default);
        await Task.Delay(50);
        Assert.Contains("end", _f.Admin.Calls);
    }
}
