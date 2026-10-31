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
        string profile = "chippi-notifications-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        ShellContext? shell = null;
        try
        {
            NotificationsPage page = null!;
            WpfTestThread.Run(() =>
            {
                shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
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
                // Away again before the features stop: the open's availability check, still running, then
                // drops its result instead of rebuilding over stopping features on another test's turn.
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            });
        }
        finally
        {
            if (shell is not null) await shell.Registry.StopAllAsync();
            foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Hardware_filter_is_checked_on_open_and_keeps_saved_places() => WpfTestThread.Run(() =>
    {
        string profile = "chippi-notice-filter-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        var module = new HardwareNotices();
        try
        {
            shell.Registry.Register(module);
            shell.Registry.SetInstalledAsync("hardware-test", true).GetAwaiter().GetResult();
            var notifier = shell.Registry.Find("hardware-test")!.Notifier!;
            notifier.SetPlaces("low-battery", NoticePlaces.Windows);
            var page = new NotificationsPage(shell);
            module.Gate.Reset();
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.DoesNotContain("Low battery", VisibleTexts(page));
            module.Gate.Set();
            PumpUntil(() => !Texts(page).Contains("Low battery"));
            Assert.Equal(1, module.Reads);
            Assert.Contains("Ping", Texts(page));
            Assert.Equal(NoticePlaces.Windows, notifier.PlacesFor("low-battery"));
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            module.Present = true;
            module.Gate.Reset();
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.DoesNotContain("Low battery", VisibleTexts(page));
            Assert.Contains("Ping", VisibleTexts(page)); // cached rows show while the fresh check waits
            module.Gate.Set();
            PumpUntil(() => module.Reads == 2 && Texts(page).Contains("Low battery"));
            Assert.Equal(NoticePlaces.Windows, notifier.PlacesFor("low-battery"));
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            module.Gate.Set();
            shell.Registry.StopAllAsync().GetAwaiter().GetResult();
            shell.Clipboard.Dispose();
            foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    });

    private static void PumpUntil(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < until)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }
        Assert.True(ready());
    }

    private sealed class HardwareNotices : IFeatureModule
    {
        public int Reads;
        public ManualResetEventSlim Gate { get; } = new(true);
        public bool Present;
        public FeatureDescriptor Descriptor { get; } = new("hardware-test", "Hardware", "Test", FeatureGroup.System);
        public IReadOnlySet<string>? AvailableNoticeKinds { get { Interlocked.Increment(ref Reads); if (!Gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return Present ? new HashSet<string> { "ping", "low-battery" } : new HashSet<string> { "ping" }; } }
        public Task StartAsync(ModuleContext context, CancellationToken ct)
        {
            context.Notifier.Declare([new("ping", "Ping", NoticePlaces.Island), new("low-battery", "Low battery", NoticePlaces.Island)]);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static IEnumerable<string> VisibleTexts(DependencyObject root)
    {
        if (root is UIElement { Visibility: Visibility.Collapsed or Visibility.Hidden }) yield break;
        if (root is TextBlock text) yield return text.Text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in VisibleTexts(child)) yield return item;
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
