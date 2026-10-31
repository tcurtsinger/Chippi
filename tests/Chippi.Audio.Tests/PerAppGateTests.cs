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
}
