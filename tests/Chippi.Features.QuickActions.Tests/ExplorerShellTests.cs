// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Features.QuickActions.Windows;

namespace Chippi.Features.QuickActions.Tests;

public sealed class ExplorerShellTests
{
    // A shell that inherited Chippi's working folder kept the install folder open until the next sign-in, and Setup
    // failed to replace it (2026-11-17).
    [Fact]
    public void Restarted_shell_starts_in_System32_not_Chippis_folder()
    {
        var start = ExplorerShell.StartInfo();

        Assert.Equal(Environment.SystemDirectory, start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), start.FileName);
    }
}
