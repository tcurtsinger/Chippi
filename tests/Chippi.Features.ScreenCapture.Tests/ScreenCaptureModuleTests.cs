// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Input;
using Chippi.Capture;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Chippi.UI.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class ScreenCaptureModuleTests
{
    [Fact]
    public void Failed_freeze_does_not_leave_an_editor_and_a_second_delay_starter_cancels() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var layer = new Layer();
        var module = new ScreenCaptureModule(layer);
        var ui = new Ui();
        var hub = new IslandActivityHub();
        var context = new ModuleContext(ScreenCaptureModule.Id, new Settings(), NullLogger.Instance, hub.CreatePublisher(ScreenCaptureModule.Id), ui) { DataDirectory = folder };
        try
        {
            module.StartAsync(context, default).GetAwaiter().GetResult();
            Assert.NotNull(ui.Page);
            Assert.Equal(8, context.Shortcuts.List.Count);
            layer.Fail = true;
            module.Start(Chippi.Capture.CaptureMode.Region, CaptureDoor.Shortcut);
            layer.Fail = false;
            module.Start(Chippi.Capture.CaptureMode.Region, CaptureDoor.Shortcut, delay: 3);
            Assert.Equal(2, layer.Starts);
            var delayed = layer.Last!;
            module.Start(Chippi.Capture.CaptureMode.Region, CaptureDoor.Shortcut, delay: 3);
            Assert.Equal(CaptureEnd.Cancelled, delayed.End);
            Assert.False(delayed.HandedBack);
            Assert.Equal(3, layer.Starts);
            module.StopAsync(default).GetAwaiter().GetResult();
            Assert.Empty(context.Shortcuts.List);
            Assert.Null(module.Model);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); Directory.Delete(folder, true); }
    });
    private sealed class Settings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();
        public void Save<T>(T value) where T : class { }
    }
    private sealed class Ui : IModuleUi
    {
        public Func<string?, object>? Page;
        public void SetSettingsPage(Func<string?, object> createView) => Page = createView;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => null!;
    }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class Layer : ICaptureLayer
    {
        public int Starts;
        public bool Fail;
        public Session? Last;
        public IDisposable Use() => new Lease();
        public PixelRect? LastRegion { get; set; }
        public ICaptureSession? Current => Last is { End: null } ? Last : null;
        public ICaptureSession? Start(CaptureRequest request)
        {
            Starts++;
            if (Current is { } current) { current.Close(CaptureEnd.Cancelled, handBack: false); return null; }
            Last = new Session(request);
            if (Fail) Last.Close(CaptureEnd.Failed);
            return Last;
        }
        public ICaptureSession? Reopen(CapturedImage image, CaptureDoor door) => null;
    }
    private sealed class Session(CaptureRequest request) : ICaptureSession
    {
        public CaptureRequest Request => request;
        public CaptureEnd? End;
        public bool HandedBack;
        public Freeze? Freeze => null;
        public Selection? Selection => null;
        public IReadOnlyList<CaptureSurface> Surfaces => [];
        public bool Tooling { get; set; }
        public event Action<Selection>? Selected { add { } remove { } }
        public event Action<PixelPoint, uint>? Picked { add { } remove { } }
        public event Action<KeyEventArgs>? KeyDown { add { } remove { } }
        public event Action? Frozen { add { } remove { } }
        public event Action<CaptureEnd>? Closed;
        public void SetBars(FrameworkElement? tools, FrameworkElement? output) { }
        public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true) { End = end; HandedBack = handBack; Closed?.Invoke(end); }
    }
}
