// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using Chippi.UI.Displays;

namespace Chippi.Features.Displays;

/// <summary>What one monitor's worker knows, for the panel and Settings.</summary>
/// <param name="Failed">Brightness doesn't answer (a read of VCP 0x10 failed, twice).</param>
/// <param name="Retrying">Try again is reading it.</param>
/// <param name="Brightness">The last level read or written, 0 to 100; null before the first read lands.</param>
/// <param name="HasContrast">A read of VCP 0x12 answered.</param>
/// <param name="WriteFailed">The last write didn't go through; the level above is the last one that did.</param>
internal sealed record MonitorState(bool Failed, bool Retrying, int? Brightness, bool HasContrast, int? Contrast, bool WriteFailed);

/// <summary>
/// One monitor's DDC/CI work on its own background thread (docs/phase-7/displays.md, Writing): never the UI thread. A write
/// that's waiting is replaced by a newer one, so a slider is paced by the monitor's own speed; a value the monitor already has
/// isn't sent; a key press on a level nobody has read or written for 10 seconds reads first.
/// </summary>
internal sealed class MonitorWorker : IDisposable
{
    /// <summary>A level older than this is read again before a key press adds to it.</summary>
    public const long StaleMs = 10_000;

    /// <summary>A first failed read is tried once more after this, since DDC/CI fails now and then.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMilliseconds(500);

    private sealed class Axis(Vcp code)
    {
        public readonly Vcp Code = code;
        public int? Level;
        public int Max = 100;
        public bool Answers;
        public int? Pending;
        public int? Writing;

        /// <summary>Key presses that arrived while the level was being read: they're added to it once it is.</summary>
        public int Delta;

        public bool NeedsRead;

        /// <summary>A key press, not a slider, is behind the work queued: a failure shows on the bar.</summary>
        public bool Keyed;

        public long Touched;
    }

    private readonly object _gate = new();
    private readonly Axis _brightness = new(Vcp.Brightness);
    private readonly Axis _contrast = new(Vcp.Contrast);
    private readonly DisplayInfo _display;
    private readonly IMonitorBus _bus;
    private readonly Func<long> _nowMs;
    private readonly Action<TimeSpan> _sleep;
    private readonly Thread _thread;
    private IMonitor? _monitor;
    private bool _stop, _reopen, _readAll, _retrying, _probed, _writeFailed;

    public MonitorWorker(DisplayInfo display, IMonitorBus bus, Func<long> nowMs, Action<TimeSpan> sleep)
    {
        _display = display;
        _bus = bus;
        _nowMs = nowMs;
        _sleep = sleep;
        _thread = new Thread(Run) { IsBackground = true, Name = "Chippi display " + display.Id };
        _thread.Start();
    }

    public string Id => _display.Id;

    /// <summary>Something about the monitor changed. Raised on the worker's thread.</summary>
    public event Action? Changed;

    /// <summary>A read or write didn't go through; true when a key press was behind it. Raised on the worker's thread.</summary>
    public event Action<bool>? ControlFailed;

    public MonitorState State
    {
        get
        {
            lock (_gate)
                return new MonitorState(_probed && !_brightness.Answers, _retrying, _brightness.Level, _contrast.Answers, _contrast.Level, _writeFailed);
        }
    }

    /// <summary>Reads brightness and contrast again.</summary>
    public void Read()
    {
        lock (_gate)
        {
            _readAll = true;
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>Drops the monitor's handle and reads again: after a display change or resume, and for Try again.</summary>
    public void Reopen(bool retrying = false)
    {
        lock (_gate)
        {
            _reopen = true;
            _readAll = true;
            _retrying = retrying;
            Monitor.Pulse(_gate);
        }
        if (retrying) Changed?.Invoke();
    }

    /// <summary>A slider's value: it replaces any write still waiting.</summary>
    public void Set(Vcp code, int percent)
    {
        lock (_gate)
        {
            var axis = Of(code);
            percent = Math.Clamp(percent, 0, 100);
            axis.Delta = 0;
            axis.NeedsRead = false;
            axis.Keyed = false;
            if (percent == axis.Writing && axis.Pending is null) return;
            axis.Pending = percent == axis.Level && axis.Writing is null ? null : percent;
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>A key press: moves the level by <paramref name="delta"/>, from the level being written if one is.</summary>
    public void Step(Vcp code, int delta)
    {
        lock (_gate)
        {
            var axis = Of(code);
            axis.Keyed = true;
            if (axis.Pending is not null || axis.Writing is not null)
                axis.Pending = Math.Clamp((axis.Pending ?? axis.Writing!.Value) + delta, 0, 100);
            else if (axis.NeedsRead)
                axis.Delta += delta;
            else if (axis.Level is null || !axis.Answers || _nowMs() - axis.Touched > StaleMs)
            {
                axis.NeedsRead = true;
                axis.Delta = delta;
            }
            else
                axis.Pending = Math.Clamp(axis.Level.Value + delta, 0, 100);
            Monitor.Pulse(_gate);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stop = true;
            Monitor.Pulse(_gate);
        }
        // At most one read or write is in flight; wait for it (displays.md, Stopping the feature).
        _thread.Join(TimeSpan.FromSeconds(5));
        _monitor?.Dispose();
        _monitor = null;
    }

    private Axis Of(Vcp code) => code == Vcp.Brightness ? _brightness : _contrast;

    private void Run()
    {
        while (true)
        {
            Action? job = null;
            lock (_gate)
            {
                while (!_stop && (job = Pick()) is null)
                    Monitor.Wait(_gate);
                if (_stop) return;
            }
            try { job!(); }
            catch (Exception ex) { Log.Warn("Displays: monitor work failed", ex); }
        }
    }

    private Action? Pick()
    {
        if (_reopen)
        {
            _reopen = false;
            return () =>
            {
                _monitor?.Dispose();
                _monitor = null;
            };
        }
        if (_readAll)
        {
            _readAll = false;
            return ReadAll;
        }
        foreach (var axis in new[] { _brightness, _contrast })
        {
            if (axis.NeedsRead)
            {
                axis.NeedsRead = false;
                return () => ReadForKey(axis);
            }
            if (axis.Pending is { } value)
            {
                axis.Pending = null;
                axis.Writing = value;
                return () => Write(axis, value);
            }
        }
        return null;
    }

    private bool Read(Axis axis, out int percent)
    {
        percent = 0;
        try
        {
            _monitor ??= _bus.Open(_display);
            if (_monitor is null || !_monitor.TryRead(axis.Code, out int current, out int max) || max <= 0) return false;
            axis.Max = max;
            percent = (int)Math.Round(current * 100.0 / max);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Displays: read failed", ex);
            return false;
        }
    }

    private void ReadAll()
    {
        bool answered = Read(_brightness, out int level);
        if (!answered)
        {
            _sleep(RetryAfter);
            answered = Read(_brightness, out level);
        }
        bool contrast = Read(_contrast, out int contrastLevel);
        lock (_gate)
        {
            _probed = true;
            _retrying = false;
            _brightness.Answers = answered;
            _contrast.Answers = contrast;
            // A read that lands while a write is waiting or in flight would put the slider back.
            if (answered && _brightness.Pending is null && _brightness.Writing is null)
            {
                _brightness.Level = level;
                _brightness.Touched = _nowMs();
            }
            if (!answered) _brightness.Level = null;
            if (contrast && _contrast.Pending is null && _contrast.Writing is null)
            {
                _contrast.Level = contrastLevel;
                _contrast.Touched = _nowMs();
            }
            if (!contrast) _contrast.Level = null;
            if (answered) _writeFailed = false;
        }
        Changed?.Invoke();
    }

    private void ReadForKey(Axis axis)
    {
        bool ok = Read(axis, out int percent);
        bool keyed;
        lock (_gate)
        {
            keyed = axis.Keyed;
            if (ok)
            {
                axis.Answers = true;
                axis.Level = percent;
                axis.Touched = _nowMs();
                if (axis.Delta != 0)
                {
                    int target = Math.Clamp(percent + axis.Delta, 0, 100);
                    axis.Pending = target == percent ? null : target;
                }
            }
            else
            {
                if (axis.Code == Vcp.Brightness) axis.Answers = false;
                axis.Level = null;
                _probed = true;
            }
            axis.Delta = 0;
            if (axis.Pending is null) axis.Keyed = false;
        }
        if (!ok) ControlFailed?.Invoke(keyed);
        Changed?.Invoke();
    }

    private void Write(Axis axis, int percent)
    {
        bool ok;
        bool skipped;
        lock (_gate) skipped = percent == axis.Level;
        if (skipped) ok = true;
        else
        {
            try
            {
                _monitor ??= _bus.Open(_display);
                ok = _monitor is not null && _monitor.TryWrite(axis.Code, (int)Math.Round(percent * axis.Max / 100.0));
            }
            catch (Exception ex)
            {
                Log.Warn("Displays: write failed", ex);
                ok = false;
            }
        }
        bool keyed;
        lock (_gate)
        {
            keyed = axis.Keyed;
            axis.Writing = null;
            if (ok)
            {
                axis.Level = percent;
                axis.Touched = _nowMs();
                _writeFailed = false;
            }
            else _writeFailed = true;
            if (axis.Pending is null) axis.Keyed = false;
        }
        if (!ok) ControlFailed?.Invoke(keyed);
        if (!skipped) Changed?.Invoke();
    }
}
