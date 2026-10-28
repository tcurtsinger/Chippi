// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Clipboard;

namespace Chippi.Clipboard.Tests;

/// <summary>
/// An in-memory clipboard with Windows' rules: a sequence number that moves with each new copy (when it's
/// written, not when it's closed, and not with a delayed render), owner tracking, delayed rendering and change events. Tests never touch the real one.
/// </summary>
public sealed class FakeClipboard : IClipboardSystem
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, byte[]> _data = [];
    private readonly Dictionary<string, (Func<byte[]> Render, Action? Requested)> _delayed = [];
    private uint _sequence = 1;
    private string? _owner;

    public event Action? Changed;

    public bool Listening { get; set; }

    /// <summary>Opens that fail before one succeeds; int.MaxValue keeps it busy.</summary>
    public int BusyOpens { get; set; }

    /// <summary>Runs after the clipboard is opened and before the work: another app's copy that raced in.</summary>
    public Action? OnOpen { get; set; }

    /// <summary>Every content format read through a session (privacy tests check nothing was read).</summary>
    public List<string> Reads { get; } = [];

    public int Transactions { get; private set; }

    public uint Sequence { get { lock (_gate) return _sequence; } }

    public string? OwnerPath() { lock (_gate) return _owner; }

    public bool IsAvailable(string format) { lock (_gate) return _data.ContainsKey(format) || _delayed.ContainsKey(format); }

    /// <summary>Another app copies: replaces everything, moves the sequence number, notifies listeners.</summary>
    public void Copy(string? owner, params (string Format, byte[] Data)[] formats)
    {
        lock (_gate)
        {
            _data.Clear();
            _delayed.Clear();
            foreach (var (format, data) in formats)
                _data[format] = data;
            _owner = owner;
            _sequence++;
        }
        if (Listening)
            Changed?.Invoke();
    }

    public void CopyText(string text, string? owner = @"C:\Apps\Editor.exe", params (string Format, byte[] Data)[] extra) =>
        Copy(owner, [(ClipFormat.UnicodeText, ClipCodec.EncodeText(text)), .. extra]);

    /// <summary>Another app reads a format, rendering it if it's offered delayed (what a paste target does).</summary>
    public byte[]? ExternalRead(string format)
    {
        lock (_gate)
            return Fetch(format);
    }

    public string? Text => ExternalRead(ClipFormat.UnicodeText) is { } data ? ClipCodec.DecodeText(data, out _) : null;

    public IReadOnlyDictionary<string, byte[]> Snapshot() { lock (_gate) return new Dictionary<string, byte[]>(_data); }

    public Task<T> TransactAsync<T>(Func<IClipboardSession, T> work, CancellationToken cancel = default)
    {
        lock (_gate)
        {
            Transactions++;
            if (BusyOpens > 0)
            {
                if (BusyOpens != int.MaxValue)
                    BusyOpens--;
                return Task.FromException<T>(new ClipboardBusyException());
            }
        }
        OnOpen?.Invoke();
        var session = new Session(this);
        T result;
        lock (_gate)
            result = work(session);
        if (session.Changed)
        {
            lock (_gate)
                _owner = "Chippi";
            if (Listening)
                Changed?.Invoke();
        }
        AfterClose?.Invoke();
        return Task.FromResult(result);
    }

    /// <summary>Runs after the clipboard is closed and before the caller continues: another app's copy that raced in.</summary>
    public Action? AfterClose { get; set; }

    public void Dispose() { }

    private byte[]? Fetch(string format)
    {
        if (_data.TryGetValue(format, out var data))
            return data;
        if (!_delayed.Remove(format, out var offer))
            return null;
        _data[format] = offer.Render();
        offer.Requested?.Invoke();
        return _data[format];
    }

    private sealed class Session(FakeClipboard owner) : IClipboardSession
    {
        private bool _changed;

        /// <summary>The first change in a transaction moves the sequence number, while the clipboard is still held.</summary>
        public bool Changed
        {
            get => _changed;
            private set
            {
                if (!_changed)
                    owner._sequence++;
                _changed = value;
            }
        }

        public uint Sequence => owner._sequence;

        public string? OwnerPath() => owner._owner;

        public bool IsAvailable(string format) => owner._data.ContainsKey(format) || owner._delayed.ContainsKey(format);

        public IReadOnlyList<string> Formats() => [.. owner._data.Keys, .. owner._delayed.Keys];

        public long SizeOf(string format) => owner.Fetch(format)?.Length ?? -1;

        public byte[]? Get(string format)
        {
            owner.Reads.Add(format);
            return owner.Fetch(format);
        }

        public void Empty()
        {
            owner._data.Clear();
            owner._delayed.Clear();
            Changed = true;
        }

        public void Set(string format, byte[] data)
        {
            owner._data[format] = data;
            Changed = true;
        }

        public void SetDelayed(string format, Func<byte[]> render, Action? requested = null)
        {
            owner._delayed[format] = (render, requested);
            Changed = true;
        }
    }
}
