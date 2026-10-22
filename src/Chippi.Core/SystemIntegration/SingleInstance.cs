// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Chippi.Core.SystemIntegration;

/// <summary>
/// One Chippi per user session. The first launch owns a named mutex and listens on a named
/// event; a later launch sets that event and exits, and the first instance raises
/// <see cref="ActivationRequested"/> (the shell opens the flyout).
/// </summary>
/// <remarks>
/// Windows only lets the process the user just started take the foreground. So before it signals,
/// the second launch hands that right to the running instance with <c>AllowSetForegroundWindow</c>
/// (the owner publishes its process id in a small named memory block for this), and the flyout
/// can come to the front without any workaround.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly MemoryMappedFile? _owner;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, MemoryMappedFile? owner)
    {
        _mutex = mutex;
        _activate = activate;
        _owner = owner;
        _listener = new Thread(Listen) { IsBackground = true, Name = "Chippi single-instance listener" };
        _listener.Start();
    }

    private readonly Lock _gate = new();
    private EventHandler? _activationRequested;
    private bool _pending;

    /// <summary>
    /// Raised on a background thread each time another launch asks this instance to show itself.
    /// A request that arrives before anyone subscribes is held and raised on the first subscribe,
    /// so a second launch during startup is never lost.
    /// </summary>
    public event EventHandler? ActivationRequested
    {
        add
        {
            bool deliver;
            lock (_gate)
            {
                _activationRequested += value;
                deliver = _pending;
                _pending = false;
            }
            if (deliver)
                value?.Invoke(this, EventArgs.Empty);
        }
        remove
        {
            lock (_gate)
                _activationRequested -= value;
        }
    }

    /// <summary>
    /// Returns the owner object when this is the first instance. Otherwise signals the running
    /// instance and returns null; the caller should exit.
    /// </summary>
    /// <param name="name">Session-local name. Tests pass a unique one.</param>
    public static SingleInstance? TryAcquire(string name = "Chippi")
    {
        // Create-or-open, never open-only: whichever launch gets here first creates the event, so
        // a second launch racing the first's startup still lands its signal.
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.SingleInstance", out bool createdNew);
        if (createdNew)
            return new SingleInstance(mutex, activate, PublishOwner(name));

        AllowOwnerToTakeForeground(name);
        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop.Set();
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
        _activate.Dispose();
        _owner?.Dispose();
        _stop.Dispose();
    }

    private void Listen()
    {
        var handles = new WaitHandle[] { _stop, _activate };
        while (WaitHandle.WaitAny(handles) == 1)
        {
            EventHandler? handler;
            lock (_gate)
            {
                handler = _activationRequested;
                if (handler is null)
                    _pending = true;
            }
            handler?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Lets the running instance take the foreground when it opens the flyout. Best effort: a refusal only means no focus.</summary>
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
