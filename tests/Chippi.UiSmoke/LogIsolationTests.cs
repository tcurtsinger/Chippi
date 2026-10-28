// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
namespace Chippi.UiSmoke;

public sealed class LogIsolationTests
{
    [Fact]
    public void Every_child_launch_has_an_explicit_dev_profile()
    {
        var info = DesktopSession.StartInfo(Path.Combine(Path.GetTempPath(), "Chippi.exe"), "log-regression");
        Assert.Equal("log-regression", info.Environment["CHIPPI_DEV_PROFILE"]);
        Assert.False(info.UseShellExecute);
        Assert.Throws<ArgumentException>(() => DesktopSession.StartInfo(info.FileName, ""));
    }
}
