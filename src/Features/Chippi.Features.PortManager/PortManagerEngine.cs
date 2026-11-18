// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;

namespace Chippi.Features.PortManager;

/// <summary>
/// The live list behind Settings › System › Ports and the lookups Search makes. Reads run off the UI thread and are applied on
/// the caller's (the UI thread's) context; nothing runs unless the page is visible (<see cref="SetVisible"/>) or a door asks.
/// New rows are marked for a few seconds and ended ones stay, marked, for a few more (TCPView's idea).
/// </summary>
public sealed class PortManagerEngine : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2), NewFor = TimeSpan.FromSeconds(4), EndedFor = TimeSpan.FromSeconds(3);
    private readonly IPortSystem _system;
    private readonly PortScanner _scanner;
    private readonly PortEnder _ender;
    private readonly Func<long> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Dictionary<string, PortRow> _known = [];
    private readonly Dictionary<string, long> _newUntil = [];
    private readonly Dictionary<string, (PortRow Row, long Until)> _ended = [];
    private CancellationTokenSource? _loop;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _baseline, _paused, _scanning, _disposed;
    private string _signature = "";
    private readonly Dictionary<int, (long At, IReadOnlyList<PortRow> Rows)> _lookups = [];
    private readonly object _lookupGate = new();
    private volatile Snapshot? _snapshot;
    private int _epoch;

    /// <summary>The rows of the last good full read and when it ended; replaced whole, so any thread may read it.</summary>
    private sealed record Snapshot(long At, PortRow[] Rows);
    /// <summary>How long one port's focused read answers Search again.</summary>
    public static readonly TimeSpan LookupCache = TimeSpan.FromMilliseconds(250);

    public PortManagerEngine(IPortSystem system, IAdminHelper? admin = null, Func<long>? nowMs = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _system = system;
        _scanner = new PortScanner(system);
        _now = nowMs ?? (() => Environment.TickCount64);
        _delay = delay ?? Task.Delay;
        _ender = new PortEnder(system, admin, delay, nowMs);
    }

    /// <summary>The list as shown: every row, with new and ended ones marked.</summary>
    public IReadOnlyList<PortListRow> Rows { get; private set; } = [];

    /// <summary>The last read failed; the rows are the last good ones. Null otherwise.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised on the caller's context when the list, a mark or the error changed.</summary>
    public event Action? Changed;

    /// <summary>Pause stops the 2-second refresh; a manual <see cref="RefreshAsync"/> still reads.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Changed?.Invoke();
        }
    }

    public bool IsVisible => _loop != null;

    /// <summary>The page opened or closed. While visible the list refreshes every two seconds (unless paused); hidden, nothing runs.</summary>
    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        if (visible == (_loop != null)) return;
        if (!visible)
        {
            _loop!.Cancel();
            _loop.Dispose();
            _loop = null;
            return;
        }
        _loop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = LoopAsync(_loop.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await RefreshAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                await _delay(Interval, ct);
                if (!_paused) await RefreshAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One read off the UI thread, then the list is updated. A read already running is not repeated.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_scanning || _disposed) return;
        _scanning = true;
        IReadOnlyList<PortRow>? rows = null;
        string? error = null;
        int epoch = _epoch;
        try { rows = await Task.Run(() => _scanner.Scan(), ct); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or InvalidDataException) { error = "Read failed"; }
        finally { _scanning = false; }
        ct.ThrowIfCancellationRequested();
        if (_disposed) return;
        Apply(rows, error, _now(), snapshot: epoch == _epoch);   // a read that began before an action is not an answer for Search
    }

    internal void Apply(IReadOnlyList<PortRow>? rows, string? error, long now, bool snapshot = true)
    {
        if (rows is not null)
        {
            var current = new Dictionary<string, PortRow>();
            foreach (var row in rows) current.TryAdd(row.Key, row);
            if (_baseline)
            {
                foreach (var key in current.Keys.Where(k => !_known.ContainsKey(k)))
                {
                    _newUntil[key] = now + (long)NewFor.TotalMilliseconds;
                    _ended.Remove(key);
                }
                foreach (var (key, old) in _known)
                    if (!current.ContainsKey(key)) _ended[key] = (old, now + (long)EndedFor.TotalMilliseconds);
            }
            _baseline = true;
            if (snapshot) _snapshot = new Snapshot(now, [.. rows]);
            _known.Clear();
            foreach (var (key, row) in current) _known[key] = row;
        }
        foreach (var key in _newUntil.Where(p => p.Value <= now || !_known.ContainsKey(p.Key)).Select(p => p.Key).ToList()) _newUntil.Remove(key);
        foreach (var key in _ended.Where(p => p.Value.Until <= now).Select(p => p.Key).ToList()) _ended.Remove(key);
        var list = new List<PortListRow>(_known.Count + _ended.Count);
        foreach (var (key, row) in _known) list.Add(new(row, _newUntil.ContainsKey(key) ? PortMark.New : PortMark.None));
        foreach (var (_, (row, _)) in _ended) list.Add(new(row, PortMark.Ended));
        list.Sort((a, b) => Compare(a.Row.Endpoint, b.Row.Endpoint));
        string signature = string.Join('\n', list.Select(r => $"{r.Row.Key}|{r.Mark}|{r.Row.Verb}|{r.Row.Holder.Kind}|{r.Row.Holder.Name}|{r.Row.Holder.PortCount}|{r.Row.ServiceName}")) + "|" + error;
        bool changed = signature != _signature;
        _signature = signature;
        Rows = list;
        Error = error;
        if (changed) Changed?.Invoke();
    }

    internal static int Compare(PortEndpoint a, PortEndpoint b)
    {
        int c = a.LocalPort.CompareTo(b.LocalPort);
        if (c == 0) c = a.Protocol.CompareTo(b.Protocol);
        if (c == 0) c = string.CompareOrdinal(a.LocalAddress.ToString(), b.LocalAddress.ToString());
        if (c == 0) c = a.RemotePort.CompareTo(b.RemotePort);
        if (c == 0) c = string.CompareOrdinal(a.Key, b.Key);
        return c;
    }

    /// <summary>
    /// What holds <paramref name="port"/>, for Search on every keystroke, so it never waits on a full read. It answers from the last
    /// full read while that is under <see cref="Interval"/> old; otherwise from one focused read (the tables and only that port's holders),
    /// kept for <see cref="LookupCache"/> so typing never reads twice in a row. Empty when nothing is on it or the read failed.
    /// </summary>
    public IReadOnlyList<PortRow> Lookup(int port)
    {
        long now = _now();
        if (_snapshot is { } snapshot && now - snapshot.At < (long)Interval.TotalMilliseconds)
            return [.. snapshot.Rows.Where(r => r.Endpoint.LocalPort == port)];
        lock (_lookupGate)
        {
            if (_lookups.TryGetValue(port, out var cached) && now - cached.At < (long)LookupCache.TotalMilliseconds) return cached.Rows;
            var rows = Read(port);
            if (_lookups.Count >= 32) _lookups.Clear();
            _lookups[port] = (_now(), rows);
            return rows;
        }
    }

    /// <summary>A read for after an action or a Retry: never from a snapshot or a cache, and off the caller's (UI) thread.</summary>
    public async Task<IReadOnlyList<PortRow>> LookupFreshAsync(int port, CancellationToken ct = default)
    {
        var rows = await Task.Run(() => Read(port), ct);
        _snapshot = null;   // this read is newer than the last full one: the next keystroke is answered from it
        lock (_lookupGate) _lookups[port] = (_now(), rows);
        return rows;
    }

    private IReadOnlyList<PortRow> Read(int port)
    {
        try { return _scanner.Scan(e => e.LocalPort == port); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or InvalidDataException) { return []; }
    }

    private void Forget()
    {
        _epoch++;
        _snapshot = null;
        lock (_lookupGate) _lookups.Clear();
    }

    /// <summary>
    /// Frees a confirmed row (the question was asked and answered yes) and reads again. <paramref name="force"/> is the Force end step
    /// after a close request left the process running.
    /// </summary>
    public async Task<PortEndResult> FreeAsync(PortRow row, bool force = false, CancellationToken ct = default)
    {
        var result = await _ender.FreeAsync(row, force, ct);
        Forget();
        await AfterAsync(result);
        return result;
    }

    public async Task<PortEndResult> StopAsync(PortRow row, CancellationToken ct = default)
    {
        var result = await _ender.StopAsync(row, ct);
        Forget();
        await AfterAsync(result);
        return result;
    }

    /// <summary>The flow that asked is over (result shown, dismissed or cancelled): the admin helper session a verb left open ends.</summary>
    public Task EndAdminSessionAsync() => _ender.EndAdminSessionAsync();

    private async Task AfterAsync(PortEndResult result)
    {
        if (result.State is PortEndState.Cancelled or PortEndState.NeedsAdmin) return;
        try { await RefreshAsync(); }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
