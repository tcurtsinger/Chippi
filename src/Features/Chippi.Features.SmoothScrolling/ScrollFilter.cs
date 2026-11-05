// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using Chippi.Core.Input;

namespace Chippi.Features.SmoothScrolling;

/// <summary>What the filter asks of Windows, so tests can answer for it.</summary>
internal interface IScrollEnvironment
{
    /// <summary>The window under the point, or 0.</summary>
    nint WindowAt(int x, int y);

    nint WindowUnderCursor();

    bool IsWindow(nint window);

    /// <summary>The exe behind a window, or null when it can't be read.</summary>
    string? AppPath(nint window);

    /// <summary>Ctrl, Shift, Alt or Windows is down: the wheel means something else (zoom, sideways) and passes.</summary>
    bool ModifierDown();

    /// <summary>Whether wheel messages sent from here reach the window: false for a window of a higher integrity (an elevated app), whose input Windows blocks from us.</summary>
    bool CanSend(nint window);

    /// <summary>One wheel message, marked as Chippi's. Never called from the hook's thread or under the filter's lock.</summary>
    void Send(bool horizontal, int delta);

    /// <summary>Start ticking: <c>Tick</c> is called each frame until it says false.</summary>
    void Wake();
}

/// <summary>The settings the filter reads, as one snapshot.</summary>
internal sealed record ScrollTuning(bool Smooth, bool Linear, int DistancePercent, int ResponseMs, int CoastMs, IReadOnlyList<AppScroll> Apps);

/// <summary>
/// Takes a wheel mouse's notches and sends them back as a glide (Smooth), as one notch each (Linear), or both. Only whole
/// notches (a delta that is a multiple of 120) count as a wheel mouse's: anything finer is a touchpad's or a precision
/// wheel's and passes untouched. Ctrl, Shift and Alt with the wheel pass, so zoom and sideways scrolling are the app's.
/// </summary>
internal sealed class ScrollFilter(IScrollEnvironment env, Func<bool> paused) : IInputFilter, IDisposable
{
    private const int Notch = 120;

    private readonly object _gate = new();
    private readonly ScrollEngine _engine = new(Stopwatch.Frequency);
    private volatile ScrollTuning _tuning = new(true, false, 100, 150, 0, []);
    private bool _disposed;

    public InputOrder Order => InputOrder.SmoothScrolling;

    public InputDevices Devices => InputDevices.Mouse;

    public bool Pauses => true;

    /// <summary>Only the Sandbox probe: its <c>SendInput</c> counts as a hand's.</summary>
    public bool IncludeInjected { get; init; }

    public ScrollTuning Tuning
    {
        get => _tuning;
        set => _tuning = value;
    }

    public bool Key(in KeyInput key) => false;

    public bool Mouse(in MouseInput mouse)
    {
        if (mouse.Kind is not (MouseKind.Wheel or MouseKind.HorizontalWheel) || (mouse.Injected && !IncludeInjected))
            return false;
        var tuning = _tuning;
        int delta = mouse.Delta;
        if (delta == 0 || delta % Notch != 0 || (!tuning.Smooth && !tuning.Linear))
            return false;
        if (env.ModifierDown())
        {
            Cancel(); // the wheel means something else now: no tail of ours into it
            return false;
        }
        nint window = env.WindowAt(mouse.X, mouse.Y);
        if (window == 0)
            return false;
        if (!env.CanSend(window))
        {
            Cancel(); // elevated: Windows would drop what we send, so its wheel stays native
            return false;
        }
        int speed = tuning.DistancePercent;
        if (FindApp(tuning, env.AppPath(window)) is { } app)
        {
            if (app.Percent == 0)
            {
                Cancel(); // left alone: native, and no tail of ours into it
                return false;
            }
            speed = speed * app.Percent / 100;
        }
        // Linear: a notch is a notch, however big the message a driver made of a fast spin.
        double amount = tuning.Linear ? Math.Sign(delta) * Notch : delta;
        amount = amount * speed / 100.0;
        if (!tuning.Smooth && Math.Abs(amount - delta) < 0.5)
            return false; // nothing to change: the app's own message goes through
        lock (_gate)
        {
            if (_disposed)
                return false;
            _engine.Add(mouse.Kind == MouseKind.Wheel ? 0 : 1, amount, window, tuning.Smooth ? tuning.ResponseMs + tuning.CoastMs : 0, mouse.Timestamp);
        }
        env.Wake();
        return true;
    }

    public void Cancel()
    {
        lock (_gate)
            _engine.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _engine.Cancel();
        }
    }

    /// <summary>One frame: sends what the glide owes, unless the pointer moved to another window, the window went, or a pause began, or a modifier went down. False once nothing is left.</summary>
    public bool Tick(long now)
    {
        (int v, int h) = (0, 0);
        bool more;
        lock (_gate)
        {
            if (_disposed || _engine.Idle)
                return false;
            if (paused() || env.ModifierDown() || env.WindowUnderCursor() != _engine.Target || !env.IsWindow(_engine.Target))
            {
                _engine.Cancel();
                return false;
            }
            (v, h) = _engine.Step(now);
            more = !_engine.Idle;
        }
        if (v != 0)
            env.Send(false, v);
        if (h != 0)
            env.Send(true, h);
        return more;
    }

    private static AppScroll? FindApp(ScrollTuning tuning, string? path)
    {
        if (string.IsNullOrEmpty(path) || tuning.Apps.Count == 0)
            return null;
        string name = Path.GetFileName(path);
        foreach (var app in tuning.Apps)
            if (string.Equals(Path.GetFileName(app.App), name, StringComparison.OrdinalIgnoreCase))
                return app;
        return null;
    }
}
