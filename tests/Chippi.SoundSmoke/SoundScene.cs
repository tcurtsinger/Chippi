// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.App.Shell;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Audio.Tests;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Routing;
using Chippi.Features.Sound.Volume;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Audio.Tests.FakeDeviceSystem;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Chippi.SoundSmoke;

// This Application never runs App.OnStartup. Every external dependency is supplied below.
internal static class SmokeDispatcher
{
    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var path in new[] { "Themes/Dark.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/{path}") });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "Sound smoke dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    public static Task Run(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui.Value.BeginInvoke(async () =>
        {
            try { Chippi.SmokeTests.SmokeLog.Check(); await test(); Chippi.SmokeTests.SmokeLog.Check(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    public static async Task Until(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow >= end) throw new TimeoutException("Sound UI did not reach the expected state");
            await Task.Delay(10);
        }
    }
}

internal sealed class SoundScene : IAsyncDisposable
{
    private readonly string _profile = "s7-" + Guid.NewGuid().ToString("N");
    private readonly string _root;
    public FakeDeviceSystem Devices { get; } = new() { AppPolicyAvailable = true };
    public FakePipelines Pipelines { get; } = new();
    public FakeHotkey Hotkey { get; } = new();
    public FakeRequirement Requirement { get; } = new();
    public AudioService Audio { get; }
    public ShellContext Shell { get; }
    public AudioRoutingModule Routing { get; }
    public MicProcessingModule Processing { get; }
    public AppVolumeModule Volume { get; }
    public MicMuteModule Mute { get; }

    public SoundScene()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", _profile);
        Directory.CreateDirectory(_root);
        Devices.With(Speaker("speakers", "Desk speakers"), Speaker("headphones", "Headphones"), Mic("desk", "Desk mic"), CableInput(), CableOutput())
            .WithDefault("speakers").WithDefault("desk");
        Devices.WithSession(new("music-session", "speakers", 123, @"C:\Fake\Music.exe", "Music", false, SessionState.Active, .5f, false));
        Audio = new(() => Devices, new DispatcherSynchronizationContext(), options: new()
        {
            Debounce = TimeSpan.FromMilliseconds(10), PipelineFactory = Pipelines.Factory,
        });
        Routing = new(Audio, VbCable.IsCable);
        Processing = new(Audio, () => Routing.Settings.Microphone.Devices, Path.Combine(_root, "processing"), new ForbiddenSetup());
        Volume = new(Audio);
        Mute = new(Audio, () => Hotkey, _ => throw new InvalidOperationException("Smoke test attempted to play a real tone"));
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetSection("features", new { preset = FeaturePreset.Custom, installed = new[] { "audio-routing", "mic-processing", "app-volume", "mic-mute" }, capabilitiesUsed = Array.Empty<string>() });
        Shell = new(config, NullLoggerFactory.Instance, false, _profile);
        Shell.Registry.Register(Routing);
        // Replace only the external requirement factory. The real processing module and all its views run.
        Shell.Registry.Register(new RequirementModule(Processing, Requirement));
        Shell.Registry.Register(Volume);
        Shell.Registry.Register(Mute);
    }

    public async Task Start()
    {
        await Shell.Registry.StartAsync();
        Assert.All(Shell.Registry.Features, e => Assert.True(e.IsRunning, e.Id + ": " + e.LastError));
    }

    public async ValueTask DisposeAsync()
    {
        await Shell.Registry.StopAllAsync();
        Assert.True(Hotkey.Disposed);
        Assert.Null(Pipelines.Live);
        Assert.False(Devices.GetLevel("desk").Muted);
        Assert.False(Devices.GetLevel("CableOut").Muted);
        Directory.Delete(_root, true);
    }

    private sealed class ForbiddenSetup : IDriverSetup
    {
        public Task<int?> RunAsync(string setupPath, CancellationToken ct) => throw new InvalidOperationException("Native setup must never run in smoke tests");
    }

    private sealed class RequirementModule(IFeatureModule inner, IFeatureRequirement requirement) : IFeatureModule
    {
        public FeatureDescriptor Descriptor { get; } = inner.Descriptor with { Requirement = () => requirement };
        public Task StartAsync(ModuleContext context, CancellationToken ct) => inner.StartAsync(context, ct);
        public Task StopAsync(CancellationToken ct) => inner.StopAsync(ct);
        public Task UninstallAsync(CancellationToken ct) => inner.UninstallAsync(ct);
    }
}

internal sealed class FakeHotkey : IMicHotkey
{
    public bool Disposed { get; private set; }
    public bool Register(MicShortcut shortcut, Action pressed) => true;
    public void Dispose() => Disposed = true;
}

internal sealed class FakeRequirement : IFeatureRequirement
{
    public RequirementState State { get; private set; } = RequirementState.Ready;
    public event EventHandler? StateChanged;
    public int Installs { get; private set; }
    public int SettingsOpened { get; private set; }
    public void Change(RequirementState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
    public Task InstallAsync(CancellationToken ct) { Installs++; Change(RequirementState.RestartNeeded); return Task.CompletedTask; }
    public void OpenSettings() => SettingsOpened++;
    public void Dispose() { }
}

internal sealed class FakeMedia : IMediaSessions
{
    public IReadOnlyList<MediaTrack> Sessions { get; } = [new("fake-music", "Music", "Test song", "Test artist", false, true, true, true, false, true, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), default, null)];
    public MediaTrack? Current => Sessions[0];
    public event EventHandler? Changed { add { } remove { } }
    public int Plays { get; private set; }
    public void Select(string? id) { }
    public Task PreviousAsync() => Task.CompletedTask;
    public Task PlayPauseAsync() { Plays++; return Task.CompletedTask; }
    public Task NextAsync() => Task.CompletedTask;
    public void Dispose() { }
}
