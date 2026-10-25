// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.KeepAwake.Tests;

/// <summary>The flyout panel's paths: chips start a length without changing the default.</summary>
public sealed class KeepAwakeSessionTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"Chippi-session-{Guid.NewGuid():N}.json");
    private readonly RecordingNative _native = new();
    private readonly KeepAwakeModule _module;
    private readonly ModuleRegistry _registry;
    private readonly IslandActivityHub _hub = new();

    public KeepAwakeSessionTests()
    {
        _module = new KeepAwakeModule(_native);
        _registry = new ModuleRegistry(new ConfigStore(_path), _hub, NullLoggerFactory.Instance);
        _registry.Register(_module);
    }

    public Task InitializeAsync() => _registry.StartAsync();

    public async Task DisposeAsync()
    {
        await _registry.StopAllAsync();
        File.Delete(_path);
    }

    [Fact]
    public async Task A_chip_starts_its_length_and_keeps_the_default()
    {
        var oneHour = KeepAwakeText.Lengths.Single(l => l.Label == "1 hr").Length;

        await _module.StartSessionAsync(oneHour);

        Assert.True(_module.IsOn);
        Assert.True(KeepAwakeText.SameLength(oneHour, _module.Active!));
        Assert.NotNull(_module.Deadline);
        Assert.Equal(new KeepAwakeSettings(), _module.Settings);
    }

    [Fact]
    public async Task Stopping_clears_the_active_length()
    {
        await _module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.Indefinitely });
        Assert.Null(_module.Deadline);

        await _module.StopSessionAsync();

        Assert.False(_module.IsOn);
        Assert.Null(_module.Active);
        Assert.False(_native.Calls.Last().Active);
    }

    [Fact]
    public async Task Keep_screen_on_reaches_a_running_session_without_moving_its_deadline()
    {
        await _module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.Duration, DurationMinutes = 30 });
        var deadline = _module.Deadline;

        await _module.ApplyAsync(_module.Settings with { KeepDisplayOn = true });

        Assert.Equal((true, true), _native.Calls.Last());
        Assert.Equal(deadline, _module.Deadline);
        Assert.Equal(30, _module.Active!.DurationMinutes);
    }

    [Fact]
    public async Task Changing_the_default_length_leaves_a_running_session_alone()
    {
        await _module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.Duration, DurationMinutes = 30 });
        int calls = _native.Calls.Count;

        await _module.ApplyAsync(_module.Settings with { DurationMinutes = 240 });

        Assert.Equal(30, _module.Active!.DurationMinutes);
        Assert.Equal(240, _module.Settings.DurationMinutes);
        Assert.Equal(calls + 1, _native.Calls.Count); // only the screen call, same request
    }

    // Karl's S6 review: the real tile had no status and the activity no tile, so the grid showed neither.
    [Fact]
    public async Task The_island_tile_shows_the_state_and_the_live_activity_points_at_it()
    {
        var tile = Assert.Single(_registry.Features.Single().IslandTiles);
        Assert.Equal("Off", tile.Status);

        await _module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.Duration, DurationMinutes = 60 });
        Assert.Equal("1 hr left", tile.Status);
        var awake = Assert.Single(_hub.Activities);
        Assert.Equal(KeepAwakeModule.TileKey, awake.TileKey);

        await _module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.Indefinitely });
        Assert.Equal("No limit", tile.Status);

        await _module.StopSessionAsync();
        Assert.Equal("Off", tile.Status);
        Assert.Empty(_hub.Activities);
    }

    [Theory]
    [InlineData(0.2, "1 min")]
    [InlineData(45, "45 min")]
    [InlineData(59.5, "1 hr")]
    [InlineData(60, "1 hr")]
    [InlineData(90, "1 hr 30 min")]
    public void Time_left_rounds_up_to_the_minute(double minutes, string expected) =>
        Assert.Equal(expected, KeepAwakeText.Left(TimeSpan.FromMinutes(minutes)));

    private sealed class RecordingNative : IExecutionState
    {
        public ConcurrentQueue<(bool Active, bool Display)> Calls { get; } = new();

        public void Set(bool active, bool display) => Calls.Enqueue((active, display));
    }
}
