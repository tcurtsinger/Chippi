// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

public sealed class ChippiPagesTests
{
    [Fact]
    public void Shortcuts_lists_every_editable_shortcut_in_one_card_and_Add_features_shows_a_failure_reason() => WpfTestThread.Run(() =>
    {
        string profile = "chippi-pages-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        try
        {
            shell.Registry.Register(new FailedFeature());
            shell.Registry.SetInstalledAsync("failed-test", true).GetAwaiter().GetResult();
            KeyShortcut? saved = null;
            using var hotkey = shell.Shortcuts.Add("paste-plain-text", "Paste as plain text", new ShortcutEdits(() => saved, _ => null, () => saved = null));
            var shortcuts = new ShortcutsPage(shell);
            Assert.Contains(Find<TextBlock>(shortcuts), t => t.Text == "Paste as plain text");
            Assert.Contains(Find<Button>(shortcuts), b => AutomationProperties.GetName(b) == "Record Paste as plain text");
            var features = new AddFeaturesPage(shell);
            Assert.Contains(Find<SettingRow>(features), r => r.Header == "Failed feature" && r.Description == "Start failed \u00b7 Access denied");
        }
        finally
        {
            shell.Registry.StopAllAsync().GetAwaiter().GetResult();
            shell.Clipboard.Dispose();
            foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    });

    [Fact]
    public void A_failed_shortcut_feature_has_no_sidebar_entry_and_unknown_errors_have_no_invented_reason() => WpfTestThread.Run(() =>
    {
        string profile = "chippi-failed-shortcut-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        try
        {
            shell.Registry.Register(new FailedFeature("mic-mute", unknown: true));
            shell.Registry.SetInstalledAsync("mic-mute", true).GetAwaiter().GetResult();
            var window = new Chippi.App.Settings.SettingsWindow(shell, "general");
            try { Assert.DoesNotContain(window.NavigationPages, p => p.Id == "shortcuts"); }
            finally { window.Close(); }
            var features = new AddFeaturesPage(shell);
            Assert.Contains(Find<SettingRow>(features), r => r.Header == "Failed feature" && r.Description == "Start failed");
        }
        finally
        {
            shell.Registry.StopAllAsync().GetAwaiter().GetResult();
            shell.Clipboard.Dispose();
            foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    });

    private sealed class FailedFeature(string id = "failed-test", bool unknown = false) : IFeatureModule
    {
        public FeatureDescriptor Descriptor { get; } = new(id, "Failed feature", "Description", FeatureGroup.System);
        public Task StartAsync(ModuleContext context, CancellationToken ct) => Task.FromException(unknown ? new InvalidOperationException("Not about a device") : new UnauthorizedAccessException("Raw sensitive path"));
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Find<T>(child)) yield return item;
    }
}

public sealed class ShortcutsPageTimings
{
    /// <summary>CHIPPI_SHORTCUTS_TIMINGS names a report: the page's build and layout with 15 shortcuts, 200 runs.</summary>
    [Fact]
    public void Page_open_time_with_a_dozen_shortcuts() => WpfTestThread.Run(() =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_SHORTCUTS_TIMINGS") is not { Length: > 0 } report)
            return;
        string profile = "chippi-shortcuts-time-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        try
        {
            var hotkeys = new List<IHotkey>();
            for (int i = 0; i < 15; i++)
            {
                KeyShortcut? saved = i % 3 == 0 ? null : new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, (uint)(0x41 + i));
                hotkeys.Add(shell.Shortcuts.Add("test", "Shortcut " + i, new ShortcutEdits(() => saved, _ => null, () => saved = null)));
            }
            var times = new List<double>();
            for (int run = 0; run < 200; run++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var page = new ShortcutsPage(shell);
                page.Measure(new Size(760, double.PositiveInfinity));
                page.Arrange(new Rect(0, 0, 760, page.DesiredSize.Height));
                page.UpdateLayout();
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            File.WriteAllText(report, $"p50 {times[100]:F1} ms, p99 {times[197]:F1} ms, max {times[^1]:F1} ms (page build and layout, 15 shortcuts, default templates, 200 runs)");
            foreach (var h in hotkeys) h.Dispose();
        }
        finally
        {
            shell.Registry.StopAllAsync().GetAwaiter().GetResult();
            shell.Clipboard.Dispose();
            foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    });
}
