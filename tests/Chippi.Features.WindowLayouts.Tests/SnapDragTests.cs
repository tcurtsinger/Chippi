// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.WindowLayouts.Tests;

public sealed class SnapDragTests
{
    private static readonly ActionWindow Target = new(42, 7, 8, 9, "App", @"C:\app.exe", "App", "Notes", false);
    private static readonly SnapDisplay Display = new("display", "Portrait", 123, true, new(-900, 0, 900, 1600), new(-900, 40, 900, 1560));

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void ShiftReleaseEscapeAndPauseCancelWithoutMovingTheWindow(bool shift, bool escape, bool paused)
    {
        WpfTestThread.Run(() =>
        {
            var f = new Fixture();
            var drag = f.Drag();
            drag.Begin(Target);
            drag.Move(-700, 100, true, false, false);
            Assert.Equal(0, f.Overlay.Pointed);
            drag.Move(-700, 100, shift, escape, paused);
            drag.EndAsync(true, false, false).GetAwaiter().GetResult();
            Assert.Empty(f.Pc.Placed);
            Assert.False(f.Overlay.Visible);
            Assert.Equal(1, f.Pc.Ended);
            f.Stop();
        });
    }

    [Fact]
    public void DropUsesThePointerDisplayAndTheOriginalWindow()
    {
        WpfTestThread.Run(() =>
        {
            var f = new Fixture();
            var drag = f.Drag();
            drag.Begin(Target);
            drag.Move(-200, 100, true, false, false);
            drag.EndAsync(true, false, false).GetAwaiter().GetResult();
            Assert.Equal([(Target, "halves", "right", (nint)123)], f.Pc.Placed);
            Assert.Equal(1, f.Pc.Begun);
            Assert.Equal(1, f.Pc.Ended);
            Assert.False(f.Overlay.Visible);
            f.Stop();
        });
    }

    [Fact]
    public void ElevatedExcludedAndPausedTargetsNeverShowZones()
    {
        WpfTestThread.Run(() =>
        {
            var f = new Fixture();
            var drag = f.Drag();
            drag.Begin(Target with { Elevated = true });
            Assert.False(drag.Active);
            f.Store.Value = f.Store.Value with { ExcludedApps = [Target.Executable] };
            f.Module.Layouts!.Configure(true, 8, 0, [Target.Executable]);
            drag.Begin(Target);
            Assert.False(drag.Active);
            f.Module.Layouts.Configure(true, 8, 0, []);
            f.Pc.Allowed = false;
            drag.Begin(Target);
            Assert.False(drag.Active);
            Assert.Equal(0, f.Overlay.Shown);
            f.Stop();
        });
    }

    [Fact]
    public void RemovedDisplayAndTaskbarDropDoNotCommit()
    {
        WpfTestThread.Run(() =>
        {
            var f = new Fixture();
            var drag = f.Drag();
            drag.Begin(Target);
            drag.Move(-700, 20, true, false, false); // taskbar is outside the work area
            drag.EndAsync(true, false, false).GetAwaiter().GetResult();
            Assert.Empty(f.Pc.Placed);
            drag.Begin(Target);
            drag.Move(-700, 100, true, false, false);
            f.Displays = [];
            drag.Move(-700, 100, true, false, false);
            drag.EndAsync(true, false, false).GetAwaiter().GetResult();
            Assert.Empty(f.Pc.Placed);
            f.Stop();
        });
    }

    private sealed class Fixture
    {
        public readonly Store Store = new();
        public readonly Pc Pc = new();
        public readonly Overlay Overlay = new();
        public IReadOnlyList<SnapDisplay> Displays = [Display];
        public readonly WindowLayoutsModule Module;
        public Fixture()
        {
            Module = new(() => Displays);
            Module.StartAsync(new ModuleContext(WindowLayoutsModule.Id, Store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(WindowLayoutsModule.Id), new Ui()) { Actions = Pc }, default).GetAwaiter().GetResult();
        }
        public SnapDragController Drag() => new(Module, () => Displays, Overlay);
        public void Stop() => Module.StopAsync(default).GetAwaiter().GetResult();
    }
    private sealed class Store : IModuleSettings
    {
        public WindowLayoutsSettings Value = new() { ShiftDragEnabled = true };
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (WindowLayoutsSettings)(object)value;
    }
    private sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException();
        public void SetSettingsPage(Func<string?, object> page) { }
    }
    private sealed class Overlay : IZoneOverlay
    {
        public bool Visible;
        public int Pointed = -1, Shown;
        public void Show(SnapDisplay display, IReadOnlyList<SnapZone> zones) { Visible = true; Shown++; }
        public void Point(int index) => Pointed = index;
        public void Hide() => Visible = false;
        public void Dispose() { }
    }
    private sealed class Pc : IActionPc
    {
        public bool Allowed = true;
        public int Begun, Ended;
        public List<(ActionWindow, string, string, nint)> Placed = [];
        public bool CanSnap(ActionWindow target) => Allowed && !target.Elevated;
        public void BeginWindowDrag(ActionWindow target) => Begun++;
        public void EndWindowDrag(ActionWindow target) => Ended++;
        public void WindowZone(ActionWindow target, string layout, string zone, nint monitor = 0) => Placed.Add((target, layout, zone, monitor));
        public bool? HasApp(string app) => true;
        public bool Exists(string path) => true;
        public nint WindowUnderPointer() => 99;
        public nint WindowInFront() => 99;
        public void Open(string target) { }
        public void Window(nint target, WindowAction action) { }
        public void Media(MediaAction action) { }
        public Task PressAsync(KeyCombo keys) => Task.CompletedTask;
        public Task TypeAsync(string text) => Task.CompletedTask;
        public void Run(CommandShell shell, string line) { }
        public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => Task.FromResult<(bool, string?)>((false, null));
    }
}
