// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace Chippi.Core.SystemIntegration;

/// <summary>A later launch's request, with the arguments it was started with (empty for a plain launch).</summary>
public sealed class ActivationEventArgs(IReadOnlyList<string> arguments) : EventArgs
{
    public IReadOnlyList<string> Arguments { get; } = arguments;
}

/// <summary>
/// One Chippi per user session. The first launch owns a named mutex and listens on a named pipe; a
/// later launch sends its command-line arguments down the pipe and exits, and the first instance
/// raises <see cref="ActivationRequested"/> with them (the shell routes a verb such as
/// <c>--organize</c>, or shows the island's pages).
/// </summary>
/// <remarks>
/// The pipe is per user and session (its name carries both, and it accepts the current user only).
/// Windows only lets the process the user just started take the foreground, so before it sends, the
/// second launch hands that right to the running instance with <c>AllowSetForegroundWindow</c> (the
/// owner publishes its process id in a small named memory block for this).
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const int AsfwAny = -1;
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly Mutex _mutex;
    private readonly MemoryMappedFile? _owner;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _listener;
    private readonly Lock _gate = new();
    private readonly Queue<IReadOnlyList<string>> _pending = new();
    private EventHandler<ActivationEventArgs>? _activationRequested;
    private bool _disposed;

    private SingleInstance(Mutex mutex, MemoryMappedFile? owner, string pipeName)
    {
        _mutex = mutex;
        _owner = owner;
        _pipeName = pipeName;
        _listener = new Thread(Listen) { IsBackground = true, Name = "Chippi single-instance listener" };
        _listener.Start();
    }

    /// <summary>
    /// Raised on a background thread each time another launch asks this instance to act. Requests
    /// that arrive before anyone subscribes are held, in order, and raised on the first subscribe,
    /// so a second launch during startup is never lost.
    /// </summary>
    public event EventHandler<ActivationEventArgs>? ActivationRequested
    {
        add
        {
            List<IReadOnlyList<string>> held;
            lock (_gate)
            {
                _activationRequested += value;
                held = [.. _pending];
                _pending.Clear();
            }
            foreach (var arguments in held)
                value?.Invoke(this, new ActivationEventArgs(arguments));
        }
        remove
        {
            lock (_gate)
                _activationRequested -= value;
        }
    }

    /// <summary>
    /// Returns the owner object when this is the first instance. Otherwise sends
    /// <paramref name="arguments"/> to the running instance and returns null; the caller should exit.
    /// </summary>
    /// <param name="name">Session-local name. Tests pass a unique one.</param>
    /// <param name="arguments">This launch's arguments, delivered to the running instance.</param>
    public static SingleInstance? TryAcquire(string name = "Chippi", IReadOnlyList<string>? arguments = null)
    {
        string pipeName = PipeName(name);
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.SingleInstance", out bool createdNew);
        if (createdNew)
            return new SingleInstance(mutex, PublishOwner(name), pipeName);

        mutex.Dispose();
        AllowOwnerToTakeForeground(name);
        Send(pipeName, arguments ?? []);
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop.Cancel();
        _listener.Join(TimeSpan.FromSeconds(2));
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from a thread that does not own it; closing the handle below frees it anyway.
        }
        _mutex.Dispose();
        _owner?.Dispose();
        _stop.Dispose();
    }

    /// <summary>Pipes are machine-wide, so the name carries the user and session; Local\ names do not apply to pipes.</summary>
    private static string PipeName(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        return $"{name}.Activate.{Process.GetCurrentProcess().SessionId}.{identity.User?.Value ?? "user"}";
    }

    private void Listen()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
                reading.CancelAfter(SendTimeout);
                var arguments = JsonSerializer.DeserializeAsync<string[]>(server, cancellationToken: reading.Token).AsTask().GetAwaiter().GetResult();
                Deliver(arguments ?? []);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                // A broken or garbled request is dropped; keep listening. A pipe we cannot create
                // (another instance of this user and session still owns it) is retried shortly.
                if (token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(200)))
                    return;
            }
        }
    }

    private void Deliver(IReadOnlyList<string> arguments)
    {
        EventHandler<ActivationEventArgs>? handler;
        lock (_gate)
        {
            handler = _activationRequested;
            if (handler is null)
                _pending.Enqueue(arguments);
        }
        handler?.Invoke(this, new ActivationEventArgs(arguments));
    }

    /// <summary>Sends this launch's arguments, retrying while the owner is still starting its listener. Best effort.</summary>
    private static void Send(string pipeName, IReadOnlyList<string> arguments)
    {
        var deadline = DateTime.UtcNow + SendTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                JsonSerializer.Serialize(client, arguments.ToArray());
                client.Flush();
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
    /// <summary>The owner's process id, readable by later launches. Null if the block cannot be made (the fallback grants any process).</summary>
    private static MemoryMappedFile? PublishOwner(string name)
    {
        try
        {
            var block = MemoryMappedFile.CreateOrOpen($@"Local\{name}.Owner", sizeof(int));
            using var view = block.CreateViewAccessor(0, sizeof(int));
            view.Write(0, Environment.ProcessId);
            return block;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Lets the running instance take the foreground when it shows the island's pages. Best effort: a refusal only means no focus.</summary>
    private static void AllowOwnerToTakeForeground(string name)
    {
        // Unknown owner (still starting up): let any process take the foreground once instead.
        int owner = ReadOwner(name) is int id and > 0 ? id : AsfwAny;
        _ = AllowSetForegroundWindow(owner);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>The process id the running instance published, or null.</summary>
    internal static int? ReadOwner(string name)
    {
        try
        {
            using var block = MemoryMappedFile.OpenExisting($@"Local\{name}.Owner", MemoryMappedFileRights.Read);
            using var view = block.CreateViewAccessor(0, sizeof(int), MemoryMappedFileAccess.Read);
            return view.ReadInt32(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
