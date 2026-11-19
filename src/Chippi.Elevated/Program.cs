// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO.Pipes;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--pipe" || !Messages.PipeNameAllowed(args[1])) return 2;
        int failedStage = 4;
        try
        {
            if (!PeerIdentity.CurrentIsElevated()) return 3;
            if (!PeerIdentity.RestrictDllSearch()) return 7;
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.ConnectAsync(connect.Token).ConfigureAwait(false);
            failedStage = 5;
            uint peerPid = LocalPipe.ServerId(pipe.SafePipeHandle);
            string chippiPath = Path.Combine(AppContext.BaseDirectory, "Chippi.exe");
            using var peer = PeerIdentity.Open(peerPid, chippiPath, elevated: false);
            failedStage = 6;
            var runner = new PackageUpdater(peer.StandardToken);
            var ports = new PortActions(new NativePortSystem(), [Environment.ProcessId, (int)peerPid], Path.GetFullPath(chippiPath));
            var categories = new CategoryActions(CategoryRoots.ForWindows());
            using var recordStore = new NativeRecordStore(peer.StandardToken);
            var uninstalls = new UninstallActions(new NativeUninstallRegistry(peer.StandardToken), recordStore, new NativeFsProbe(peer.StandardToken), new NativeProcessLauncher(), new NativeUninstallHost());
            uninstalls.Maintain();   // records past 30 days go each time the helper starts
            string ownerSid = NativeLeftoverSystem.SidOf(peer.StandardToken) ?? throw new UnauthorizedAccessException("No owner.");
            string ownerProfile = NativeLeftoverSystem.ProfileFolder(ownerSid) ?? throw new UnauthorizedAccessException("No owner profile.");
            var leftovers = new LeftoverActions(recordStore, new NativeInstalledApps(ownerSid), new NativeFsProbe(peer.StandardToken), new NativeLeftoverRegistry(peer.StandardToken),
                new NativeLeftoverSystem(ownerProfile), new NativeProcessLauncher());
            await new HelperSession(runner, ports: ports, categories: categories, uninstalls: uninstalls, leftovers: leftovers).RunAsync(pipe).ConfigureAwait(false);
            return 0;
        }
        catch (Exception)
        { return failedStage; }
    }
}
