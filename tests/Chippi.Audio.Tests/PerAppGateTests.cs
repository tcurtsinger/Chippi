// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Runtime.InteropServices;
namespace Chippi.Audio.Tests;
public sealed class PerAppGateTests
{
    [Theory]
    [InlineData(26300,Architecture.X64,true)]
    [InlineData(26300,Architecture.Arm64,false)]
    [InlineData(26200,Architecture.X64,false)]
    [InlineData(26301,Architecture.X64,false)]
    public void Only_the_verified_native_x64_build_is_allowed(int build, Architecture architecture, bool allowed) =>
        Assert.Equal(allowed,PerAppDevicePolicy.IsVerifiedBuild(new Version(10,0,build),architecture));

    [Theory]
    [InlineData(26300,Architecture.X64,Architecture.X64,null)]
    [InlineData(26100,Architecture.X64,Architecture.X64,"Needs Windows build 26300")]
    [InlineData(26400,Architecture.X64,Architecture.X64,"Not checked on Windows build 26400 yet")]
    [InlineData(26300,Architecture.Arm64,Architecture.Arm64,"Not available on Arm PCs")]
    [InlineData(26300,Architecture.Arm64,Architecture.X64,"Not available on Arm PCs")]
    public void Settings_says_why_a_pc_has_no_per_app_output(int build, Architecture os, Architecture process, string? reason) =>
        Assert.Equal(reason,PerAppDevicePolicy.Unsupported(new Version(10,0,build),os,process));
}
