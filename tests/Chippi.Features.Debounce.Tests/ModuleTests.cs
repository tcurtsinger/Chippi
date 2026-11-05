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

namespace Chippi.Features.Debounce.Tests;

public sealed class ModuleTests
{
    [Fact]
    public async Task Both_switches_start_off_and_a_filter_is_in_the_hub_only_while_its_switch_is_on()
    {
        var hub = new FakeHub();
        var store = new Store();
        var module = new DebounceModule();
        await module.StartAsync(Context(hub, store, new Ui()), default);
        Assert.Empty(hub.Filters);

        module.Update(s => s with { ClickEnabled = true });
        Assert.Single(hub.Filters);
        Assert.Equal(InputDevices.Mouse, hub.Filters[0].Devices);
        module.Update(s => s with { KeyEnabled = true, KeyMs = 12 });
        Assert.Equal(2, hub.Filters.Count);
        module.Update(s => s with { ClickEnabled = false });
        Assert.Single(hub.Filters);
        Assert.Equal(InputDevices.Keyboard, hub.Filters[0].Devices);
        Assert.Equal(12, store.Value.KeyMs);

        await module.StopAsync(default);
        Assert.Empty(hub.Filters);
    }

    [Fact]
    public async Task Settings_are_clamped_and_a_refused_hook_says_so_without_throwing()
    {
        var hub = new FakeHub { Refuse = true };
        var module = new DebounceModule();
        await module.StartAsync(Context(hub, new Store { Value = new DebounceSettings { ClickEnabled = true, ClickMs = 900, Keys = [new(0x45, 999), new(0, 5)] } }, new Ui()), default);
        Assert.Equal(100, module.Settings.ClickMs);
        Assert.Equal([new KeyWindow(0x45, 200)], module.Settings.Keys);
        Assert.Equal("Chippi cannot watch the mouse right now.", module.MouseError);
        Assert.Null(module.KeyboardError);
    }

    [Fact]
    public void The_rows_toggle_by_UIA_save_and_show_the_ignored_count()
    {
        WpfTestThread.Run(() =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
            bool light = WpfTestThread.ShotsTheme == "Light";
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
            Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
            var hub = new FakeHub();
            var store = new Store { Value = new DebounceSettings { Keys = [new(0x45, 30), new(0x20, 15)] } };
            var ui = new Ui();
            var module = new DebounceModule();
            module.StartAsync(Context(hub, store, ui), default).GetAwaiter().GetResult();

            var mouse = (DebounceRows)ui.Page!("mouse");
            var toggle = Find<ToggleButton>(mouse).Single();
            toggle.ApplyTemplate();
            var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
            Assert.False(store.Value.ClickEnabled);
            Shot(mouse, "mouse-off");
            peer.Toggle();
            Assert.True(store.Value.ClickEnabled);
            Assert.Single(hub.Filters);
            Shot(mouse, "mouse-on");

            var keyboard = (DebounceRows)ui.Page!("keyboard");
            var keySwitch = Find<ToggleButton>(keyboard).First();
            keySwitch.ApplyTemplate();
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(keySwitch)).Toggle();
            Assert.True(store.Value.KeyEnabled);
            Assert.Contains(Find<TextBlock>(keyboard), t => t.Text == "E");
            Assert.Contains(Find<TextBlock>(keyboard), t => t.Text == "Space");
            Shot(keyboard, "keyboard-on");
            var remove = Find<Button>(keyboard).First(b => b.Content as string == "Remove");
            remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Single(store.Value.Keys);
            module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    private static ModuleContext Context(IInputHub hub, Store store, Ui ui) =>
        new(DebounceModule.Id, store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(DebounceModule.Id), ui) { Input = hub };

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
        using var output = File.Create(Path.Combine(folder, "debounce-" + name + ".png"));
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
        public DebounceSettings Value { get; set; } = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T settings) where T : class => Value = (DebounceSettings)(object)settings;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<string?, object>? Page { get; private set; }
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Debounce has no tile");
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
    }
}
