// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Notifications;
using Microsoft.Windows.AppNotifications;

namespace Chippi.ToastSpike;

internal sealed class Presenter(Action<string> activate) : IToastPresenter, IDisposable
{
    public void Register()
    {
        AppNotificationManager.Default.NotificationInvoked += Invoked;
        try { AppNotificationManager.Default.Register(); }
        catch
        {
            AppNotificationManager.Default.NotificationInvoked -= Invoked;
            throw;
        }
    }

    private void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => activate(args.Argument);

    public void Show(string moduleId, Notice notice) => AppNotificationManager.Default.Show(new AppNotification(Payload.Xml(notice)));

    public static void Uninstall() => AppNotificationManager.Default.UnregisterAll();

    public void Dispose()
    {
        AppNotificationManager.Default.Unregister();
        AppNotificationManager.Default.NotificationInvoked -= Invoked;
    }
}
