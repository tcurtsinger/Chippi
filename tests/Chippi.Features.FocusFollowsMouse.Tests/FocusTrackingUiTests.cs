// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Island;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.FocusFollowsMouse.Tests;

public sealed class FocusTrackingUiTests
{
    [Fact]
    public void UiaToggleSavesRestoresAndReportsFailureWithoutNativeSettings()
    {
        WpfTestThread.Run(() =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
            bool light = WpfTestThread.ShotsTheme == "Light";
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
            Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
            var settings = new WindowsSettings();
            var store = new Store();
            var ui = new Ui();
            var module = new FocusFollowsMouseModule(() => new FocusTrackingEngine(settings, new Journal()));
            module.StartAsync(new ModuleContext(FocusFollowsMouseModule.Id, store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(FocusFollowsMouseModule.Id), ui), default).GetAwaiter().GetResult();
            var watch = Stopwatch.StartNew();
            var view = (FocusTrackingRows)ui.Page!(null);
            watch.Stop();
            var toggle = Find<ToggleButton>(view).Single();
            toggle.ApplyTemplate();
            var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
            Assert.False(store.Value.Enabled);
            Assert.Equal(0, settings.Writes);
            var command = Assert.Single(ui.Commands());
            Assert.Equal(ActionsTile.Input, command.Tile);
            Assert.False(command.IsOn);
            command.Run().GetAwaiter().GetResult();
            Assert.True(Assert.Single(ui.Commands()).IsOn);
            command.Run().GetAwaiter().GetResult();
            Assert.False(store.Value.Enabled);
            Shot(view, "off");
            peer.Toggle();
            Assert.True(store.Value.Enabled);
            Assert.Equal("On", toggle.Content);
            Assert.Equal(new uint[] { 1, 1, 250 }, settings.Values);
            Shot(view, "on");
            peer.Toggle();
            Assert.False(store.Value.Enabled);
            Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
            settings.Fail = true;
            peer.Toggle();
            Assert.False(store.Value.Enabled);
            Assert.False(toggle.IsChecked);
            Assert.Equal("Focus change failed", module.Error);
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Focus change failed" && t.Visibility == Visibility.Visible);
            Shot(view, "failed");
            settings.Fail = false;
            peer.Toggle();
            Assert.True(store.Value.Enabled);
            module.StopAsync(default).GetAwaiter().GetResult();
            Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
            if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is { Length: > 0 } folder)
                File.WriteAllText(Path.Combine(folder, "focus-timing.txt"), $"Row construction: {watch.Elapsed.TotalMilliseconds:F3} ms. Injected settings, offscreen; no window or native adapter.\n");
        });
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Enabled\":null}")]
    [InlineData("{\"Enabled\":{\"Original\":0}}")]
    [InlineData("{\"999\":{\"Original\":0,\"Applied\":1}}")]
    public async Task DamagedJournalDoesNotBlockEnableOrUninstall(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var settings = new WindowsSettings();
            var store = new Store();
            var module = new FocusFollowsMouseModule(() => new FocusTrackingEngine(settings, new TrackingJournal(path)));
            await module.StartAsync(new ModuleContext(FocusFollowsMouseModule.Id, store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(FocusFollowsMouseModule.Id), new Ui()), default);
            Assert.Equal(0, settings.Writes);

            Assert.Null(module.Error);
            module.SetEnabled(true);
            Assert.True(store.Value.Enabled);
            Assert.Null(module.Error);
            await module.StopAsync(default);
            Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
            File.WriteAllText(path, json);
            int before = settings.Writes;
            await module.UninstallAsync(default);
            Assert.Equal(before, settings.Writes);
            Assert.Null(module.Error);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task SharedPauseStartsPausedAndDispatchesChangesWithoutReapplyingAfterStop()
    {
        var windows = new WindowsSettings();
        var store = new Store();
        store.Save(new FocusTrackingSettings { Enabled = true });
        var input = new Hub { Paused = true };
        var queue = new Queue<Action>();
        var module = new FocusFollowsMouseModule(() => new FocusTrackingEngine(windows, new Journal()), queue.Enqueue);
        await module.StartAsync(new ModuleContext(FocusFollowsMouseModule.Id, store, NullLogger.Instance,
            new IslandActivityHub().CreatePublisher(FocusFollowsMouseModule.Id), new Ui()) { Input = input }, default);
        Assert.True(module.Descriptor.PausesInFullscreen);
        Assert.Equal(0, windows.Writes);
        input.Pause(false);
        Assert.Equal(0, windows.Writes);
        queue.Dequeue()();
        Assert.Equal(new uint[] { 1, 1, 250 }, windows.Values);
        input.Pause(true);
        queue.Dequeue()();
        Assert.Equal(new uint[] { 0, 0, 500 }, windows.Values);
        input.Pause(false);
        await module.StopAsync(default);
        queue.Dequeue()();
        Assert.Equal(new uint[] { 0, 0, 500 }, windows.Values);
        Assert.Equal(0, input.Listeners);
    }

    private sealed class Hub : IInputHub
    {
        public bool Paused { get; set; }
        public bool FullscreenInFront => Paused;
        private Action? _changed;
        public int Listeners { get; private set; }
        public event Action? PauseChanged { add { _changed += value; Listeners++; } remove { _changed -= value; Listeners--; } }
        public void Pause(bool paused) { Paused = paused; _changed?.Invoke(); }
        public IInputRegistration Add(IInputFilter filter) => throw new InvalidOperationException("Focus uses native tracking, not an input filter");
    }
    private static void Shot(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        view.Measure(new Size(768, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, 768, view.DesiredSize.Height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1536, (int)Math.Ceiling(view.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(folder, "focus-" + name + ".png"));
        encoder.Save(output);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class WindowsSettings : ITrackingSettings
    {
        public uint[] Values { get; } = [0, 0, 500];
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public uint Read(TrackingSetting key) => Values[(int)key];
        public void Write(TrackingSetting key, uint value)
        {
            if (Fail) throw new IOException("Injected setting failure");
            Writes++;
            Values[(int)key] = value;
        }
    }
    private sealed class Journal : ITrackingJournal
    {
        public Dictionary<TrackingSetting, TrackingOwnership> Load() => [];
        public void Save(IReadOnlyDictionary<TrackingSetting, TrackingOwnership> ownership) { }
    }
    private sealed class Store : IModuleSettings
    {
        public FocusTrackingSettings Value { get; private set; } = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T settings) where T : class => Value = (FocusTrackingSettings)(object)settings;
    }
    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>> Commands = null!;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
        public Func<string?, object>? Page { get; private set; }
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Focus has no tile");
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
    }
}
