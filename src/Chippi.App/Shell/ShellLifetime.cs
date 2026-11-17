// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Diagnostics;

namespace Chippi.App.Shell;

/// <summary>
/// The rules for how the shell starts failing and ends: a failure before the tray exists must not leave an
/// invisible process holding the single-instance lock, and stopping modules never waits forever.
/// </summary>
internal static class ShellLifetime
{
    /// <summary>How long Quit waits for the modules to stop before it exits anyway.</summary>
    public static readonly TimeSpan QuitLimit = TimeSpan.FromSeconds(10);

    /// <summary>Windows ends a program that is still running a few seconds after a logoff or shutdown asks it to.</summary>
    public static readonly TimeSpan SessionEndLimit = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Whether an exception on the UI thread ends the app. Until the tray is composed nothing visible keeps
    /// the app reachable, so it must exit and free the single-instance lock; after that a transient UI
    /// exception is logged and the tray app carries on.
    /// </summary>
    public static bool ExitOnUnhandled(bool trayComposed) => !trayComposed;

    /// <summary>
    /// Runs <paramref name="stop"/> with a token that cancels after <paramref name="limit"/>, and stops waiting
    /// then even if it ignores the token. Returns true when it finished in time. A failure is logged, not thrown.
    /// </summary>
    public static async Task<bool> StopWithinAsync(Func<CancellationToken, Task> stop, TimeSpan limit)
    {
        using var timeout = new CancellationTokenSource(limit);
        using var delayCts = new CancellationTokenSource();
        Task task;
        try
        {
            task = stop(timeout.Token);
        }
        catch (Exception ex)
        {
            Log.Error("Stopping modules failed", ex);
            return true;
        }
        var delay = Task.Delay(limit, delayCts.Token);
        var first = await Task.WhenAny(task, delay);
        delayCts.Cancel();
        if (first != task)
        {
            Log.Warn($"Stopping modules took longer than {limit.TotalSeconds:0} s; exiting anyway");
            // Nobody awaits it now: observe a later failure so it isn't reported as unobserved.
            _ = task.ContinueWith(t => Log.Error("Stopping modules failed after the wait ended", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            return false;
        }
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            Log.Warn("Stopping modules was cancelled; exiting anyway");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Stopping modules failed", ex);
        }
        return true;
    }

    /// <summary>
    /// <see cref="StopWithinAsync"/> from code that can't await (OnExit): the UI thread keeps pumping while it
    /// waits, since modules stop on it and a blocking wait would hang them.
    /// </summary>
    public static void StopWithin(Dispatcher dispatcher, Func<CancellationToken, Task> stop, TimeSpan limit)
    {
        var frame = new DispatcherFrame();
        var work = StopWithinAsync(stop, limit);
        void Release()
        {
            try
            {
                dispatcher.BeginInvoke(() => frame.Continue = false);
            }
            catch (Exception)
            {
                frame.Continue = false;
            }
        }
        _ = work.ContinueWith(_ => Release(), TaskScheduler.Default);
        // Backstop that doesn't depend on the work: the frame never outlives the limit by more than a margin.
        using var backstop = new CancellationTokenSource();
        _ = Task.Delay(limit + TimeSpan.FromSeconds(2), backstop.Token)
            .ContinueWith(_ => frame.Continue = false, TaskContinuationOptions.OnlyOnRanToCompletion);
        try
        {
            Dispatcher.PushFrame(frame);
        }
        catch (InvalidOperationException ex)
        {
            // The dispatcher has already shut down: there is nothing to pump, so stop without waiting.
            Log.Warn("Stopping modules: the dispatcher was not available to wait on", ex);
        }
        finally
        {
            backstop.Cancel();
        }
    }
}
