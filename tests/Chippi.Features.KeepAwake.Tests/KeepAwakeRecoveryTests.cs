// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.KeepAwake;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.KeepAwake.Tests;

public sealed class KeepAwakeRecoveryTests
{
    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("reconfigure")]
    public async Task Next_toggle_after_native_failure_uses_a_fresh_worker_without_reinstall(string failingOperation)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-recovery-{Guid.NewGuid():N}.json");
        var native = new FailingNative();
        var module = new KeepAwakeModule(native);
        var island = new IslandActivityHub();
        var registry = new ModuleRegistry(new ConfigStore(path), island, NullLoggerFactory.Instance);
        registry.Register(module);
        int celebrations = 0;
        island.Succeeded += (_, _) => Interlocked.Increment(ref celebrations);
        try
        {
            await registry.StartAsync();
            await module.ApplyAsync(new() { KeepDisplayOn = true });
            if (failingOperation != "start") await module.ToggleAsync();
            native.FailNext = failingOperation == "stop" ? "inactive" : "active";
            await Assert.ThrowsAsync<InvalidOperationException>(() => failingOperation == "reconfigure"
                ? module.ApplyAsync(new() { KeepDisplayOn = true, DurationMinutes = 90 })
                : module.ToggleAsync());
            await native.Released.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(module.Tile.IsOn);
            Assert.Null(island.Current);
            Assert.NotNull(module.Error);
            Assert.True(registry.Find("keep-awake")!.IsRunning);
            int oldWorker = native.Calls.First().Thread;

            await module.ToggleAsync();

            Assert.True(module.Tile.IsOn);
            Assert.Null(module.Error);
            Assert.Equal("keep-awake", island.Current!.ModuleId);
            Assert.Equal(IslandActivityKind.Status, island.Current.Kind);
            var recoveredStart = native.Calls.Last();
            Assert.True(recoveredStart.Active);
            Assert.True(recoveredStart.Display);
            Assert.NotEqual(oldWorker, recoveredStart.Thread);
            Assert.Equal(0, celebrations);
            Assert.Single(registry.Find("keep-awake")!.ToolTiles);

            await registry.SetInstalledAsync("keep-awake", false);
            Assert.False(native.Calls.Last().Active);
            Assert.Equal(recoveredStart.Thread, native.Calls.Last().Thread);
            Assert.False(module.Tile.IsOn);
            Assert.Null(island.Current);
            Assert.Equal(0, celebrations);
            await Assert.ThrowsAsync<InvalidOperationException>(() => module.ToggleAsync());
        }
        finally
        {
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Recovery_waits_for_failed_worker_release_before_starting_fresh_worker()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Chippi-recovery-{Guid.NewGuid():N}.json");
        using var releaseGate = new ManualResetEventSlim();
        var native = new FailingNative { FailNext = "active", ReleaseGate = releaseGate };
        var module = new KeepAwakeModule(native);
        var registry = new ModuleRegistry(new ConfigStore(path), new IslandActivityHub(), NullLoggerFactory.Instance);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => module.ToggleAsync());
            await native.Releasing.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var retry = module.ToggleAsync();

            Assert.False(retry.IsCompleted);
            Assert.Single(native.Calls, c => c.Active);
            releaseGate.Set();
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(module.Tile.IsOn);
            Assert.Equal(2, native.Calls.Count(c => c.Active));
            Assert.NotEqual(native.Calls.First().Thread, native.Calls.Last().Thread);
        }
        finally
        {
            releaseGate.Set();
            await registry.StopAllAsync();
            File.Delete(path);
        }
    }

    private sealed class FailingNative : IExecutionState
    {
        public string? FailNext;
        public ManualResetEventSlim? ReleaseGate { get; init; }
        public TaskCompletionSource Releasing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<(bool Active, bool Display, int Thread)> Calls { get; } = new();
        public void Set(bool active, bool display)
        {
            Calls.Enqueue((active, display, Environment.CurrentManagedThreadId));
            if (FailNext == (active ? "active" : "inactive"))
            {
                FailNext = null;
                throw new InvalidOperationException("Simulated Windows execution-state failure");
            }
            if (!active)
            {
                Releasing.TrySetResult();
                ReleaseGate?.Wait();
                Released.TrySetResult();
            }
        }
    }
}
