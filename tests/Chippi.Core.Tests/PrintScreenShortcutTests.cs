// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Core.Tests;

/// <summary>Print Screen in the shortcut service and Windows' own Print Screen setting (docs/phase-7/capture-layer.md, Print Screen).</summary>
public sealed class PrintScreenShortcutTests
{
    private static readonly KeyShortcut PrintScreen = new(0, KeyShortcut.PrintScreen);
    private static readonly KeyShortcut CtrlAltR = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x52);

    [Fact]
    public void Print_Screen_is_bindable_alone_and_with_any_modifiers_and_nothing_else_is_bare()
    {
        Assert.True(PrintScreen.IsBindable);
        Assert.Equal("Print Screen", PrintScreen.ToString());
        Assert.True(new KeyShortcut(ShortcutModifiers.Shift, KeyShortcut.PrintScreen).IsBindable);
        Assert.True(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, KeyShortcut.PrintScreen).IsBindable);
        Assert.Equal("Ctrl+Shift+Print Screen", new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Shift, KeyShortcut.PrintScreen).ToString());
        Assert.False(PrintScreen.IsValid); // the older features' own rule still needs Ctrl or Alt

        foreach (uint key in new uint[] { 0x41, 0x31, 0x70, 0x2D, 0x2E, 0x20, 0x0D, 0x13, 0x91 })
        {
            Assert.False(new KeyShortcut(0, key).IsBindable);
            Assert.False(new KeyShortcut(ShortcutModifiers.Shift, key).IsBindable);
        }
        Assert.False(new KeyShortcut((ShortcutModifiers)16, KeyShortcut.PrintScreen).IsBindable); // no unknown modifiers
    }

    [Fact]
    public void Win_Print_Screen_stays_Windows_and_Print_Screen_is_checked_like_any_shortcut()
    {
        var windows = new ShortcutServiceTests.FakeRegistrar();
        var service = new ShortcutService(windows, new MemoryPrintScreenSetting());
        Assert.Equal(ShortcutService.WindowsText, service.Check(new KeyShortcut(ShortcutModifiers.Windows, KeyShortcut.PrintScreen)));
        Assert.Null(service.Check(PrintScreen));

        Assert.True(service.Add("screen-capture", "Capture region").Register(PrintScreen, () => { }));
        Assert.Equal("Capture region already uses this", service.Check(PrintScreen));
    }

    [Fact]
    public void Holding_Print_Screen_saves_what_Windows_had_and_turns_its_setting_off_once()
    {
        var setting = new MemoryPrintScreenSetting(); // unset: on
        var service = new ShortcutService(new ShortcutServiceTests.FakeRegistrar(), setting);
        var region = service.Add("screen-capture", "Capture region");

        Assert.True(region.Register(PrintScreen, () => { }));
        Assert.Equal(0, setting.Value);
        Assert.Equal(new PrintScreenClaim(null), setting.Claim);
        Assert.Equal(1, setting.Writes);

        // A second Print Screen shortcut (with a modifier) and registering again don't write again.
        Assert.True(service.Add("text-and-qr", "Copy text").Register(new KeyShortcut(ShortcutModifiers.Shift, KeyShortcut.PrintScreen), () => { }));
        Assert.True(region.Register(PrintScreen, () => { }));
        Assert.Equal(1, setting.Writes);
        Assert.All(service.List, e => Assert.Null(e.Error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(0)]
    public void Changing_removing_or_disposing_the_last_Print_Screen_shortcut_puts_back_what_was_found(int? found)
    {
        foreach (var letGo in new Action<IHotkey>[] { h => h.Register(CtrlAltR, () => { }), h => h.Unregister(), h => h.Dispose() })
        {
            var setting = new MemoryPrintScreenSetting(found);
            var service = new ShortcutService(new ShortcutServiceTests.FakeRegistrar(), setting);
            var region = service.Add("screen-capture", "Capture region");
            var text = service.Add("text-and-qr", "Copy text");
            Assert.True(region.Register(PrintScreen, () => { }));
            Assert.True(text.Register(new KeyShortcut(ShortcutModifiers.Shift, KeyShortcut.PrintScreen), () => { }));
            Assert.Equal(0, setting.Value);

            text.Unregister(); // another one still holds it
            Assert.Equal(0, setting.Value);
            Assert.NotNull(setting.Claim);

            letGo(region);
            Assert.Equal(found, setting.Value);
            Assert.Null(setting.Claim);
        }
    }

    [Fact]
    public void Quitting_keeps_the_setting_off_for_the_next_start_and_a_hold_nothing_uses_is_let_go_after_the_features_start()
    {
        var setting = new MemoryPrintScreenSetting(1);
        var service = new ShortcutService(new ShortcutServiceTests.FakeRegistrar(), setting);
        var region = service.Add("screen-capture", "Capture region");
        Assert.True(region.Register(PrintScreen, () => { }));
        service.Closing();
        region.Dispose();
        Assert.Equal(0, setting.Value);
        Assert.Equal(new PrintScreenClaim(1), setting.Claim);

        // The next start: Screen capture is gone, so nothing takes Print Screen.
        var next = new ShortcutService(new ShortcutServiceTests.FakeRegistrar(), setting);
        next.Add("mic-mute", "Mute microphone").Register(CtrlAltR, () => { });
        next.Started();
        Assert.Equal(1, setting.Value);
        Assert.Null(setting.Claim);
    }

    [Fact]
    public void Windows_setting_turned_back_on_reads_Used_by_Windows_until_Print_Screen_is_recorded_again()
    {
        var setting = new MemoryPrintScreenSetting();
        var service = new ShortcutService(new ShortcutServiceTests.FakeRegistrar(), setting);
        KeyShortcut? saved = null;
        IHotkey region = null!;
        region = service.Add("screen-capture", "Capture region", new ShortcutEdits(() => saved, keys =>
        {
            if (!region.Register(keys, () => { })) return region.Error;
            saved = keys;
            return null;
        }, () => { region.Unregister(); saved = null; }));
        Assert.Null(service.Change(service.List[0].Id, PrintScreen));
        Assert.Equal(0, setting.Value);
        int writes = setting.Writes, changes = 0;
        service.Changed += () => changes++;

        setting.SetOutside(1);
        Assert.Equal(ShortcutService.UsedByWindowsText, service.List[0].Error);
        Assert.True(service.List[0].Held); // RegisterHotKey succeeded either way: the value tells
        Assert.Equal(1, changes);
        Assert.Equal(writes, setting.Writes); // left on: Travis chose it

        // A restart while it's on doesn't turn it off either.
        var restart = new ShortcutService(new ShortcutServiceTests.FakeRegistrar(), setting);
        var again = restart.Add("screen-capture", "Capture region");
        Assert.True(again.Register(PrintScreen, () => { }));
        Assert.Equal(ShortcutService.UsedByWindowsText, again.Error);
        Assert.Equal(1, setting.Value);

        // Recording Print Screen again turns it off and clears the line.
        Assert.Null(service.Change(service.List[0].Id, PrintScreen));
        Assert.Equal(0, setting.Value);
        Assert.Null(service.List[0].Error);
        Assert.Equal(new PrintScreenClaim(null), setting.Claim); // still what was found first

        // Turned off from outside: the line clears too.
        setting.SetOutside(1);
        setting.SetOutside(0);
        Assert.Null(service.List[0].Error);
    }

    [Fact]
    public void Without_a_setting_the_service_leaves_Windows_alone()
    {
        var service = new ShortcutService(new ShortcutServiceTests.FakeRegistrar());
        var region = service.Add("screen-capture", "Capture region");
        Assert.True(region.Register(PrintScreen, () => { }));
        region.Dispose();
    }
}
