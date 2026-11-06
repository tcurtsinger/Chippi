// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Features.WindowLayouts;

/// <summary>Physical desktop pixels, saved independently of a live window handle.</summary>
public sealed record ArrangementBounds(int Left, int Top, int Width, int Height)
{
    public ArrangementBounds Fit(ArrangementBounds from, ArrangementBounds to)
    {
        var fit = DisplayMove.Fit((Left, Top, Width, Height),
            (from.Left, from.Top, from.Width, from.Height), (to.Left, to.Top, to.Width, to.Height));
        return new(fit.Left, fit.Top, fit.Width, fit.Height);
    }
}

/// <summary>A candidate supplied by the shared window list, with identity already resolved.</summary>
public sealed record ArrangementCandidate(ActionWindow Window, string AppIdentity, ArrangementBounds Bounds,
    bool CurrentDesktop = true, bool Eligible = true);

public sealed record ArrangementMatch(ArrangementCandidate Candidate, bool Guessed);

/// <summary>One matching session per run. Selected windows stay reserved across rows and retries.</summary>
public sealed class ArrangementMatcher
{
    private readonly HashSet<(nint Handle, uint Process, long Started, long Lifetime)> _reserved = [];

    internal bool IsReserved(ActionWindow window) => _reserved.Contains(Key(window));
    internal void Release(ActionWindow window) => _reserved.Remove(Key(window));

    public ArrangementMatch? Match(string appIdentity, string className, string titleHint,
        ArrangementBounds destination, IReadOnlyList<ArrangementCandidate> windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appIdentity);
        var candidates = windows.Where(w => w.Eligible && w.CurrentDesktop && !w.Window.Elevated
            && string.Equals(w.AppIdentity, appIdentity, StringComparison.OrdinalIgnoreCase)
            && !_reserved.Contains(Key(w.Window))).ToList();
        if (candidates.Count == 0) return null;
        var byClass = candidates.Where(w => string.Equals(w.Window.ClassName, className, StringComparison.Ordinal)).ToList();
        if (byClass.Count > 0) candidates = byClass;
        if (!string.IsNullOrEmpty(titleHint))
        {
            var byTitle = candidates.Where(w => string.Equals(w.Window.Title, titleHint, StringComparison.Ordinal)).ToList();
            if (byTitle.Count > 0) candidates = byTitle;
        }
        // OrderBy is stable: exact distance ties keep this run's enumeration order.
        var selected = candidates.OrderBy(w => Distance(w.Bounds, destination)).First();
        _reserved.Add(Key(selected.Window));
        return new(selected, candidates.Count > 1);
    }

    private static (nint, uint, long, long) Key(ActionWindow window) =>
        (window.Handle, window.ProcessId, window.ProcessStarted, window.Lifetime);

    private static double Distance(ArrangementBounds a, ArrangementBounds b)
    {
        double left = (double)a.Left - b.Left, top = (double)a.Top - b.Top;
        double right = (double)a.Left + a.Width - b.Left - b.Width;
        double bottom = (double)a.Top + a.Height - b.Top - b.Height;
        return left * left + top * top + right * right + bottom * bottom;
    }
}
