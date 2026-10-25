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
}