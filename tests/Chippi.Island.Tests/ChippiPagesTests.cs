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

    [Fact]
    public void Record_and_Change_edit_in_the_row_and_an_Edit_link_lands_on_its_row_and_saves() => WpfTestThread.Run(() =>
    {
        string profile = "chippi-edit-link-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        try
        {
            KeyShortcut? first = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x7B), second = null;
            using var a = shell.Shortcuts.Add("test-a", "First", new ShortcutEdits(() => first, k => { first = k; return null; }, () => first = null));
            using var b = shell.Shortcuts.Add("test-b", "Second", new ShortcutEdits(() => second, k => { second = k; return null; }, () => second = null));
            using var c = shell.Shortcuts.Add("test-c", "Third", new ShortcutEdits(() => null, _ => null, () => { }));

            // Record, on an unset row: the editor takes that row's place, between First and Third.
            var page = new ShortcutsPage(shell) { Recorder = new FakeRecorder(null) };
            Click(Find<Button>(page).Single(x => AutomationProperties.GetName(x) == "Record Second"));
            var rows = Find<Card>(page).First().Children.OfType<FrameworkElement>().ToList();
            int editor = rows.FindIndex(r => r is StackPanel);
            Assert.True(editor > 0);
            Assert.Contains(Find<TextBlock>(rows[editor - 1]), t => t.Text == "First");
            Assert.Contains(Find<TextBlock>(rows[editor]), t => t.Text == "Second");
            Assert.Contains(Find<TextBlock>(rows[editor + 1]), t => t.Text == "Third");
            Assert.DoesNotContain(Find<Button>(page), x => AutomationProperties.GetName(x) == "Record Second");

            // A feature's Edit link: Settings opens on Shortcuts with Second's editor in its row, recording; Save keeps the keys.
            var keys = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift, 0x7A);
            var recorder = new FakeRecorder(new Chippi.Core.Actions.KeyCombo(Chippi.Core.Actions.KeyModifiers.Ctrl | Chippi.Core.Actions.KeyModifiers.Alt | Chippi.Core.Actions.KeyModifiers.Shift, 0x7A));
            var window = new Chippi.App.Settings.SettingsWindow(shell, "shortcuts");
            try
            {
                var listed = Find<ShortcutsPage>(window).Single();
                listed.Recorder = recorder;
                window.Navigate("shortcuts/test-b/Second");
                Assert.Equal("shortcuts", window.CurrentPage);
                Assert.Equal(1, recorder.Calls);
                rows = Find<Card>(listed).First().Children.OfType<FrameworkElement>().ToList();
                editor = rows.FindIndex(r => r is StackPanel);
                Assert.Contains(Find<TextBlock>(rows[editor - 1]), t => t.Text == "First");
                Assert.Contains(Find<TextBlock>(rows[editor]), t => t.Text == "Second");
                var save = Find<Button>(rows[editor]).Single(x => x.Content is "Save");
                Assert.True(save.IsEnabled);
                Click(save);
                Assert.Equal(keys, second);
                Assert.DoesNotContain(Find<Card>(listed).First().Children.OfType<FrameworkElement>(), r => r is StackPanel);
                Assert.Equal(keys, shell.Shortcuts.List.Single(e => e.Owner == "test-b").Shortcut);

                // Change, from a set row's menu: the editor opens in First's row.
                window.Navigate("shortcuts/test-a/First");
                rows = Find<Card>(listed).First().Children.OfType<FrameworkElement>().ToList();
                Assert.Contains(Find<TextBlock>(rows[rows.FindIndex(r => r is StackPanel)]), t => t.Text == "First");
                // A shortcut no feature lists: the list alone.
                window.Navigate("shortcuts/test-z/Missing");
                Assert.Equal("shortcuts", window.CurrentPage);
            }
            finally { window.Close(); }
        }
        finally
        {
            shell.Registry.StopAllAsync().GetAwaiter().GetResult();
            shell.Clipboard.Dispose();
            foreach (string folder in new[] { root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile) })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    /// <summary>Stands in for the input hub's recorder: "records" its keys at once (none: keeps listening).</summary>
    private sealed class FakeRecorder(Chippi.Core.Actions.KeyCombo? keys) : Chippi.Core.Actions.IKeyRecorder, IDisposable
    {
        public int Calls;

        public IDisposable Record(Action<Chippi.Core.Actions.KeyCombo?> recorded)
        {
            Calls++;
            if (keys is { } k) recorded(k);
            return this;
        }

        public void Dispose() { }
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
