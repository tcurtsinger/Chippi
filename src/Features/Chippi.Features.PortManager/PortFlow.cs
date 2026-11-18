// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;

namespace Chippi.Features.PortManager;

public enum FlowStep
{
    Idle,
    /// <summary>The question ("End node.exe?") is up: nothing has happened.</summary>
    Asking,
    /// <summary>Confirmed, and the engine is working: a close request, the UAC prompt, or the end itself.</summary>
    Working,
    /// <summary>A close request left the process running: "Force end node.exe?".</summary>
    ForceAsking,
    /// <summary>The result is in <see cref="PortFlow.Outcome"/>.</summary>
    Result,
}

public enum OutcomeKind
{
    /// <summary>"Port 3000 freed".</summary>
    Freed,
    /// <summary>Something holds the port again (a process that restarts itself): its row, with its verb.</summary>
    Holder,
    /// <summary>End failed, Stop failed, Needs admin: Caution, with Retry.</summary>
    Failed,
    /// <summary>Already closed, Changed: nothing went wrong; neutral.</summary>
    Neutral,
    /// <summary>Kept: the helper or the scan refused; Caution.</summary>
    Kept,
}

/// <summary>What the screens say after a verb: the same words in the island's rows and Settings' strips.</summary>
/// <param name="Title">"Port 3000 freed", "Port 3000 · node.exe", "End failed".</param>
/// <param name="Detail">The second line: "Already closed", "Changed", "Kept", "PID 5012", the process for a failure.</param>
/// <param name="Row">What holds the port now, when something does.</param>
/// <param name="Verb">The selected row's verb: Free, Stop, Open or Retry; null for none.</param>
public sealed record PortOutcome(OutcomeKind Kind, string Title, string? Detail, PortRow? Row, string? Verb);

/// <summary>
/// The question, the work and the result of one Free or Stop, shared by the island's page and Settings' strips. It asks, then runs the
/// engine only when <see cref="ConfirmAsync"/> is called (the screens' verb button); nothing here ends anything by itself. One flow
/// is one confirmed row at a time.
/// </summary>
public sealed class PortFlow(PortManagerEngine engine, Func<long>? nowMs = null)
{
    /// <summary>A close request is shown as "Closing" only once it has taken this long: a process with no window ends at once.</summary>
    public static readonly TimeSpan ClosingAfter = TimeSpan.FromMilliseconds(250);
    private readonly Func<long> _now = nowMs ?? (() => Environment.TickCount64);
    private CancellationTokenSource? _cts;
    private long _startedAt;
    private bool _retrying;

    public FlowStep Step { get; private set; }
    public PortRow? Row { get; private set; }
    public bool Force { get; private set; }
    public PortEndResult? Result { get; private set; }
    public PortOutcome? Outcome { get; private set; }
    public event Action? Changed;

    public int Port => Row?.Endpoint.LocalPort ?? 0;
    public bool Admin => Row?.NeedsAdmin == true;

    /// <summary>The title of the question: "End node.exe?", "Stop Print Spooler?", "Force end node.exe?".</summary>
    public string Title => Row is null ? "" : Row.Verb == PortVerb.Stop ? $"Stop {ServiceName(Row)}?" : (Force ? "Force end " : "End ") + Row.Holder.Name + "?";

    /// <summary>The line under the title: "Port 3000 · PID 4120", "3 ports · PID 14220", "Port 9100"; none for Force end.</summary>
    public string Detail
    {
        get
        {
            if (Row is not { } row || Force) return "";
            string port = row.Verb == PortVerb.Free && row.Holder.PortCount > 1 ? $"{row.Holder.PortCount} ports" : $"Port {row.Endpoint.LocalPort}";
            return row.Verb == PortVerb.Stop ? port : $"{port} · PID {row.Holder.Pid}";
        }
    }

    /// <summary>Closing: a close request is out, and it has taken longer than <see cref="ClosingAfter"/>.</summary>
    public bool ShowsClosing => Step == FlowStep.Working && !Admin && !Force && Row?.Verb == PortVerb.Free && Elapsed >= ClosingAfter;

    /// <summary>"Waiting for admin": the UAC prompt, or the helper at work.</summary>
    public bool WaitsForAdmin => Step == FlowStep.Working && Admin;

    /// <summary>How far through the close request's three seconds, 0 to 1.</summary>
    public double Progress => Math.Clamp(Elapsed / PortEnder.CloseWait, 0, 1);

    private TimeSpan Elapsed => Step == FlowStep.Working ? TimeSpan.FromMilliseconds(_now() - _startedAt) : TimeSpan.Zero;

    internal static string ServiceName(PortRow row) => row.ServiceDisplay ?? row.ServiceName ?? row.Holder.Name;

    /// <summary>Asks the question for a row; ignored while work is running.</summary>
    public void Ask(PortRow row)
    {
        if (Step == FlowStep.Working) return;
        Row = row;
        Force = false;
        Result = null;
        Outcome = null;
        Step = FlowStep.Asking;
        Changed?.Invoke();
    }

    /// <summary>Cancel: leaves a question; during a close request or a prompt it stops waiting (the helper is not stopped once sent).</summary>
    public void Cancel()
    {
        switch (Step)
        {
            case FlowStep.Asking or FlowStep.ForceAsking:
                Dismiss();
                break;
            case FlowStep.Working:
                _cts?.Cancel();
                break;
        }
    }

    /// <summary>Leaves the question or the result; nothing was or is ended.</summary>
    public void Dismiss()
    {
        if (Step == FlowStep.Working) return;
        EndSession();
        Step = FlowStep.Idle;
        Row = null;
        Result = null;
        Outcome = null;
        Force = false;
        Changed?.Invoke();
    }

    /// <summary>The verb in the question (End, Stop, Force end): runs it.</summary>
    public async Task ConfirmAsync()
    {
        if (Step is not (FlowStep.Asking or FlowStep.ForceAsking) || Row is not { } row) return;
        Force = Step == FlowStep.ForceAsking;
        Step = FlowStep.Working;
        _startedAt = _now();
        var cts = _cts = new CancellationTokenSource();
        Changed?.Invoke();
        PortEndResult result;
        try { result = row.Verb == PortVerb.Stop ? await engine.StopAsync(row, cts.Token) : await engine.FreeAsync(row, Force, cts.Token); }
        catch (OperationCanceledException) { result = new(PortEndState.Cancelled, row.Verb); }
        finally
        {
            _cts = null;
            cts.Dispose();
        }
        switch (result.State)
        {
            case PortEndState.StillRunning:
                Force = true;
                Step = FlowStep.ForceAsking;
                break;
            case PortEndState.Cancelled:
                Step = FlowStep.Asking;
                Dismiss();
                return;
            default:
                Result = result;
                Outcome = await DescribeAsync(row, result);
                EndSession();
                Step = FlowStep.Result;
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Retry: asks again about whatever holds the port now (read again off the UI thread); with nothing there, the port is already free.</summary>
    public async Task RetryAsync()
    {
        if (Step != FlowStep.Result || Row is not { } row || _retrying) return;
        _retrying = true;
        PortRow? now;
        try { now = Held(await engine.LookupFreshAsync(row.Endpoint.LocalPort)); }
        finally { _retrying = false; }
        if (Step != FlowStep.Result || !ReferenceEquals(Row, row)) return;
        if (now is { Verb: not PortVerb.None }) Ask(now);
        else
        {
            Outcome = new PortOutcome(OutcomeKind.Neutral, Holder(row), "Already closed", null, null);
            Changed?.Invoke();
        }
    }

    private static PortRow? Held(IReadOnlyList<PortRow> rows) => rows.FirstOrDefault(r => r.Endpoint.IsHeld && r.Verb != PortVerb.None) ?? rows.FirstOrDefault(r => r.Endpoint.IsHeld);

    private static string Holder(PortRow row) => $"Port {row.Endpoint.LocalPort} · " + (row.Verb == PortVerb.Stop ? ServiceName(row) : row.Holder.Name);

    private async Task<PortOutcome> DescribeAsync(PortRow row, PortEndResult result)
    {
        int port = row.Endpoint.LocalPort;
        var now = Held(await engine.LookupFreshAsync(port));
        string who = row.Verb == PortVerb.Stop ? ServiceName(row) : row.Holder.Name;
        switch (result.State)
        {
            case PortEndState.Freed:
                return now is null ? new(OutcomeKind.Freed, $"Port {port} freed", null, null, null)
                    : new(OutcomeKind.Holder, Holder(now), "PID " + now.Holder.Pid, now, VerbOf(now));
            case PortEndState.AlreadyClosed:
                return new(OutcomeKind.Neutral, Holder(row), "Already closed", null, null);
            case PortEndState.Changed:
                return new(OutcomeKind.Neutral, Holder(now ?? row), "Changed", now, now is null ? null : VerbOf(now));
            case PortEndState.Kept:
                return new(OutcomeKind.Kept, Holder(now ?? row), "Kept", now, now is null ? "Open" : VerbOf(now));
            default:
                return new(OutcomeKind.Failed, result.Text, who, row, "Retry");
        }
    }

    /// <summary>The question or the result is over: the helper session a verb left open (for Force end) is ended, not kept for later.</summary>
    private void EndSession() => _ = engine.EndAdminSessionAsync();

    internal static string VerbOf(PortRow row) => row.Verb switch { PortVerb.Free => "Free", PortVerb.Stop => "Stop", _ => "Open" };
}

/// <summary>Settings › Ports' search box: a port, a process or a service.</summary>
public static class PortFilter
{
    public static bool Matches(PortRow row, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return true;
        var e = row.Endpoint;
        if (text.All(char.IsAsciiDigit))
            return e.LocalPort.ToString(CultureInfo.InvariantCulture).StartsWith(text, StringComparison.Ordinal)
                || (e.RemotePort > 0 && e.RemotePort.ToString(CultureInfo.InvariantCulture).StartsWith(text, StringComparison.Ordinal))
                || row.Holder.Pid.ToString(CultureInfo.InvariantCulture) == text;
        return row.Holder.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || (row.ServiceDisplay?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (row.ServiceName?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || e.LocalAddress.ToString().Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}
