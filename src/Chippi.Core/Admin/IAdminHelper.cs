// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Core.Admin;

public sealed record AdminUpdateResult(string PackageId, string State, int? ExitCode = null, string? Reason = null);
/// <summary>One reviewed package: the helper installs exactly ExpectedVersion, or fails with "Version changed".</summary>
public sealed record AdminUpdateItem(string PackageId, string ExpectedVersion, bool IncludeUnknown = false);
public sealed record AdminUpdateProgress(string PackageId, int Index, int Count, AdminUpdateResult? Result = null);

/// <summary>Run only after the caller has confirmed the exact batch. Cancellation skips pending packages, never kills an installer; a hung helper times out and releases the session.</summary>
public interface IAdminHelper
{
    Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items,
        IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default);
}
