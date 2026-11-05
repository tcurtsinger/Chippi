// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Core.Tests;

/// <summary>The shortcut service (docs/phase-6/architecture.md §4) on a fake <c>RegisterHotKey</c>.</summary>
public sealed class ShortcutServiceTests
{
    private static readonly KeyShortcut CtrlShiftM = new(ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x4D);
    private static readonly KeyShortcut CtrlAltV = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x56);

    [Fact]
    public void Every_offered_shortcut_is_listed_set_or_not_and_a_press_runs_its_own_action()
    {
        var windows = new FakeRegistrar();
        var service = new ShortcutService(windows);
        int changes = 0;
        service.Changed += () => changes++;
        var mute = service.Add("mic-mute", "Mute microphone");
        var paste = service.Add("paste-plain-text", "Paste as plain text");
        Assert.Equal([new("mic-mute", "Mute microphone", null, null), new("paste-plain-text", "Paste as plain text", null, null)], service.List);
        Assert.Empty(windows.Held);

        int muted = 0, pasted = 0;
        Assert.True(mute.Register(CtrlShiftM, () => muted++));
        Assert.True(paste.Register(CtrlAltV, () => pasted++));
        Assert.Equal([CtrlShiftM, CtrlAltV], service.List.Select(s => s.Shortcut));
        windows.Press(CtrlAltV);
        Assert.Equal((0, 1), (muted, pasted));
        Assert.Equal(4, changes);
    }

    [Fact]
    public void A_bindable_shortcut_may_hold_the_Windows_key_and_the_service_asks_Windows_for_it_but_a_plain_one_still_may_not()
    {
        var windows = new FakeRegistrar();
        var service = new ShortcutService(windows);
        var winJ = new KeyShortcut(ShortcutModifiers.Windows, 0x4A);
        Assert.False(winJ.IsValid);
        Assert.True(winJ.IsBindable);
        Assert.Equal("Win+J", winJ.ToString());
        Assert.True(service.Add("shortcuts", "Snap left").Register(winJ, () => { }));
        Assert.Equal([winJ], windows.Held.Values);

        Assert.False(new KeyShortcut(ShortcutModifiers.Shift, 0x4A).IsBindable); // no Ctrl, Alt or Win
        Assert.False(new KeyShortcut(ShortcutModifiers.Control, 0x14).IsBindable); // Caps Lock is a door, not a key
        Assert.True(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x25).IsBindable); // Left arrow
        Assert.True(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0xBC).IsBindable); // comma
        Assert.Equal("Ctrl+Alt+Left", new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x25).ToString());
        Assert.Equal("Win+;", new KeyShortcut(ShortcutModifiers.Windows, 0xBA).ToString());
        Assert.Equal("Win+Ctrl+Alt+Shift+K", new KeyShortcut((ShortcutModifiers)15, 0x4B).ToString());
    }

    [Fact]
    public void Keys_another_Chippi_shortcut_holds_are_refused_by_name_without_asking_Windows()
    {
        var windows = new FakeRegistrar();
        var service = new ShortcutService(windows);
        var mute = service.Add("mic-mute", "Mute microphone");
        var bar = service.Add("command-bar", "Command bar");
        Assert.True(mute.Register(CtrlShiftM, () => { }));
        Assert.True(bar.Register(CtrlAltV, () => { }));
        int asked = windows.Asked;

        Assert.False(bar.Register(CtrlShiftM, () => { }));
        Assert.Equal("Used by Mute microphone", bar.Error);
        Assert.Equal(asked, windows.Asked);
        Assert.Equal(CtrlAltV, service.List[1].Shortcut); // its own keys stay
        Assert.Equal("Used by Mute microphone", service.List[1].Error);

        // Once the holder lets go, the keys are free.
        mute.Unregister();
        Assert.True(bar.Register(CtrlShiftM, () => { }));
        Assert.Null(bar.Error);
        Assert.Equal([CtrlShiftM], windows.Held.Values);
    }

    [Fact]
    public void Keys_Windows_refuses_are_in_use_by_another_app_and_the_old_keys_keep_working()
    {
        var windows = new FakeRegistrar();
        var service = new ShortcutService(windows);
        var mute = service.Add("mic-mute", "Mute microphone");
        int pressed = 0;
        Assert.True(mute.Register(CtrlShiftM, () => pressed++));
        windows.Taken.Add(CtrlAltV);

        Assert.False(mute.Register(CtrlAltV, () => { }));
        Assert.Equal(ShortcutService.InUse, mute.Error);
        Assert.Equal(CtrlShiftM, service.List[0].Shortcut);
        windows.Press(CtrlShiftM);
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_change_holds_the_new_keys_before_letting_the_old_ones_go()
    {
        var windows = new FakeRegistrar();
        var mute = new ShortcutService(windows).Add("mic-mute", "Mute microphone");
        Assert.True(mute.Register(CtrlShiftM, () => { }));
        Assert.True(mute.Register(CtrlAltV, () => { }));
        Assert.Equal(["register Ctrl+Shift+M", "register Ctrl+Alt+V", "unregister Ctrl+Shift+M"], windows.Calls);
        // The same keys again only take the new action.
        Assert.True(mute.Register(CtrlAltV, () => { }));
        Assert.Equal(3, windows.Calls.Count);
    }

    [Fact]
    public void Unregister_keeps_it_listed_unset_and_Dispose_takes_it_off_the_list()
    {
        var windows = new FakeRegistrar();
        var service = new ShortcutService(windows);
        var mute = service.Add("mic-mute", "Mute microphone");
        int pressed = 0;
        mute.Register(CtrlShiftM, () => pressed++);

        mute.Unregister();
        Assert.Equal([new("mic-mute", "Mute microphone", null, null)], service.List);
        Assert.Empty(windows.Held);

        mute.Register(CtrlShiftM, () => pressed++);
        mute.Dispose();
        Assert.Empty(service.List);
        Assert.Empty(windows.Held);
        Assert.False(mute.Register(CtrlShiftM, () => pressed++)); // a disposed shortcut holds nothing
        int changes = 0;
        service.Changed += () => changes++;
        mute.Unregister();
        mute.Dispose();
        Assert.Equal(0, changes); // a disposed shortcut reports nothing more
        Assert.Empty(windows.Held);
        Assert.Equal(0, pressed);
    }

    [Fact]
    public void Without_a_shell_every_shortcut_is_refused_and_still_listed()
    {
        var service = new ShortcutService(RefusingRegistrar.Instance);
        var mute = service.Add("mic-mute", "Mute microphone");
        Assert.False(mute.Register(CtrlShiftM, () => { }));
        Assert.Equal(ShortcutService.InUse, mute.Error);
        Assert.Single(service.List);
    }

    [Theory]
    [InlineData(0x31u, "Ctrl+Shift+1")] // not "D1"
    [InlineData(0x4Du, "Ctrl+Shift+M")]
    [InlineData(0x75u, "Ctrl+Shift+F6")]
    [InlineData(0x20u, "Ctrl+Shift+Space")]
    public void Shortcuts_read_as_their_keys(uint key, string shown) =>
        Assert.Equal(shown, new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Shift, key).ToString());

    private sealed class FakeRegistrar : IHotkeyRegistrar
    {
        public readonly Dictionary<int, KeyShortcut> Held = [];
        public readonly HashSet<KeyShortcut> Taken = []; // by another app
        public readonly List<string> Calls = [];
        public int Asked;

        public event Action<int>? Pressed;

        public bool Register(int id, KeyShortcut shortcut)
        {
            Asked++;
            if (Taken.Contains(shortcut) || Held.ContainsValue(shortcut))
                return false;
            Held[id] = shortcut;
            Calls.Add("register " + shortcut);
            return true;
        }

        public void Unregister(int id)
        {
            if (Held.Remove(id, out var shortcut))
                Calls.Add("unregister " + shortcut);
        }

        public void Press(KeyShortcut shortcut) => Pressed?.Invoke(Held.Single(h => h.Value == shortcut).Key);
    }
}
