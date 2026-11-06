// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Features.WindowLayouts;

public sealed record ArrangementLiveWindow(ArrangementCandidate Candidate, string Target, string DisplayId,
    string DisplayName, int Dpi, SavedWindowPlacement? Placement, bool Fullscreen = false);

/// <param name="Skipped">Why each window that wasn't captured was left out.</param>
/// <param name="SkippedWindows">The same windows for the review: "Task Manager (admin)", "Save As (dialog)". Local only, never logged.</param>
public sealed record ArrangementCapture(IReadOnlyList<ArrangementRow> Rows, IReadOnlyList<string> Skipped,
    IReadOnlyList<string>? SkippedWindows = null);

public static class ArrangementCaptureBuilder
{
    public static ArrangementCapture Capture(IReadOnlyList<ArrangementLiveWindow> windows)
    {
        var rows = new List<ArrangementRow>();
        var skipped = new List<string>();
        var names = new List<string>();
        foreach (var live in windows)
        {
            var candidate = live.Candidate;
            string? reason = !candidate.CurrentDesktop ? "Another desktop"
                : candidate.Window.Elevated ? "Needs admin" : live.Fullscreen ? "Fullscreen"
                : !candidate.Eligible ? "Unavailable window" : null;
            if (reason is not null)
            {
                skipped.Add(reason);
                // A dialog is named by its title ("Save As"); anything else by its app.
                string name = !candidate.Eligible && candidate.Window.Title.Length > 0 ? candidate.Window.Title
                    : candidate.Window.App.Length > 0 ? candidate.Window.App : candidate.Window.Title;
                names.Add($"{name} ({reason switch { "Another desktop" => "another desktop", "Needs admin" => "admin", "Fullscreen" => "fullscreen", _ => "dialog" }})");
                continue;
            }
            // An unresolved target remains in the draft for review, but IsValid prevents saving it.
            rows.Add(new ArrangementRow { AppIdentity = candidate.AppIdentity, Target = live.Target,
                App = candidate.Window.App, ClassName = candidate.Window.ClassName, TitleHint = candidate.Window.Title,
                DisplayId = live.DisplayId, DisplayName = live.DisplayName, Dpi = live.Dpi, Placement = live.Placement });
        }
        return new(rows, skipped, names);
    }
}
