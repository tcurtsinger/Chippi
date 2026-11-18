// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;
using Chippi.Core.Admin;

namespace Chippi.Features.Cleaner;

/// <summary>The tag a row carries: Check first is Caution; Needs admin is the shield column, not a tag.</summary>
public enum CleanerTag { None, CheckFirst }

/// <summary>One fixed category. The folders are fixed in code (<see cref="CleanerTargets"/>), never read from settings or a request.</summary>
public sealed record CleanerCategory(string Id, string Name, CleanerTag Tag, bool DefaultTicked, AdminCategory? Admin)
{
    public bool NeedsAdmin => Admin is not null;
}

/// <summary>The seven categories of the approved mockup, in its order.</summary>
public static class CleanerCatalog
{
    public const string Temp = "temp", Thumbnails = "thumb", Apps = "app", WindowsTemp = "wtemp", WindowsUpdate = "wu",
        DeliveryOptimization = "do", Dumps = "dump";

    public static readonly IReadOnlyList<CleanerCategory> All =
    [
        new(Temp, "User temp files", CleanerTag.None, true, null),
        new(Thumbnails, "Thumbnail and icon caches", CleanerTag.None, true, null),
        new(Apps, "App caches", CleanerTag.CheckFirst, false, null),
        new(WindowsTemp, "Windows temp", CleanerTag.None, false, AdminCategory.WindowsTemp),
        new(WindowsUpdate, "Windows Update cache", CleanerTag.None, false, AdminCategory.WindowsUpdateDownloads),
        new(DeliveryOptimization, "Delivery Optimization cache", CleanerTag.None, false, AdminCategory.DeliveryOptimization),
        new(Dumps, "Crash dumps and logs", CleanerTag.None, false, AdminCategory.SystemCrashDumps),
    ];

    public static CleanerCategory Find(string id) => All.First(c => c.Id == id);
}

/// <summary>Where a row is. The status words (In use, Unavailable, Needs admin, Failed, Kept, Cancelled) are <see cref="CleanerText.Status"/>.</summary>
public enum CleanerRowState
{
    /// <summary>Nothing measured yet, or no size can be read without admin ("—").</summary>
    Unmeasured,
    /// <summary>A fresh measure is running for this row; any size shown is from earlier.</summary>
    Measuring,
    Measured,
    /// <summary>A clean is running or waiting on this row.</summary>
    Cleaning,
    Cleaned,
    /// <summary>Its folder is missing, unreadable or a link.</summary>
    Unavailable,
    /// <summary>Every app whose cache this is was running: skipped, never forced.</summary>
    InUse,
    NeedsAdmin,
    Failed,
    /// <summary>Not run because the run stopped before reaching it (a failed helper).</summary>
    Kept,
    Cancelled,
}

/// <summary>One row of the list. Bytes is the size the rule selects (measured) or what was freed (cleaned); null shows "—".</summary>
public sealed record CleanerRow(CleanerCategory Category, bool Ticked, CleanerRowState State, long? Bytes = null, int Files = 0, int InUseFiles = 0,
    bool LowerBound = false, bool FromEarlier = false, IReadOnlyList<string>? SkippedApps = null)
{
    /// <summary>"820 MB", "1.6 GB+", "—", or a status word.</summary>
    public string Text => CleanerText.Status(State) ?? CleanerText.Size(Bytes, LowerBound);
}

/// <summary>What a confirm asks: the ticked categories that can run, and the total. <see cref="Asked"/> is every category the run was for, runnable or not (the result lists them all).</summary>
public sealed record CleanerPlan(IReadOnlyList<CleanerPlanItem> Items, bool AdminDeclined, bool HelperFailed, IReadOnlyList<string> Asked)
{
    public long Bytes => Items.Sum(i => i.Bytes);
    public bool LowerBound => Items.Any(i => i.LowerBound);
    public bool NeedsAdmin => Items.Any(i => i.Category.NeedsAdmin);
    public bool IsEmpty => Items.Count == 0 || Items.All(i => i.Bytes == 0 && i.Files == 0);
    /// <summary>"Clean 1.0 GB?"</summary>
    public string Question => "Clean " + CleanerText.Size(Bytes, LowerBound) + "?";
}

public sealed record CleanerPlanItem(CleanerCategory Category, long Bytes, int Files, bool LowerBound);

/// <summary>What a run did, for the result list, the header line and the bar.</summary>
public sealed record CleanerRunResult(IReadOnlyList<CleanerRow> Rows, long FreedBytes, int InUseFiles, bool HelperFailed, bool Cancelled)
{
    public int Failed => Rows.Count(r => r.State == CleanerRowState.Failed);
    public int NeedsAdmin => Rows.Count(r => r.State == CleanerRowState.NeedsAdmin);

    /// <summary>"1.0 GB freed".</summary>
    public string Freed => CleanerText.Size(FreedBytes, false) + " freed";
    /// <summary>The header's second part: "1 failed", "Admin helper failed", or null.</summary>
    public string? Meta => HelperFailed ? "Admin helper failed" : Failed > 0 ? Failed + " failed" : null;
    /// <summary>The bar's title: "1.0 GB freed", or "Clean failed" when nothing was freed and something failed.</summary>
    public string BarTitle => FreedBytes == 0 && (Failed > 0 || HelperFailed) ? "Clean failed" : Freed;
    /// <summary>The bar's second line: "2 need admin", the failed category, or "Cleaner".</summary>
    public string BarDetail =>
        NeedsAdmin > 0 ? NeedsAdmin + " need admin"
        : Rows.FirstOrDefault(r => r.State == CleanerRowState.Failed) is { } failed ? failed.Category.Name
        : "Cleaner";
    public bool BarCaution => NeedsAdmin > 0 || Failed > 0 || HelperFailed;
}

/// <summary>The words and sizes of the approved mockup: terse, no sentences.</summary>
public static class CleanerText
{
    public static string? Status(CleanerRowState state) => state switch
    {
        CleanerRowState.InUse => "In use",
        CleanerRowState.Unavailable => "Unavailable",
        CleanerRowState.NeedsAdmin => "Needs admin",
        CleanerRowState.Failed => "Failed",
        CleanerRowState.Kept => "Kept",
        CleanerRowState.Cancelled => "Cancelled",
        _ => null,
    };

    /// <summary>"820 MB", "1.0 GB", "0 B"; a trailing "+" marks a lower bound; null is "—".</summary>
    public static string Size(long? bytes, bool lowerBound = false)
    {
        if (bytes is not { } b) return "—";
        var c = CultureInfo.InvariantCulture;
        string text = b switch
        {
            < 1000 => b + " B",
            < 1_000_000 => Math.Max(1, Math.Round(b / 1000.0, MidpointRounding.AwayFromZero)).ToString("0", c) + " KB",
            < 1_000_000_000 => Math.Round(b / 1e6, MidpointRounding.AwayFromZero).ToString("0", c) + " MB",
            _ => (b / 1e9).ToString("0.0", c) + " GB",
        };
        return lowerBound ? text + "+" : text;
    }
}
