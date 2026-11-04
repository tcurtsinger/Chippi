// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.CommandBar.Tests;

/// <summary>Settings recovery using in-memory storage and shortcuts; no windows or system actions.</summary>
public sealed class SettingsRecoveryTests
{
    [Fact]
    public void A_failed_engine_or_category_write_restores_the_control_and_retry_clears_its_error() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var view = new CommandBarSettingsPage(f.Module);
        Load(view);
        try
        {
            var engine = Find<ComboBox>(view).Single();
            Assert.Equal("Search engine", AutomationProperties.GetName(engine));
            var files = Find<ToggleButton>(view).Single(t => AutomationProperties.GetName(t) == "Files and folders");
            f.Store.FailSaving = true;
            engine.SelectedIndex = (int)WebEngine.Bing;
            Assert.Equal((int)WebEngine.Google, engine.SelectedIndex);
            Assert.Equal(WebEngine.Google, f.Module.Settings.WebSearch);
            Assert.Single(Find<TextBlock>(view), IsSaveError);
            files.IsChecked = false;
            Assert.True(files.IsChecked);
            Assert.DoesNotContain(ResultSwitch.Files, f.Module.Settings.Off);
            Assert.Equal(2, Find<TextBlock>(view).Count(IsSaveError));
            f.Store.FailSaving = false;
            engine.SelectedIndex = (int)WebEngine.Bing;
            files.IsChecked = false;
            Assert.Equal(WebEngine.Bing, f.Module.Settings.WebSearch);
            Assert.Contains(ResultSwitch.Files, f.Module.Settings.Off);
            Assert.DoesNotContain(Find<TextBlock>(view), IsSaveError);
        }
        finally { Unload(view); }
    });

    [Fact]
    public void Hide_and_undo_are_transactional_and_report_failure_without_losing_the_saved_result()
    {
        using var f = new Fixture();
        var chrome = App("Chrome");
        f.Store.FailSaving = true;
        Assert.False(f.Module.Hide(chrome));
        Assert.Empty(f.Module.Settings.Hidden);
        Assert.Equal("Save failed", f.Module.SettingsError);
        f.Store.FailSaving = false;
        Assert.True(f.Module.Hide(chrome));
        Assert.Single(f.Module.Settings.Hidden);
        Assert.Null(f.Module.SettingsError);
        f.Store.FailSaving = true;
        Assert.False(f.Module.Unhide(chrome.Key));
        Assert.Single(f.Module.Settings.Hidden);
        f.Store.FailSaving = false;
        Assert.True(f.Module.Unhide(chrome.Key));
        Assert.Empty(f.Module.Settings.Hidden);
        Assert.Null(f.Module.SettingsError);
    }

    [Fact]
    public void Hidden_rows_keep_their_controls_and_restore_from_a_disabled_category_offers_explicit_enable() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Module.Hide(App("Chrome"));
        f.Module.Hide(App("Spotify"));
        f.Module.SetOn(ResultSwitch.Apps, false);
        var view = new CommandBarSettingsPage(f.Module);
        Load(view);
        try
        {
            var next = Named(view, "Show Spotify");
            f.Module.SetWebSearch(WebEngine.Bing);
            Assert.Same(next, Named(view, "Show Spotify"));
            Assert.Contains(Find<SettingRow>(view), r => r.Header == "Chrome" && r.Description == "App · Apps off");
            f.Store.FailSaving = true;
            Click(Named(view, "Show Chrome"));
            Assert.Equal(2, f.Module.Settings.Hidden.Count);
            Assert.Contains(Find<TextBlock>(view), IsSaveError);
            f.Store.FailSaving = false;
            Click(Named(view, "Show Chrome"));
            Assert.Same(next, Named(view, "Show Spotify"));
            Assert.Single(f.Module.Settings.Hidden);
            Assert.Contains(ResultSwitch.Apps, f.Module.Settings.Off);
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Shown · Apps off");
            Assert.DoesNotContain(Find<TextBlock>(view), IsSaveError);
            Click(next);
            Assert.Empty(f.Module.Settings.Hidden);
            Assert.Contains(ResultSwitch.Apps, f.Module.Settings.Off);
            Click(Named(view, "Enable Apps results"));
            Assert.DoesNotContain(ResultSwitch.Apps, f.Module.Settings.Off);
        }
        finally { Unload(view); }
    });

    [Fact]
    public void A_failed_shortcut_save_restores_the_registered_chord_and_keeps_the_keys_visible() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var view = new ShortcutRow(f.Module, "Command bar");
        Load(view);
        try
        {
            var previous = f.Module.Settings.Shortcut;
            f.Store.FailSaving = true;
            Assert.False(f.Module.ChangeShortcut(NewShortcut));
            Assert.Equal(previous, f.Module.Settings.Shortcut);
            Assert.Equal(previous, f.Hotkey.Shortcut);
            Assert.Equal(previous, f.Store.Value.Shortcut);
            Assert.Equal("Save failed", f.Module.ShortcutError);
            Assert.Contains(Find<TextBlock>(view), t => t.Text == previous!.ToString());
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Save failed" && AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite);
            f.Store.FailSaving = false;
            Assert.True(f.Module.ChangeShortcut(NewShortcut));
            Assert.Equal(NewShortcut, f.Hotkey.Shortcut);
            Assert.Null(f.Module.ShortcutError);
        }
        finally { Unload(view); }
    });

    [Fact]
    public void A_shortcut_save_failure_with_a_failed_rollback_disables_the_unsaved_chord()
    {
        using var f = new Fixture();
        var previous = f.Module.Settings.Shortcut;
        f.Hotkey.Outcomes.Enqueue(true); // The proposed chord registers.
        f.Hotkey.Outcomes.Enqueue(false); // Another app claimed the previous chord before rollback.
        f.Store.FailSaving = true;
        Assert.False(f.Module.ChangeShortcut(NewShortcut));
        Assert.Equal(previous, f.Module.Settings.Shortcut);
        Assert.Equal(previous, f.Store.Value.Shortcut);
        Assert.Null(f.Hotkey.Pressed);
        Assert.Equal(1, f.Hotkey.Disposals);
        Assert.Equal("Save failed · Shortcut inactive", f.Module.ShortcutError);
    }

    [Fact]
    public void A_failed_shortcut_add_or_remove_leaves_the_persisted_registration_in_effect()
    {
        using var f = new Fixture();
        f.Store.FailSaving = true;
        Assert.False(f.Module.RemoveShortcut());
        Assert.Equal(CommandBarSettings.DefaultShortcut, f.Hotkey.Shortcut);
        Assert.NotNull(f.Hotkey.Pressed);
        Assert.Equal(CommandBarSettings.DefaultShortcut, f.Module.Settings.Shortcut);
        f.Store.FailSaving = false;
        Assert.True(f.Module.RemoveShortcut());
        Assert.Null(f.Module.Settings.Shortcut);
        Assert.Null(f.Hotkey.Pressed);
        f.Store.FailSaving = true;
        Assert.False(f.Module.ChangeShortcut(NewShortcut));
        Assert.Null(f.Module.Settings.Shortcut);
        Assert.Null(f.Hotkey.Pressed);
        Assert.Equal("Save failed", f.Module.ShortcutError);
    }

    [Fact]
    public void A_registration_conflict_keeps_the_old_keys_and_error_visible() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var view = new ShortcutRow(f.Module, "Command bar");
        Load(view);
        try
        {
            f.Hotkey.Outcomes.Enqueue(false);
            Assert.False(f.Module.ChangeShortcut(NewShortcut));
            Assert.Equal(CommandBarSettings.DefaultShortcut, f.Hotkey.Shortcut);
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Ctrl+Alt+Space");
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Shortcut in use");
            Assert.Equal(0, f.Store.Writes);
        }
        finally { Unload(view); }
    });

    [Theory]
    [InlineData("tab")]
    [InlineData("focus")]
    [InlineData("unload")]
    [InlineData("escape")]
    [InlineData("cancel")]
    public void Leaving_shortcut_capture_restores_search(string exit) => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var view = new ShortcutRow(f.Module, "Command bar");
        Load(view);
        Click(Named(view, "Change Command bar shortcut"));
        f.Hotkey.Pressed!();
        Assert.Equal(0, f.Summoned);
        switch (exit)
        {
            case "tab":
            case "escape":
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new NoWindowSource(), 0, exit == "tab" ? Key.Tab : Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                view.RaiseEvent(key);
                Assert.True(key.Handled);
                break;
            case "focus":
                view.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, view, new Button())
                    { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                break;
            case "unload": Unload(view); break;
            default: Click(Named(view, "Cancel Command bar shortcut")); break;
        }
        f.Hotkey.Pressed!();
        Assert.Equal(1, f.Summoned);
        Assert.Null(f.Module.ShortcutError);
        Assert.DoesNotContain(Find<Button>(view), b => AutomationProperties.GetName(b) == "Cancel Command bar shortcut");
        Unload(view);
    });

    [Fact]
    public void Unrelated_settings_changes_preserve_shortcut_controls() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var view = new ShortcutRow(f.Module, "Command bar");
        Load(view);
        try
        {
            var change = Named(view, "Change Command bar shortcut");
            f.Module.SetWebSearch(WebEngine.Bing);
            f.Module.SetOn(ResultSwitch.Emoji, false);
            Assert.Same(change, Named(view, "Change Command bar shortcut"));
        }
        finally { Unload(view); }
    });

    [Theory]
    [InlineData("App", ResultSwitch.Apps)]
    [InlineData("Window · Chrome", ResultSwitch.Windows)]
    [InlineData("File · Reports", ResultSwitch.Files)]
    [InlineData("Windows Settings", ResultSwitch.Settings)]
    [InlineData("Clipboard", ResultSwitch.Clipboard)]
    [InlineData("Emoji", ResultSwitch.Emoji)]
    [InlineData("Chippi", ResultSwitch.Chippi)]
    public void Previously_hidden_entries_recover_their_category(string kind, ResultSwitch expected) =>
        Assert.Equal(expected, new HiddenResult("legacy-key", "Name", kind).Switch);

    private static MicShortcut NewShortcut => new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x4B);
    private static Result App(string name) => new("app:" + name, ResultKind.App, name, "App", () => Task.CompletedTask);
    private static bool IsSaveError(TextBlock text) => text.Text == "Save failed" && text.Visibility == Visibility.Visible;
    private static void Load(FrameworkElement view) => view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    private static void Unload(FrameworkElement view) => view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Button Named(DependencyObject view, string name) => Find<Button>(view).Single(b => AutomationProperties.GetName(b) == name);
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class Fixture : IDisposable
    {
        internal Store Store { get; } = new();
        internal FakeHotkey Hotkey { get; } = new();
        internal CommandBarModule Module { get; }
        internal int Summoned;
        internal Fixture()
        {
            Module = new CommandBarModule(() => new NoPc(), () => Hotkey);
            Module.StartAsync(new ModuleContext(CommandBarModule.Id, Store, NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(CommandBarModule.Id), new NoUi())
            {
                DataDirectory = Path.Combine(Path.GetTempPath(), "chippi-tests", "command-bar-settings-unused"),
                SummonIslandPage = _ => { Summoned++; return true; },
            }, default).GetAwaiter().GetResult();
        }
        public void Dispose() => Module.StopAsync(default).GetAwaiter().GetResult();
    }

    private sealed class Store : IModuleSettings
    {
        internal CommandBarSettings Value = new();
        internal bool FailSaving;
        internal int Writes;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (FailSaving) throw new IOException("Fixture disk unavailable");
            Writes++;
            Value = (CommandBarSettings)(object)value;
        }
    }

    private sealed class FakeHotkey : IMicHotkey
    {
        internal Queue<bool> Outcomes { get; } = [];
        internal MicShortcut? Shortcut;
        internal Action? Pressed;
        internal int Disposals;
        public bool Register(MicShortcut shortcut, Action pressed)
        {
            if (Outcomes.TryDequeue(out bool available) && !available) return false;
            Shortcut = shortcut;
            Pressed = pressed;
            return true;
        }
        public void Dispose() { Disposals++; Pressed = null; Shortcut = null; }
    }

    private sealed class NoPc : IPc
    {
        public IReadOnlyList<StartApp> Apps => [];
        public event Action? AppsChanged { add { } remove { } }
        public void RefreshApps() { }
        public IReadOnlyList<OpenWindow> Windows() => [];
        public Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct) => Task.FromResult<IReadOnlyList<FoundFile>>([]);
        public void Open(string target) => throw new InvalidOperationException("No OS actions in this fixture");
        public void Switch(nint window) => throw new InvalidOperationException("No OS actions in this fixture");
        public Task<ImageSource?> AppIconAsync(StartApp app, int pixels) => Task.FromResult<ImageSource?>(null);
        public void Dispose() { }
    }

    private sealed class NoUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(CommandBarModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class NoWindowSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
