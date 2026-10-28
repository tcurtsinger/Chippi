// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Diagnostics;

namespace Chippi.SoundSmoke;

public sealed class EarlyLogTests
{
    [Fact]
    public void Notification_cleanup_logs_use_the_dev_profile_before_Wpf_startup()
    {
        var previous = Log.Default;
        try
        {
            Chippi.App.Program.ConfigureLog(["--cleanup-dev-notifications", "--dev-profile", Chippi.SmokeTests.SmokeLog.Profile + "-child"]);
            Assert.Equal(Path.Combine(Chippi.SmokeTests.SmokeLog.Root + "-child", "logs"), Log.Default.Directory);
            Chippi.SmokeTests.SmokeLog.Check();
        }
        finally { Log.Default = previous; }
    }
}
