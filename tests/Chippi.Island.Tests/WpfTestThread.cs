// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Threading;

namespace Chippi.Island.Tests;

internal static class WpfTestThread
{
    internal static string ShotsTheme => Environment.GetEnvironmentVariable("CHIPPI_SHOTS_THEME") == "light" ? "Light" : "Dark";

    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // A real click or Esc on the PC while the tests run must not close a test's panel; tests press for themselves.
                Chippi.App.Island.IslandWindow.WatchesInput = false;
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
}
