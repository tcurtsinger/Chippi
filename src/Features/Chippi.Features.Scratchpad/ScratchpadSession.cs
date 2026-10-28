// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;

namespace Chippi.Features.Scratchpad;

public sealed record ScratchpadState(ScratchpadDocument? Document, bool LoadFailed, bool SaveFailed, bool HasChanges);

/// <summary>One serialized store worker; typing debounces, navigation/closing flushes. No clipboard use.</summary>
public sealed class ScratchpadSession : IAsyncDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(800);
    private readonly Func<IScratchpadStore> _open;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Lock _gate = new();
    private readonly Task _worker;
    private Task? _stop;
    private IScratchpadStore? _store;
    private ScratchpadState _state = new(null, false, false, false);
    private DateTimeOffset _due = DateTimeOffset.MaxValue;

    public ScratchpadSession(Func<IScratchpadStore> open, TimeProvider? time = null)
    {
        _open = open;
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _worker = Task.Run(() =>
        {
            try
            {
                foreach (var work in _queue.GetConsumingEnumerable())
                    work();
            }
            finally { _queue.Dispose(); }
        });
    }

    public ScratchpadState State => Volatile.Read(ref _state);
    /// <summary>Raised on the worker; views marshal the snapshot to their dispatcher.</summary>
    public event Action<ScratchpadState>? Changed;

    public Task<bool> LoadAsync() => Enqueue(() =>
    {
        if (State.Document is not null)
            return true;
        try
        {
            _store?.Dispose();
            _store = null;
            _store = _open();
            var document = _store.Load();
            Set(new(document, false, false, false));
            return true;
        }
        catch
        {
            Set(new(null, true, false, false));
            return false;
        }
    });

    public Task EditAsync(string text) => Enqueue(() =>
    {
        if (State.Document is not { } document)
            return false;
        if (document.Selected.Text == text)
            return true;
        Set(State with { Document = document.Edit(text), HasChanges = true });
        _due = _time.GetUtcNow() + SaveDelay;
        _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        return true;
    });

    public Task<bool> ChangeAsync(Func<ScratchpadDocument, ScratchpadDocument> change) => Enqueue(() =>
    {
        if (State.Document is not { } document)
            return false;
        var next = change(document);
        next.Validate();
        Set(State with { Document = next, HasChanges = true });
        return Save();
    });

    public Task<bool> FlushAsync() => Enqueue(Save);

    private bool Save()
    {
        _due = DateTimeOffset.MaxValue;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (_store is null || State.Document is null)
            return false;
        if (!State.HasChanges)
            return true;
        try
        {
            _store.Save(State.Document);
            Set(State with { SaveFailed = false, HasChanges = false });
            return true;
        }
        catch
        {
            Set(State with { SaveFailed = true });
            return false;
        }
    }

    private void Set(ScratchpadState state)
    {
        Volatile.Write(ref _state, state);
        Changed?.Invoke(state);
    }

    private Task<T> Enqueue<T>(Func<T> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stop is not null)
                return Task.FromException<T>(new ObjectDisposedException(nameof(ScratchpadSession)));
            _queue.Add(() =>
            {
                try { result.SetResult(work()); }
                catch (Exception error) { result.SetException(error); }
            });
        }
        return result.Task;
    }

    private void OnTimer()
    {
        lock (_gate)
        {
            if (_stop is null)
                _queue.Add(() => { if (_time.GetUtcNow() >= _due) Save(); });
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_stop is not null)
                return new(_stop);
            _queue.Add(() =>
            {
                try { Save(); }
                finally { _timer.Dispose(); _store?.Dispose(); }
            });
            _queue.CompleteAdding();
            _stop = _worker;
            return new(_stop);
        }
    }
}
