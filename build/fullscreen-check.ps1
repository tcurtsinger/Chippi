# SPDX-License-Identifier: GPL-3.0-or-later
# Prints the foreground window's process and whether it covers its whole monitor. Read-only.
# Run it before anything that shows a window or sends input: wait while a game or Zoom is in front.
# Exits 1 when the foreground is fullscreen or Zoom, so scripts can gate on the exit code.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Fg {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][Fg]::SetProcessDPIAware()
$h = [Fg]::GetForegroundWindow()
$r = New-Object Fg+RECT; [void][Fg]::GetWindowRect($h, [ref]$r)
$procId = 0; [void][Fg]::GetWindowThreadProcessId($h, [ref]$procId)
$mi = New-Object Fg+MONITORINFO; $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
[void][Fg]::GetMonitorInfo([Fg]::MonitorFromWindow($h, 2), [ref]$mi)
$m = $mi.rcMonitor
$full = $r.L -le $m.L -and $r.T -le $m.T -and $r.R -ge $m.R -and $r.B -ge $m.B
$name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
"foreground=$name pid=$procId fullscreen=$full primary=$(($mi.dwFlags -band 1) -eq 1)"
if ($full -or $name -like 'Zoom*') { exit 1 }
