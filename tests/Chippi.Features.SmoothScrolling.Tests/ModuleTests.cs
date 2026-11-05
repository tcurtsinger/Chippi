// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.SmoothScrolling.Tests;

public sealed class ModuleTests
{
    [Fact]
    public async Task Both_switches_start_off_and_the_filter_is_in_the_hub_only_while_one_is_on()
    {
        var hub = new FakeHub();
        var store = new Store();
        var module = new SmoothScrollingModule();
        await module.StartAsync(Context(hub, store, new Ui()), default);
        Assert.Empty(hub.Filters);

        module.Update(s => s with { LinearEnabled = true });
        Assert.Single(hub.Filters);
        Assert.Equal(InputDevices.Mouse, hub.Filters[0].Devices);
        Assert.True(hub.Filters[0].Pauses);
        module.Update(s => s with { SmoothEnabled = true, ResponseMs = 200 });
        Assert.Single(hub.Filters);
        Assert.Equal(200, store.Value.ResponseMs);
        module.Update(s => s with { LinearEnabled = false });
        Assert.Single(hub.Filters);
        module.Update(s => s with { SmoothEnabled = false });
        Assert.Empty(hub.Filters);

        module.Update(s => s with { SmoothEnabled = true });
        await module.StopAsync(default);
        Assert.Empty(hub.Filters);
    }

    [Fact]
    public async Task Settings_are_clamped_apps_are_matched_once_and_a_refused_hook_says_so()
    {
        var hub = new FakeHub { Refuse = true };
        var module = new SmoothScrollingModule();
        var saved = new SmoothScrollingSettings
        {
            SmoothEnabled = true, DistancePercent = 9000, ResponseMs = 1, CoastMs = 9999,
            Apps = [new(@"C:\a\Chrome.exe", 150), new(@"D:\b\chrome.EXE", 0), new("", 100)],
        };
        await module.StartAsync(Context(hub, new Store { Value = saved }, new Ui()), default);
        Assert.Equal(300, module.Settings.DistancePercent);
        Assert.Equal(50, module.Settings.ResponseMs);
        Assert.Equal(400, module.Settings.CoastMs);
        Assert.Equal([new AppScroll(@"D:\b\chrome.EXE", 0)], module.Settings.Apps);
        Assert.Equal("Watching the mouse failed", module.Error);
    }

    [Fact]
    public void The_rows_toggle_by_UIA_save_and_list_apps()
    {
        WpfTestThread.Run(() =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
            bool light = WpfTestThread.ShotsTheme == "Light";
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
            Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
            var hub = new FakeHub();
            var store = new Store { Value = new SmoothScrollingSettings { Apps = [new(@"C:\Windows\notepad.exe", 150), new(@"C:\Windows\explorer.exe", 0)] } };
            var ui = new Ui();
            var module = new SmoothScrollingModule();
            module.StartAsync(Context(hub, store, ui), default).GetAwaiter().GetResult();

            var rows = (SmoothScrollingRows)ui.Page!("mouse");
            var toggles = Find<ToggleButton>(rows).ToList();
            Assert.Equal(2, toggles.Count);
            Shot(rows, "off");
            foreach (var toggle in toggles) toggle.ApplyTemplate();
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggles[0])).Toggle();
            Assert.True(store.Value.SmoothEnabled);
            Assert.False(store.Value.LinearEnabled);
            Assert.Single(hub.Filters);
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggles[1])).Toggle();
            Assert.True(store.Value.LinearEnabled);
            Shot(rows, "on");

            Assert.Contains(Find<Chippi.UI.Controls.SettingRow>(rows), r => r.Header as string == "Scrolling per app" && r.Description == "2 apps");
            var combos = Find<ComboBox>(rows).ToList();
            Assert.Equal(2, combos.Count);
            Assert.Equal("150%", combos[0].SelectedItem);
            Assert.Equal("Leave alone", combos[1].SelectedItem);
            combos[0].SelectedItem = "200%";
            Assert.Equal(200, store.Value.Apps.Single(a => a.App.EndsWith("notepad.exe")).Percent);
            var remove = Find<Button>(rows).First(b => b.Content as string == "Remove");
            remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Single(store.Value.Apps);
            module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    private static ModuleContext Context(IInputHub hub, Store store, Ui ui) =>
        new(SmoothScrollingModule.Id, store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(SmoothScrollingModule.Id), ui) { Input = hub };

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
        using var output = File.Create(Path.Combine(folder, "smooth-scrolling-" + name + ".png"));
        encoder.Save(output);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class FakeHub : IInputHub
    {
        public List<IInputFilter> Filters { get; } = [];
        public bool Refuse { get; init; }
        public bool Paused { get; set; }
        public bool FullscreenInFront => false;
        public event Action? PauseChanged { add { } remove { } }

        public IInputRegistration Add(IInputFilter filter)
        {
            if (Refuse) throw new System.ComponentModel.Win32Exception(5);
            Filters.Add(filter);
            return new Registration(() => Filters.Remove(filter));
        }

        private sealed class Registration(Action remove) : IInputRegistration
        {
            public void Dispose() => remove();
            public void Refresh() { }
            public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { }
            public void Mask() { }
        }
    }

    private sealed class Store : IModuleSettings
    {
        public SmoothScrollingSettings Value { get; set; } = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T settings) where T : class => Value = (SmoothScrollingSettings)(object)settings;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<string?, object>? Page { get; private set; }
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Smooth scrolling has no tile");
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
    }
}
