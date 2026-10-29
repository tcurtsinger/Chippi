// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media.Imaging;
using Chippi.Core.Clipboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Shelf;

/// <summary>
/// The Shelf's items (design notes #37 to #46). Store work runs one call at a time off the UI thread;
/// <see cref="Changed"/> may come on any thread. Logs never carry an item's content or paths.
/// </summary>
public sealed class ShelfService : IDisposable
{
    private readonly IClipboardHub _hub;
    private readonly Func<ShelfStore> _open;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly Lock _gate = new();
    private ShelfStore? _store;
    private IReadOnlyList<ShelfItem> _items = [];
    private ShelfLimit _limit;
    private bool _disposed;

    /// <param name="open">Opens the store; called on first need.</param>
    public ShelfService(IClipboardHub hub, Func<ShelfStore> open, TimeProvider? time = null, ILogger? log = null)
    {
        _hub = hub;
        _open = open;
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>The items the page shows at once, 8 × 5 (refresh brief Part D #7). At this many, drops are refused; nothing is thrown away.</summary>
    public const int MaxItems = 40;

    /// <summary>Raised after the items or the limit line change.</summary>
    public event Action? Changed;

    /// <summary>Every item, newest first.</summary>
    public IReadOnlyList<ShelfItem> Items { get { lock (_gate) return _items; } }

    /// <summary>Why the last drop wasn't kept, until the next drop or a removal (notes #44, #45).</summary>
    public ShelfLimit Limit { get { lock (_gate) return _limit; } }

    /// <summary>Loads what's kept, if anything is. Call once at start.</summary>
    public Task LoadAsync(bool exists) => exists ? RunAsync(Publish) : Task.CompletedTask;

    /// <summary>Keeps dropped files as one item (notes #40); over <see cref="ClipLimits.Files"/> nothing is kept.</summary>
    public Task AddFilesAsync(IReadOnlyList<string> files) =>
        files.Count > ClipLimits.Files ? Refuse(ShelfLimit.Files) : files.Count == 0 ? Task.CompletedTask : AddAsync(ShelfDrop.FromFiles(files));

    /// <summary>Keeps dropped text, or a link when it's one web link.</summary>
    public Task AddTextAsync(string text) => text.Length == 0 ? Task.CompletedTask : AddAsync(ShelfDrop.FromText(text));

    /// <summary>Keeps a dropped picture (frozen) as PNG with a thumbnail, encoded on the worker.</summary>
    public Task AddPictureAsync(BitmapSource picture) => RunAsync(store =>
    {
        if (ShelfPictures.Encode(picture) is not { } encoded)
        {
            SetLimit(ShelfLimit.Picture);
            Publish(store);
            return;
        }
        Keep(store, ShelfDrop.FromPicture(picture.PixelWidth, picture.PixelHeight, encoded.Thumbnail, encoded.Png));
    });

    public Task PinAsync(long id, bool pinned) => RunAsync(store =>
    {
        store.SetPinned(id, pinned);
        Publish(store);
    });

    public Task RemoveAsync(long id) => RunAsync(store =>
    {
        store.Remove(id);
        lock (_gate)
        {
            if (_limit is ShelfLimit.Full or ShelfLimit.Items)
                _limit = ShelfLimit.None;
        }
        Publish(store);
    });

    /// <summary>Puts the item on the clipboard and keeps it (notes #39). A missing file isn't copied.</summary>
    public async Task<ClipStatus> CopyAsync(ShelfItem item, CancellationToken cancel = default) =>
        await ContentAsync(item).ConfigureAwait(false) is { } content ? await _hub.WriteAsync(content, cancel).ConfigureAwait(false) : ClipStatus.Empty;

    /// <summary>A picture's full PNG, for dragging it out.</summary>
    public Task<byte[]?> PictureAsync(long id) => RunAsync(store => store.Picture(id));

    /// <summary>Stops and closes the store; a store call in progress finishes first.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
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

    // ---- Files on disk: attributes only, so nothing online-only downloads (notes #42, #43) ----

    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>A cloud placeholder: listed by name, never opened.</summary>
    public static bool OnlineOnly(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Paths of <paramref name="item"/> that are gone.</summary>
    public static int Missing(ShelfItem item) => item.Files?.Count(path => !Exists(path)) ?? 0;

    // ---- Work ----

    private Task AddAsync(ShelfDrop drop) => RunAsync(store => Keep(store, drop));

    private void Keep(ShelfStore store, ShelfDrop drop)
    {
        if (Items.Count >= MaxItems)
        {
            SetLimit(ShelfLimit.Items);
            Publish(store);
            return;
        }
        SetLimit(store.Add(drop, _time.GetUtcNow()) is null ? ShelfLimit.Full : ShelfLimit.None);
        Publish(store);
    }

    private Task Refuse(ShelfLimit limit) => RunAsync(store =>
    {
        SetLimit(limit);
        Publish(store);
    });

    private void SetLimit(ShelfLimit limit)
    {
        lock (_gate)
            _limit = limit;
    }

    private async Task<ClipContent?> ContentAsync(ShelfItem item)
    {
        switch (item.Kind)
        {
            case ShelfKind.Files:
                return item.Files is { Count: > 0 } files && Missing(item) == 0 ? ClipContent.FromFiles(files) : null;
            case ShelfKind.Picture:
                return await PictureAsync(item.Id).ConfigureAwait(false) is { } png ? ClipContent.FromImage(ShelfPictures.Decode(png)) : null;
            default:
                return item.Text is { Length: > 0 } text ? ClipContent.FromText(text) : null;
        }
    }

    private void Publish(ShelfStore store)
    {
        var items = store.Load();
        lock (_gate)
            _items = items;
        Changed?.Invoke();
    }

    private Task RunAsync(Action<ShelfStore> work) => RunAsync<bool>(store =>
    {
        work(store);
        return true;
    });

    private async Task<T?> RunAsync<T>(Func<ShelfStore, T> work)
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
            _log.LogWarning("Shelf: {Error}", ex.GetType().Name);
            return default;
        }
        finally
        {
            _worker.Release();
        }
    }
}
