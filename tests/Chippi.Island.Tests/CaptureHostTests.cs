// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.App.Island;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.ColorPicker;
using Chippi.Features.ScreenCapture;
using Chippi.Features.ScreenText;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

public sealed class CaptureHostTests
{
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
