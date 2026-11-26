// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.MacDesktop.MenuBar;

namespace Chippi.Features.MacDesktop.Tests;

public sealed class AppMenuLabelTests
{
    [Theory]
    [InlineData("&File", "File", null)]
    [InlineData("&Save\tCtrl+S", "Save", "Ctrl+S")]
    [InlineData("Find && Replace...\tCtrl+H", "Find & Replace...", "Ctrl+H")]
    [InlineData("E&xit", "Exit", null)]
    public void MnemonicsGoAndShortcutsSplitOff(string text, string label, string? shortcut) =>
        Assert.Equal((label, shortcut), AppMenus.Label(text));

    [Fact]
    public void SettingsRejectASizeOutOfRangeAndTheIslandNeedsTheMenuBar()
    {
        Assert.Throws<ArgumentException>(() => new MacDesktopSettings { MagnifiedSize = MacDesktopSettings.MaxMagnified + 1 }.Validate());
        Assert.False(new MacDesktopSettings().ShowsIsland); // the Windows desktop
        Assert.True(new MacDesktopSettings { Style = DesktopStyle.MacStyle }.ShowsIsland);
        Assert.False(new MacDesktopSettings { Style = DesktopStyle.MacStyle, MenuBar = false }.ShowsIsland);
        Assert.False(new MacDesktopSettings { Style = DesktopStyle.MacStyle, Island = false }.ShowsIsland);
        new MacDesktopSettings().Validate();
        Assert.False(new MacDesktopSettings().HidesTaskbar);
        Assert.True(new MacDesktopSettings { Style = DesktopStyle.MacStyle }.HidesTaskbar);
        Assert.False(new MacDesktopSettings { Style = DesktopStyle.MacStyle, MenuBar = false, Dock = false }.HidesTaskbar);
    }
}
