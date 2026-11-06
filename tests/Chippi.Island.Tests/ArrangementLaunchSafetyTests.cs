// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Actions;
using Chippi.UI.Actions;

namespace Chippi.Island.Tests;

public sealed class ArrangementLaunchSafetyTests
{
    [Theory]
    [InlineData(0u, false, false)]
    [InlineData(1u, true, true)]
    [InlineData(2u, true, false)]
    [InlineData(3u, true, true)]
    [InlineData(4u, true, true)]
    public void App_hidden_windows_stay_excluded_while_other_desktops_remain_reportable(uint flags, bool switcherExcludes, bool captureExcludes)
    {
        Assert.Equal(switcherExcludes, WindowsWindowList.CloakingExcludes(flags, false));
        Assert.Equal(captureExcludes, WindowsWindowList.CloakingExcludes(flags, true));
    }
    [Fact]
    public void Capture_without_process_identity_never_selects_a_shortcuts_arguments()
    {
        RegisteredAppTargets.Target[] targets = [new("skin", "vlc.exe", "--skin", ""), new("reset", "vlc.exe", "--reset-config", "")];
        Assert.Null(Chippi.App.Shell.ArrangementDesktop.RegisteredTarget(null, targets));
        Assert.Same(targets[1], Chippi.App.Shell.ArrangementDesktop.RegisteredTarget("reset", targets));
        Assert.Null(Chippi.App.Shell.ArrangementDesktop.RegisteredTarget("missing", targets));
    }
    [Theory]
    [InlineData(0u, false)]
    [InlineData(0x2000u, true)]
    public void Shortcut_run_as_admin_flag_is_read_before_launch(uint flags, bool expected)
    {
        string path = Path.GetTempFileName();
        try
        {
            var header = new byte[24];
            BitConverter.GetBytes(0x4cu).CopyTo(header, 0);
            new Guid("00021401-0000-0000-C000-000000000046").ToByteArray().CopyTo(header, 4);
            BitConverter.GetBytes(flags).CopyTo(header, 20);
            File.WriteAllBytes(path, header);
            Assert.Equal(expected, RegisteredAppTargets.ShortcutNeedsAdmin(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("asInvoker", false)]
    [InlineData("requireAdministrator", true)]
    [InlineData("highestAvailable", true)]
    public void Manifest_elevation_is_refused_before_process_creation(string level, bool expected)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            $"<assembly><trustInfo><requestedExecutionLevel level='{level}' /></trustInfo></assembly>"));
        Assert.Equal(expected, ExecutableLaunchPolicy.ManifestNeedsAdmin(stream));
    }

    [Fact]
    public void Relative_executable_is_refused_without_shell_fallback()
    {
        using var pc = new WindowsActionPc();
        Assert.Equal("Invalid launch details", Assert.Throws<ActionFailure>(() => pc.LaunchExecutable(new("cmd.exe"))).Message);
    }
}
