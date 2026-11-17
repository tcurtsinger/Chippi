// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class RestartManagerLayoutTests
{
    // RM_UNIQUE_PROCESS is a DWORD and a FILETIME (12 bytes); RM_PROCESS_INFO is 668 bytes. RmGetList fills an array at that
    // stride, so a wider managed struct reads every holder after the first as garbage.
    [Fact]
    public void The_Restart_Manager_structs_match_the_native_sizes()
    {
        Assert.Equal(12, Marshal.SizeOf<WindowsProcesses.RmUniqueProcess>());
        Assert.Equal(668, Marshal.SizeOf<WindowsProcesses.RmProcessInfo>());
    }
}
