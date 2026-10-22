// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Notifications;

namespace Chippi.Features.Downloads;

/// <summary>The organizer's event kinds and the activation verbs its notices use.</summary>
public static class DownloadsNotices
{
    public const string Organized = "organized";
    public const string Held = "held";
    public const string Failed = "failed";
    public const string ArchiveImported = "archive-imported";
    public const string ReadyToReview = "ready-to-review";

    /// <summary>The Explorer menu: <c>--organize "&lt;folder&gt;"</c>.</summary>
    public const string OrganizeVerb = "organize";

    /// <summary>Opens a run in History: <c>--downloads-run &lt;id&gt;</c>.</summary>
    public const string RunVerb = "downloads-run";

    /// <summary>Opens the Tidy tab's pending list.</summary>
    public const string ReviewVerb = "downloads-review";

    /// <summary>Defaults from the approved mockups; ready to review is island only (Travis, 2026-10-22).</summary>
    public static IReadOnlyList<NoticeKind> Kinds { get; } =
    [
        new(Organized, "Organized", NoticePlaces.Island),
        new(Held, "Held", NoticePlaces.Windows | NoticePlaces.Island),
        new(Failed, "Failed", NoticePlaces.Windows | NoticePlaces.Island),
        new(ArchiveImported, "Archive imported", NoticePlaces.Windows),
        new(ReadyToReview, "Ready to review", NoticePlaces.Island),
    ];

    public static string ReviewActivation => $"--{ReviewVerb}";

    public static string RunActivation(long runId) => $"--{RunVerb} {runId}";
}
