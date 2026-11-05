// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.TextSnippets.Tests;

public sealed class ModuleTests
{
    private sealed class Hub : IInputHub
    {
        public List<IInputFilter> Filters { get; } = [];

        public bool Paused => false;

        public bool FullscreenInFront => false;

        public bool ForegroundElevated { get; set; }

        public event Action? PauseChanged { add { } remove { } }

        public IInputRegistration Add(IInputFilter filter)
        {
            Filters.Add(filter);
            return new Registration(() => Filters.Remove(filter));
        }

        private sealed class Registration(Action remove) : IInputRegistration
        {
            public void Dispose() => remove();

            public void Refresh() { }

            public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { }

            public void Mask() { }
        }
    }

    private sealed class Store : IModuleSettings
    {
        public TextSnippetsSettings Value { get; set; } = new();

        public T Get<T>() where T : class, new() => (T)(object)Value;

        public void Save<T>(T settings) where T : class => Value = (TextSnippetsSettings)(object)settings;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>>? Commands { get; private set; }

        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Text snippets has no tile");

        public void SetSettingsPage(Func<string?, object> page) { }

        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
    }

    private sealed class Sender : ISnippetSender
    {
        public List<(int Backspaces, string Text)> Sent { get; } = [];

        public Task<bool> SendAsync(int backspaces, string text, Delimiter? after, Func<bool>? valid = null, CancellationToken ct = default)
        {
            Sent.Add((backspaces, text));
            return Task.FromResult(true);
        }
    }

    private sealed class Reader : IKeyReader
    {
        public nint Foreground() => 1;

        public bool CommandHeld() => false;

        public char CharOf(uint vk, uint scan) => '\0';
    }

    private static ModuleContext Context(Hub hub, Store store, Ui ui) =>
        new(TextSnippetsModule.Id, store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(TextSnippetsModule.Id), ui) { Input = hub };

    private static Snippet S(string name, string trigger, string text, bool enabled = true) =>
        new() { Name = name, Trigger = trigger, Text = text, Enabled = enabled };

    [Fact]
    public async Task ExpansionStartsOffAndTheFilterIsInTheHubOnlyWhileItsSwitchIsOn()
    {
        var hub = new Hub();
        var store = new Store();
        var module = new TextSnippetsModule(new Sender(), new Reader(), () => false);
        await module.StartAsync(Context(hub, store, new Ui()), default);
        Assert.False(module.Settings.ExpandWhileTyping);
        Assert.Empty(hub.Filters);

        module.Update(s => s with { ExpandWhileTyping = true });
        var filter = Assert.Single(hub.Filters);
        Assert.Equal(InputOrder.Snippets, filter.Order);
        Assert.True(filter.Pauses);
        Assert.True(store.Value.ExpandWhileTyping);

        module.Update(s => s with { ExpandWhileTyping = false });
        Assert.Empty(hub.Filters);

        module.Update(s => s with { ExpandWhileTyping = true });
        await module.StopAsync(default);
        Assert.Empty(hub.Filters);
    }

    [Fact]
    public async Task EnabledSnippetsAreCommandsFoundByNameAndTrigger()
    {
        var ui = new Ui();
        var module = new TextSnippetsModule(new Sender(), new Reader(), () => false);
        await module.StartAsync(Context(new Hub(), new Store { Value = new() { Snippets = [S("Work email", ";em", "a@b.c"), S("Off", ";off", "x", enabled: false)] } }, ui), default);
        var command = Assert.Single(ui.Commands!());
        Assert.Equal("Work email", command.Name);
        Assert.Contains(";em", command.Words);
        Assert.Equal(";em", command.Detail);
    }

    [Fact]
    public async Task InsertTypesTheTextWithNoBackspacesEvenWithExpansionOff()
    {
        var sender = new Sender();
        var ui = new Ui();
        var module = new TextSnippetsModule(sender, new Reader(), () => false);
        await module.StartAsync(Context(new Hub(), new Store { Value = new() { Snippets = [S("Hi", ";hi", "Hello {{date:yyyy}}")] } }, ui), default);
        await ui.Commands!()[0].Run();
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(0, sent.Backspaces);
        Assert.Equal($"Hello {DateTime.Now:yyyy}", sent.Text);
    }

    [Fact]
    public async Task InsertRefusesAnAdminWindowAndAMissingSnippet()
    {
        var sender = new Sender();
        var module = new TextSnippetsModule(sender, new Reader(), () => false);
        var snippet = S("Hi", ";hi", "x");
        await module.StartAsync(Context(new Hub { ForegroundElevated = true }, new Store { Value = new() { Snippets = [snippet] } }, new Ui()), default);
        var admin = await Assert.ThrowsAsync<InvalidOperationException>(() => module.InsertAsync(snippet.Id));
        Assert.Equal(TextSnippetsModule.AdminWindow, admin.Message);
        var gone = await Assert.ThrowsAsync<InvalidOperationException>(() => module.InsertAsync("nope"));
        Assert.Equal(TextSnippetsModule.NotInserted, gone.Message);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task UnknownSoundsAndBlankAppsAreNormalized()
    {
        var module = new TextSnippetsModule(new Sender(), new Reader(), () => false);
        await module.StartAsync(Context(new Hub(), new Store(), new Ui()), default);
        module.Update(s => s with { Sound = "Gone", IgnoredApps = [@"C:\a\x.exe", " ", @"c:\a\X.EXE"] });
        Assert.Null(module.Settings.Sound);
        Assert.Single(module.Settings.IgnoredApps);
    }
}
