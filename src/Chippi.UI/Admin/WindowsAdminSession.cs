// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Chippi.Elevated.Protocol;

namespace Chippi.UI.Admin;

public sealed class WindowsAdminSession : IAdminSessionFactory
{
    private readonly bool _allowElevation;
    private readonly Func<string, string, Task<Process?>> _launch;
    public WindowsAdminSession(bool allowElevation) : this(allowElevation, LaunchElevated) { }
    internal WindowsAdminSession(bool allowElevation, Func<string, string, Task<Process?>> launch)
    { _allowElevation = allowElevation; _launch = launch; }

    private static Task<Process?> LaunchElevated(string executable, string name) => Task.Run(() => Process.Start(new ProcessStartInfo(executable)
    {
        UseShellExecute = true, Verb = "runas", Arguments = "--pipe " + name,
        WorkingDirectory = AppContext.BaseDirectory, WindowStyle = ProcessWindowStyle.Hidden,
    }));

    public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
    {
        if (!_allowElevation) throw new UnauthorizedAccessException("Admin operations are refused in dev profiles.");
        if (PeerIdentity.CurrentIsElevated()) throw new UnauthorizedAccessException("Chippi must run as standard user.");
        ct.ThrowIfCancellationRequested();
        string executable = Path.Combine(AppContext.BaseDirectory, "Chippi.Elevated.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Admin helper missing.", executable);
        string name = "chippi-admin-" + Guid.NewGuid().ToString("N");
        var pipe = LocalPipe.Create(name);
        IDisposable? hold = null;
        Process? process = null;
        PeerIdentity? peer = null;
        try
        {
            // No other process can write, rename or delete the helper from here until it has started.
            hold = LaunchGuard.Hold(executable);
            // The UAC prompt can stay open indefinitely, without blocking WPF's dispatcher.
            process = await _launch(executable, name).ConfigureAwait(false) ?? throw new Win32Exception("Admin helper did not start.");
            hold.Dispose(); hold = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (LocalPipe.ClientId(pipe.SafePipeHandle) != process.Id) throw new UnauthorizedAccessException("Unexpected helper process.");
            peer = PeerIdentity.Open((uint)process.Id, executable, elevated: true);
            return new Connection(pipe, process, peer);
        }
        catch (Exception error)
        {
            int? exit = null;
            try { if (process is { HasExited: true }) exit = process.ExitCode; } catch (Win32Exception) { }
            hold?.Dispose();
            pipe.Dispose(); peer?.Dispose(); process?.Dispose();
            if (exit is { } code) throw new InvalidOperationException($"Admin helper exited during startup (exit {code}).", error);
            throw;
        }
    }
    private sealed class Connection(NamedPipeServerStream pipe, Process process, PeerIdentity peer) : IAdminConnection
    {
        public Stream Stream => pipe;
        public ValueTask DisposeAsync() { pipe.Dispose(); peer.Dispose(); process.Dispose(); return ValueTask.CompletedTask; }
    }
}
