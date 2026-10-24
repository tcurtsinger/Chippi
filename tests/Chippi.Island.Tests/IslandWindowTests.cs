// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Threading;
using Chippi.App.Island;

namespace Chippi.Island.Tests;

/// <summary>The tab window itself, built on its own UI thread with the app's resources.</summary>
public sealed class IslandWindowTests
{
    [Fact]
    public void Esc_closes_the_island_without_throwing_on_the_dispatcher()
    {
        Exception? thrown = null;
        OnUiThread(() =>
        {
            var window = new IslandWindow();
            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
            {
                thrown = e.Exception;
                e.Handled = true; // as App does, which is why it went unnoticed
            };
            window.Escape();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        });
        Assert.Null(thrown);
    }

    private static void OnUiThread(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var source in new[] { "Themes/Dark.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/{source}") });
                test();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The UI thread failed.", failure);
    }
}
