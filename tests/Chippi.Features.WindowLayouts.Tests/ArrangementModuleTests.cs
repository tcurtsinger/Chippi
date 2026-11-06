// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.WindowLayouts.Tests;

public sealed class ArrangementModuleTests
{
    [Fact]
    public async Task Cancel_during_discovery_starts_no_apps_and_a_second_invocation_reuses_the_run()
    {
        Fixture? fixture = null;
        Task? first = null;
        WpfTestThread.Run(() =>
        {
            fixture = new();
            first = fixture.Module.LaunchArrangementAsync("work");
            Assert.Same(first, fixture.Module.LaunchArrangementAsync("work"));
            fixture.Module.CancelArrangement();
            fixture.Desktop.Prepared.SetResult();
        });
        await first!;
        WpfTestThread.Run(() => {
            Assert.Equal(0, fixture!.Desktop.Launched);
            Assert.Single(fixture.Module.Arrangements!.Results);
            Assert.Equal(ArrangementRowState.Cancelled, fixture.Module.Arrangements.Results[0].State);
            fixture.Stop();
        });
    }

    [Fact]
    public async Task Stop_during_discovery_prevents_a_late_launch()
    {
        Fixture? fixture = null;
        Task? pending = null;
        WpfTestThread.Run(() =>
        {
            fixture = new();
            pending = fixture.Module.LaunchArrangementAsync("work");
            fixture.Stop();
            fixture.Desktop.Prepared.SetResult();
        });
        await pending!;
        Assert.Equal(0, fixture!.Desktop.Launched);
    }

    [Fact]
    public void Deleted_arrangement_removes_its_action_and_shortcut()
    {
        WpfTestThread.Run(() =>
        {
            var fixture = new Fixture();
            Assert.Contains(fixture.Ui.Commands(), c => c.Key == "window-layouts/arrangement/work");
            Assert.Single(fixture.Shortcuts.List);
            fixture.Module.Layouts!.DeleteArrangement("work");
            Assert.DoesNotContain(fixture.Ui.Commands(), c => c.Key == "window-layouts/arrangement/work");
            Assert.Empty(fixture.Shortcuts.List);
            fixture.Stop();
        });
    }

    [Fact]
    public void Failed_shortcut_save_restores_the_previous_binding()
    {
        WpfTestThread.Run(() =>
        {
            var fixture = new Fixture();
            var previous = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x74);
            var next = previous with { Key = 0x75 };
            long id = fixture.Shortcuts.List.Single().Id;
            Assert.Null(fixture.Shortcuts.Change(id, previous));
            fixture.Store.FailSave = true;
            Assert.Equal("Save failed", fixture.Shortcuts.Change(id, next));
            Assert.Equal(previous, fixture.Store.Value.Arrangements.Single().Shortcut);
            Assert.Equal(previous, fixture.Registrar.Held.Values.Single());
            fixture.Stop();
        });
    }

    [Fact]
    public async Task Lock_during_discovery_reports_a_stop_and_starts_no_apps()
    {
        Fixture? fixture = null;
        Task? pending = null;
        WpfTestThread.Run(() =>
        {
            fixture = new();
            pending = fixture.Module.LaunchArrangementAsync("work");
            fixture.Module.LockArrangements();
            fixture.Desktop.Prepared.SetResult();
        });
        await pending!;
        WpfTestThread.Run(() =>
        {
            Assert.Equal(0, fixture!.Desktop.Launched);
            Assert.Equal("Session locked", ArrangementSummary.Of(fixture.Module).Stopped);
            Assert.Equal(1, ArrangementSummary.Of(fixture.Module).Cancelled);
            fixture.Stop();
        });
    }

    private sealed class Fixture
    {
        public readonly Store Store = new();
        public readonly Ui Ui = new();
        public readonly Desktop Desktop = new();
        public readonly Registrar Registrar = new();
        public readonly ShortcutService Shortcuts;
        public readonly WindowLayoutsModule Module = new();
        public Fixture()
        {
            Shortcuts = new(Registrar);
            Module.CreateArrangementDesktop = _ => Desktop;
            Module.PointerDisplayId = () => "pointer";
            Module.StartAsync(new ModuleContext(WindowLayoutsModule.Id, Store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(WindowLayoutsModule.Id), Ui) { Shortcuts = Shortcuts }, default)
                .GetAwaiter().GetResult();
        }
        public void Stop() => Module.StopAsync(default).GetAwaiter().GetResult();
    }
    private sealed class Store : IModuleSettings
    {
        public bool FailSave;
        public WindowLayoutsSettings Value = new() { Arrangements = [new() { Id = "work", Name = "Work", Rows = [new()
        { AppIdentity = "editor", Target = "C:\\apps\\editor.exe", DisplayId = "display",
            Placement = new(0, 0, 500, 400, 0, 0, 1920, 1040, false, false) }] }] };
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        { if (FailSave) throw new IOException("Disk full"); Value = (WindowLayoutsSettings)(object)value; }
    }
    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>> Commands = () => [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException();
        public void SetSettingsPage(Func<string?, object> view) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
    }
    private sealed class Registrar : IHotkeyRegistrar
    {
        public Dictionary<int, KeyShortcut> Held = [];
        public bool Register(int id, KeyShortcut keys) { Held[id] = keys; return true; }
        public void Unregister(int id) => Held.Remove(id);
        public event Action<int>? Pressed { add { } remove { } }
    }
    private sealed class Desktop : IArrangementDesktop
    {
        public readonly TaskCompletionSource Prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Launched;
        public Task PrepareAsync() => Prepared.Task;
        public IReadOnlyList<ArrangementLiveWindow> Windows() => [];
        public IReadOnlyList<SnapDisplay> Displays() => [];
        public string? StopReason(IReadOnlySet<ArrangementProcess> launched) => null;
        public ArrangementProcess? Launch(ArrangementRow row) { Launched++; return null; }
        public void Place(ActionWindow target, SavedWindowPlacement placement, nint monitor) => throw new InvalidOperationException();
    }
}
