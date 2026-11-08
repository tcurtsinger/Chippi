// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Threading;

namespace Chippi.Island.Tests;

internal static partial class WpfTestThread
{
    internal static string ShotsTheme => Environment.GetEnvironmentVariable("CHIPPI_SHOTS_THEME") == "light" ? "Light" : "Dark";

    /// <summary>A project's own setup on the UI thread before its first test; the island tests' is in WpfTestThread.Island.cs.</summary>
    static partial void Prepare();

    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Prepare();
                // Shots render in the light theme with CHIPPI_SHOTS_THEME=light.
                foreach (var source in new[] { $"Themes/{ShotsTheme}.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/{source}") });
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    internal static void Run(Action test) => Ui.Value.Invoke(test);

    /// <summary>
    /// Waits for <paramref name="task"/> while this UI thread keeps running, so work that comes back to it can
    /// finish: the module registry resumes each start and stop here. Blocking with GetResult on it would hang.
    /// </summary>
    internal static void Wait(Task task)
    {
        if (!task.IsCompleted && SynchronizationContext.Current is DispatcherSynchronizationContext)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
