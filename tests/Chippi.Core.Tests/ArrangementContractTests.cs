// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Core.Tests;

public sealed class ArrangementContractTests
{
    [Theory]
    [InlineData("editor.exe")]
    [InlineData("https://example.com/app.exe")]
    [InlineData("C:\\apps\\script.cmd")]
    public void Executable_launch_rejects_shell_targets(string path) =>
        Assert.False(new ExecutableLaunch(path).IsValid);

    [Fact]
    public void Executable_launch_requires_an_absolute_working_folder()
    {
        Assert.True(new ExecutableLaunch("C:\\apps\\editor.exe", "--new-window", "C:\\work").IsValid);
        Assert.False(new ExecutableLaunch("C:\\apps\\editor.exe", "", "relative").IsValid);
        Assert.False(new ExecutableLaunch("C:\\apps\\editor.exe", "bad\0argument").IsValid);
    }

    [Fact]
    public void Saved_geometry_refuses_empty_work_areas_and_overflow()
    {
        var saved = new SavedWindowPlacement(-1920, 0, 800, 600, -1920, 0, 1920, 1040, false, false);
        Assert.True(saved.IsValid);
        Assert.False((saved with { WorkWidth = 0 }).IsValid);
        Assert.False((saved with { Left = int.MaxValue }).IsValid);
        Assert.False((saved with { Height = -1 }).IsValid);
    }
}
