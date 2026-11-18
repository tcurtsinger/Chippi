// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Cleaner;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Cleaner.Tests;

public sealed class ModuleTests : IDisposable
{
    private readonly Fixture _f = new();
    public void Dispose() => _f.Dispose();

    private sealed class Ui : IModuleUi
    {
        public List<Func<IReadOnlyList<ChippiCommand>>> Commands = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("cleaner", spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands.Add(commands);
    }

    private sealed class Settings : IModuleSettings
    {
        public object? Stored;
        public T Get<T>() where T : class, new() => Stored as T ?? new();
        public void Save<T>(T value) where T : class => Stored = value;
    }

    private sealed class Island : IIslandPublisher
    { public void Publish(IslandActivity activity) { } public void Clear(string key) { } public void ReportSuccess(string? message = null) { } }

    private sealed class Rig
    {
        public readonly Ui Ui = new();
        public readonly Settings Settings = new();
        public readonly List<string?> Opened = [];
        public readonly CleanerModule Module;
        public readonly ModuleContext Context;
        public readonly FakeAdmin Admin = new();

        public Rig(Fixture f, bool withAdmin = true)
        {
            Module = new CleanerModule(f.Roots, () => f.Running);
            Context = new ModuleContext(CleanerModule.Id, Settings, NullLogger.Instance, new Island(), Ui)
            {
                OpenSettings = Opened.Add,
                Admin = withAdmin ? Admin : null,
            };
            Assert.True(Module.StartAsync(Context, default).IsCompletedSuccessfully);
        }

        public IReadOnlyList<ChippiCommand> Commands => Ui.Commands.Single()();
    }

    [Fact]
    public void The_descriptor_is_in_Apps_needs_admin_and_carries_the_badge()
    {
        var d = new CleanerModule().Descriptor;
        Assert.Equal(("cleaner", "Cleaner", FeatureGroup.Apps), (d.Id, d.Name, d.Group));
        Assert.Contains(Capability.Admin, d.Requires);
        Assert.Equal("Admin for some categories", d.Badges.Single().Text);
    }

    [Fact]
    public async Task Start_adds_the_commands_the_Apps_menu_row_and_two_unassigned_shortcuts_and_stop_removes_them()
    {
        var rig = new Rig(_f);
        Assert.Equal(["Cleaner", "Scan for junk files", "Clean junk files", "Clean junk files"], rig.Commands.Select(c => c.Name));
        Assert.Equal(ChippiCommandKind.Page, rig.Commands[0].Kind);
        Assert.Equal(CleanerModule.ShortcutNames, rig.Context.Shortcuts.List.Select(s => s.Name));
        Assert.All(rig.Context.Shortcuts.List, s => Assert.Null(s.Shortcut)); Assert.All(rig.Context.Shortcuts.List, s => Assert.True(s.Editable));
        Assert.Null(rig.Commands[2].Tile);   // Search's verb asks
        Assert.False(rig.Commands[2].MenuOnly);
        var row = rig.Commands[3];   // Actions › Apps opens the list
        Assert.Equal("apps", row.Tile!.Key);
        Assert.True(row.Tile.Menu);
        Assert.True(row.MenuOnly);
        await rig.Module.StopAsync(default);
        Assert.Empty(rig.Context.Shortcuts.List);
        Assert.Null(rig.Module.Engine);
    }

    [Fact]
    public async Task Clean_carries_the_shield_only_when_an_admin_category_is_ticked_and_shows_the_last_total()
    {
        _f.File(_f.Temp("a.tmp"), 2_500_000);
        var rig = new Rig(_f);
        Assert.False(rig.Commands[2].NeedsAdmin);
        Assert.Equal("", rig.Commands[3].MenuNote);   // nothing measured yet
        await rig.Commands[1].Run();   // Scan
        Assert.Equal("3 MB", rig.Commands[3].MenuNote);
        Assert.Equal("Cleaner", rig.Commands[2].Detail);
        Assert.True(rig.Module.Engine!.SetTicked("wu", true));
        Assert.True(rig.Commands[2].NeedsAdmin);
        Assert.NotNull(rig.Settings.Stored);   // ticks and sizes are in the module's section
        Assert.Contains(((CleanerSettings)rig.Settings.Stored!).Ticks, p => p is { Key: "wu", Value: true });
    }

    [Fact]
    public async Task Clean_never_cleans_silently_it_measures_then_asks()
    {
        string tmp = _f.File(_f.Temp("a.tmp"), 1000);
        var rig = new Rig(_f);
        await rig.Commands[2].Run();   // no confirm wired: nothing is deleted
        Assert.True(File.Exists(tmp));
        Assert.Equal([null], rig.Opened);   // Settings opened in place of the missing screen

        CleanerPlan? asked = null;
        rig.Module.Confirm = plan => { asked = plan; return Task.CompletedTask; };
        await rig.Commands[2].Run();
        Assert.Equal("Clean 1 KB?", asked!.Question);
        Assert.True(File.Exists(tmp));   // asking deletes nothing
    }

    [Fact]
    public async Task Clean_with_admin_ticked_shows_the_prompt_step_before_the_question_and_cancel_ends_the_session()
    {
        _f.File(_f.Temp("a.tmp"), 1000);
        var rig = new Rig(_f);
        rig.Module.Engine!.SetTicked("wtemp", true);
        var steps = new List<string>();
        rig.Admin.OnClean = () => steps.Add("clean");
        rig.Module.Confirm = async plan =>
        {
            steps.Add("question " + plan.Question + " after " + string.Join("|", rig.Admin.Calls));
            await rig.Module.Engine!.CancelPlanAsync();
        };
        await rig.Module.RequestCleanAsync();
        Assert.Equal(["question Clean 2 KB? after measure WindowsTemp"], steps);
        Assert.Equal(["measure WindowsTemp", "end"], rig.Admin.Calls);
    }

    [Fact]
    public async Task With_nothing_to_clean_no_question_is_asked_and_an_open_session_ends()
    {
        Directory.CreateDirectory(_f.Temp());
        var rig = new Rig(_f);
        rig.Module.Engine!.SetTicked("wtemp", true);
        rig.Admin.Measure = c => new(c, AdminCategoryState.Unavailable);
        bool asked = false;
        rig.Module.Confirm = _ => { asked = true; return Task.CompletedTask; };
        await rig.Module.RequestCleanAsync();
        Assert.False(asked);
        Assert.Contains("end", rig.Admin.Calls);
    }

    [Fact]
    public async Task Stopping_ends_a_helper_session_a_measure_left_open()
    {
        _f.File(_f.Temp("a.tmp"), 10);
        var rig = new Rig(_f);
        rig.Module.Engine!.SetTicked("wtemp", true);
        rig.Module.Confirm = _ => Task.CompletedTask;   // the screen leaves the question open
        await rig.Module.RequestCleanAsync();
        await rig.Module.StopAsync(default);
        Assert.Equal("end", rig.Admin.Calls[^1]);
    }
}
