// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

[Collection("ReducedMotion")] // an import applies its reduced-motion setting
public sealed class GeneralImportTests
{
    [Fact]
    public void Selecting_a_file_waits_for_Replace_and_Cancel_keeps_current_settings() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigStore(Path.Combine(root, "settings.json"));
        config.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Dark });
        var shell = new ShellContext(config, NullLoggerFactory.Instance, false, "import-test");
        try
        {
            var page = new GeneralPage(shell);
            string path = Path.Combine(root, "incoming.json");
            File.WriteAllText(path, "invalid JSON that must never be read before Replace");
            page.ConfirmImport(path);
            Assert.Contains(Find<Chippi.UI.Controls.SettingRow>(page), t => Equals(t.Header, "Replace every setting with incoming.json?"));
            Assert.Equal(ThemePreference.Dark, config.GetSection<AppSettings>(AppSettings.SectionName).Theme);
            Find<Button>(page).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain(Find<Button>(page), b => Equals(b.Content, "Replace"));
            Assert.Equal(ThemePreference.Dark, config.GetSection<AppSettings>(AppSettings.SectionName).Theme);
            page.ConfirmImport(path);
            Find<Button>(page).Single(b => Equals(b.Content, "Replace")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(Find<TextBlock>(page), t => t.Text == "Import failed \u00b7 Invalid settings file");
            Assert.Equal(ThemePreference.Dark, config.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        }
        finally { shell.Clipboard.Dispose(); Directory.Delete(root, true); }
    });

    [Fact]
    public void A_valid_export_import_rebuilds_the_page_and_reports_success() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-valid-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigStore(Path.Combine(root, "settings.json"));
        config.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Dark, ReduceMotion = true });
        string path = Path.Combine(root, "export.json");
        config.Export(path);
        config.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Dark });
        var shell = new ShellContext(config, NullLoggerFactory.Instance, false, "valid-import-test");
        try
        {
            var page = new GeneralPage(shell);
            var oldContent = page.Content;
            var operation = (Task)typeof(GeneralPage).GetMethod("ImportAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, [path])!;
            operation.GetAwaiter().GetResult();
            Assert.True(config.GetSection<AppSettings>(AppSettings.SectionName).ReduceMotion);
            Assert.NotSame(oldContent, page.Content);
            Assert.Contains(Find<TextBlock>(page), t => t.Text == $"Imported from {path}");
        }
        finally { shell.Clipboard.Dispose(); Chippi.UI.Themes.Motion.Override = null; Directory.Delete(root, true); }
    });

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Find<T>(child)) yield return item;
    }
}
