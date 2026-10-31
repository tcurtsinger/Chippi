// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.Sound.Tests;

public sealed class ShortcutModifierTests
{
    [Fact]
    public void Shortcuts_require_control_or_alt_even_when_shift_is_present()
    {
        foreach (uint key in Enumerable.Range(0x30, 10).Concat(Enumerable.Range(0x41, 26)).Concat(Enumerable.Range(0x70, 11)).Select(key => (uint)key))
        {
            Assert.False(new MicShortcut(ShortcutModifiers.Shift, key).IsValid);
            Assert.False(new MicShortcut(0, key).IsValid);
            Assert.True(new MicShortcut(ShortcutModifiers.Control, key).IsValid);
            Assert.True(new MicShortcut(ShortcutModifiers.Alt | ShortcutModifiers.Shift, key).IsValid);
        }
    }

    [Theory]
    [InlineData(0x31u, "Ctrl+Shift+1")] // not "D1"
    [InlineData(0x4Du, "Ctrl+Shift+M")]
    [InlineData(0x75u, "Ctrl+Shift+F6")]
    public void Shortcuts_read_as_their_keys(uint key, string shown) =>
        Assert.Equal(shown, new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Shift, key).ToString());
}
