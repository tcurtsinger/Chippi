// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Clipboard;

/// <summary>
/// <see cref="IClipboardHub"/> over an <see cref="IClipboardSystem"/> and an <see cref="IForeground"/>
/// (architecture §2 to §5). The privacy gate runs while the clipboard is held, before any content is
/// read; markers checked earlier only skip work. Logs never carry clipboard content.
/// </summary>
public sealed class ClipboardHub : IClipboardHub, IDisposable
{
    /// <summary>Apps often write a copy in several steps; read once the sequence number stops moving.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    /// <summary>After the first request for pasted data, how long before the previous copy goes back.</summary>
    public static readonly TimeSpan RestoreAfterRequest = TimeSpan.FromMilliseconds(300);

    /// <summary>When nothing asks for the pasted data, how long before the previous copy goes back (the spike's wait).</summary>
    public static readonly TimeSpan RestoreWithoutRequest = TimeSpan.FromMilliseconds(1500);

    public static readonly TimeSpan FrontTimeout = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan ModifiersTimeout = TimeSpan.FromSeconds(1);

    private readonly IClipboardSystem _clipboard;
    private readonly IForeground _foreground;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private readonly List<Lease> _leases = [];
    private readonly Queue<uint> _own = new();
    private readonly SemaphoreSlim _pasting = new(1, 1);
    private ITimer? _settle;
    private Task _processing = Task.CompletedTask;
    private bool _disposed;

    /// <summary>The latest copy being read (tests wait on it).</summary>
    internal Task Processing => _processing;

    public ClipboardHub(IClipboardSystem clipboard, IForeground foreground, TimeProvider? time = null, ILogger? log = null)
    {
        _clipboard = clipboard;
        _foreground = foreground;
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger.Instance;
        _clipboard.Changed += OnChanged;
    }

    public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null)
    {
        var lease = new Lease(this, copied, skip);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _leases.Add(lease);
            _clipboard.Listening = true;
        }
        return lease;
    }

    public async Task<ClipRead> ReadAsync(CancellationToken cancel = default)
    {
        try
        {
            return await _clipboard.TransactAsync(session =>
            {
                uint seen = session.Sequence;
                var read = Read(session, out _);
                // A delayed-render read runs code in the source app: drop the read if the copy moved meanwhile.
                return session.Sequence == seen ? read : new ClipRead(ClipStatus.Busy);
            }, cancel).ConfigureAwait(false);
        }
        catch (ClipboardBusyException)
        {
            return new ClipRead(ClipStatus.Busy);
        }
    }

    public async Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default)
    {
        try
        {
            var (_, written) = await _clipboard.WriteAsync(session =>
            {
                session.Empty();
                Put(session, content);
                return true;
            }, cancel).ConfigureAwait(false);
            MarkOwn(written);
            return ClipStatus.Ok;
        }
        catch (ClipboardBusyException)
        {
            return ClipStatus.Busy;
        }
    }

    public PasteTarget? CaptureTarget() => _foreground.Current();

    public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) =>
        PasteCoreAsync(_ => content, ofCopy: false, target, cancel);

    public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) =>
        _foreground.Current() is { } target
            ? PasteCoreAsync(saved => saved.Text is { } text ? ClipContent.FromText(text) : null, ofCopy: true, target, cancel)
            : PlainWithoutTargetAsync(cancel);

    public void Beep() => _foreground.Beep();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _leases.Clear();
            _settle?.Dispose();
            _clipboard.Listening = false;
        }
        _clipboard.Changed -= OnChanged;
    }

    // ---- Listening ----

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed || _leases.Count == 0)
                return;
            _settle ??= _time.CreateTimer(_ => _processing = Task.Run(ProcessAsync), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _settle.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ProcessAsync()
    {
        uint seen = _clipboard.Sequence;
        Lease[] leases;
        lock (_gate)
        {
            if (_disposed || _own.Contains(seen))
                return;
            leases = [.. _leases];
        }
        // Early exits only; the real gate is inside the transaction.
        if (leases.Length == 0 || _clipboard.IsAvailable(ClipFormat.Exclude) || _clipboard.IsAvailable(ClipFormat.ViewerIgnore))
            return;
        ClipCopy? copy;
        Lease[] wanted;
        try
        {
            (copy, wanted) = await _clipboard.TransactAsync<(ClipCopy?, Lease[])>(session =>
            {
                if (session.Sequence != seen)
                    return (null, []);
                string? owner = session.OwnerPath();
                Lease[] wanting = owner is null ? leases : [.. leases.Where(l => l.Skip?.Invoke(owner) != true)];
                if (wanting.Length == 0)
                    return (null, wanting);
                var read = Read(session, out var limit);
                // A delayed-render read runs code in the source app: drop the read if the copy moved meanwhile.
                if (session.Sequence != seen || read.Status is not (ClipStatus.Ok or ClipStatus.TooLarge))
                    return (null, wanting);
                return (new ClipCopy(seen, _time.GetUtcNow(), read.Content, read.Privacy, limit), wanting);
            }).ConfigureAwait(false);
        }
        catch (ClipboardBusyException)
        {
            _log.LogInformation("Clipboard busy; copy {Sequence} not read", seen);
            return;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Reading copy {Sequence} failed: {Error}", seen, ex.GetType().Name);
            return;
        }
        if (copy is null)
            return;
        foreach (var lease in wanted)
        {
            lock (_gate)
            {
                if (!_leases.Contains(lease))
                    continue;
            }
            try
            {
                lease.Copied(copy);
            }
            catch (Exception ex)
            {
                _log.LogError("A clipboard listener failed: {Error}", ex.GetType().Name);
            }
        }
    }

    private void Release(Lease lease)
    {
        lock (_gate)
        {
            if (!_leases.Remove(lease) || _leases.Count > 0 || _disposed)
                return;
            _settle?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _clipboard.Listening = false;
        }
    }

    private sealed class Lease(ClipboardHub owner, Action<ClipCopy> copied, Func<string, bool>? skip) : IDisposable
    {
        public Action<ClipCopy> Copied { get; } = copied;

        public Func<string, bool>? Skip { get; } = skip;

        public void Dispose() => owner.Release(this);
    }

    // ---- The gate and the reader ----

    /// <summary>Must run inside a transaction. Markers first; no content format is touched for a private copy.</summary>
    internal static ClipRead Read(IClipboardSession session, out ClipLimit limit)
    {
        limit = ClipLimit.None;
        if (session.IsAvailable(ClipFormat.Exclude) || session.IsAvailable(ClipFormat.ViewerIgnore))
            return new ClipRead(ClipStatus.Private);
        var privacy = session.IsAvailable(ClipFormat.History) && !ClipCodec.HistoryAllowed(session.Get(ClipFormat.History))
            ? ClipPrivacy.NoHistory
            : ClipPrivacy.None;

        if (session.IsAvailable(ClipFormat.Files) && ClipCodec.DecodeFiles(session.Get(ClipFormat.Files) ?? []) is { Count: > 0 } files)
        {
            if (files.Count > ClipLimits.Files)
                return TooLarge(ClipLimit.Files, privacy, out limit);
            return new ClipRead(ClipStatus.Ok, ClipContent.FromFiles(files), privacy);
        }
        foreach (var format in new[] { ClipFormat.DibV5, ClipFormat.Dib })
        {
            if (!session.IsAvailable(format) || session.Get(format) is not { } dib || !ClipCodec.TryDibSize(dib, out int w, out int h))
                continue;
            if ((long)w * h > ClipLimits.ImagePixels)
                return TooLarge(ClipLimit.Image, privacy, out limit);
            if (ClipCodec.DecodeDib(dib) is { } image)
                return new ClipRead(ClipStatus.Ok, ClipContent.FromImage(image), privacy);
        }
        if (session.IsAvailable(ClipFormat.UnicodeText))
        {
            if (session.SizeOf(ClipFormat.UnicodeText) > ClipLimits.TextRawBytes)
                return TooLarge(ClipLimit.Text, privacy, out limit);
            var text = ClipCodec.DecodeText(session.Get(ClipFormat.UnicodeText) ?? [], out bool tooLarge);
            if (tooLarge)
                return TooLarge(ClipLimit.Text, privacy, out limit);
            if (!string.IsNullOrEmpty(text))
                return new ClipRead(ClipStatus.Ok, ClipContent.FromText(text), privacy);
        }
        return new ClipRead(ClipStatus.Empty, Privacy: privacy);
    }

    private static ClipRead TooLarge(ClipLimit which, ClipPrivacy privacy, out ClipLimit limit)
    {
        limit = which;
        return new ClipRead(ClipStatus.TooLarge, Privacy: privacy, Limit: which);
    }

    private static void Put(IClipboardSession session, ClipContent content)
    {
        if (content.Files is { Count: > 0 } files)
        {
            session.Set(ClipFormat.Files, ClipCodec.EncodeFiles(files));
            session.Set(ClipFormat.DropEffect, ClipCodec.CopyEffect);
        }
        if (content.Image is { } image)
            session.Set(ClipFormat.Dib, ClipCodec.EncodeDib(image));
        if (!string.IsNullOrEmpty(content.Text))
            session.Set(ClipFormat.UnicodeText, ClipCodec.EncodeText(content.Text));
    }

    /// <summary>Takes the sequence number the clipboard reported for the write, never one sampled later: another copy can land meanwhile.</summary>
    private void MarkOwn(uint sequence)
    {
        if (sequence == 0)
            return;
        lock (_gate)
        {
            _own.Enqueue(sequence);
            while (_own.Count > 8)
                _own.Dequeue();
        }
    }

    // ---- Pasting ----

    /// <summary>What a copy tells other clipboard managers; a plain-text copy of it says the same.</summary>
    private static readonly string[] Markers = [ClipFormat.Exclude, ClipFormat.ViewerIgnore, ClipFormat.History, ClipFormat.Cloud];

    private static readonly (string, byte[])[] Excluded = [(ClipFormat.Exclude, ClipCodec.Dword(0))];

    /// <summary>The previous copy, kept to put back: every plain-memory format, its markers included.</summary>
    private sealed record Saved(IReadOnlyList<(string Format, byte[] Data)> Formats, string? Text, IReadOnlyList<(string Format, byte[] Data)> Markers);

    private static Saved Save(IClipboardSession session)
    {
        var formats = new List<(string, byte[])>();
        foreach (var format in session.Formats())
            if (session.Get(format) is { } data)
                formats.Add((format, data));
        var markers = new List<(string, byte[])>();
        foreach (var marker in Markers)
            if (session.IsAvailable(marker))
                markers.Add((marker, session.Get(marker) ?? ClipCodec.Dword(0)));
        string? text = null;
        if (session.IsAvailable(ClipFormat.UnicodeText) && session.SizeOf(ClipFormat.UnicodeText) <= ClipLimits.TextRawBytes)
            text = ClipCodec.DecodeText(session.Get(ClipFormat.UnicodeText) ?? [], out _);
        return new Saved(formats, string.IsNullOrEmpty(text) ? null : text, markers);
    }

    /// <summary>Plain paste with nothing in front: the plain text, alone, becomes the copy.</summary>
    private async Task<PasteOutcome> PlainWithoutTargetAsync(CancellationToken cancel)
    {
        try
        {
            var (wrote, sequence) = await _clipboard.WriteAsync(session =>
            {
                var saved = Save(session);
                if (saved.Text is null)
                    return false;
                session.Empty();
                PutPlain(session, saved.Text, saved.Markers, delayed: null);
                return true;
            }, cancel).ConfigureAwait(false);
            if (!wrote)
                return PasteOutcome.NoText;
            MarkOwn(sequence);
            return PasteOutcome.Copied;
        }
        catch (ClipboardBusyException)
        {
            return PasteOutcome.Busy;
        }
    }

    /// <param name="ofCopy">The content is the current copy converted (plain paste), so it keeps that copy's markers.</param>
    private async Task<PasteOutcome> PasteCoreAsync(Func<Saved, ClipContent?> choose, bool ofCopy, PasteTarget target, CancellationToken cancel)
    {
        await _pasting.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool canSend = _foreground.IsAlive(target) && _foreground.CanSendInput(target);
            Saved saved;
            ClipContent? content;
            uint ours;
            IReadOnlyList<(string, byte[])> markers = [];
            try
            {
                ((saved, content), ours) = await _clipboard.WriteAsync(session =>
                {
                    var previous = Save(session);
                    var chosen = choose(previous);
                    if (chosen is null || chosen.IsEmpty)
                        return (previous, (ClipContent?)null);
                    if (ofCopy)
                        markers = previous.Markers;
                    session.Empty();
                    // Not sending keys: the content simply becomes the copy, with the markers it had.
                    if (!canSend)
                    {
                        Put(session, chosen, markers);
                        return (previous, chosen);
                    }
                    // Marked so no clipboard manager (Chippi's own listener included) records the temporary copy.
                    if (IsText(chosen))
                        PutPlain(session, chosen.Text!, [.. markers, .. Excluded], delayed: () => requested.TrySetResult());
                    else
                        Put(session, chosen, [.. markers, .. Excluded]);
                    return (previous, chosen);
                }, cancel).ConfigureAwait(false);
            }
            catch (ClipboardBusyException)
            {
                return PasteOutcome.Busy;
            }
            if (content is null)
                return PasteOutcome.NoText;
            MarkOwn(ours);
            if (!canSend)
                return PasteOutcome.Copied;
            if (!await _foreground.BringToFrontAsync(target, FrontTimeout, cancel).ConfigureAwait(false))
            {
                // The target is gone or wouldn't come forward: leave the content as the copy, without the temporary marker.
                await LeaveAsCopyAsync(content, ours, markers, cancel).ConfigureAwait(false);
                return PasteOutcome.Copied;
            }
            await _foreground.WaitForModifiersReleasedAsync(ModifiersTimeout, cancel).ConfigureAwait(false);
            _foreground.SendPaste();
            // Restore timing is a heuristic: the first data request may come from another reader, and
            // a slow target can read after the restore (architecture §5).
            var waitForRequest = Task.Delay(RestoreWithoutRequest, _time, cancel);
            if (await Task.WhenAny(requested.Task, waitForRequest).ConfigureAwait(false) == requested.Task)
                await Task.Delay(RestoreAfterRequest, _time, cancel).ConfigureAwait(false);
            await RestoreAsync(saved, ours, cancel).ConfigureAwait(false);
            return PasteOutcome.Sent;
        }
        finally
        {
            _pasting.Release();
        }
    }

    private static bool IsText(ClipContent content) => content.Text is not null && content.Files is null && content.Image is null;

    private static void Put(IClipboardSession session, ClipContent content, IReadOnlyList<(string Format, byte[] Data)> markers)
    {
        if (IsText(content))
            PutPlain(session, content.Text!, markers, delayed: null);
        else
        {
            Put(session, content);
            foreach (var (format, data) in markers)
                session.Set(format, data);
        }
    }

    private static void PutPlain(IClipboardSession session, string text, IReadOnlyList<(string Format, byte[] Data)> markers, Action? delayed)
    {
        if (delayed is null)
            session.Set(ClipFormat.UnicodeText, ClipCodec.EncodeText(text));
        else
            session.SetDelayed(ClipFormat.UnicodeText, () => ClipCodec.EncodeText(text), delayed);
        foreach (var (format, data) in markers)
            session.Set(format, data);
    }

    private async Task LeaveAsCopyAsync(ClipContent content, uint ours, IReadOnlyList<(string, byte[])> markers, CancellationToken cancel)
    {
        try
        {
            var (_, written) = await _clipboard.WriteAsync(session =>
            {
                if (session.Sequence != ours)
                    return false;
                session.Empty();
                Put(session, content, markers);
                return true;
            }, cancel).ConfigureAwait(false);
            MarkOwn(written);
        }
        catch (ClipboardBusyException)
        {
            _log.LogInformation("Clipboard busy; paste content left marked");
        }
    }

    /// <summary>Puts the previous copy back, comparing and writing in one open block so a newer copy is never overwritten.</summary>
    private async Task RestoreAsync(Saved saved, uint ours, CancellationToken cancel)
    {
        try
        {
            var (_, written) = await _clipboard.WriteAsync(session =>
            {
                if (session.Sequence != ours)
                    return false;
                session.Empty();
                foreach (var (format, data) in saved.Formats)
                    session.Set(format, data);
                return true;
            }, cancel).ConfigureAwait(false);
            MarkOwn(written);
        }
        catch (ClipboardBusyException)
        {
            _log.LogInformation("Clipboard busy; previous copy not restored");
        }
    }
}
