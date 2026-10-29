// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using Chippi.App.Settings.Features;
using Chippi.App.Settings.Pages;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

public sealed class RefreshSettingsTests
{
    [Fact]
    public void Clipboard_registration_orders_settings_without_changing_the_features() => WithShell(shell =>
    {
        Assert.Equal(["clipboard-history", "paste-plain-text", "clean-url"],
            shell.Registry.Features.Where(e => e.Descriptor.Group == FeatureGroup.ClipboardText).Select(e => e.Descriptor.Id));
    });

    [Fact]
    public void Snapshot_search_self_check_uses_a_feature_that_is_still_upcoming() => WithShell(shell =>
    {
        FeatureCatalog.RegisterPlaceholders(shell.Registry);
        var page = new AddFeaturesPage(shell);
        page.VerifySearchSnapshot();
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    });

    private static void WithShell(Action<ShellContext> test) => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-refresh-e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, "refresh-e-test");
        try
        {
            Chippi.App.App.RegisterClipboardFeatures(shell);
            test(shell);
        }
        finally
        {
            shell.Clipboard.Dispose();
            Directory.Delete(root, true);
        }
    });
}