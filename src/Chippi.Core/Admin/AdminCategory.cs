// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Core.Admin;

/// <summary>The four Windows categories only the admin helper can read or clean. Each maps to folders fixed in the helper's own code.</summary>
public enum AdminCategory { WindowsTemp, WindowsUpdateDownloads, DeliveryOptimization, SystemCrashDumps }

/// <summary>
/// Measured and Cleaned carry the bytes, file count and in-use count. Unavailable: the folder is missing or is a link. Failed: the helper
/// or Windows failed. NeedsAdmin: the prompt was declined or the helper couldn't start, so nothing ran. Cancelled: stopped before or during
/// the clean (a cancelled clean still reports what it freed).
/// </summary>
public enum AdminCategoryState { Measured, Cleaned, Unavailable, Failed, NeedsAdmin, Cancelled }

/// <summary>Bytes and Files are what the rule selects (Measured) or what was deleted (Cleaned); InUse counts files skipped because something held them open.</summary>
public sealed record AdminCategoryResult(AdminCategory Category, AdminCategoryState State, long Bytes = 0, int Files = 0, int InUse = 0, string? Reason = null)
{
    /// <summary>The status words of the approved mockup; null when the result carries a size instead.</summary>
    public string? Text => State switch
    {
        AdminCategoryState.Unavailable => "Unavailable",
        AdminCategoryState.NeedsAdmin => "Needs admin",
        AdminCategoryState.Cancelled => "Cancelled",
        AdminCategoryState.Failed => "Failed",
        _ => null,
    };
}

/// <summary>Reported before a category starts (Result null) and when it ends.</summary>
public sealed record AdminCategoryProgress(AdminCategory Category, int Index, int Count, AdminCategoryResult? Result = null);
