// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.PortManager.Tests;

public sealed class SearchTests
{
    [Theory]
    [InlineData("port 3000", 3000)]
    [InlineData("  Port   3000  ", 3000)]
    [InlineData("PORT 1", 1)]
    [InlineData("port 65535", 65535)]
    public void A_port_number_is_a_port_query(string text, int number) => Assert.Equal(new PortQuery.Port(number), PortQuery.Parse(text));

    [Theory]
    [InlineData("port 0")]
    [InlineData("port 65536")]
    [InlineData("port 70000")]
    [InlineData("port 99999999999999999999")]
    [InlineData("port 0000000070")]
    public void A_number_that_is_no_port_is_invalid(string text) => Assert.IsType<PortQuery.Invalid>(PortQuery.Parse(text));

    [Theory]
    [InlineData("port abc")]
    [InlineData("port")]
    [InlineData("port ")]
    [InlineData("port 3000 node")]
    [InlineData("port -1")]
    [InlineData("port 30a")]
    [InlineData("ports")]
    [InlineData("support 3000")]
    [InlineData("")]
    public void Anything_else_is_an_ordinary_search(string text) => Assert.IsType<PortQuery.NotPort>(PortQuery.Parse(text));

    private static PortRow[] Rows(FakeSystem system, int port) => [.. new PortScanner(system).Scan(e => e.LocalPort == port)];

    [Fact]
    public void An_own_process_reads_as_the_mockups_row_with_Free()
    {
        var s = new FakeSystem().Process(4120, "node.exe");
        s.Listen(3000, 4120);
        var a = PortAnswer.For(3000, Rows(s, 3000));
        Assert.Equal(("Port 3000 · node.exe", "PID 4120", "Free", false), (a.Name, a.Detail, a.Verb, a.NeedsAdmin));
    }

    [Fact]
    public void One_process_on_ipv4_and_ipv6_is_one_row_that_says_both()
    {
        var s = new FakeSystem().Process(4120, "node.exe");
        s.Listen(3000, 4120);
        s.Listen(3000, 4120, "::1");
        var a = PortAnswer.For(3000, Rows(s, 3000));
        Assert.Equal(("Port 3000 · node.exe", "IPv4, IPv6 · PID 4120", "Free"), (a.Name, a.Detail, a.Verb));
    }

    [Fact]
    public void A_service_an_admin_process_and_the_ambiguous_host_read_as_the_mockups()
    {
        var s = new FakeSystem().Process(2880, "spoolsv.exe", canTerminate: false).Process(3340, "postgres.exe", canTerminate: false).Process(1124, "svchost.exe", canTerminate: false);
        s.Services.Add(new("Spooler", "Print Spooler", 2880));
        s.Services.Add(new("RpcSs", "Remote Procedure Call", 1124));
        s.Services.Add(new("Other", "Other", 1124));
        s.Listen(9100, 2880, "0.0.0.0");
        s.Listen(5432, 3340, "0.0.0.0");
        s.Listen(135, 1124, "0.0.0.0");
        var svc = PortAnswer.For(9100, Rows(s, 9100));
        Assert.Equal(("Port 9100 · Print Spooler", "Service · PID 2880", "Stop", true), (svc.Name, svc.Detail, svc.Verb, svc.NeedsAdmin));
        var adm = PortAnswer.For(5432, Rows(s, 5432));
        Assert.Equal(("Port 5432 · postgres.exe", "PID 3340", "Free", true), (adm.Name, adm.Detail, adm.Verb, adm.NeedsAdmin));
        var amb = PortAnswer.For(135, Rows(s, 135));
        Assert.Equal(("Port 135 · svchost.exe", "Shared service · PID 1124", "Open", false), (amb.Name, amb.Detail, amb.Verb, amb.NeedsAdmin));
    }

    [Fact]
    public void System_chippi_and_time_wait_have_no_verb_beyond_Open()
    {
        var s = new FakeSystem().Process(6204, "Chippi.exe", path: @"C:\Chippi\Chippi.exe");
        s.Listen(445, 4, "0.0.0.0");
        s.Listen(47100, 6204);
        s.Connect(50122, 0, TcpState.TimeWait);
        var sys = PortAnswer.For(445, Rows(s, 445));
        Assert.Equal(("Port 445 · System", "Windows · PID 4", "Open"), (sys.Name, sys.Detail, sys.Verb));
        var chp = PortAnswer.For(47100, Rows(s, 47100));
        Assert.Equal(("Port 47100 · Chippi", "PID 6204", "Open"), (chp.Name, chp.Detail, chp.Verb));
        var tw = PortAnswer.For(50122, Rows(s, 50122));
        Assert.Equal(("Port 50122", "Time wait", null), (tw.Name, tw.Detail, tw.Verb));
    }

    [Fact]
    public void Two_processes_on_one_port_get_Open_and_nothing_on_it_is_text_only()
    {
        var s = new FakeSystem().Process(9012, "java.exe").Process(9188, "java.exe");
        s.Listen(8080, 9012);
        s.Listen(8080, 9188, "0.0.0.0");
        var multi = PortAnswer.For(8080, Rows(s, 8080));
        Assert.Equal(("Port 8080 · 2 processes", "PID 9012, 9188", "Open"), (multi.Name, multi.Detail, multi.Verb));
        var none = PortAnswer.For(3001, Rows(s, 3001));
        Assert.Equal(("Port 3001 not in use", null, null), (none.Name, none.Detail, none.Verb));
    }

    [Fact]
    public void A_connection_only_port_shows_its_state_without_a_verb()
    {
        var s = new FakeSystem().Process(14220, "chrome.exe");
        s.Connect(49811, 14220, TcpState.Established);
        var a = PortAnswer.For(49811, Rows(s, 49811));
        Assert.Equal(("Port 49811 · chrome.exe", "Established · PID 14220", "Open"), (a.Name, a.Detail, a.Verb));
    }

    // The module, started on a context that records what it opens. Nothing reaches a real PC.
    private sealed class Ui : IModuleUi
    {
        public List<Func<IReadOnlyList<ChippiCommand>>> Commands = [];
        public List<Func<string, IReadOnlyList<ChippiCommand>>> Searches = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("port-manager", spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands.Add(commands);
        public void AddCommandSearch(Func<string, IReadOnlyList<ChippiCommand>> search) => Searches.Add(search);
    }
    private sealed class Settings : IModuleSettings
    { public T Get<T>() where T : class, new() => new(); public void Save<T>(T value) where T : class { } }
    private sealed class Island : IIslandPublisher
    { public void Publish(IslandActivity activity) { } public void Clear(string key) { } public void ReportSuccess(string? message = null) { } }

    private sealed class Fixture
    {
        public readonly FakeSystem System = new FakeSystem().Process(4120, "node.exe").Process(2880, "spoolsv.exe", canTerminate: false);
        public readonly Ui Ui = new();
        public readonly List<string?> Settings = [];
        public readonly List<string> Summoned = [];
        public readonly PortManagerModule Module;
        public readonly ModuleContext Context;

        public Fixture()
        {
            System.Listen(3000, 4120);
            System.Services.Add(new("Spooler", "Print Spooler", 2880));
            System.Listen(9100, 2880, "0.0.0.0");
            Module = new PortManagerModule(() => System);
            Context = new ModuleContext(PortManagerModule.Id, new SearchTests.Settings(), NullLogger.Instance, new Island(), Ui)
            {
                OpenSettings = Settings.Add,
                SummonIslandPage = key => { Summoned.Add(key); return true; },
            };
            Assert.True(Module.StartAsync(Context, default).IsCompletedSuccessfully);
        }

        public IReadOnlyList<ChippiCommand> Search(string text) => Ui.Searches.Single()(text);
    }

    [Fact]
    public async Task Start_registers_commands_search_and_both_unassigned_shortcuts_and_reads_nothing()
    {
        var f = new Fixture();
        Assert.Equal(0, f.System.Reads);
        Assert.Equal(PortManagerModule.ShortcutNames, f.Context.Shortcuts.List.Select(s => s.Name));
        Assert.Equal(["Ports", "Find port", "Free port"], f.Ui.Commands.Single()().Select(c => c.Name));
        await f.Module.StopAsync(default);
        Assert.Empty(f.Context.Shortcuts.List);
        Assert.Null(f.Module.Engine);
    }

    [Fact]
    public void The_descriptor_needs_admin_and_is_in_system()
    {
        var d = new PortManagerModule().Descriptor;
        Assert.Equal(("port-manager", Chippi.Core.Modules.FeatureGroup.System), (d.Id, d.Group));
        Assert.Contains(Capability.Admin, d.Requires);
    }

    [Fact]
    public async Task Port_3000_is_a_value_row_with_Free_and_the_Ports_row_and_nothing_ends_without_a_screen()
    {
        var f = new Fixture();
        var rows = f.Search("port 3000");
        Assert.Equal(["Port 3000 · node.exe", "Ports"], rows.Select(r => r.Name));
        Assert.Equal((ChippiCommandKind.Value, "Free", false), (rows[0].Kind, rows[0].Verb, rows[0].NeedsAdmin));
        Assert.Equal((ChippiCommandKind.Page, "Settings › System"), (rows[1].Kind, rows[1].Detail));
        // No Request wired: the verb row only opens Settings at that port.
        f.Module.Request = null;
        await rows[0].Run();
        Assert.Equal(["port:3000"], f.Settings);
        Assert.DoesNotContain(f.System.Calls, c => c.StartsWith("terminate") || c.StartsWith("close"));
        Assert.Equal(PortManagerModule.SectionFor(3000), f.Settings.Single());
    }

    [Fact]
    public async Task A_verb_row_asks_the_screens_confirm_with_the_row_and_does_nothing_else()
    {
        var f = new Fixture();
        PortRow? asked = null;
        f.Module.Request = row => { asked = row; return Task.CompletedTask; };
        var verb = f.Search("port 3000")[0];
        await verb.Run();
        Assert.Equal((4120, PortVerb.Free), (asked!.Holder.Pid, asked.Verb));
        Assert.Empty(f.System.Calls);
        var stop = f.Search("port 9100")[0];
        Assert.Equal(("Stop", true, "Port 9100 · Print Spooler"), (stop.Verb, stop.NeedsAdmin, stop.Name));
    }

    [Fact]
    public void Nothing_on_the_port_and_an_invalid_port_are_text_rows_followed_by_the_Ports_row()
    {
        var f = new Fixture();
        var none = f.Search("port 3001");
        Assert.Equal(["Port 3001 not in use", "Ports"], none.Select(r => r.Name));
        Assert.Equal("", none[0].Verb);
        var invalid = f.Search("port 70000");
        Assert.Equal(["Invalid port", "Ports"], invalid.Select(r => r.Name));
        Assert.Equal("", invalid[0].Verb);
        Assert.Empty(f.Search("port abc"));
        Assert.Empty(f.Search("ports"));
        Assert.Empty(f.Search("port "));
    }

    [Fact]
    public async Task The_Ports_row_opens_Settings_and_the_shortcuts_open_Search_without_ending_anything()
    {
        var f = new Fixture();
        f.Module.Pick = null; // no picker wired: Free port summons Search
        var commands = f.Ui.Commands.Single()();
        await commands[0].Run();
        Assert.Equal([(string?)null], f.Settings);
        await commands[1].Run();
        await commands[2].Run();
        Assert.Equal(["command-bar", "command-bar"], f.Summoned);
        Assert.Empty(f.System.Calls);
        bool picked = false;
        f.Module.Pick = () => { picked = true; return Task.CompletedTask; };
        await commands[2].Run();
        Assert.True(picked);
    }
}
