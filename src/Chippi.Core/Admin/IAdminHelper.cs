// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Core.Admin;

public sealed record AdminUpdateResult(string PackageId, string State, int? ExitCode = null, string? Reason = null);
public sealed record AdminUpdateProgress(string PackageId, int Index, int Count);

/// <summary>Run only after the caller has confirmed the exact batch. Cancellation skips pending packages, never kills an installer.</summary>
public interface IAdminHelper
{
    Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<string> packageIds,
        IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default);
}
