// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Modules;

namespace Chippi.Core.Tests;

/// <summary>The shared action model (docs/phase-6/architecture.md §3) on a fake PC. The Radial menu's tests run every kind.</summary>
public sealed class ActionRunnerTests
{
    private static readonly ChippiAction SnapLeft = new() { Kind = ActionKind.Window, Value = nameof(WindowAction.SnapLeft) };

    [Fact]
    public async Task A_window_action_acts_on_its_doors_target()
    {
        var pc = new FakePc { UnderPointer = 11, InFront = 22 };
        var runner = new ActionRunner(pc, () => [], () => null, text => text, beforeIsland: () => 33);

        await runner.RunAsync(SnapLeft, WindowTarget.UnderPointer);
        await runner.RunAsync(SnapLeft, WindowTarget.InFront);
        await runner.RunAsync(SnapLeft, WindowTarget.BeforeIsland);
        await runner.RunAsync(SnapLeft, (nint)44); // as the Radial menu read it when the wheel opened

        Assert.Equal([(11, WindowAction.SnapLeft), (22, WindowAction.SnapLeft), (33, WindowAction.SnapLeft), (44, WindowAction.SnapLeft)], pc.Windowed);
    }

    [Fact]
    public async Task No_window_fails_as_the_bar_says_it_and_other_kinds_never_read_one()
    {
        var pc = new FakePc();
        var runner = new ActionRunner(pc, () => [], () => null, text => text); // no island: nothing was in front before it

        var failure = await Assert.ThrowsAsync<ActionFailure>(() => runner.RunAsync(SnapLeft, WindowTarget.BeforeIsland));
        Assert.Equal("No window", failure.Message);

        await runner.RunAsync(new ChippiAction { Kind = ActionKind.Link, Value = "github.com" }, WindowTarget.UnderPointer);
        Assert.Equal(["https://github.com"], pc.Opened);
        Assert.Equal(0, pc.WindowReads);
    }

    [Fact]
    public async Task Before_island_is_read_as_each_action_runs_and_never_reads_the_pointer_or_front()
    {
        var pc = new FakePc { UnderPointer = 11, InFront = 22 };
        nint before = 33;
        var runner = new ActionRunner(pc, () => [], () => null, text => text, beforeIsland: () => before);

        Assert.Equal((nint)33, runner.WindowOf(WindowTarget.BeforeIsland));

        before = 55; // the island opened again, over another window
        await runner.RunAsync(SnapLeft, WindowTarget.BeforeIsland);

        before = 0; // the island opened with nothing in front
        Assert.Equal((nint)0, runner.WindowOf(WindowTarget.BeforeIsland));
        var failure = await Assert.ThrowsAsync<ActionFailure>(() => runner.RunAsync(SnapLeft, WindowTarget.BeforeIsland));
        Assert.Equal("No window", failure.Message);

        Assert.Equal([(55, WindowAction.SnapLeft)], pc.Windowed);
        Assert.Equal(0, pc.WindowReads);
    }

    [Fact]
    public void Apps_count_as_missing_only_once_the_Start_menu_is_read()
    {
        var pc = new FakePc();
        var runner = new ActionRunner(pc, () => [], () => null, text => text);
        var steam = new ChippiAction { Kind = ActionKind.App, Value = "Steam" };

        Assert.False(runner.IsMissing(steam));
        pc.Apps = ["Spotify"];
        Assert.True(runner.IsMissing(steam));
        Assert.False(runner.IsMissing(steam with { Value = "spotify" }));
    }

    [Fact]
    public async Task A_Chippi_action_runs_its_command_but_never_a_clipboard_copy()
    {
        var ran = new List<string>();
        IReadOnlyList<ChippiCommand> commands =
        [
            new("keep-awake/toggle", "Keep awake", "Coffee", () => { ran.Add("keep-awake"); return Task.CompletedTask; }),
            new("clipboard/7", "A copy", "Clipboard", () => { ran.Add("copy"); return Task.CompletedTask; }) { Kind = ChippiCommandKind.Paste },
        ];
        var runner = new ActionRunner(new FakePc(), () => commands, () => null, text => text);

        await runner.RunAsync(new ChippiAction { Kind = ActionKind.Chippi, Value = "keep-awake/toggle" }, WindowTarget.InFront);
        await Assert.ThrowsAsync<ActionFailure>(() => runner.RunAsync(new ChippiAction { Kind = ActionKind.Chippi, Value = "clipboard/7" }, WindowTarget.InFront));

        Assert.Equal(["keep-awake"], ran);
        Assert.Equal(["keep-awake/toggle"], runner.ChippiCommands().Select(c => c.Key));
    }

    [Theory]
    [InlineData(ActionKind.SettingsPage, "ms-settings:bluetooth", "Bluetooth & devices")]
    [InlineData(ActionKind.SettingsPage, "ms-settings:nowhere", "Settings")]
    [InlineData(ActionKind.App, "Spotify", "Spotify")]
    [InlineData(ActionKind.Window, "OtherScreen", "Other screen")]
    [InlineData(ActionKind.Keys, "ctrl+shift+t", "Ctrl+Shift+T")]
    public void Names_come_from_what_it_does(ActionKind kind, string value, string name) =>
        Assert.Equal(name, ActionCatalog.NameFor(new ChippiAction { Kind = kind, Value = value }, v => v == "ms-settings:bluetooth" ? "Bluetooth & devices" : null));

    internal sealed class FakePc : IActionPc
    {
        public nint UnderPointer { get; init; }
        public nint InFront { get; init; }
        public int WindowReads { get; private set; }
        public IReadOnlyList<string>? Apps { get; set; }
        public List<(nint, WindowAction)> Windowed { get; } = [];
        public List<string> Opened { get; } = [];

        public bool? HasApp(string parsingName) => Apps?.Contains(parsingName, StringComparer.OrdinalIgnoreCase);

        public bool Exists(string path) => false;

        public nint WindowUnderPointer()
        {
            WindowReads++;
            return UnderPointer;
        }

        public nint WindowInFront()
        {
            WindowReads++;
            return InFront;
        }

        public void Open(string target) => Opened.Add(target);

        public void Window(nint window, WindowAction action)
        {
            if (window == 0)
                throw new ActionFailure("No window");
            Windowed.Add((window, action));
        }

        public void Media(MediaAction action) { }

        public Task PressAsync(KeyCombo keys) => Task.CompletedTask;

        public Task TypeAsync(string text) => Task.CompletedTask;

        public void Run(CommandShell shell, string line) { }

        public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => Task.FromResult<(bool, string?)>((false, null));
    }
}
