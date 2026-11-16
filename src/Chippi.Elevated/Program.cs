// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO.Pipes;
using System.Text.Json;
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
            using var peer = PeerIdentity.Open(LocalPipe.ServerId(pipe.SafePipeHandle), Path.Combine(AppContext.BaseDirectory, "Chippi.exe"), elevated: false);
            failedStage = 6;
            var runner = new PackageUpdater(peer.StandardToken);
            await new HelperSession(runner).RunAsync(pipe).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or JsonException or OperationCanceledException)
        { return failedStage; }
    }
}
