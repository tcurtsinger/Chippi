// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.CloseProtection.Tests;

public sealed class CloseProtectionModuleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstPressQueuesFeedbackOrBeepAndExpiryClearsIt(bool visible)
    {
        var f = new Fixture(visible);
        await f.Start();
        Assert.Equal(InputDevices.None, f.Hub.Filter!.Devices);
        var command = Assert.Single(f.Ui.Commands());
        Assert.Equal(ActionsTile.Input, command.Tile);
        Assert.False(command.IsOn);
        await command.Run();
        Assert.True(f.Store.Get<CloseProtectionSettings>().Enabled);
        Assert.True(Assert.Single(f.Ui.Commands()).IsOn);
        await command.Run();
        Assert.False(f.Store.Get<CloseProtectionSettings>().Enabled);
        f.Module.SetEnabled(true);
        Assert.True(f.Key(true));
        Assert.Equal(1, f.Hub.Masks);
        Assert.Empty(f.Activities.Activities);
        Assert.Equal(0, f.Desktop.Beeps);
        f.Desktop.Drain();
        if (visible)
        {
            var activity = Assert.Single(f.Activities.Activities);
            Assert.Equal("Press again to close", activity.Title);
            Assert.Equal("Alt+F4 · Notepad", activity.Source);
            Assert.Equal("ShieldX", activity.Icon);
            Assert.True(activity.IsFeedback);
        }
        else Assert.Equal(1, f.Desktop.Beeps);
        f.Key(false);
        f.Clock.Now = CloseProtectionFilter.IntervalTicks;
        f.Clock.Tick();
        f.Desktop.Drain();
        Assert.Empty(f.Activities.Activities);
        await f.Module.StopAsync(default);
        Assert.True(f.Desktop.Disposed);
        Assert.True(f.Hub.Removed);
    }

    [Fact]
    public async Task FocusPauseAndStopInvalidateQueuedFeedback()
    {
        var f = new Fixture(true);
        await f.Start();
        f.Module.SetEnabled(true);
        f.Key(true);
        f.Desktop.ChangeForeground();
        f.Desktop.Drain();
        Assert.Empty(f.Activities.Activities);
        f.Key(false);
        f.Key(true);
        f.Module.SetPaused(true);
        f.Desktop.Drain();
        Assert.Empty(f.Activities.Activities);
        await f.Module.StopAsync(default);
        f.Desktop.Drain();
        Assert.Empty(f.Activities.Activities);
    }

    [Fact]
    public async Task SettingsSaveFailureDoesNotEnableTheFilter()
    {
        var f = new Fixture(true);
        await f.Start();
        f.Store.Fail = true;
        f.Module.SetEnabled(true);
        Assert.False(f.Module.Settings.Enabled);
        Assert.Equal(InputDevices.None, f.Hub.Filter!.Devices);
        var command = Assert.Single(f.Ui.Commands());
        Assert.Equal(ActionsTile.Input, command.Tile);
        Assert.False(command.IsOn);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Run());
        Assert.Equal("Close protection failed", f.Module.Error);
        await f.Module.StopAsync(default);
    }

    [Fact]
    public async Task FailedDesktopStartDisposesObservation()
    {
        var f = new Fixture(true);
        f.Desktop.FailStart = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Start());
        Assert.True(f.Desktop.Disposed);
        Assert.Null(f.Hub.Filter);
    }

    [Fact]
    public async Task SharedPauseClearsPendingAndResumeNeedsANewFirstPress()
    {
        var f = new Fixture(true);
        await f.Start();
        Assert.True(f.Module.Descriptor.PausesInFullscreen);
        f.Module.SetEnabled(true);
        Assert.True(f.Key(true));
        f.Key(false);
        f.Desktop.Drain();
        Assert.NotEmpty(f.Activities.Activities);
        f.Hub.Pause(true);
        f.Desktop.Drain();
        Assert.Empty(f.Activities.Activities);
        Assert.False(f.Key(true));
        f.Hub.Pause(false);
        Assert.True(f.Key(true));
        await f.Module.StopAsync(default);
    }

    [Fact]
    public async Task FailedMaskStillShowsThePendingLineAndPassesTheSecondPress()
    {
        var f = new Fixture(true);
        await f.Start();
        f.Module.SetEnabled(true);
        f.Hub.FailMask = true;
        Assert.True(f.Key(true));
        f.Desktop.Drain();
        Assert.Equal("Press again to close", Assert.Single(f.Activities.Activities).Title);
        Assert.True(f.Key(false));
        Assert.False(f.Key(true));
        await f.Module.StopAsync(default);
    }

    internal sealed class Fixture(bool visible)
    {
        public Desktop Desktop { get; } = new();
        public Hub Hub { get; } = new();
        public Store Store { get; } = new();
        public Clock Clock { get; } = new();
        public Ui Ui { get; } = new();
        public IslandActivityHub Activities { get; } = new();
        public CloseProtectionModule Module { get; private set; } = null!;
        public async Task Start()
        {
            Module = new CloseProtectionModule(() => Desktop, Clock);
            await Module.StartAsync(new ModuleContext(CloseProtectionModule.Id, Store, NullLogger.Instance,
                Activities.CreatePublisher(CloseProtectionModule.Id), Ui)
            { Input = Hub, CanShowIslandFeedback = () => visible }, default);
        }
        public bool Key(bool down) => Hub.Filter!.Key(new KeyInput(0x73, 0, down, false, false, false, Clock.Now));
    }
    internal sealed class Desktop : ICloseDesktop
    {
        private readonly Queue<Action> _work = new();
        public CloseTarget? Current { get; } = new(1, 1, 1, "notepad.exe", "Notepad", true);
        public int Beeps;
        public bool Disposed;
        public CloseModifiers Modifiers() => CloseModifiers.Alt;
        public event Action? ForegroundChanged;
        public event Action? DesktopChanged { add { } remove { } }
        public bool FailStart;
        public void Start() { if (FailStart) throw new InvalidOperationException("Injected start failure"); }
        public void Post(Action work) => _work.Enqueue(work);
        public void Beep() => Beeps++;
        public Task<IReadOnlyList<CloseApp>> OpenAppsAsync() => Task.FromResult<IReadOnlyList<CloseApp>>([]);
        public void Dispose() => Disposed = true;
        public void ChangeForeground() => ForegroundChanged?.Invoke();
        public void Drain() { while (_work.TryDequeue(out var work)) work(); }
    }
    internal sealed class Hub : IInputHub, IInputRegistration
    {
        public IInputFilter? Filter;
        public bool Removed;
        public bool Paused { get; set; }
        public bool FullscreenInFront => Paused;
        public event Action? PauseChanged;
        public void Pause(bool paused) { Paused = paused; PauseChanged?.Invoke(); }
        public IInputRegistration Add(IInputFilter filter) { Filter = filter; return this; }
        public void Refresh() { }
        public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) => throw new InvalidOperationException("Close protection sends no keys");
        public int Masks;
        public bool FailMask;
        public void Mask() { Masks++; if (FailMask) throw new InvalidOperationException("Injected mask failure"); }
        public void Dispose() => Removed = true;
    }
    internal sealed class Store : IModuleSettings
    {
        private CloseProtectionSettings _settings = new();
        public bool Fail;
        public T Get<T>() where T : class, new() => (T)(object)_settings;
        public void Save<T>(T value) where T : class
        {
            if (Fail) throw new InvalidOperationException("Injected save failure");
            _settings = (CloseProtectionSettings)(object)value;
        }
    }
    internal sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("No Close protection tile");
        public Func<IReadOnlyList<ChippiCommand>> Commands = null!;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
        public Func<string?, object>? Page;
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
    }
    internal sealed class Clock : TimeProvider
    {
        public long Now;
        private TimerCallback? _callback;
        private object? _state;
        public override long TimestampFrequency => Stopwatch.Frequency;
        public override long GetTimestamp() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; return new Timer(); }
        public void Tick() => _callback?.Invoke(_state);
        internal sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
