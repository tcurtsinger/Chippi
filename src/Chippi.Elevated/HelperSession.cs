// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

public interface IPackageUpdater { Task<AdminReply> UpdateAsync(AdminRequest request); }

public sealed class HelperSession(IPackageUpdater updater, TimeProvider? time = null)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    public async Task RunAsync(Stream pipe, CancellationToken stop = default)
    {
        await WriteReply(pipe, new AdminReply(Guid.Empty, "Ready"), stop).ConfigureAwait(false);
        var seen = new HashSet<Guid>();
        while (!stop.IsCancellationRequested)
        {
            using var idle = new CancellationTokenSource(IdleTimeout, time ?? TimeProvider.System);
            using var read = CancellationTokenSource.CreateLinkedTokenSource(stop, idle.Token);
            AdminRequest? request;
            try { request = await Messages.ReadRequestAsync(pipe, read.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (idle.IsCancellationRequested) { return; }
            if (request is null) return;
            if (!seen.Add(request.Id) || seen.Count > 1024) throw new InvalidDataException("Repeated or excessive request.");
            if (request.Verb == "Done")
            {
                await WriteReply(pipe, new AdminReply(request.Id, "Done"), stop).ConfigureAwait(false);
                return;
            }
            await WriteReply(pipe, new AdminReply(request.Id, "Progress", request.PackageId), stop).ConfigureAwait(false);
            // Once started, an installer finishes even if Chippi cancels or disconnects.
            var result = await updater.UpdateAsync(request).ConfigureAwait(false);
            await WriteReply(pipe, result, stop).ConfigureAwait(false);
        }
    }

    private async Task WriteReply(Stream pipe, AdminReply reply, CancellationToken stop)
    {
        using var timeout = new CancellationTokenSource(IdleTimeout, time ?? TimeProvider.System);
        using var write = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        await Messages.WriteAsync(pipe, reply, write.Token).ConfigureAwait(false);
    }
}
