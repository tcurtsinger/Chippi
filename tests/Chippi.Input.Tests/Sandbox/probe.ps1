# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Runs inside Windows Sandbox, started by run.ps1. Drives the real hooks with SendInput, without Chippi's marker, so
# Chippi takes it for a hand's; aims it at target.ps1's window, and writes what happened to C:\results.

$ErrorActionPreference = 'Stop'
$results = 'C:\results'
$checks = (Get-Content -LiteralPath "$results\checks.txt") -split ','
$found = [ordered]@{ checks = $checks }

function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value ('{0:HH:mm:ss.fff} {1}' -f (Get-Date), $text) }
function Text { if (Test-Path "$results\target-text.txt") { Get-Content -LiteralPath "$results\target-text.txt" -Raw } else { '' } }
function TargetLog { if (Test-Path "$results\target.log") { @(Get-Content -LiteralPath "$results\target.log") } else { @() } }

try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class Probe
{
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }

    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

    static void Send(Input input) { SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))); }

    static void Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0)
    {
        var input = new Input { Type = 0 };
        input.Data.Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = flags, Data = data };
        Send(input);
    }

    public static void MoveTo(int x, int y)
    {
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
        Mouse(0x8001, (x * 65535 + width / 2) / (width - 1), (y * 65535 + height / 2) / (height - 1));
    }

    public static void Left(bool down) { Mouse(down ? 0x0002u : 0x0004u); }
    public static void Middle(bool down) { Mouse(down ? 0x0020u : 0x0040u); }
    public static void Back(bool down) { Mouse(down ? 0x0080u : 0x0100u, 0, 0, 1); }

    public static void Key(ushort vk, bool down)
    {
        var input = new Input { Type = 1 };
        input.Data.Key = new KeyInput { Vk = vk, Flags = down ? 0u : 2u };
        Send(input);
    }

    /// <summary>From one point to another over about <paramref name="ms"/> milliseconds, a move a millisecond, as a hand would.</summary>
    public static void Glide(int x0, int y0, int x1, int y1, int ms)
    {
        timeBeginPeriod(1);
        for (int t = 1; t <= ms; t++) { MoveTo(x0 + (x1 - x0) * t / ms, y0 + (y1 - y0) * t / ms); Thread.Sleep(1); }
    }

    /// <summary>Circles for <paramref name="ms"/> milliseconds at up to a thousand moves a second; returns the moves sent.</summary>
    public static int Circle(int x, int y, int radius, int ms)
    {
        timeBeginPeriod(1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int moves = 0;
        while (clock.ElapsedMilliseconds < ms)
        {
            double angle = moves * 0.01;
            MoveTo(x + (int)(radius * Math.Cos(angle)), y + (int)(radius * Math.Sin(angle)));
            moves++;
            Thread.Sleep(1);
        }
        return moves;
    }
}
'@

    # The app the input is aimed at, and its text box in front.
    Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\target.ps1'
    Start-Sleep -Seconds 5
    $x = 500; $y = 400
    [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false)
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

    # A dev profile with only the Radial menu: Middle and Back open it, and its four slices type which way was flicked.
    $profileDir = Join-Path $env:APPDATA 'Chippi-dev\sandbox'
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
    @'
{"version":1,
 "app":{"islandEnabled":false},
 "modules":{"radial-menu":{"holdMiddle":true,"holdBack":true,"slices":[
   {"kind":"text","value":"up","name":"Up"},{"kind":"text","value":"right","name":"Right"},
   {"kind":"text","value":"down","name":"Down"},{"kind":"text","value":"left","name":"Left"}]}},
 "features":{"preset":"custom","installed":["radial-menu"],"capabilitiesUsed":[]}}
'@ | Set-Content -LiteralPath (Join-Path $profileDir 'settings.json') -Encoding ASCII

    $env:CHIPPI_INPUT_TIMINGS = "$results\hook-timings.csv"
    if ($checks -contains 'watchdog') { $env:CHIPPI_INPUT_DROP_HOOKS = '45' }
    $chippi = Start-Process -FilePath 'C:\chippi\Chippi.exe' -ArgumentList '--dev-profile sandbox' -PassThru
    Note "Chippi started, pid $($chippi.Id)"
    Start-Sleep -Seconds 25
    [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false)
    Start-Sleep -Milliseconds 500
    $chippi.Refresh()
    if ($chippi.HasExited) { throw "Chippi exited ($($chippi.ExitCode))" }

    function Flick([string]$button, [int]$dx, [int]$dy) {
        $before = Text
        if ($button -eq 'middle') { [Probe]::Middle($true) } else { [Probe]::Back($true) }
        Start-Sleep -Milliseconds 400
        [Probe]::Glide($x, $y, $x + $dx, $y + $dy, 120)
        Start-Sleep -Milliseconds 150
        if ($button -eq 'middle') { [Probe]::Middle($false) } else { [Probe]::Back($false) }
        Start-Sleep -Milliseconds 1500
        [Probe]::MoveTo($x, $y)
        Start-Sleep -Milliseconds 300
        $typed = (Text).Substring($before.Length)
        Note "flick $button ($dx,$dy): typed '$typed'"
        return $typed
    }

    if ($checks -contains 'radial' -or $checks -contains 'watchdog') {
        $radial = [ordered]@{}
        # A quick click goes to the app, down and up.
        [Probe]::Middle($true); Start-Sleep -Milliseconds 60; [Probe]::Middle($false)
        Start-Sleep -Milliseconds 800
        $radial.middleClick = (TargetLog) -join '; '
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

        # Held, flicked and let go: the slice it points at runs.
        $radial.middleHoldFlickUp = Flick 'middle' 0 -150
        $radial.backHoldFlickRight = Flick 'back' 150 0
        $radial.wheelInputReachedApp = (TargetLog) -join '; '
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

        if ($checks -contains 'radial') {
            # Moved before the hold's 250 ms: the app's own drag, its down replayed before its up.
            [Probe]::Middle($true); [Probe]::Glide($x, $y, $x + 60, $y, 60); [Probe]::Middle($false)
            Start-Sleep -Milliseconds 800
            [Probe]::MoveTo($x, $y)
            $radial.middleDrag = (TargetLog) -join '; '
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

            # Esc while it's open closes it, and neither Esc nor the button reaches the app.
            $before = Text
            [Probe]::Middle($true); Start-Sleep -Milliseconds 400
            [Probe]::Key(0x1B, $true); [Probe]::Key(0x1B, $false)
            Start-Sleep -Milliseconds 300
            [Probe]::Glide($x, $y, $x, $y - 150, 120); [Probe]::Middle($false)
            Start-Sleep -Milliseconds 1500
            [Probe]::MoveTo($x, $y)
            $radial.escDismiss = 'typed ''' + (Text).Substring($before.Length) + '''; app heard: ' + ((TargetLog) -join '; ')
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        }
        $found.radial = $radial
    }

    if ($checks -contains 'watchdog') {
        # Both hooks are dropped 45 s after start; the pointer and keys move for 10 s, then the wheel must work again.
        $wait = $chippi.StartTime.AddSeconds(47) - (Get-Date)
        if ($wait.TotalMilliseconds -gt 0) { Start-Sleep -Milliseconds ([int]$wait.TotalMilliseconds) }
        Note 'hooks dropped; moving the mouse and pressing Shift'
        for ($i = 0; $i -lt 10; $i++) {
            [Probe]::Circle($x, $y, 100, 900) | Out-Null
            [Probe]::Key(0x10, $true); [Probe]::Key(0x10, $false)
        }
        [Probe]::MoveTo($x, $y)
        Start-Sleep -Milliseconds 300
        $found.watchdog = [ordered]@{ afterDropFlickUp = (Flick 'middle' 0 -150) }
    }

    if ($checks -contains 'cost') {
        $cost = @()
        for ($run = 1; $run -le 3; $run++) {
            $chippi.Refresh()
            $before = $chippi.TotalProcessorTime.TotalMilliseconds
            $moves = [Probe]::Circle($x, $y, 150, 60000)
            $chippi.Refresh()
            $cpu = $chippi.TotalProcessorTime.TotalMilliseconds - $before
            Note "cost run ${run}: $moves moves, $cpu ms CPU"
            $cost += [ordered]@{ run = $run; moves = $moves; cpuMs = [math]::Round($cpu, 1) }
            Start-Sleep -Seconds 5
        }
        $found.cost = $cost
    }

    # The hub writes its timings every 10 s while events come.
    Start-Sleep -Seconds 12
    $logs = Join-Path $env:LOCALAPPDATA 'Chippi-dev\sandbox\logs'
    if (Test-Path $logs) { Copy-Item -Path $logs -Destination "$results\chippi-logs" -Recurse }
    $found.hubLog = @(Get-ChildItem "$results\chippi-logs" -Filter *.log -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName } | Where-Object { $_ -match 'Input hub' } | ForEach-Object { [string]$_ })
    Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue
}
catch {
    $found.error = $_.ToString()
    Note "failed: $_"
}
finally {
    $found | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$results\results.json" -Encoding UTF8
    Set-Content -LiteralPath "$results\done.txt" -Value 'done'
    shutdown.exe /s /t 5
}
