// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;

namespace Chippi.Features.Sound.Tests;

/// <summary>Runs unshown Sound controls on a single STA that stays available while each test's audio fixture stops.</summary>
internal static class SoundUiThread
{
    private static readonly Lazy<Task<Dispatcher>> Ui = new(Start);

    private static Task<Dispatcher> Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher;
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            }
            catch (Exception ex)
            {
                ready.SetException(ex);
                return;
            }
            ready.SetResult(dispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Sound test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    internal static Task Sta(Action test) => Sta(() => { test(); return Task.CompletedTask; });

    internal static async Task Sta(Func<Task> test)
    {
        var dispatcher = await Ui.Value.ConfigureAwait(false);
        await dispatcher.InvokeAsync(test).Task.Unwrap().ConfigureAwait(false);
    }
}
