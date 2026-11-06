// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.WindowLayouts;

/// <summary>
/// A run in a few counts, said the same way on the bar and the run page (arrangements.html, sheet-arrangements-island).
/// Skipped and failed windows need attention; cancelled ones were never reached.
/// </summary>
public sealed record ArrangementSummary(bool Running, int Ready, int Total, int Problems, int Guessed, int Moved,
    int Cancelled, string? Stopped)
{
    /// <summary>Done with every window ready where it was saved: the bar folds it.</summary>
    public bool Clean => !Running && Problems == 0 && Guessed == 0 && Moved == 0 && Cancelled == 0;

    public bool Attention => !Running && (Problems > 0 || Stopped is not null);

    /// <summary>The bar's first line: "5 of 12 ready", "Cancelled", "Stopped".</summary>
    public string Headline => Running || Cancelled == 0 ? $"{Ready} of {Total} ready" : Stopped is not null ? "Stopped" : "Cancelled";

    /// <summary>The bar's second line after the name: "3 need attention", "2 guessed", "Fullscreen app".</summary>
    public string Note => Cancelled > 0 ? Stopped ?? $"{Ready} ready"
        : Problems > 0 ? $"{Problems} need attention"
        : Guessed > 0 ? $"{Guessed} guessed"
        : Moved == 1 ? "1 on another display" : $"{Moved} on another display";

    /// <summary>The run page's line over its rows.</summary>
    public string Status => Running ? $"{Ready} of {Total} ready"
        : Cancelled > 0 ? Stopped is not null ? $"Stopped · {Stopped}" : $"{Ready} ready · {Cancelled} cancelled"
        : Problems > 0 ? $"{Ready} of {Total} ready · {Problems} need attention"
        : Guessed > 0 ? $"{Ready} of {Total} ready · {Guessed} guessed"
        : $"{Ready} of {Total} ready";

    public static ArrangementSummary Of(WindowLayoutsModule module)
    {
        bool preparing = module.ArrangementsPreparing;
        var runner = module.Arrangements;
        bool running = preparing || runner?.IsRunning == true;
        IReadOnlyList<ArrangementRowResult> results = preparing || runner is null ? [] : runner.Results;
        int total = results.Count > 0 ? results.Count : module.ArrangementRun?.Rows.Count ?? 0;
        int cancelled = results.Count(r => r.State == ArrangementRowState.Cancelled);
        return new(running, results.Count(r => r.State == ArrangementRowState.Ready), total,
            results.Count(r => r.State is ArrangementRowState.Failed or ArrangementRowState.Skipped),
            results.Count(r => r.Guessed && r.State == ArrangementRowState.Ready),
            results.Count(r => r.SubstituteDisplay is not null && r.State == ArrangementRowState.Ready),
            cancelled, cancelled > 0 && !module.ArrangementCancelled ? runner?.Stopped : null);
    }
}
