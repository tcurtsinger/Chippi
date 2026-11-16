// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Chippi.Elevated.Protocol;

namespace Chippi.UI.Admin;

public sealed class WindowsAdminSession(bool allowElevation) : IAdminSessionFactory
{
    public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
    {
        if (!allowElevation) throw new UnauthorizedAccessException("Admin operations are refused in dev profiles.");
        if (PeerIdentity.CurrentIsElevated()) throw new UnauthorizedAccessException("Chippi must run as standard user.");
        ct.ThrowIfCancellationRequested();
        string executable = Path.Combine(AppContext.BaseDirectory, "Chippi.Elevated.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Admin helper missing.", executable);
        string name = "chippi-admin-" + Guid.NewGuid().ToString("N");
        var pipe = LocalPipe.Create(name);
        Process? process = null;
        PeerIdentity? peer = null;
        try
        {
            // The UAC prompt can stay open indefinitely, without blocking WPF's dispatcher.
            process = await Task.Run(() => Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true, Verb = "runas", Arguments = "--pipe " + name,
                WorkingDirectory = AppContext.BaseDirectory, WindowStyle = ProcessWindowStyle.Hidden,
            })).ConfigureAwait(false) ?? throw new Win32Exception("Admin helper did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (LocalPipe.ClientId(pipe.SafePipeHandle) != process.Id) throw new UnauthorizedAccessException("Unexpected helper process.");
            peer = PeerIdentity.Open((uint)process.Id, executable, elevated: true);
            return new Connection(pipe, process, peer);
        }
        catch { pipe.Dispose(); peer?.Dispose(); process?.Dispose(); throw; }
    }
    private sealed class Connection(NamedPipeServerStream pipe, Process process, PeerIdentity peer) : IAdminConnection
    {
        public Stream Stream => pipe;
        public ValueTask DisposeAsync() { pipe.Dispose(); peer.Dispose(); process.Dispose(); return ValueTask.CompletedTask; }
    }
}
