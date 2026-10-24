// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

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
        WpfTestThread.Run(() =>
        {
            var window = new IslandWindow();
            DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
            {
                thrown = e.Exception;
                e.Handled = true; // as App does, which is why it went unnoticed
            };
            Dispatcher.CurrentDispatcher.UnhandledException += handler;
            try
            {
                window.Escape();
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.UnhandledException -= handler;
                window.Close();
            }
        });
        Assert.Null(thrown);
    }
}
