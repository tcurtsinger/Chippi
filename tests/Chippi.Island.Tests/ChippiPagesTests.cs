// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

public sealed class ChippiPagesTests
{
    [Fact]
    public void Shortcuts_reuses_the_features_shortcut_section_and_Add_features_shows_a_failure_reason() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, "chippi-pages-test");
        var shortcut = new ShortcutFeature();
        try
        {
            shell.Registry.Register(shortcut);
            shell.Registry.Register(new FailedFeature());
            shell.Registry.SetInstalledAsync("paste-plain-text", true).GetAwaiter().GetResult();
            shell.Registry.SetInstalledAsync("failed-test", true).GetAwaiter().GetResult();
            var shortcuts = new ShortcutsPage(shell);
            Assert.Equal("shortcut-list", shortcut.Section);
            Assert.Single(Find<Card>(shortcuts));
            Assert.Contains(Find<TextBlock>(shortcuts), t => t.Text == "Existing shortcut editor");
            var features = new AddFeaturesPage(shell);
            Assert.Contains(Find<SettingRow>(features), r => r.Header == "Failed feature" && r.Description == "Start failed \u00b7 Access denied");
        }
        finally { shell.Registry.StopAllAsync().GetAwaiter().GetResult(); shell.Clipboard.Dispose(); Directory.Delete(root, true); }
    });

    [Fact]
    public void A_failed_shortcut_feature_has_no_sidebar_entry_and_unknown_errors_have_no_invented_reason() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-failed-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, "failed-shortcut-test");
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
        finally { shell.Registry.StopAllAsync().GetAwaiter().GetResult(); shell.Clipboard.Dispose(); Directory.Delete(root, true); }
    });

    private sealed class ShortcutFeature : IFeatureModule
    {
        public string? Section;
        public FeatureDescriptor Descriptor { get; } = new("paste-plain-text", "Paste as plain text", "Shortcut", FeatureGroup.ClipboardText);
        public Task StartAsync(ModuleContext context, CancellationToken ct)
        {
            context.Ui.SetSettingsPage(section => { Section = section; return new TextBlock { Text = "Existing shortcut editor" }; });
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
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
