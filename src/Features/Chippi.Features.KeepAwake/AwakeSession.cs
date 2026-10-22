// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace Chippi.Features.KeepAwake;

public interface IExecutionState
{
    void Set(bool active, bool display);
}

internal sealed class WindowsExecutionState : IExecutionState
{
    public void Set(bool active, bool display)
    {
        var flags = EXECUTION_STATE.ES_CONTINUOUS;
        if (active) flags |= EXECUTION_STATE.ES_SYSTEM_REQUIRED;
        if (active && display) flags |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
        if (PInvoke.SetThreadExecutionState(flags) == 0)
            throw new InvalidOperationException("Windows could not change the sleep prevention request.");
    }
}

/// <summary>All native calls, including release, run on this one thread. No UI thread waits for it.</summary>
public sealed class AwakeSession : IAsyncDisposable
{
    private readonly BlockingCollection<(Action Run, TaskCompletionSource Completion)> _commands = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IExecutionState _native;
    private readonly TimeProvider _clock;
    private KeepAwakeSettings? _settings;
    private DateTimeOffset? _deadline;
    private long _started;
    private bool _active;
    private readonly Lock _gate = new();
    private bool _disposed;

    public AwakeSession(IExecutionState native, TimeProvider? clock = null)
    {
        _native = native;
        _clock = clock ?? TimeProvider.System;
        new Thread(Work) { IsBackground = true, Name = "Chippi Keep awake" }.Start();
    }

    public event Action<bool, DateTimeOffset?, bool, Exception?>? Changed;

    public Task StartAsync(KeepAwakeSettings settings) => Enqueue(() =>
    {
        settings.Validate();
        var deadline = settings.Deadline(_clock.GetLocalNow());
        _native.Set(true, settings.KeepDisplayOn);
        _settings = settings;
        _deadline = deadline;
        _started = _clock.GetTimestamp();
        _active = true;
        Notify(false, null);
    });

    public Task StopAsync() => Enqueue(() => Release(false));

    // Also lets timing tests advance an injected clock without sleeping.
    public Task CheckAsync() => Enqueue(CheckDeadline);

    private Task Enqueue(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(AwakeSession)));
            _commands.Add((action, completion));
        }
        return completion.Task;
    }

    private void CheckDeadline()
    {
        if (!_active || _settings is null) return;
        bool elapsed = _settings.Mode == AwakeMode.Duration
            ? _clock.GetElapsedTime(_started) >= TimeSpan.FromMinutes(_settings.DurationMinutes)
            : _deadline is { } end && _clock.GetLocalNow() >= end;
        if (elapsed) Release(true);
    }

    private void Release(bool timed)
    {
        if (!_active) return;
        _native.Set(false, false);
        _active = false;
        _deadline = null;
        Notify(timed, null);
    }

    private void Notify(bool timed, Exception? error) => Changed?.Invoke(_active, _deadline, timed, error);

    private void Work()
    {
        try
        {
            while (!_commands.IsCompleted)
            {
                if (_commands.TryTake(out var command, 250))
                {
                    try { command.Run(); command.Completion.SetResult(); }
                    catch (Exception ex)
                    {
                        command.Completion.SetException(ex);
                        if (ex is not ArgumentException) throw;
                    }
                }
                CheckDeadline();
            }
        }
        catch (Exception ex)
        {
            _active = false;
            Notify(false, ex);
        }
        finally
        {
            try { _native.Set(false, false); }
            catch (Exception ex) { Notify(false, ex); }
            lock (_gate) { _disposed = true; _commands.CompleteAdding(); }
            // Never execute queued starts after a failed worker or after shutdown.
            while (_commands.TryTake(out var pending)) pending.Completion.TrySetException(new ObjectDisposedException(nameof(AwakeSession)));
            _finished.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_disposed) { _disposed = true; _commands.CompleteAdding(); }
        }
        return new ValueTask(_finished.Task);
    }
}
