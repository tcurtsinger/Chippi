// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Island;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.ColorPicker;
using Chippi.Features.ScreenCapture;
using Chippi.Features.ScreenText;
using Chippi.UI.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

public sealed class CaptureHostTests
{
    [Fact]
    public void Leaving_Capture_resets_its_Recent_page() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")),
            NullLoggerFactory.Instance, false, "capture-navigation-test-" + Guid.NewGuid().ToString("N"));
        var screen = new ScreenCaptureModule(shell.Capture);
        var host = new CapturePanelHost(shell, screen, new ScreenTextModule(shell.Capture), new ColorPickerModule(shell.Capture));
        try
        {
            var context = new ModuleContext(ScreenCaptureModule.Id, new CaptureSettings(), NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(ScreenCaptureModule.Id), new CaptureUi()) { DataDirectory = root };
            WpfTestThread.Wait(screen.StartAsync(context, default));
            host.OpenRecent(Guid.NewGuid().ToString()); // A removed fixture capture still opens its Recent page.
            var body = Assert.IsType<ContentControl>(host.Tile.Spec.CreateSection());
            Assert.IsType<CaptureRecentView>(body.Content);

            body.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            Assert.IsType<CapturePanelView>(body.Content);
        }
        finally
        {
            WpfTestThread.Wait(screen.StopAsync(default));
            host.Dispose();
            shell.Clipboard.Dispose(); shell.Input.Dispose(); shell.Hotkeys.Dispose();
            Directory.Delete(root, true);
        }
    });

    private sealed class CaptureSettings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => (T)(object)new ScreenCaptureSettings
            { Shortcuts = new() { [ScreenCaptureShortcuts.CaptureRegion] = null } };
        public void Save<T>(T value) where T : class { }
    }

    private sealed class CaptureUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(ScreenCaptureModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> page) { }
    }

    [Fact]
    public void Shared_host_survives_reentrant_registration_and_follows_provider_removal() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "chippi-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigStore(Path.Combine(root, "settings.json"));
        config.SetSection("features", new { preset = FeaturePreset.Custom, installed = new[] { ScreenTextModule.Id }, capabilitiesUsed = Array.Empty<string>() });
        var shell = new ShellContext(config, NullLoggerFactory.Instance, false, "karl-host-test-" + Guid.NewGuid().ToString("N"));
        var screen = new ScreenCaptureModule(shell.Capture);
        var host = new CapturePanelHost(shell, screen, new ScreenTextModule(shell.Capture), new ColorPickerModule(shell.Capture));
        shell.Registry.Register(new Provider());
        try
        {
            WpfTestThread.Wait(shell.Registry.StartAsync());
            // Same synchronous notification that caused the original startup stack overflow.
            shell.Shortcuts.Changed += host.Refresh;
            host.Refresh();
            Assert.True(host.Visible);
            Assert.False(host.CanCapture);
            Assert.Equal(new[] { "Text", "QR code" }, host.Tools.Select(t => t.Name));
            Assert.Single(shell.Shortcuts.List, x => x.Owner == "capture" && x.Name == "Open Capture");
            Assert.Single(shell.Registry.Commands(), x => x.Key == "page/capture");
            host.Refresh();
            Assert.Single(shell.Shortcuts.List, x => x.Owner == "capture");
            WpfTestThread.Wait(shell.Registry.SetInstalledAsync(ScreenTextModule.Id, false));
            host.Refresh();
            Assert.False(host.Visible);
            Assert.DoesNotContain(shell.Shortcuts.List, x => x.Owner == "capture");
            Assert.DoesNotContain(shell.Registry.Commands(), x => x.Key == "page/capture");
            shell.Shortcuts.Changed -= host.Refresh;
        }
        finally
        {
            WpfTestThread.Wait(shell.Registry.StopAllAsync());
            host.Dispose();
            shell.Clipboard.Dispose(); shell.Input.Dispose(); shell.Hotkeys.Dispose();
            Directory.Delete(root, true);
        }
    });

    private sealed class Provider : IFeatureModule
    {
        public FeatureDescriptor Descriptor { get; } = new(ScreenTextModule.Id, "Text", "", FeatureGroup.Capture);
        public Task StartAsync(ModuleContext context, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
