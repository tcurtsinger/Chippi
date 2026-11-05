// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

public sealed class SettingsSurfaceTests
{
    [Fact]
    public void A_settings_switch_changes_gallery_visibility_without_running_the_action()
    {
        WpfTestThread.Run(() =>
        {
            var pc = new FakePc();
            var store = new Store();
            var module = new QuickActionsModule(() => pc, () => new NoHotkey());
            module.StartAsync(new ModuleContext(QuickActionsModule.Id, store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new NoUi()), default).GetAwaiter().GetResult();
            var view = new QuickActionsSettingsPage(module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var toggle = Find<ToggleButton>(view).Single(t => AutomationProperties.GetName(t) == "Show Restart Explorer in Actions");
                toggle.ApplyTemplate();
                Assert.Equal("Hidden", toggle.Content);
                var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
                peer.Toggle();
                Assert.True(module.Settings.Shows(QuickActionId.Explorer));
                Assert.Equal("Shown", toggle.Content);
                Assert.Equal("Shown", AutomationProperties.GetItemStatus(toggle));
                peer.Toggle();
                Assert.False(module.Settings.Shows(QuickActionId.Explorer));
                Assert.Equal("Hidden", toggle.Content);
                Assert.Empty(pc.Calls);
            }
            finally
            {
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                module.StopAsync(default).GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Shortcut_rows_keep_their_controls_across_PC_polls_and_unrelated_edits()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var view = new ShortcutList(f.Module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var dark = Named(view, "Set Dark mode shortcut");
                var wifi = Named(view, "Set Wi-Fi shortcut");
                var initial = Find<SettingRow>(view).ToArray();
                var constructed = new HashSet<SettingRow>(initial);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int poll = 0; poll < 100; poll++)
                {
                    f.Module.RefreshAsync().GetAwaiter().GetResult();
                    var current = Find<SettingRow>(view).ToArray();
                    Assert.Equal(initial.Length, current.Length);
                    for (int i = 0; i < initial.Length; i++) Assert.Same(initial[i], current[i]);
                    constructed.UnionWith(current);
                }
                watch.Stop();
                Assert.Equal(initial.Length, constructed.Count);
                if (Environment.GetEnvironmentVariable("CHIPPI_ACTIONS_SHORTCUT_PROOF") is { Length: > 0 } report)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                    File.WriteAllText(report, $"Unchanged FakePc polls: 100\nRows: {initial.Length}\nBefore: {initial.Length * 100} SettingRow constructions, derived from previous Refresh clearing/rebuilding every row; no before timing claimed.\nAfter: {constructed.Count - initial.Length} new SettingRows, measured by object identity after every poll.\nCurrent 100 polls including identity assertions: {watch.Elapsed.TotalMilliseconds:F3} ms.\nNo windows, actual shortcuts or Windows actions.\n");
                }
                f.Module.SetShown(QuickActionId.Dark, true);
                Assert.Same(dark, Named(view, "Set Dark mode shortcut"));
                Assert.Same(wifi, Named(view, "Set Wi-Fi shortcut"));
                f.Module.ChangeShortcut(QuickActionId.WiFi, new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x57));
                Assert.Same(dark, Named(view, "Set Dark mode shortcut"));
                Assert.NotSame(wifi, Named(view, "Change Wi-Fi shortcut"));
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("focus")]
    [InlineData("unavailable")]
    [InlineData("unload")]
    public void Leaving_shortcut_capture_restores_commands(string exit)
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var view = new ShortcutList(f.Module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Named(view, "Set Bluetooth shortcut").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            f.Module.PressedAsync(QuickActionId.Lock).GetAwaiter().GetResult();
            Assert.Empty(f.Pc.Calls);
            switch (exit)
            {
                case "tab":
                    var tab = new KeyEventArgs(Keyboard.PrimaryDevice, new NoWindowSource(), 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    view.RaiseEvent(tab);
                    Assert.True(tab.Handled);
                    Assert.Empty(f.Module.ShortcutErrors);
                    break;
                case "focus":
                    view.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, view, new Button()) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                    break;
                case "unavailable":
                    f.Pc.State = f.Pc.State with { Unsupported = new HashSet<QuickActionId> { QuickActionId.Bluetooth } };
                    f.Module.RefreshAsync().GetAwaiter().GetResult();
                    break;
                default:
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    break;
            }
            f.Module.PressedAsync(QuickActionId.Lock).GetAwaiter().GetResult();
            Assert.Equal(["run Lock"], f.Pc.Calls);
            Assert.DoesNotContain(Find<Button>(view), b => AutomationProperties.GetName(b) == "Cancel Bluetooth shortcut");
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        });
    }

    [Fact]
    public void A_shortcut_conflict_keeps_the_registered_keys_visible_beside_the_error()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var view = new ShortcutList(f.Module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var registered = new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x57);
                Assert.True(f.Module.ChangeShortcut(QuickActionId.WiFi, registered));
                f.Hotkey.Available = false;
                Assert.False(f.Module.ChangeShortcut(QuickActionId.WiFi, new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x56)));
                Assert.Equal(registered, f.Module.Settings.ShortcutOf(QuickActionId.WiFi));
                Assert.Contains(Find<TextBlock>(view), text => text.Text == registered.ToString());
                Assert.Contains(Find<TextBlock>(view), text => text.Text == "Shortcut in use" && AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public void The_kept_app_menu_bounds_long_lists_without_omitting_apps_or_Browse()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            string longName = new('W', 180);
            f.Pc.State = f.Pc.State with { Apps = [.. Enumerable.Range(1, 50).Select(i => new OpenApp($@"C:\Apps\app{i}.exe", longName + i, false)), new(@"D:\Update\APP1.exe", "Duplicate", false)] };
            f.Module.RefreshAsync().GetAwaiter().GetResult();
            var view = new QuickActionsSettingsPage(f.Module);
            var popup = view.AddMenu(new Button()); // Build and measure only; never creates a popup window.
            Assert.False(popup.IsOpen);
            var border = Assert.IsType<Border>(popup.Child);
            border.Measure(new Size(320, 500));
            border.Arrange(new Rect(border.DesiredSize));
            border.UpdateLayout();
            Assert.True(border.ActualWidth <= 320);
            Assert.True(border.ActualHeight <= 330);
            Assert.Equal(51, Find<Button>(border).Count());
            Assert.Contains(Find<Button>(border), b => AutomationProperties.GetName(b) == "Browse…");
            var app = Find<Button>(border).First(b => AutomationProperties.GetName(b) == longName + "2");
            Assert.Equal(longName + "2", app.ToolTip);
            Assert.Equal(TextTrimming.CharacterEllipsis, Assert.IsType<TextBlock>(app.Content).TextTrimming);
            app.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(f.Module.Settings.Keeps(@"C:\Apps\app2.exe"));
            Assert.Empty(f.Pc.Calls);
        });
    }

    [Fact]
    public void A_visibility_save_failure_restores_the_switch_and_retry_clears_the_error()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var view = new QuickActionsSettingsPage(f.Module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var toggle = Find<ToggleButton>(view).Single(t => AutomationProperties.GetName(t) == "Show Restart Explorer in Actions");
                var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
                f.Settings.FailSaving = true;
                peer.Toggle();
                Assert.False(f.Module.Settings.Shows(QuickActionId.Explorer));
                Assert.False(toggle.IsChecked);
                Assert.Equal("Hidden", toggle.Content);
                Assert.Contains(Find<TextBlock>(view), IsSaveError);
                f.Settings.FailSaving = false;
                peer.Toggle();
                Assert.True(f.Module.Settings.Shows(QuickActionId.Explorer));
                Assert.True(toggle.IsChecked);
                Assert.DoesNotContain(Find<TextBlock>(view), IsSaveError);
                Assert.Empty(f.Pc.Calls);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public void Repeated_kept_app_edits_keep_the_footer_and_failed_writes_can_be_retried()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var view = new QuickActionsSettingsPage(f.Module);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                var add = Find<Button>(view).Single(b => Equals(b.Content, "Add app"));
                for (int i = 0; i < 2; i++)
                {
                    f.Module.Keep(@"C:\Apps\first.exe");
                    f.Module.Keep(@"C:\Apps\second.exe");
                    f.Module.Unkeep(@"C:\Apps\first.exe");
                    f.Module.Unkeep(@"C:\Apps\second.exe");
                    Assert.Same(add, Find<Button>(view).Single(b => Equals(b.Content, "Add app")));
                }
                f.Module.Keep(@"C:\Apps\first.exe");
                f.Settings.FailSaving = true;
                Named(view, "Remove first from kept apps").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(f.Module.Settings.Keeps(@"C:\Apps\first.exe"));
                Assert.Contains(Find<TextBlock>(view), IsSaveError);
                f.Settings.FailSaving = false;
                Named(view, "Remove first from kept apps").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(f.Module.Settings.Keeps(@"C:\Apps\first.exe"));
                Assert.DoesNotContain(Find<TextBlock>(view), IsSaveError);

                f.Pc.State = f.Pc.State with { Apps = [FakePc.Spotify] };
                f.Module.RefreshAsync().GetAwaiter().GetResult();
                f.Settings.FailSaving = true;
                Named(view.AddMenu(add).Child, "Spotify").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(f.Module.Settings.Keeps(FakePc.Spotify.Path));
                Assert.Contains(Find<TextBlock>(view), IsSaveError);
                f.Settings.FailSaving = false;
                Named(view.AddMenu(add).Child, "Spotify").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(f.Module.Settings.Keeps(FakePc.Spotify.Path));
                Assert.DoesNotContain(Find<TextBlock>(view), IsSaveError);
                Assert.Same(add, Find<Button>(view).Single(b => Equals(b.Content, "Add app")));
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    private static bool IsSaveError(TextBlock text) => text.Text == "Save failed" && text.Visibility == Visibility.Visible;

    private static Button Named(DependencyObject view, string name) => Find<Button>(view).Single(button => AutomationProperties.GetName(button) == name);

    private sealed class Fixture : IDisposable
    {
        internal FakePc Pc { get; } = new();
        internal Store Settings { get; } = new();
        internal TestHotkey Hotkey { get; } = new();
        internal QuickActionsModule Module { get; }
        internal Fixture()
        {
            Module = new QuickActionsModule(() => Pc, () => Hotkey);
            Module.StartAsync(new ModuleContext(QuickActionsModule.Id, Settings, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new NoUi()), default).GetAwaiter().GetResult();
        }
        public void Dispose() => Module.StopAsync(default).GetAwaiter().GetResult();
    }

    private sealed class TestHotkey : IMicHotkey
    {
        internal bool Available = true;
        public bool Register(MicShortcut shortcut, Action pressed) => Available;
        public void Dispose() { }
    }

    private sealed class NoWindowSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class Store : IModuleSettings
    {
        private QuickActionsSettings _value = new();
        internal bool FailSaving;
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class
        {
            if (FailSaving) throw new IOException("Fixture disk unavailable");
            _value = (QuickActionsSettings)(object)value;
        }
    }

    private sealed class NoUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }

    private sealed class NoHotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
