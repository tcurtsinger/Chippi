// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Core.Modules;
namespace Chippi.Core.Tests;
public sealed class IslandOpenHoldTests
{
    [Fact]
    public void Overlapping_scopes_and_repeated_disposal_do_not_release_another_hold()
    {
        var holds = new IslandOpenHold();
        var dialog = holds.Hold(); var drag = holds.Hold();
        Assert.True(holds.IsHeld);
        dialog.Dispose(); dialog.Dispose(); Assert.True(holds.IsHeld);
        drag.Dispose(); Assert.False(holds.IsHeld);
        using (holds.Hold()) Assert.True(holds.IsHeld);
        Assert.False(holds.IsHeld);
    }
}
