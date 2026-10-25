// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.App.Settings;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.Downloads;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Chippi.Island.Tests;

public sealed class SettingsWindowTests
{
    [Theory]
    [InlineData("preview", "PreviewView")]
    [InlineData("explorer", "PreviewView")]
    [InlineData("review", "PreviewView")]
    [InlineData("history/run", "HistorySection")]
    [InlineData("history/undo", "HistorySection")]
    [InlineData("rules", "RulesSection")]
    [InlineData("preview", "PreviewView", true)]
    [InlineData("history/run", "HistorySection", true)]
    public void Repeated_deep_link_reopens_requested_section_after_user_changes_tab(string route, string expectedView, bool fromGeneral = false)
    {
        WpfTestThread.Run(() =>
        {
            string profile = "settings-route-" + Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), profile);
            Directory.CreateDirectory(root);
            var config = new ConfigStore(Path.Combine(root, "settings.json"));
            config.SetModuleSettings("downloads-organizer", new DownloadsSettings
            {
                Initialized = true, OrganizeInExplorer = false,
                Folders = [new WatchedFolder { Path = root, Mode = FolderMode.Manual }],
            });
            var shell = new ShellContext(config, NullLoggerFactory.Instance, false, profile);
            var module = new DownloadsModule();
            shell.Registry.Register(module);
            SettingsWindow? window = null;
            try
            {
                shell.Registry.SetInstalledAsync("downloads-organizer", true).GetAwaiter().GetResult();
                var history = module.Engine!.History;
                long run = history.BeginRun(RunTrigger.Preview, root);
                for (int i = 0; i < 11; i++)
                {
                    long op = history.Plan(run, HistoryOperationKind.Move, Path.Combine(root, $"{i}.txt"), Path.Combine(root, "sorted", $"{i}.txt"), "r", "Text", 1);
                    history.MarkCompleted(op, Path.Combine(root, "sorted", $"{i}.txt"), "hash", DateTimeOffset.UtcNow);
                }
                history.EndRun(run);
                string section = route switch
                {
                    "preview" or "explorer" => route + "/" + root,
                    "history/run" => "history/" + run,
                    "history/undo" => "history/" + run + "/undo",
                    _ => route,
                };
                string link = "module:downloads-organizer/" + section;
                window = new SettingsWindow(shell, link); // cache the same link used later
                Assert.Contains(Descendants(window.PageContent), c => c.GetType().Name == expectedView);
                var selector = Descendants(window.PageContent).OfType<SegmentedControl>().Single(c => AutomationProperties.GetName(c) == "Section");
                selector.SelectedIndex = route == "rules" ? 2 : 1; // user switches tabs inside the cached page

                if (fromGeneral) window.Navigate("general");
                shell.Commands.RegisterSettings(page => window.Navigate(page!));
                var request = route switch
                {
                    "preview" or "explorer" => new DownloadsRequest(DownloadsRequestKind.Organize, root, FromExplorer: route == "explorer"),
                    "review" => new DownloadsRequest(DownloadsRequestKind.Review),
                    "history/run" => new DownloadsRequest(DownloadsRequestKind.Run, RunId: run),
                    "history/undo" => new DownloadsRequest(DownloadsRequestKind.Undo, RunId: run),
                    _ => new DownloadsRequest(DownloadsRequestKind.Rules),
                };
                module.RequestView(request); // actual module -> registry -> shell -> existing Settings

                Assert.Contains(Descendants(window.PageContent), c => c.GetType().Name == expectedView);
                if (route.StartsWith("history/", StringComparison.Ordinal))
                {
                    Assert.Contains(Descendants(window.PageContent).OfType<Button>(), b => AutomationProperties.GetName(b) == "Hide files"); // requested run expanded
                    if (route == "history/undo")
                    {
                        Assert.Contains(Descendants(window.PageContent).OfType<TextBlock>(), t => t.Text == "Undo 11 moves?");
                        Assert.Contains(Descendants(window.PageContent).OfType<Button>(), b => AutomationProperties.GetName(b) == "Confirm undo" && Equals(b.Content, "Undo"));
                    }
                }
            }
            finally
            {
                window?.Close();
                shell.Registry.StopAllAsync().GetAwaiter().GetResult();
                foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });
    }

    /// <summary>Travis: a run collapsed only when it had more than 10 files. Every run starts collapsed and its header toggles it.</summary>
    [Fact]
    public void Every_history_run_starts_collapsed_and_its_header_toggles_it()
    {
        WpfTestThread.Run(() =>
        {
            string profile = "settings-history-" + Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), profile);
            Directory.CreateDirectory(root);
            var config = new ConfigStore(Path.Combine(root, "settings.json"));
            config.SetModuleSettings("downloads-organizer", new DownloadsSettings
            {
                Initialized = true, OrganizeInExplorer = false,
                Folders = [new WatchedFolder { Path = root, Mode = FolderMode.Manual }],
            });
            var shell = new ShellContext(config, NullLoggerFactory.Instance, false, profile);
            var module = new DownloadsModule();
            shell.Registry.Register(module);
            SettingsWindow? window = null;
            try
            {
                shell.Registry.SetInstalledAsync("downloads-organizer", true).GetAwaiter().GetResult();
                var history = module.Engine!.History;
                foreach (int files in new[] { 2, 11 })
                {
                    long run = history.BeginRun(RunTrigger.Preview, root);
                    for (int i = 0; i < files; i++)
                    {
                        long op = history.Plan(run, HistoryOperationKind.Move, Path.Combine(root, $"{run}-{i}.txt"), Path.Combine(root, "sorted", $"{run}-{i}.txt"), "r", "Text", 1);
                        history.MarkCompleted(op, Path.Combine(root, "sorted", $"{run}-{i}.txt"), "hash", DateTimeOffset.UtcNow);
                    }
                    history.EndRun(run);
                }
                window = new SettingsWindow(shell, "module:downloads-organizer/history");
                IEnumerable<string?> Toggles() => Descendants(window.PageContent).OfType<IconButton>().Select(AutomationProperties.GetName).Where(n => n is "Hide files" || n?.StartsWith("Show ", StringComparison.Ordinal) == true);

                Assert.Equal(["Show 11 files", "Show 2 files"], Toggles().OrderBy(n => n));
                Assert.Contains(Descendants(window.PageContent).OfType<Button>(), b => AutomationProperties.GetName(b) == "Undo run" && Descendants(b).OfType<TextBlock>().Any(t => t.Text == "Undo"));

                // A click on the header's text, not the chevron, opens the run.
                var title = Descendants(window.PageContent).OfType<TextBlock>().First(t => t.Text.StartsWith("Organize now", StringComparison.Ordinal));
                title.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseUpEvent });
                Assert.Contains("Hide files", Toggles());
            }
            finally
            {
                window?.Close();
                shell.Registry.StopAllAsync().GetAwaiter().GetResult();
                foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });
    }

    [Fact]
    public void Unavailable_page_and_failed_factory_warn_and_a_retry_can_recover()
    {
        WpfTestThread.Run(() =>
        {
            string profile = "settings-failure-" + Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), profile);
            Directory.CreateDirectory(root);
            using var logs = new WarningLog();
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
            var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), factory, false, profile);
            var module = new FailingPageModule();
            shell.Registry.Register(module);
            SettingsWindow? window = null;
            try
            {
                shell.Registry.SetInstalledAsync("failing-page", true).GetAwaiter().GetResult();
                window = new SettingsWindow(shell, "general");
                window.Navigate("module:missing/rules");
                Assert.Contains(logs.Messages, m => m.Contains("module:missing/rules") && m.Contains("unavailable"));
                window.Navigate("module:failing-page/rules");
                Assert.Contains(logs.Messages, m => m.Contains("module:failing-page/rules") && m.Contains("failed to load"));
                Assert.Contains(Descendants(window.PageContent).OfType<TextBlock>(), t => t.Text.StartsWith("Couldn't open", StringComparison.Ordinal));
                window.Navigate("module:failing-page/rules");
                Assert.Contains(Descendants(window.PageContent).OfType<TextBlock>(), t => t.Text == "Recovered rules");
            }
            finally
            {
                window?.Close();
                shell.Registry.StopAllAsync().GetAwaiter().GetResult();
                foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });
    }

    private sealed class FailingPageModule : IFeatureModule
    {
        public FeatureDescriptor Descriptor { get; } = new("failing-page", "Failing page", "Test page", FeatureGroup.Files);
        public Task StartAsync(ModuleContext context, CancellationToken ct)
        {
            int calls = 0;
            context.Ui.SetSettingsPage(section => ++calls == 1
                ? throw new InvalidOperationException("Factory failed")
                : new TextBlock { Text = "Recovered " + section });
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class WarningLog : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Messages.Add(formatter(state, exception));
        }
        public void Dispose() { }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }
}
