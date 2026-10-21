// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.SystemIntegration;

/// <summary>
/// One Chippi per user session. The first launch owns a named mutex and listens on a named
/// event; a later launch sets that event and exits, and the first instance raises
/// <see cref="ActivationRequested"/> (the shell opens the flyout).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
        _listener = new Thread(Listen) { IsBackground = true, Name = "Chippi single-instance listener" };
        _listener.Start();
    }

    /// <summary>Raised on a background thread each time another launch asks this instance to show itself.</summary>
    public event EventHandler? ActivationRequested;

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
            return new SingleInstance(mutex, activate);

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
        _stop.Dispose();
    }

    private void Listen()
    {
        var handles = new WaitHandle[] { _stop, _activate };
        while (WaitHandle.WaitAny(handles) == 1)
            ActivationRequested?.Invoke(this, EventArgs.Empty);
    }
}
