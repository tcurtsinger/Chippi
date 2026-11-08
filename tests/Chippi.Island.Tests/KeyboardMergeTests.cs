// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Settings;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Input;
using Chippi.Features.CloseProtection;
using Chippi.Features.Debounce;
using Chippi.Features.FocusFollowsMouse;
using Microsoft.Extensions.Logging.Abstractions;
namespace Chippi.Island.Tests;
public sealed class KeyboardMergeTests
{
    [Fact]
    public void OneKeyboardPageContainsBothRealFeaturesAndItsCloseSwitchWorks() => WpfTestThread.Run(() =>
    {
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        string profile = "keyboard-merge-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        shell.Services.Input = new Input();
        var close = new CloseProtectionModule(() => new Desktop());
        SettingsWindow? window = null;
        try
        {
            shell.Registry.Register(new DebounceModule());
            shell.Registry.Register(close);
            WpfTestThread.Wait(shell.Registry.SetInstalledAsync("debounce", true));
            WpfTestThread.Wait(shell.Registry.SetInstalledAsync("close-protection", true));
            string path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            Chippi.UI.Controls.AppIcon.LoadAsync(path, 80).GetAwaiter().GetResult();
            close.AddApp(path);
            window = new SettingsWindow(shell, "page:keyboard");
            window.Width = 1040; window.Height = 840;
            window.Measure(new Size(1040, 840)); window.Arrange(new Rect(0, 0, 1040, 840)); window.UpdateLayout();
            Assert.Single(window.NavigationPages, p => p.Id == "page:keyboard");
            Assert.Single(Find<CloseProtectionRows>(window.PageContent));
            Assert.Contains(Find<UserControl>(window.PageContent), c => c.GetType().Assembly.GetName().Name == "Chippi.Features.Debounce");
            Shot(window, "off");
            var toggle = Find<ToggleButton>(window.PageContent).Single(t => AutomationProperties.GetName(t) == "Close protection");
            var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
            peer.Toggle();
            Assert.True(close.Settings.Enabled);
            window.Navigate("page:mouse");
            window.Navigate("page:keyboard");
            Assert.True(Find<ToggleButton>(window.PageContent).Single(t => AutomationProperties.GetName(t) == "Close protection").IsChecked);
            Shot(window, "on");
        }
        finally
        {
            window?.Close();
            WpfTestThread.Wait(shell.Registry.StopAllAsync());
            shell.Input.Dispose(); shell.Clipboard.Dispose();
            Directory.Delete(root, true);
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile);
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }
    });
    [Fact]
    public void OneMousePageContainsBothRealFeaturesAndFocusSwitchPersists() => WpfTestThread.Run(() =>
    {
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        string profile = "mouse-merge-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), profile);
        Directory.CreateDirectory(root);
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, profile);
        shell.Services.Input = new Input();
        var values = new TrackingValues();
        var focus = new FocusFollowsMouseModule(() => new FocusTrackingEngine(values, new TrackingJournalStub()));
        SettingsWindow? window = null;
        try
        {
            shell.Registry.Register(new DebounceModule());
            shell.Registry.Register(focus);
            WpfTestThread.Wait(shell.Registry.SetInstalledAsync("debounce", true));
            WpfTestThread.Wait(shell.Registry.SetInstalledAsync("focus-follows-mouse", true));
            window = new SettingsWindow(shell, "page:mouse");
            window.Width = 1040; window.Height = 840;
            window.Measure(new Size(1040, 840)); window.Arrange(new Rect(0, 0, 1040, 840)); window.UpdateLayout();
            Assert.Single(window.NavigationPages, p => p.Id == "page:mouse");
            var rootView = (FrameworkElement)window.Content;
            rootView.Measure(new Size(1040, 840)); rootView.Arrange(new Rect(0, 0, 1040, 840)); rootView.UpdateLayout();
            var focusRows = Assert.Single(Find<FocusTrackingRows>(window.PageContent));
            var clicks = Find<UserControl>(window.PageContent).Single(c => c.GetType().Assembly.GetName().Name == "Chippi.Features.Debounce");
            Assert.True(focusRows.TransformToAncestor(window.PageContent).Transform(new Point()).Y < clicks.TransformToAncestor(window.PageContent).Transform(new Point()).Y);
            Assert.Contains(Find<UserControl>(window.PageContent), c => c.GetType().Assembly.GetName().Name == "Chippi.Features.Debounce");
            Shot(window, "off", "mouse");
            var toggle = Find<ToggleButton>(window.PageContent).Single(t => AutomationProperties.GetName(t) == "Focus follows mouse");
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle)).Toggle();
            Assert.True(focus.Settings.Enabled);
            Assert.Equal(new uint[] { 1, 1, 250 }, values.Values);
            window.Navigate("general"); window.Navigate("page:mouse");
            Assert.True(Find<ToggleButton>(window.PageContent).Single(t => AutomationProperties.GetName(t) == "Focus follows mouse").IsChecked);
            Shot(window, "on", "mouse");
        }
        finally
        {
            window?.Close(); WpfTestThread.Wait(shell.Registry.StopAllAsync());
            shell.Input.Dispose(); shell.Clipboard.Dispose();
            Directory.Delete(root, true);
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile);
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }
        Assert.Equal(new uint[] { 0, 0, 500 }, values.Values);
    });
    private sealed class TrackingValues : ITrackingSettings
    {
        public uint[] Values { get; } = [0, 0, 500];
        public uint Read(TrackingSetting key) => Values[(int)key];
        public void Write(TrackingSetting key, uint value) => Values[(int)key] = value;
    }
    private sealed class TrackingJournalStub : ITrackingJournal
    {
        public Dictionary<TrackingSetting, TrackingOwnership> Load() => [];
        public void Save(IReadOnlyDictionary<TrackingSetting, TrackingOwnership> values) { }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }
    private static void Shot(FrameworkElement view, string state, string page = "keyboard")
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        if (view is Window window) view = (FrameworkElement)window.Content;
        view.Measure(new Size(1040, 840)); view.Arrange(new Rect(0, 0, 1040, 840)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1040, 840, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, page + "-merged-" + state + ".png")); png.Save(file);
    }
    private sealed class Input : IInputHub, IInputRegistration
    {
        public bool Paused => false;
        public bool FullscreenInFront => false;
        public event Action? PauseChanged { add { } remove { } }
        public IInputRegistration Add(IInputFilter filter) => this;
        public void Refresh() { }
        public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) => throw new InvalidOperationException("No native input in this UI check");
        public void Mask() => throw new InvalidOperationException("No native input in this UI check");
        public void Dispose() { }
    }
    private sealed class Desktop : ICloseDesktop
    {
        public CloseTarget? Current => null;
        public CloseModifiers Modifiers() => CloseModifiers.None;
        public event Action? ForegroundChanged { add { } remove { } }
        public event Action? DesktopChanged { add { } remove { } }
        public void Start() { }
        public void Post(Action work) => work();
        public void Beep() => throw new InvalidOperationException("No sound in this UI check");
        public Task<IReadOnlyList<CloseApp>> OpenAppsAsync() => Task.FromResult<IReadOnlyList<CloseApp>>([]);
        public void Dispose() { }
    }
}