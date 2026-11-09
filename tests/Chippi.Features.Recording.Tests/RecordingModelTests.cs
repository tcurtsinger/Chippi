// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Chippi.Media;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Chippi.Features.Recording.Tests;

public sealed class RecordingModelTests
{
    [Fact]
    public void Menu_and_preview_fit_the_island_and_trim_handles_support_keyboard_and_automation() => WithModel((model, backend, _, _) =>
    {
        var context = new ModuleContext(RecordingModule.Id, new Settings(), NullLogger.Instance, null!, null!);
        var view = new RecordingView(model, context);
        view.SetResourceReference(Control.BackgroundProperty, "IslandBrush");
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        void Check(string name)
        {
            view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout();
            var content = (StackPanel)((DockPanel)view.Content).Children[1]; Assert.True(content.DesiredSize.Height <= 304, $"{name} exceeds the island height: {content.DesiredSize.Height}");
            string? shots = Environment.GetEnvironmentVariable("CHIPPI_RECORDING_SHOTS");
            if (shots == null) return;
            Directory.CreateDirectory(shots); var image = new RenderTargetBitmap(992, 608, 192, 192, PixelFormats.Pbgra32); image.Render(view);
            using var output = File.Create(Path.Combine(shots, name + ".png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(output);
        }
        try
        {
            Check("recording-menu"); WpfTestThread.Wait(model.StartAsync()); Check("recording-active");
            WpfTestThread.Wait(model.StopAsync()); Check("recording-preview");
            var content = (StackPanel)((DockPanel)view.Content).Children[1]; var range = Assert.Single(content.Children.OfType<RecordingRange>());
            var thumb = range.Children.OfType<System.Windows.Controls.Primitives.Thumb>().First();
            var peer = UIElementAutomationPeer.CreatePeerForElement(thumb)!; var value = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
            value.SetValue(.5); Assert.Equal(.5, range.Start); Assert.Equal(2, range.End);
        }
        finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    });
    [Fact]
    public void Cancelling_countdown_never_opens_capture_or_creates_a_file() => WithModel((model, backend, _, _) =>
    {
        model.Delay = (_, token) => Task.Delay(Timeout.Infinite, token);
        var start = model.StartAsync(); Assert.Equal(RecordingPhase.Countdown, model.Phase);
        WpfTestThread.Wait(model.StopAsync()); WpfTestThread.Wait(start);
        Assert.False(model.Active); Assert.Equal(0, backend.Starts);
    });
    [Fact]
    public void Stop_saves_once_after_file_closure_pause_resume_share_one_session_and_discard_removes_only_owned_partial() => WithModel((model, backend, store, folder) =>
    {
        WpfTestThread.Wait(model.StartAsync()); Assert.Equal(RecordingPhase.Recording, model.Phase);
        WpfTestThread.Wait(model.StartAsync()); Assert.Equal(1, backend.Starts);
        model.TogglePause(); Assert.True(model.IsPaused); model.TogglePause(); Assert.False(model.IsPaused);
        int opened = 0; model.ResultReady += () => { Assert.False(model.Active); opened++; };
        WpfTestThread.Wait(model.StopAsync()); Assert.Single(store.Items); Assert.Equal(1, opened); Assert.True(File.Exists(store.Items[0].Path));
        string original = store.Items[0].Path;
        model.Setup(RecordingTarget.Screen); WpfTestThread.Wait(model.StartAsync()); WpfTestThread.Wait(model.DiscardAsync());
        Assert.Single(store.Items); Assert.True(File.Exists(original)); Assert.Single(Directory.GetFiles(folder)); Assert.Equal(1, opened);
    });
    [Fact]
    public void Finish_failure_preserves_partial_and_never_publishes_success_or_recent() => WithModel((model, backend, store, folder) =>
    {
        model.Inspect = (_, _) => throw new InvalidDataException("bad video");
        int opened = 0; model.ResultReady += () => opened++;
        WpfTestThread.Wait(model.StartAsync()); WpfTestThread.Wait(model.StopAsync());
        Assert.Empty(store.Items); Assert.Equal(0, opened); Assert.Contains("partial", model.Message);
        Assert.EndsWith(".partial.mp4", Assert.Single(Directory.GetFiles(folder)));
    });
    [Fact]
    public void Each_new_setup_resets_audio_defaults_and_busy_capture_blocks_start() => WithModel((model, backend, _, _) =>
    {
        model.MicAudio = true; model.SystemAudio = false; model.Setup(RecordingTarget.Window);
        Assert.True(model.SystemAudio); Assert.False(model.MicAudio);
        model.CanStart = () => false; WpfTestThread.Wait(model.StartAsync()); Assert.Equal(0, backend.Starts); Assert.Contains("capture", model.Message);
    });
    private static void WithModel(Action<RecordingModel, Backend, Store, string> test) => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-record-model-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var settings = new Settings { Value = new() { SaveFolder = folder } };
        var context = new ModuleContext(RecordingModule.Id, settings, NullLogger.Instance, null!, null!);
        var backend = new Backend(); using var store = new Store(); using var model = new RecordingModel(context, backend, store);
        model.Delay = (_, _) => Task.CompletedTask; model.Inspect = (_, _) => Task.FromResult(new RecordingInfo(TimeSpan.FromSeconds(2), 640, 360, [])); model.Setup(RecordingTarget.Window);
        try { test(model, backend, store, folder); }
        finally { WpfTestThread.Wait(model.ShutdownAsync()); Directory.Delete(folder, true); }
    });
    private sealed class Settings : IModuleSettings
    {
        public RecordingSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (RecordingSettings)(object)value;
    }
    private sealed class Store : IRecordingStore
    {
        public List<RecordingEntry> Items = [];
        public IReadOnlyList<RecordingEntry> Read() => Items.ToArray();
        public void Add(RecordingEntry entry, byte[] poster) => Items.Add(entry);
        public byte[]? Poster(Guid id) => null;
        public void Dispose() { }
    }
    private sealed class Backend : IRecordingBackend
    {
        public int Starts;
        public IReadOnlyList<RecordingSource> Sources(RecordingTarget target) => [new(target, 123, "Fixture", 640, 360)];
        public RecordingDeviceNames Devices() => new("Fixture output", "Fixture input");
        public Task<IRecordingSession> StartAsync(RecordingSource source, RecordingOptions options, string partialPath, CancellationToken cancellationToken)
        { Starts++; File.WriteAllText(partialPath, "fixture bytes"); return Task.FromResult<IRecordingSession>(new Session(partialPath)); }
    }
    private sealed class Session(string path) : IRecordingSession
    {
        private readonly TaskCompletionSource<RecordingResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan Elapsed => TimeSpan.FromSeconds(2);
        public bool IsPaused { get; private set; }
        public Task<RecordingResult> Completion => _done.Task;
        public event Action<string>? Warning { add { } remove { } }
        public event Action? SourceClosed { add { } remove { } }
        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;
        public Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
        { _done.TrySetResult(new(path, Elapsed, 640, 360, 60, 0, "Fixture", null)); return Completion; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
