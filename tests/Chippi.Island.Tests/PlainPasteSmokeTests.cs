// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.Clipboard.Tests;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.PlainPaste;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Real shortcut module, publisher and tab together. All Windows services are replaced; the window has no HWND.</summary>
public sealed class PlainPasteSmokeTests
{
    [Theory]
    [InlineData(PasteOutcome.Sent, null, false)]
    [InlineData(PasteOutcome.Copied, "Plain text copied", false)]
    [InlineData(PasteOutcome.NoText, "No text copied", true)]
    [InlineData(PasteOutcome.Busy, "Clipboard busy", true)]
    public void Standalone_shortcut_drives_and_releases_the_real_tab_glyph(PasteOutcome outcome, string? name, bool caution) => WpfTestThread.Run(() =>
    {
        var clock = new ManualTime(); var hub = new Hub(outcome); var key = new Hotkey(); var ui = new Ui();
        var activities = new IslandActivityHub(); var window = new IslandWindow(); int registrations = 0;
        var module = new PlainPasteModule(() => { registrations++; return key; }, clock);
        void Refresh(object? sender, EventArgs args) => window.SetActivities(activities.Activities, PetMood.Idle);
        activities.Changed += Refresh;
        try
        {
            module.StartAsync(new ModuleContext(PlainPasteModule.Id, new Settings(), NullLogger.Instance, activities.CreatePublisher(PlainPasteModule.Id), ui) { Clipboard = hub }, default).GetAwaiter().GetResult();
            Assert.Equal(0, registrations); Assert.NotNull(ui.SettingsFactory);
            module.InvokeAsync().GetAwaiter().GetResult(); Assert.Equal(0, hub.Calls);
            Assert.True(module.ChangeShortcut(new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x56)));
            activities.CreatePublisher("fake-player").Publish(new IslandActivity("track", IslandActivityKind.Media, "Invented track", "Fake player"));
            key.Pressed!();
            Assert.Equal(1, hub.Calls); Assert.Equal(name is null ? 0 : 1, hub.Beeps);
            // The bar: the feedback for 6 s, then the track again (its title over its app, and the music glyph: no session here, so no bars).
            var glyph = window.GlyphIcon; var widget = window.PuckButton; const string track = "Chippi, Invented track · Fake player";
            if (name is null)
            {
                Assert.Same(LucideIcons.Music, glyph.Data); Assert.Equal(track, AutomationProperties.GetName(widget));
            }
            else
            {
                Assert.Same(LucideIcons.ClipboardType, glyph.Data); Assert.Equal($"Chippi, {name}", AutomationProperties.GetName(widget));
                Assert.Equal(((SolidColorBrush)window.FindResource(caution ? "IslandCautionBrush" : "IslandAccentBrush")).Color, ((SolidColorBrush)glyph.Brush).Color);
                clock.Advance(TimeSpan.FromMilliseconds(5999)); Assert.Same(LucideIcons.ClipboardType, glyph.Data);
                clock.Advance(TimeSpan.FromMilliseconds(1)); Assert.Same(LucideIcons.Music, glyph.Data);
                Assert.Equal(track, AutomationProperties.GetName(widget));
            }
            Assert.True(window.BarShown);
            Assert.Single(activities.Activities); Assert.Equal("fake-player", activities.Activities[0].ModuleId);
            Assert.False(window.IsVisible); Assert.Equal(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(window).Handle);
        }
        finally
        {
            module.StopAsync(default).GetAwaiter().GetResult(); activities.Changed -= Refresh; window.Close();
        }
        Assert.True(key.Disposed);
    });
    private sealed class Settings : IModuleSettings { private PlainPasteSettings _value = new(); public T Get<T>() where T : class, new() => (T)(object)_value; public void Save<T>(T value) where T : class => _value = (PlainPasteSettings)(object)value; }
    private sealed class Ui : IModuleUi { public Func<string?, object>? SettingsFactory; public void SetSettingsPage(Func<string?, object> factory) => SettingsFactory = factory; public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Shortcut-only feature must not create a tile."); }
    private sealed class Hotkey : IMicHotkey { public Action? Pressed; public bool Disposed; public bool Register(MicShortcut shortcut, Action pressed) { Assert.True(shortcut.IsValid); Pressed = pressed; return true; } public void Dispose() { Disposed = true; Pressed = null; } }
    private sealed class Hub(PasteOutcome outcome) : IClipboardHub
    {
        public int Calls, Beeps;
        public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) { Calls++; return Task.FromResult(outcome); }
        public void Beep() => Beeps++;
        public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null) => throw new InvalidOperationException("No clipboard listener allowed.");
        public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => throw new InvalidOperationException("No content read allowed.");
        public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default) => throw new InvalidOperationException("No clipboard write allowed.");
        public PasteTarget? CaptureTarget() => throw new InvalidOperationException("The hub owns target capture.");
        public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new InvalidOperationException("Use plain paste only.");
    }
}
