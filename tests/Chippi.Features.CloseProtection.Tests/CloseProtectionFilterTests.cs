// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Features.CloseProtection.Tests;

public sealed class CloseProtectionFilterTests
{
    [Fact]
    public void TimerReadingAheadOfQueuedKeyDoesNotClearPendingPress()
    {
        var scene = new Scene();
        Assert.True(scene.Key(true, 0));
        Assert.True(scene.Key(false, 1));
        scene.Filter.Advance(100);
        Assert.False(scene.Key(true, 99));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistinctReleasedSecondPressPassesWithoutSendingAnything(bool controlW)
    {
        var scene = new Scene(controlW);
        Assert.True(scene.Key(true, 0));
        Assert.True(scene.Key(true, 1, repeat: true));
        Assert.True(scene.Key(false, 2));
        Assert.False(scene.Key(true, CloseProtectionFilter.IntervalTicks - 1));
        Assert.False(scene.Key(false, CloseProtectionFilter.IntervalTicks));
        Assert.Null(scene.Feedback.Last());
    }

    [Fact]
    public void ExactExpiryStartsAnotherFirstPressAndFoldsOldFeedback()
    {
        var scene = new Scene();
        Assert.True(scene.Key(true, 0));
        scene.Key(false, 1);
        scene.Filter.Advance(CloseProtectionFilter.IntervalTicks);
        Assert.Null(scene.Feedback.Last());
        Assert.True(scene.Key(true, CloseProtectionFilter.IntervalTicks));
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("process")]
    [InlineData("pause")]
    [InlineData("disable")]
    [InlineData("reset")]
    public void PendingPressDoesNotSurviveReset(string reason)
    {
        var scene = new Scene();
        scene.Key(true, 0);
        scene.Key(false, 1);
        switch (reason)
        {
            case "focus": scene.Target = scene.Target with { Window = 2 }; break;
            case "process": scene.Target = scene.Target with { ProcessStarted = 2 }; break;
            case "pause": scene.Filter.SetPaused(true); scene.Filter.SetPaused(false); break;
            case "disable": scene.Filter.Configure(false, []); scene.Filter.Configure(true, []); break;
            default: scene.Filter.Reset(); break;
        }
        Assert.True(scene.Key(true, 2));
    }

    [Fact]
    public void PauseAndDisableKeepTheSwallowedRelease()
    {
        var scene = new Scene();
        scene.Key(true, 0);
        scene.Filter.SetPaused(true);
        scene.Filter.Configure(false, []);
        Assert.True(scene.Key(false, 1));
        Assert.False(scene.Key(true, 2));
    }

    [Fact]
    public void IneligibleAndUnselectedAppsAndExtraModifiersPass()
    {
        var scene = new Scene(controlW: true);
        scene.Filter.Configure(true, []);
        Assert.False(scene.Key(true, 0));
        scene = new Scene();
        scene.Target = scene.Target with { Eligible = false };
        Assert.False(scene.Key(true, 0));
        scene.Target = scene.Target with { Eligible = true };
        scene.Modifiers |= CloseModifiers.Shift;
        Assert.False(scene.Key(true, 1));
    }

    [Fact]
    public void DefaultIsOffAndAPreviouslyHeldKeyPasses()
    {
        var target = new CloseTarget(1, 1, 1, "app.exe", "App", true);
        var filter = new CloseProtectionFilter(() => target, () => CloseModifiers.Alt);
        Assert.Equal(InputDevices.None, filter.Devices);
        Assert.False(filter.Key(new KeyInput(0x73, 0, true, false, false, false, 0)));
        filter.Configure(true, []);
        Assert.False(filter.Key(new KeyInput(0x73, 0, true, true, false, false, 1)));
    }

    [Fact]
    public void ChordsKeepIndependentWindowsAndFeedbackReturnsToTheRemainingPendingChord()
    {
        var scene = new Scene();
        scene.Key(true, 0);
        scene.Key(false, 1);
        scene.Modifiers = CloseModifiers.Control;
        Assert.True(scene.Filter.Key(new KeyInput(0x57, 0, true, false, false, false, 2)));
        scene.Filter.Key(new KeyInput(0x57, 0, false, false, false, false, 3));
        Assert.False(scene.Filter.Key(new KeyInput(0x57, 0, true, false, false, false, 4)));
        Assert.Equal(CloseChord.AltF4, scene.Feedback.Last()!.Chord);
        scene.Modifiers = CloseModifiers.Alt;
        Assert.False(scene.Key(true, 5));
        Assert.Null(scene.Feedback.Last());
    }

    [Fact]
    public void DesktopResetKeepsSwallowedRepeatsAndUpUntilReleased()
    {
        var scene = new Scene();
        scene.Key(true, 0);
        scene.Filter.Reset();
        Assert.True(scene.Key(true, 2, repeat: true));
        Assert.Null(scene.Feedback.Last());
        Assert.True(scene.Key(false, 3));
        Assert.True(scene.Key(true, 4));
        Assert.NotNull(scene.Feedback.Last());
    }
    private sealed class Scene
    {
        public CloseTarget Target = new(1, 1, 1, "app.exe", "App", true);
        public CloseModifiers Modifiers;
        public CloseProtectionFilter Filter { get; }
        public List<PendingClose?> Feedback { get; } = [];
        private readonly uint _vk;
        public Scene(bool controlW = false)
        {
            Modifiers = controlW ? CloseModifiers.Control : CloseModifiers.Alt;
            _vk = controlW ? 0x57u : 0x73u;
            Filter = new CloseProtectionFilter(() => Target, () => Modifiers);
            Filter.Feedback += Feedback.Add;
            Filter.Configure(true, ["app.exe"]);
        }
        public bool Key(bool down, long timestamp, bool repeat = false) =>
            Filter.Key(new KeyInput(_vk, 0, down, repeat, false, false, timestamp));
    }
}
