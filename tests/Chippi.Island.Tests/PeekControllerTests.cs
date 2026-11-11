// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.Core.Input;
using Chippi.Core.Shortcuts;

namespace Chippi.Island.Tests;

/// <summary>No windows, real hooks, input, clipboard or owner settings.</summary>
public sealed class PeekControllerTests
{
    [Fact]
    public void Unassigned_Peek_is_editable_without_starting_input()
    {
        using var f = new Fixture(null);
        var slot = Assert.Single(f.Shortcuts.List); Assert.Equal("peek", slot.Owner); Assert.True(slot.Editable);
        Assert.Null(slot.Shortcut); Assert.Equal(0, f.Hub.Adds);
        Assert.Null(f.Shortcuts.Change(slot.Id, Keys)); Assert.True(f.Shortcuts.List.Single().Held);
        Assert.Equal(Keys, f.Config.GetSection<AppSettings>("app").PeekShortcut); Assert.Equal(1, f.Hub.Adds);
        f.Shortcuts.Remove(slot.Id); Assert.Null(f.Config.GetSection<AppSettings>("app").PeekShortcut); Assert.Null(f.Hub.Filter);
    }

    [Fact]
    public void Hold_opens_once_and_closes_on_trigger_release()
    {
        using var f = new Fixture(Keys);
        f.Down(0xA2); f.Down(0xA4); f.Down(Keys.Key); f.Registrar.Press();
        Assert.True(f.Controller.Active); Assert.Equal(new[] { "system" }, f.Opened);
        f.Down(Keys.Key, repeat: true); Assert.Single(f.Opened);
        f.Up(Keys.Key); Assert.True(f.Controller.Active); f.Flush();
        Assert.False(f.Controller.Active); Assert.Equal(1, f.Closes);
    }

    [Fact]
    public void A_short_tap_released_before_the_UI_callback_cannot_leave_Hold_open()
    {
        using var f = new Fixture(Keys); f.Down(Keys.Key); f.Up(Keys.Key); f.Registrar.Press(); f.Flush();
        Assert.Empty(f.Opened); Assert.False(f.Controller.Active);
    }

    [Fact]
    public void An_old_release_cannot_close_a_new_hold()
    {
        using var f = new Fixture(Keys);
        f.Down(Keys.Key); f.Registrar.Press(); f.Up(Keys.Key);
        f.Down(Keys.Key); f.Registrar.Press(); f.Flush(); Assert.True(f.Controller.Active);
        f.Up(Keys.Key); f.Flush(); Assert.False(f.Controller.Active); Assert.Equal(1, f.Closes);
    }

    [Fact]
    public void Hold_closes_when_a_required_modifier_is_released_or_the_hub_resets()
    {
        using var f = new Fixture(Keys);
        f.Down(0xA2); f.Down(0xA3); f.Down(0xA4); f.Down(Keys.Key); f.Registrar.Press();
        f.Up(0xA2); f.Flush(); Assert.True(f.Controller.Active); // Right Ctrl still holds the chord.
        f.Up(0xA3); f.Flush(); Assert.False(f.Controller.Active);
        f.Up(Keys.Key); f.Down(Keys.Key); f.Registrar.Press(); Assert.True(f.Controller.Active);
        f.Hub.Filter!.InputReset(InputDevices.Keyboard); f.Flush(); Assert.False(f.Controller.Active);
    }

    [Fact]
    public void Toggle_uses_only_the_shortcut_service_and_opens_until_a_second_press()
    {
        using var f = new Fixture(Keys, PeekMode.Toggle);
        Assert.Equal(0, f.Hub.Adds);
        f.Registrar.Press(); Assert.True(f.Controller.Active); Assert.Single(f.Opened);
        f.Registrar.Press(); Assert.False(f.Controller.Active); Assert.Equal(1, f.Closes);
        f.Registrar.Press(); f.Controller.Dismissed(); Assert.False(f.Controller.Active);
        f.Registrar.Press(); Assert.True(f.Controller.Active); Assert.Equal(3, f.Opened.Count);
    }

    [Fact]
    public void Settings_changes_cancel_the_visit_and_disabling_the_island_frees_the_chord()
    {
        using var f = new Fixture(Keys, PeekMode.Toggle);
        f.Registrar.Press(); f.Write(s => s.PeekPage = "capture"); Assert.False(f.Controller.Active);
        f.Registrar.Press(); Assert.Equal("capture", f.Opened.Last());
        f.Write(s => s.IslandEnabled = false); Assert.False(f.Controller.Active); Assert.False(f.Shortcuts.List.Single().Held);
        Assert.Null(f.Hub.Filter); Assert.Equal(Keys, f.Shortcuts.List.Single().Shortcut);
        f.Write(s => s.IslandEnabled = true); Assert.True(f.Shortcuts.List.Single().Held);
        f.Registrar.Press(); Assert.True(f.Controller.Active);
    }

    [Fact]
    public void A_refused_shortcut_or_failed_save_preserves_the_previous_choice()
    {
        using var f = new Fixture(Keys, PeekMode.Toggle); long id = f.Shortcuts.List.Single().Id;
        var next = Keys with { Key = 0x51 }; f.Registrar.Refuse = true;
        Assert.NotNull(f.Shortcuts.Change(id, next)); Assert.Equal(Keys, f.Config.GetSection<AppSettings>("app").PeekShortcut);
        f.Registrar.Refuse = false;
        File.Delete(f.Config.FilePath); Directory.CreateDirectory(f.Config.FilePath);
        Assert.Equal("Save failed", f.Shortcuts.Change(id, next)); Assert.Equal(Keys, f.Shortcuts.List.Single().Shortcut);
        f.Registrar.Press(); Assert.True(f.Controller.Active);
    }

    [Fact]
    public void Failed_release_tracking_never_leaves_an_active_global_Hold_shortcut()
    {
        using var f = new Fixture(null); f.Hub.Refuse = true;
        Assert.Equal("Peek release tracking failed", f.Shortcuts.Change(f.Shortcuts.List.Single().Id, Keys));
        Assert.Null(f.Config.GetSection<AppSettings>("app").PeekShortcut);
        Assert.False(f.Shortcuts.List.Single().Held); Assert.False(f.Controller.Active); Assert.Null(f.Hub.Filter);
    }

    private static readonly KeyShortcut Keys = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x50);
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-peek-" + Guid.NewGuid().ToString("N"));
        private readonly Queue<Action> _actions = new();
        public ConfigStore Config { get; }
        public Registrar Registrar { get; } = new();
        public Hub Hub { get; } = new();
        public ShortcutService Shortcuts { get; }
        public PeekController Controller { get; }
        public List<string?> Opened { get; } = [];
        public int Closes { get; private set; }
        public Fixture(KeyShortcut? keys, PeekMode mode = PeekMode.Hold)
        {
            Directory.CreateDirectory(_root); Config = new(Path.Combine(_root, "settings.json"));
            Config.SetSection("app", new AppSettings { PeekShortcut = keys, PeekMode = mode, PeekPage = "system", IslandLastPage = "clipboard" });
            Shortcuts = new(Registrar);
            Controller = new(Config, Shortcuts, Hub, _actions.Enqueue, page => { Opened.Add(page); return true; }, () => Closes++);
        }
        public void Down(uint key, bool repeat = false) { if (Hub.Filter is { } filter) Assert.False(filter.Key(new(key, 0, true, repeat, false, false, 0))); }
        public void Up(uint key) { if (Hub.Filter is { } filter) Assert.False(filter.Key(new(key, 0, false, true, false, false, 0))); }
        public void Flush() { while (_actions.TryDequeue(out var action)) action(); }
        public void Write(Action<AppSettings> change) { var settings = Config.GetSection<AppSettings>("app"); change(settings); Config.SetSection("app", settings); Flush(); }
        public void Dispose() { Controller.Dispose(); Flush(); Assert.Empty(Shortcuts.List); Assert.Null(Hub.Filter); Directory.Delete(_root, true); }
    }
    private sealed class Registrar : IHotkeyRegistrar
    {
        private int _id;
        public bool Refuse { get; set; }
        public bool Register(int id, KeyShortcut shortcut) { if (Refuse) return false; _id = id; return true; }
        public void Unregister(int id) { if (_id == id) _id = 0; }
        public event Action<int>? Pressed;
        public void Press() { if (_id != 0) Pressed?.Invoke(_id); }
    }
    private sealed class Hub : IInputHub
    {
        public IInputFilter? Filter { get; private set; }
        public int Adds { get; private set; }
        public bool Refuse { get; set; }
        public IInputRegistration Add(IInputFilter filter) { if (Refuse) throw new InvalidOperationException("Fixture hook refused"); Adds++; Filter = filter; return new Registration(() => Filter = null); }
        public bool Paused => false;
        public bool FullscreenInFront => false;
        public event Action? PauseChanged { add { } remove { } }
        private sealed class Registration(Action dispose) : IInputRegistration
        { public void Dispose() => dispose(); public void Refresh() { } public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { } public void Mask() { } }
    }
}
