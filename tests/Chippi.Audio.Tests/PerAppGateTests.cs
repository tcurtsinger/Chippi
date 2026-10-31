// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Audio.Tests;
public sealed class PerAppGateTests
{
    [Theory]
    [InlineData(22000,true)]
    [InlineData(26100,true)]
    [InlineData(26300,true)]
    [InlineData(27000,true)]
    [InlineData(19045,false)]
    public void Every_windows_11_build_is_allowed(int build, bool allowed) =>
        Assert.Equal(allowed,PerAppDevicePolicy.IsSupportedBuild(new Version(10,0,build)));

    [Theory]
    [InlineData(26100,null)]
    [InlineData(19045,"Needs Windows 11")]
    public void Settings_says_why_a_pc_has_no_per_app_output(int build, string? reason) =>
        Assert.Equal(reason,PerAppDevicePolicy.Unsupported(new Version(10,0,build)));

    // Each write is preceded by a read that works: that read must not hide writes failing for every app.
    [Fact]
    public void Writes_failing_for_three_processes_turn_it_off_whatever_the_reads_do()
    {
        var reads = new FailedProcesses();
        var writes = new FailedProcesses();
        foreach (var pid in new[] { 10, 11 })
        {
            reads.Succeeded();
            Assert.False(writes.Failed(pid));
        }
        reads.Succeeded();
        Assert.True(writes.Failed(12));
    }

    // One app that won't take an output is that app's failure: trying it again doesn't add up.
    [Fact]
    public void The_same_process_failing_again_and_a_success_start_nothing_new()
    {
        var writes = new FailedProcesses();
        Assert.False(writes.Failed(10));
        Assert.False(writes.Failed(10));
        Assert.False(writes.Failed(11));
        writes.Succeeded();
        Assert.False(writes.Failed(12));
        Assert.False(writes.Failed(13));
    }
}
