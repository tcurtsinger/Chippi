// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

/// <summary>The token fires when the per-package ceiling passes; the updater then stops its installer tool.</summary>
public interface IPackageUpdater { Task<AdminReply> UpdateAsync(AdminRequest request, CancellationToken ceiling); }

public sealed class HelperSession(IPackageUpdater updater, TimeProvider? time = null, IPortActions? ports = null, ICategoryActions? categories = null,
    IUninstallActions? uninstalls = null, ILeftoverActions? leftovers = null)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    /// <summary>How long a package may keep running after its ceiling fires before the session answers for it.</summary>
    public static readonly TimeSpan CeilingGrace = TimeSpan.FromSeconds(15);
    public async Task RunAsync(Stream pipe, CancellationToken stop = default)
    {
        await WriteReply(pipe, new AdminReply(Guid.Empty, "Ready"), stop).ConfigureAwait(false);
        var seen = new HashSet<Guid>();
        var clock = time ?? TimeProvider.System;
        // A category job reads ahead while it runs, so a Done (Cancel) or a closed pipe stops it between files; the read is kept for the loop.
        Task<AdminRequest?>? readAhead = null;
        while (!stop.IsCancellationRequested)
        {
            AdminRequest? request;
            if (readAhead is not null)
            {
                var waiting = readAhead; readAhead = null;
                try { request = await waiting.WaitAsync(IdleTimeout, clock).ConfigureAwait(false); }
                catch (TimeoutException) { _ = waiting.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); return; }
            }
            else
            {
                using var idle = new CancellationTokenSource(IdleTimeout, clock);
                using var read = CancellationTokenSource.CreateLinkedTokenSource(stop, idle.Token);
                try { request = await Messages.ReadRequestAsync(pipe, read.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (idle.IsCancellationRequested) { return; }
            }
            if (request is null) return;
            if (!seen.Add(request.Id) || seen.Count > 1024) throw new InvalidDataException("Repeated or excessive request.");
            if (request.Verb == "Done")
            {
                await WriteReply(pipe, new AdminReply(request.Id, "Done"), stop).ConfigureAwait(false);
                return;
            }
            if (Messages.IsPortVerb(request.Verb))
            {
                // One quick, bounded action; it runs to its answer even if Chippi disconnects, like a package.
                var answer = ports is null ? new AdminReply(request.Id, Messages.PortFailed, Reason: "Port actions unavailable") : await ports.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
                await WriteReply(pipe, answer, stop).ConfigureAwait(false);
                continue;
            }
            if (Messages.IsUninstallVerb(request.Verb))
            {
                // An uninstaller finishes even if Chippi cancels or disconnects, like a package; the helper answers when its job has no processes left.
                var answer = uninstalls is null ? new AdminReply(request.Id, Messages.UninstallFailed, Reason: "Uninstall unavailable")
                    : await uninstalls.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
                await WriteReply(pipe, answer, stop).ConfigureAwait(false);
                continue;
            }
            if (Messages.IsLeftoverVerb(request.Verb))
            {
                // Like an uninstall, a leftover batch runs to its answer even if Chippi disconnects: the records and the Recycle Bin are the only state.
                var answer = leftovers is null ? new AdminReply(request.Id, Messages.LeftoversFailed, Reason: "Leftovers unavailable")
                    : await leftovers.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
                await WriteReply(pipe, answer, stop).ConfigureAwait(false);
                continue;
            }
            if (Messages.IsCategoryVerb(request.Verb))
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var work = categories is null ? Task.FromResult(new AdminReply(request.Id, Messages.CategoryFailed, Reason: "Cleaner unavailable", Category: request.Category))
                    : categories.RunAsync(request, cancel.Token);
                readAhead = Messages.ReadRequestAsync(pipe, stop);
                // Anything arriving, or the pipe closing, while a category runs stops it between files; its answer says what was done so far.
                if (await Task.WhenAny(work, readAhead).ConfigureAwait(false) != work) await cancel.CancelAsync().ConfigureAwait(false);
                var result = await work.ConfigureAwait(false);
                await WriteReply(pipe, result, stop).ConfigureAwait(false);
                continue;
            }
            await WriteReply(pipe, new AdminReply(request.Id, "Progress", request.PackageId), stop).ConfigureAwait(false);
            // Once started, an installer finishes even if Chippi cancels or disconnects.
            var package = await RunPackage(request).ConfigureAwait(false);
            await WriteReply(pipe, package, stop).ConfigureAwait(false);
        }
    }

    private async Task<AdminReply> RunPackage(AdminRequest request)
    {
        var clock = time ?? TimeProvider.System;
        using var ceiling = new CancellationTokenSource(Messages.PackageCeiling, clock);
        var run = updater.UpdateAsync(request, ceiling.Token);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ceiling.Token.Register(() => expired.TrySetResult());
        if (await Task.WhenAny(run, expired.Task).ConfigureAwait(false) == run) return await run.ConfigureAwait(false);
        try { return await run.WaitAsync(CeilingGrace, clock).ConfigureAwait(false); }
        catch (TimeoutException) { return new(request.Id, "Failed", request.PackageId, Reason: "Package update timed out"); }
    }

    private async Task WriteReply(Stream pipe, AdminReply reply, CancellationToken stop)
    {
        using var timeout = new CancellationTokenSource(IdleTimeout, time ?? TimeProvider.System);
        using var write = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        await Messages.WriteAsync(pipe, reply, write.Token).ConfigureAwait(false);
    }
}
