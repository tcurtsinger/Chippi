// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Core.Tests;

public sealed class WheelMessageTests
{
    [Theory]
    [InlineData(1, -120)]
    [InlineData(3, -360)]
    [InlineData(40, -4800)]
    [InlineData(-1, 120)]
    [InlineData(-2, 240)]
    [InlineData(100, -12000)]
    public void The_wheel_delta_sits_in_the_high_word_and_down_is_negative(int notches, int delta)
    {
        nint wParam = WheelMessage.WParam(notches);
        Assert.Equal(delta, WheelMessage.DeltaOf(wParam));
        Assert.Equal(0, (int)((ulong)wParam & 0xFFFF)); // no keys held
        Assert.Equal(0UL, (ulong)wParam >> 32);          // nothing above the 32-bit word
    }

    [Fact]
    public void Three_notches_down_is_the_known_word()
    {
        // MAKEWPARAM(0, (WORD)-360): 0xFE98 in the high word.
        Assert.Equal(unchecked((nint)0xFE980000u), WheelMessage.WParam(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-101)]
    public void No_turn_or_too_many_notches_is_refused(int notches) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMessage.WParam(notches));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1280, 720)]
    [InlineData(-1920, 300)]
    [InlineData(500, -1080)]
    [InlineData(-3000, -2000)]
    [InlineData(short.MaxValue, short.MinValue)]
    public void The_screen_point_packs_as_signed_words_and_unpacks_back(int x, int y)
    {
        nint lParam = WheelMessage.LParam(x, y);
        Assert.Equal((x, y), WheelMessage.PointOf(lParam));
        Assert.Equal(x & 0xFFFF, (int)((ulong)lParam & 0xFFFF));
        Assert.Equal(y & 0xFFFF, (int)((ulong)lParam >> 16 & 0xFFFF));
    }

    [Theory]
    [InlineData(40000, 0)]
    [InlineData(0, -40000)]
    public void A_point_that_does_not_fit_in_a_word_is_refused(int x, int y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMessage.LParam(x, y));

    [Fact]
    public void The_default_PostWheel_is_unavailable()
    {
        IActionPc pc = new NoWheelPc();
        Assert.Equal("Unavailable", Assert.Throws<ActionFailure>(() => pc.PostWheel(1, 0, 0, 3)).Message);
    }

    // Implements only the members without default bodies, to reach the interface's default PostWheel.
    private sealed class NoWheelPc : IActionPc
    {
        public bool? HasApp(string parsingName) => null;
        public bool Exists(string path) => false;
        public nint WindowUnderPointer() => 0;
        public nint WindowInFront() => 0;
        public void Open(string target) { }
        public void Window(nint window, WindowAction action) { }
        public void Media(MediaAction action) { }
        public Task PressAsync(KeyCombo keys) => Task.CompletedTask;
        public Task TypeAsync(string text) => Task.CompletedTask;
        public void Run(CommandShell shell, string line) { }
        public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => Task.FromResult<(bool, string?)>((false, null));
    }
}
