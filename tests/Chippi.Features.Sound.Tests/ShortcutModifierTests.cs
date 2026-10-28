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
}
