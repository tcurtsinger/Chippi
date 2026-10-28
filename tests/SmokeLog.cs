// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Runtime.CompilerServices;
using Chippi.Core.Diagnostics;

namespace Chippi.SmokeTests;

/// <summary>The testhost does not run App.OnStartup. Set its static log before any test can write.</summary>
internal static class SmokeLog
{
    internal static readonly string Profile = "smoke-log-" + Guid.NewGuid().ToString("N");
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", Profile);

    [ModuleInitializer]
    internal static void Initialize()
    {
        Log.Default = new RollingFileLog(Path.Combine(Root, "logs"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (Directory.Exists(Root)) Directory.Delete(Root, true); };
    }

    internal static void AssertOnlyHere(string marker)
    {
        Check();
        Assert.Contains(marker, File.ReadAllText(Log.Default.CurrentFile));
        if (Directory.Exists(RollingFileLog.DefaultDirectory))
            foreach (string file in Directory.EnumerateFiles(RollingFileLog.DefaultDirectory, "*.log"))
            {
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    Assert.DoesNotContain(marker, reader.ReadToEnd());
                }
                catch (FileNotFoundException) { } // Installed Chippi may rotate between enumeration and open.
            }
    }

    internal static void Check()
    {
        var actual = Path.GetFullPath(Log.Default.Directory);
        var production = Path.GetFullPath(RollingFileLog.DefaultDirectory);
        Assert.False(actual.Equals(production, StringComparison.OrdinalIgnoreCase)
            || actual.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Test log points at the production logs");
    }
}

public sealed class SmokeLogTests
{
    [Fact]
    public void Static_logs_stay_in_the_test_profile()
    {
        SmokeLog.Check(); // fail before a regression could write the real log
        string marker = "smoke-log-probe-" + Guid.NewGuid().ToString("N");
        Log.Info(marker);
        SmokeLog.AssertOnlyHere(marker);
    }
}
