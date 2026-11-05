# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The `scroll` check (Smooth scrolling), loaded by probe.ps1 inside Windows Sandbox. scroll-target.ps1 is the window that logs
# every wheel message it gets; the probe sends wheel input with SendInput
# and no Chippi marker, so Chippi takes it for a hand's, and reads what arrived. Times are Stopwatch milliseconds on the
# system-wide high-resolution clock, so the probe and the target share one timeline.

Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class Wheel
{
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

    public static double Now() { return Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency; }

    public static double Send(int delta, bool horizontal)
    {
        var input = new Input { Type = 0 };
        input.Data.Mouse = new MouseInput { Flags = horizontal ? 0x1000u : 0x0800u, Data = unchecked((uint)delta) };
        double t = Now();
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
        return t;
    }

    public static void Key(ushort vk, bool down)
    {
        var input = new Input { Type = 1 };
        input.Data.Key = new KeyInput { Vk = vk, Flags = down ? 0u : 2u };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
    }

    public static void Move(int x, int y)
    {
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
        var input = new Input { Type = 0 };
        input.Data.Mouse = new MouseInput { Dx = (x * 65535 + width / 2) / (width - 1), Dy = (y * 65535 + height / 2) / (height - 1), Flags = 0x8001 };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
    }

    public static void Spin(double ms) { double end = Now() + ms; while (Now() < end) { } }

    /// <summary>count notches, gapMs apart, on the high-resolution clock; returns the time the first went out.</summary>
    public static double Notches(int count, double gapMs, int delta)
    {
        timeBeginPeriod(1);
        double first = 0;
        for (int i = 0; i < count; i++) { double t = Send(delta, false); if (i == 0) first = t; if (i < count - 1) Spin(gapMs); }
        return first;
    }

    public static bool Foreground(string title) { var h = FindWindow(null, title); return h != IntPtr.Zero && SetForegroundWindow(h); }
}
'@

# One measurement: runs $act (it returns the time the first wheel message was sent), waits for the glide to settle, and
# summarises the messages the target got since.
function Measure-Scroll([string]$name, [scriptblock]$act, [int]$settleMs = 1500) {
    $before = if (Test-Path "$results\scroll.csv") { @(Get-Content -LiteralPath "$results\scroll.csv").Count } else { 0 }
    $start = & $act
    Start-Sleep -Milliseconds $settleMs
    $rows = @()
    if (Test-Path "$results\scroll.csv") { $rows = @(Get-Content -LiteralPath "$results\scroll.csv" | Select-Object -Skip $before) }
    $parsed = @($rows | ForEach-Object { $p = $_ -split ','; [pscustomobject]@{ t = [double]$p[0]; axis = $p[1]; delta = [int]$p[2]; line = [int]$p[3] } })
    $r = [ordered]@{ messages = $parsed.Count; totalDelta = [int](($parsed | Measure-Object delta -Sum).Sum) }
    if ($parsed.Count -gt 0) {
        $r.firstResponseMs = [math]::Round($parsed[0].t - $start, 1)
        $r.settleMs = [math]::Round($parsed[-1].t - $start, 1)
        $r.largestMessage = ($parsed | ForEach-Object { [math]::Abs($_.delta) } | Measure-Object -Maximum).Maximum
        $r.lastLine = $parsed[-1].line
        $r.horizontal = @($parsed | Where-Object { $_.axis -eq 'h' }).Count
    }
    Note "scroll $name : $($r | ConvertTo-Json -Compress)"
    return $r
}
