// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Clipboard.History;

/// <summary>
/// Recording and the saved copies (design notes #8 to #16). Listens only while recording is on and not
/// paused. Store work runs one call at a time off the UI thread; <see cref="Changed"/> may come on any
/// thread. Logs never carry content.
/// </summary>
public sealed class ClipboardHistory : IDisposable
{
    private readonly IClipboardHub _hub;
    private readonly Func<HistoryStore> _open;
    private readonly Func<string, string?> _autoClean;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly Lock _gate = new();
    private HistoryStore? _store;
    private IDisposable? _lease;
    private IReadOnlyList<string> _skip = [];
    private IReadOnlyList<HistoryEntry> _entries = [];
    private ClipLimit _limit;
    private bool _full, _disposed;
    private int _generation; // moves on Clear and Erase all, so a copy read before them isn't saved after

    /// <param name="open">Opens the store; called on first need, so nothing is created while recording has never been on.</param>
    /// <param name="autoClean">The cleaned link when automatic cleaning will rewrite this text, else null (notes #28).</param>
    public ClipboardHistory(IClipboardHub hub, Func<HistoryStore> open, Func<string, string?>? autoClean = null, TimeProvider? time = null, ILogger? log = null)
    {
        _hub = hub;
        _open = open;
        _autoClean = autoClean ?? (_ => null);
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>Raised after the entries, the limit line or the full state change.</summary>
    public event Action? Changed;

    /// <summary>Every saved copy, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries { get { lock (_gate) return _entries; } }

    /// <summary>The last copy's limit, shown until the next copy (notes #13); None when it was saved.</summary>
    public ClipLimit Limit { get { lock (_gate) return _limit; } }

    /// <summary>Pins fill the store: "History full · 128 MiB" until a pin goes (notes #13).</summary>
    public bool Full { get { lock (_gate) return _full; } }

    public bool Listening { get { lock (_gate) return _lease is not null; } }

    /// <summary>Loads what's saved, if anything is. Call once at start.</summary>
    public Task LoadAsync(bool exists) => exists ? RunAsync(store => Publish(store)) : Task.CompletedTask;

    /// <summary>Recording on and not paused listens; apps to skip are executable paths, never read (notes #15, #52).</summary>
    public void Apply(bool recording, bool paused, IReadOnlyList<string> appsToSkip)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _skip = [.. appsToSkip];
            bool listen = recording && !paused;
            if (listen && _lease is null)
                _lease = _hub.Listen(OnCopied, Skip);
            else if (!listen && _lease is not null)
            {
                _lease.Dispose();
                _lease = null;
            }
        }
    }

    public Task PinAsync(long id, bool pinned) => RunAsync(store =>
    {
        store.SetPinned(id, pinned, _time.GetUtcNow());
        if (!pinned)
            Unfull();
        Publish(store);
    });

    public Task DeleteAsync(long id) => RunAsync(store =>
    {
        store.Delete(id);
        Unfull();
        Publish(store);
    });

    /// <summary>Clear history: the unpinned copies among <paramref name="shown"/> (notes #11).</summary>
    public Task ClearAsync(IReadOnlyCollection<long> shown)
    {
        Interlocked.Increment(ref _generation);
        return RunAsync(store =>
        {
            store.ClearUnpinned(shown);
            Unfull();
            Publish(store);
        });
    }

    /// <summary>Erase all: every copy and pin. Recording carries on (notes #11).</summary>
    public Task EraseAllAsync()
    {
        Interlocked.Increment(ref _generation);
        return RunAsync(store =>
        {
            store.EraseAll();
            lock (_gate)
            {
                _full = false;
                _limit = ClipLimit.None;
            }
            Publish(store);
        });
    }

    /// <summary>Whether anything is saved (the uninstall question, notes #56).</summary>
    public bool Any() => Entries.Count > 0;

    /// <summary>Files of <paramref name="entry"/> that are gone. Reads attributes only; an online-only file counts as there (notes #12).</summary>
    public static int MissingFiles(HistoryEntry entry) =>
        entry.Files?.Count(path => !File.Exists(path) && !Directory.Exists(path)) ?? 0;

    /// <summary>Puts the copy on the clipboard. Not saved again: Chippi's own writes aren't recorded.</summary>
    public async Task<ClipStatus> CopyAsync(HistoryEntry entry, CancellationToken cancel = default) =>
        await ContentAsync(entry).ConfigureAwait(false) is { } content ? await _hub.WriteAsync(content, cancel).ConfigureAwait(false) : ClipStatus.Empty;

    /// <summary>Pastes the copy into <paramref name="target"/> (notes #62).</summary>
    public async Task<PasteOutcome> PasteAsync(HistoryEntry entry, PasteTarget target, CancellationToken cancel = default) =>
        await ContentAsync(entry).ConfigureAwait(false) is { } content ? await _hub.PasteAsync(content, target, cancel).ConfigureAwait(false) : PasteOutcome.NoText;

    /// <summary>
    /// Saves text Chippi itself put on the clipboard (Clean URL's Copy, notes #26), which the listener
    /// never sees. Only while recording and not paused.
    /// </summary>
    public Task RecordAsync(string text)
    {
        if (!Listening)
            return Task.CompletedTask;
        return RunAsync(store =>
        {
            if (store.Add(HistoryCapture.FromText(text), _time.GetUtcNow()).Outcome == AddOutcome.Full)
                lock (_gate) _full = true;
            Publish(store);
        });
    }

    /// <summary>A saved picture's PNG, for the selected row's preview.</summary>
    public Task<byte[]?> PictureAsync(long id) => RunAsync(store => store.Picture(id));

    /// <summary>Stops listening and closes the store; a store call in progress finishes first.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _lease?.Dispose();
            _lease = null;
        }
        _worker.Wait();
        try
        {
            _store?.Dispose();
            _store = null;
        }
        finally
        {
            _worker.Release();
        }
    }

    /// <summary>Waits for the store work queued so far (tests).</summary>
    internal async Task IdleAsync()
    {
        await _worker.WaitAsync().ConfigureAwait(false);
        _worker.Release();
    }

    // ---- Recording ----

    private bool Skip(string ownerPath)
    {
        lock (_gate)
            return _skip.Any(app => string.Equals(app, ownerPath, StringComparison.OrdinalIgnoreCase));
    }

    // On a hub thread.
    private void OnCopied(ClipCopy copy)
    {
        // A copy marked "not for history" leaves nothing at all (notes #15).
        if (copy.Privacy != ClipPrivacy.None)
            return;
        int generation = Volatile.Read(ref _generation);
        _ = RunAsync(store =>
        {
            if (generation != Volatile.Read(ref _generation))
                return;
            var (limit, capture) = Capture(copy);
            if (capture is not null && store.Add(capture, copy.At).Outcome == AddOutcome.Full)
                lock (_gate) _full = true;
            lock (_gate) _limit = limit;
            Publish(store);
        });
    }

    private (ClipLimit, HistoryCapture?) Capture(ClipCopy copy)
    {
        if (copy.Limit != ClipLimit.None)
            return (copy.Limit, null);
        var content = copy.Content;
        if (content?.Files is { Count: > 0 } files)
            return (ClipLimit.None, HistoryCapture.FromFiles(files));
        if (content?.Image is { } image)
            return Pictures.Encode(image) is { } picture
                ? (ClipLimit.None, HistoryCapture.FromPicture(image.Width, image.Height, picture.Thumbnail, picture.Png))
                : (ClipLimit.Image, null);
        if (content?.Text is { Length: > 0 } text)
            return (ClipLimit.None, HistoryCapture.FromText(_autoClean(text) ?? text));
        return (ClipLimit.None, null);
    }

    private async Task<ClipContent?> ContentAsync(HistoryEntry entry)
    {
        switch (entry.Kind)
        {
            case EntryKind.Files:
                return entry.Files is { Count: > 0 } files && MissingFiles(entry) == 0 ? ClipContent.FromFiles(files) : null;
            case EntryKind.Picture:
                return await PictureAsync(entry.Id).ConfigureAwait(false) is { } png ? ClipContent.FromImage(Pictures.Decode(png)) : null;
            default:
                return entry.Text is { Length: > 0 } text ? ClipContent.FromText(text) : null;
        }
    }

    private void Unfull()
    {
        lock (_gate)
            _full = false;
    }

    private void Publish(HistoryStore store)
    {
        var entries = store.Load(_time.GetUtcNow());
        lock (_gate)
            _entries = entries;
        Changed?.Invoke();
    }

    private Task RunAsync(Action<HistoryStore> work) => RunAsync<bool>(store =>
    {
        work(store);
        return true;
    });

    private async Task<T?> RunAsync<T>(Func<HistoryStore, T> work)
    {
        await _worker.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return default;
            }
            return await Task.Run(() => work(_store ??= _open())).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Clipboard history: {Error}", ex.GetType().Name);
            return default;
        }
        finally
        {
            _worker.Release();
        }
    }
}
