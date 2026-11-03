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
    /// <summary>Edits and navigation notify on the caller; disk results notify on the store worker.</summary>
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

    public Task EditAsync(string text) => State.Document is { } doc
        ? EditAsync(doc.SelectedId, text) : ApplyAsync(d => d.Edit(text), typing: true);

    public Task EditAsync(long id, string text) => ApplyAsync(d => d.Edit(id, text), typing: true);

    public Task<bool> ChangeAsync(Func<ScratchpadDocument, ScratchpadDocument> change) => ApplyAsync(change, typing: false);

    private Task<bool> ApplyAsync(Func<ScratchpadDocument, ScratchpadDocument> change, bool typing)
    {
        Task<bool> completion;
        bool changed;
        lock (_gate)
        {
            if (_stop is not null)
                return Task.FromException<bool>(new ObjectDisposedException(nameof(ScratchpadSession)));
            if (State.Document is null)
            {
                // A caller can queue a command immediately after starting the initial load.
                return Enqueue(() =>
                {
                    bool applied;
                    lock (_gate) applied = Apply(change, typing);
                    if (applied) Changed?.Invoke(State);
                    return typing ? State.Document is not null : Save();
                });
            }
            try { changed = Apply(change, typing); }
            catch (Exception error) { return Task.FromException<bool>(error); }
            completion = typing ? Enqueue(() => true) : Enqueue(Save);
        }
        if (changed) Changed?.Invoke(State);
        return completion;
    }

    // Publish the logical document under the lock; only the immutable snapshot goes to disk.
    private bool Apply(Func<ScratchpadDocument, ScratchpadDocument> change, bool typing)
    {
        if (State.Document is not { } document)
            return false;
        var next = change(document);
        if (ReferenceEquals(document, next))
            return false;
        next.Validate();
        Volatile.Write(ref _state, State with { Document = next, HasChanges = true });
        if (typing)
        {
            _due = _time.GetUtcNow() + SaveDelay;
            _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        return true;
    }

    public Task<bool> FlushAsync() => Enqueue(Save);

    private bool Save() => Save(onlyWhenDue: false);

    private bool Save(bool onlyWhenDue)
    {
        IScratchpadStore store;
        ScratchpadDocument snapshot;
        lock (_gate)
        {
            if (onlyWhenDue && _time.GetUtcNow() < _due) return true;
            _due = DateTimeOffset.MaxValue;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (_store is null || State.Document is not { } document) return false;
            if (!State.HasChanges) return true;
            store = _store;
            snapshot = document;
        }
        bool saved;
        try { store.Save(snapshot); saved = true; }
        catch { saved = false; }
        lock (_gate)
        {
            var latest = State;
            Volatile.Write(ref _state, latest with
            {
                SaveFailed = !saved,
                HasChanges = !saved || !ReferenceEquals(latest.Document, snapshot)
            });
        }
        Changed?.Invoke(State);
        return saved;
    }

    private void Set(ScratchpadState state)
    {
        lock (_gate) Volatile.Write(ref _state, state);
        Changed?.Invoke(State);
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
                _queue.Add(() => Save(onlyWhenDue: true));
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
