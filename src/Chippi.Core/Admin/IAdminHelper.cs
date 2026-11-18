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

    /// <summary>One confirmed action at a time, in a session that stays open briefly for the next one. WM_CLOSE to the process's windows, for Port manager's Close.</summary>
    Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default);
    /// <summary>TerminateProcess, for Port manager's Force end.</summary>
    Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default);
    /// <summary>Stops only the named service, and only when the helper attributes the endpoint to it.</summary>
    Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default);

    /// <summary>
    /// Read-only: what the Cleaner's rule would delete in each category (files over a day old). The first call of a flow shows the one UAC
    /// prompt; the session then stays open for <see cref="CleanCategoriesAsync"/>. A declined prompt answers NeedsAdmin for every name.
    /// </summary>
    Task<IReadOnlyList<AdminCategoryResult>> MeasureCategoriesAsync(IReadOnlyList<AdminCategory> names, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdminCategoryResult>>([.. names.Select(n => new AdminCategoryResult(n, AdminCategoryState.NeedsAdmin))]);

    /// <summary>
    /// Deletes for good, in the session the measure opened (no second prompt), and ends that session when done. Cancel stops between files,
    /// reports what was freed so far and ends the session with Done. Cancelling before a category starts leaves it Cancelled.
    /// </summary>
    Task<IReadOnlyList<AdminCategoryResult>> CleanCategoriesAsync(IReadOnlyList<AdminCategory> names, IProgress<AdminCategoryProgress>? progress = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdminCategoryResult>>([.. names.Select(n => new AdminCategoryResult(n, AdminCategoryState.NeedsAdmin))]);

    /// <summary>
    /// Runs each HKLM app's uninstaller, one at a time, in one helper session (one prompt for the whole queue). Chippi's own confirm comes first.
    /// The helper reads the entry itself; Chippi sends only the view and key name. Cancel skips the apps not yet started and never stops a running
    /// uninstaller. A declined prompt answers NeedsAdmin for every item and runs nothing. The session stays open briefly for the leftover step
    /// and ends with <see cref="EndSessionAsync"/>.
    /// </summary>
    Task<IReadOnlyList<AdminUninstallResult>> UninstallAsync(IReadOnlyList<AdminUninstallItem> items, IProgress<AdminUninstallProgress>? progress = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdminUninstallResult>>([.. items.Select(i => new AdminUninstallResult(i, AdminUninstallState.NeedsAdmin))]);

    /// <summary>
    /// Removes the admin-only leftovers of an app the helper uninstalled, by the id its record gave (days later is fine, in a new session; one prompt).
    /// Files and folders go to the owner's Recycle Bin, registry keys are exported then deleted. The helper decides from its own record, never from
    /// the class or path alone: each item comes back Removed, Kept (with its words), InUse, Failed, or Yours (Chippi deletes it as the owner).
    /// More than 200 items go in batches in the same session. A declined prompt answers NeedsAdmin for every item. The session stays open briefly;
    /// the flow ends it with <see cref="EndSessionAsync"/>.
    /// </summary>
    Task<IReadOnlyList<AdminLeftoverResult>> DeleteLeftoversAsync(Guid recordId, IReadOnlyList<AdminLeftoverItem> items, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdminLeftoverResult>>([.. items.Select(i => new AdminLeftoverResult(i, AdminLeftoverState.NeedsAdmin))]);

    /// <summary>
    /// Deletes the helper's record of an app (its leftovers dismissed, or all removed). True when the helper answered Dropped, also when it had no
    /// such record. False when the prompt was declined or the helper failed.
    /// </summary>
    Task<bool> DropRecordAsync(Guid recordId, CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>One app: <see cref="UninstallAsync(IReadOnlyList{AdminUninstallItem}, IProgress{AdminUninstallProgress}?, CancellationToken)"/> with a queue of one.</summary>
    async Task<AdminUninstallResult> UninstallAsync(AdminRegistryView view, string keyName, string? appName = null, CancellationToken ct = default) =>
        (await UninstallAsync([new AdminUninstallItem(view, keyName, appName)], null, ct).ConfigureAwait(false))[0];

    /// <summary>
    /// A port action or a category measure leaves the helper session open for the next job (Close, then End; measure, then clean: one prompt).
    /// The flow that asked calls this when it finishes or is cancelled, and on stop; the session also ends on its own after about 50 seconds
    /// idle. Safe with no session open.
    /// </summary>
    Task EndSessionAsync() => Task.CompletedTask;
}
