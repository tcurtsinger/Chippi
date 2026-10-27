// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Settings › Notifications. Settings keeps its built-in pages, so this one must refresh when it shows again.</summary>
public sealed class NotificationsPageTests
{
    [Fact]
    public async Task A_feature_installed_while_the_page_was_away_shows_when_it_returns()
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-notifications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ShellContext? shell = null;
        try
        {
            NotificationsPage page = null!;
            WpfTestThread.Run(() =>
            {
                shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, "notifications-test");
                page = new NotificationsPage(shell);
                Assert.DoesNotContain("Pinger", Texts(page));
            });

            // Installed from another page while this one isn't shown, so it hears no registry change.
            shell!.Registry.Register(new Pinger());
            await shell.Registry.StartAsync();
            await shell.Registry.SetInstalledAsync(Pinger.Id, true);

            WpfTestThread.Run(() =>
            {
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Contains("Pinger", Texts(page));
                Assert.Contains("Ping", Texts(page));
            });
        }
        finally
        {
            if (shell is not null) await shell.Registry.StopAllAsync();
            Directory.Delete(root, true);
        }
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text)
            yield return text.Text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Texts(child))
                yield return item;
    }

    private sealed class Pinger : IFeatureModule
    {
        public const string Id = "pinger-test";
        public FeatureDescriptor Descriptor { get; } = new(Id, "Pinger", "Declares one notice", FeatureGroup.System);

        public Task StartAsync(ModuleContext context, CancellationToken ct)
        {
            context.Notifier.Declare([new NoticeKind("ping", "Ping", NoticePlaces.Island)]);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
