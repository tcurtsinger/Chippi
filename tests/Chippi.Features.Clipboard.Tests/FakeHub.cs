// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;

namespace Chippi.Features.Clipboard.Tests;

/// <summary>The hub's contract without a clipboard: tests deliver copies and see what was written or pasted.</summary>
internal sealed class FakeHub : IClipboardHub
{
    private readonly List<(Action<ClipCopy> Copied, Func<string, bool>? Skip)> _leases = [];
    private uint _sequence;

    public int Leases { get { lock (_leases) return _leases.Count; } }

    /// <summary>The next on-demand read.</summary>
    public ClipRead Current { get; set; } = new(ClipStatus.Empty);

    public ClipStatus WriteResult { get; set; } = ClipStatus.Ok;

    public PasteOutcome PasteResult { get; set; } = PasteOutcome.Sent;

    public List<ClipContent> Written { get; } = [];

    public List<(ClipContent Content, PasteTarget Target)> Pasted { get; } = [];

    /// <summary>Another app copies: every lease that doesn't skip <paramref name="owner"/> gets it.</summary>
    public void Copy(ClipContent? content, ClipPrivacy privacy = ClipPrivacy.None, ClipLimit limit = ClipLimit.None, string owner = @"C:\Apps\Editor.exe")
    {
        var copy = new ClipCopy(++_sequence, DateTimeOffset.UtcNow, content, privacy, limit);
        (Action<ClipCopy> Copied, Func<string, bool>? Skip)[] leases;
        lock (_leases)
            leases = [.. _leases];
        foreach (var (copied, skip) in leases)
            if (skip?.Invoke(owner) != true)
                copied(copy);
    }

    public void CopyText(string text, ClipPrivacy privacy = ClipPrivacy.None, string owner = @"C:\Apps\Editor.exe") =>
        Copy(ClipContent.FromText(text), privacy, owner: owner);

    public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null)
    {
        var lease = (copied, skip);
        lock (_leases)
            _leases.Add(lease);
        return new Release(() =>
        {
            lock (_leases)
                _leases.Remove(lease);
        });
    }

    public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => Task.FromResult(Current);

    public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default)
    {
        if (WriteResult == ClipStatus.Ok)
            Written.Add(content);
        return Task.FromResult(WriteResult);
    }

    public PasteTarget? CaptureTarget() => new(1, 1);

    public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default)
    {
        Pasted.Add((content, target));
        return Task.FromResult(PasteResult);
    }

    public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) => Task.FromResult(PasteResult);

    public void Beep() { }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
