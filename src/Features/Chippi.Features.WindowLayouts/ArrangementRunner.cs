// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Features.WindowLayouts;

public enum ArrangementRowState { Waiting, Opening, Placing, Ready, Skipped, Failed, Cancelled }
public sealed record ArrangementRowResult(ArrangementRow Row, ArrangementRowState State = ArrangementRowState.Waiting,
    string? Reason = null, bool Guessed = false, string? SubstituteDisplay = null);
public readonly record struct ArrangementProcess(uint Id, long Started);

/// <summary>The shared window list, display discovery, pause policy and action PC, adapted by the shell. UI thread only.</summary>
public interface IArrangementDesktop
{
    IReadOnlyList<ArrangementLiveWindow> Windows();
    IReadOnlyList<SnapDisplay> Displays();
    string? StopReason(IReadOnlySet<ArrangementProcess> launched);
    ArrangementProcess? Launch(ArrangementRow row);
    void Place(ActionWindow target, SavedWindowPlacement placement, nint monitor);
}

/// <summary>One bounded run at a time. Cancellation leaves opened apps and completed placements alone.</summary>
public sealed class ArrangementRunner(IArrangementDesktop desktop, TimeProvider? time = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private CancellationTokenSource? _cancel;
    private readonly HashSet<ArrangementProcess> _launched = [];
    private ArrangementMatcher _matcher = new();
    private Task? _running;
    private string _invocationDisplay = "";
    public IReadOnlyList<ArrangementRowResult> Results { get; private set; } = [];
    public bool IsRunning => _running is { IsCompleted: false };
    public event Action? Changed;

    public Task LaunchAsync(SavedArrangement arrangement, string invocationDisplay)
    {
        if (IsRunning) { Changed?.Invoke(); return _running!; }
        if (!arrangement.IsValid) throw new ArgumentException("Invalid arrangement", nameof(arrangement));
        _invocationDisplay = invocationDisplay;
        _matcher = new();
        _launched.Clear();
        Results = arrangement.Rows.Select(r => new ArrangementRowResult(r)).ToArray();
        return StartAsync(false);
    }

    public Task RetryAsync() => IsRunning ? _running! : StartAsync(true);
    public void Cancel() => _cancel?.Cancel();

    private Task StartAsync(bool retry)
    {
        _cancel?.Dispose();
        _cancel = new();
        // Yield before running so a reentrant Changed handler always sees this task as active.
        _running = RunAsync(retry, _cancel.Token);
        return _running;
    }

    private async Task RunAsync(bool retry, CancellationToken ct)
    {
        await Task.Yield();
        var selected = Enumerable.Range(0, Results.Count)
            .Where(i => !retry || Results[i].State == ArrangementRowState.Failed).ToArray();
        foreach (int i in selected) Update(i, Results[i] with { State = ArrangementRowState.Waiting, Reason = null });
        try
        {
            var pending = new List<int>();
            // Reserve and place every reusable window before launching any missing app.
            foreach (int i in selected)
            {
                CheckStop(ct);
                if (!AttemptPlace(i, desktop.Windows(), ct)) pending.Add(i);
            }
            foreach (int i in pending)
            {
                CheckStop(ct);
                // Another row's launch may have created this window in the meantime.
                if (AttemptPlace(i, desktop.Windows(), ct)) continue;
                try
                {
                    CheckStop(ct);
                    Update(i, Results[i] with { State = ArrangementRowState.Opening });
                    long started = _time.GetTimestamp();
                    var process = desktop.Launch(Results[i].Row);
                    if (process is { } identity) _launched.Add(identity);
                    while (true)
                    {
                        CheckStop(ct);
                        if (_time.GetElapsedTime(started) >= TimeSpan.FromSeconds(60))
                        {
                            Update(i, Results[i] with { State = ArrangementRowState.Failed, Reason = "Launching failed: Timed out" });
                            break;
                        }
                        if (AttemptPlace(i, desktop.Windows(), ct, afterLaunch: true)) break;
                        await (delay?.Invoke(TimeSpan.FromMilliseconds(100), ct)
                            ?? Task.Delay(TimeSpan.FromMilliseconds(100), _time, ct));
                    }
                }
                catch (ActionFailure ex) { Failure(i, "Launching failed", ex); }
            }
        }
        catch (OperationCanceledException)
        {
            foreach (int i in selected.Where(i => Results[i].State is ArrangementRowState.Waiting
                or ArrangementRowState.Opening or ArrangementRowState.Placing))
                Update(i, Results[i] with { State = ArrangementRowState.Cancelled, Reason = "Cancelled" });
        }
        finally { Changed?.Invoke(); }
    }

    private bool AttemptPlace(int index, IReadOnlyList<ArrangementLiveWindow> windows, CancellationToken ct, bool afterLaunch = false)
    {
        try { return TryPlace(index, windows, ct, afterLaunch); }
        catch (ActionFailure ex) { Failure(index, "Placement failed", ex); return true; }
    }

    private bool TryPlace(int index, IReadOnlyList<ArrangementLiveWindow> windows, CancellationToken ct, bool afterLaunch)
    {
        var result = Results[index];
        var row = result.Row;
        var displays = desktop.Displays();
        var display = displays.FirstOrDefault(d => string.Equals(d.Id, row.DisplayId, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(d => string.Equals(d.Id, _invocationDisplay, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(d => d.IsMain);
        if (display is null) throw new ActionFailure("Display removed");
        var saved = row.Placement!;
        var destination = new ArrangementBounds(saved.Left, saved.Top, saved.Width, saved.Height).Fit(
            new(saved.WorkLeft, saved.WorkTop, saved.WorkWidth, saved.WorkHeight),
            new(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height));
        var match = _matcher.Match(row.AppIdentity, row.ClassName, row.TitleHint, destination,
            windows.Select(w => w.Candidate).ToArray());
        if (match is null)
        {
            var sameApp = windows.Where(w => string.Equals(w.Candidate.AppIdentity, row.AppIdentity, StringComparison.OrdinalIgnoreCase)).ToArray();
            string? refusal = sameApp.Any(w => !w.Candidate.CurrentDesktop) ? "Another desktop"
                : sameApp.Any(w => w.Candidate.Window.Elevated) ? "Needs admin"
                : afterLaunch && sameApp.Any(w => _matcher.IsReserved(w.Candidate.Window)) ? "Window already used" : null;
            if (refusal is null) return false;
            Update(index, result with { State = ArrangementRowState.Skipped, Reason = refusal });
            return true;
        }
        var target = match.Candidate.Window;
        result = result with { Guessed = match.Guessed,
            SubstituteDisplay = string.Equals(display.Id, row.DisplayId, StringComparison.OrdinalIgnoreCase) ? null : display.Name };
        if (windows.Any(w => w.Candidate.Window == target && w.Fullscreen))
        {
            Update(index, result with { State = ArrangementRowState.Skipped, Reason = "Fullscreen" });
            return true;
        }
        try
        {
            CheckStop(ct);
            Update(index, result with { State = ArrangementRowState.Placing });
            // Resolve the display again immediately before the native action.
            displays = desktop.Displays();
            var current = displays.FirstOrDefault(d => string.Equals(d.Id, row.DisplayId, StringComparison.OrdinalIgnoreCase))
                ?? displays.FirstOrDefault(d => string.Equals(d.Id, _invocationDisplay, StringComparison.OrdinalIgnoreCase))
                ?? displays.FirstOrDefault(d => d.IsMain);
            if (current is null) throw new ActionFailure("Display removed");
            result = result with { SubstituteDisplay = string.Equals(current.Id, row.DisplayId, StringComparison.OrdinalIgnoreCase)
                ? null : current.Name };
            CheckStop(ct);
            desktop.Place(target, saved, current.Handle);
            Update(index, result with { State = ArrangementRowState.Ready });
        }
        catch (ActionFailure ex)
        {
            _matcher.Release(target);
            Failure(index, "Placement failed", ex);
        }
        return true;
    }

    private void Failure(int index, string operation, ActionFailure failure) => Update(index, Results[index] with
    {
        State = failure.Message is "Needs admin" or "Fullscreen" or "Access denied" or "Window closed"
            ? ArrangementRowState.Skipped : ArrangementRowState.Failed,
        Reason = failure.Message is "Needs admin" or "Fullscreen" ? failure.Message : operation + ": " + failure.Message
    });

    private void CheckStop(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (desktop.StopReason(_launched) is not null) throw new OperationCanceledException(ct);
    }

    private void Update(int index, ArrangementRowResult result)
    {
        var results = Results.ToArray();
        results[index] = result;
        Results = results;
        Changed?.Invoke();
    }
}
