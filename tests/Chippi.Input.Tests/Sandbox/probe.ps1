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

    /// <summary>Waits <paramref name="ms"/> milliseconds on the high-resolution clock, without sleeping (a sleep ticks at 1 ms at best).</summary>
    public static void Spin(double ms)
    {
        long end = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(ms * System.Diagnostics.Stopwatch.Frequency / 1000);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < end) { }
    }

    public static void Click(double gapMs)
    {
        Left(true); Left(false); Spin(gapMs); Left(true); Left(false);
    }

    public static void TapTwice(ushort vk, double gapMs)
    {
        Key(vk, true); Key(vk, false); Spin(gapMs); Key(vk, true); Key(vk, false);
    }

    /// <summary>Installs a mouse hook of its own and logs each event's own timestamp beside a Stopwatch reading taken in the hook.</summary>
    public static string[] HookClock(int events, double gapMs)
    {
        var rows = new System.Collections.Generic.List<string>();
        var ready = new ManualResetEvent(false);
        var done = new ManualResetEvent(false);
        var thread = new Thread(() =>
        {
            LowLevelProc proc = (code, wParam, lParam) =>
            {
                if (code >= 0 && (int)wParam == 0x0200)
                {
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    uint time = (uint)Marshal.ReadInt32(lParam, 8 + 4 + 4);
                    rows.Add(time + "," + (now * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                }
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            };
            IntPtr hook = SetWindowsHookEx(14, proc, IntPtr.Zero, 0);
            ready.Set();
            MSG msg;
            while (!done.WaitOne(0)) { if (PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) { DispatchMessage(ref msg); } else { Thread.Sleep(1); } }
            UnhookWindowsHookEx(hook);
        });
        thread.IsBackground = true;
        thread.Start();
        ready.WaitOne();
        for (int i = 0; i < events; i++) { MoveTo(300 + (i % 2) * 4, 300); Spin(gapMs); }
        Thread.Sleep(300);
        done.Set();
        thread.Join();
        return rows.ToArray();
    }

    delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);

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
    # $app: more "app" settings (the pause's); $radial: more Radial menu settings. Each starts with a comma.
    function StartChippi([string]$app = '', [string]$radial = '', [string]$installed = '"radial-menu"') {
        $settings = '{"version":1,"app":{"islandEnabled":false' + $app + '},' +
            '"modules":{"radial-menu":{"holdMiddle":true,"holdBack":true' + $radial + ',"slices":[' +
            '{"kind":"text","value":"up","name":"Up"},{"kind":"text","value":"right","name":"Right"},' +
            '{"kind":"text","value":"down","name":"Down"},{"kind":"text","value":"left","name":"Left"}]}},' +
            '"features":{"preset":"custom","installed":[' + $installed + '],"capabilitiesUsed":[]}}'
        Set-Content -LiteralPath (Join-Path $profileDir 'settings.json') -Value $settings -Encoding ASCII
        $process = Start-Process -FilePath 'C:\chippi\Chippi.exe' -ArgumentList '--dev-profile sandbox' -PassThru
        Note "Chippi started, pid $($process.Id) (app$app; radial$radial)"
        Start-Sleep -Seconds 25
        [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false)
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "Chippi exited ($($process.ExitCode))" }
        return $process
    }

    $debounceOnly = ($checks -contains 'debounce' -or $checks -contains 'timestamps') -and -not ($checks -contains 'radial' -or $checks -contains 'watchdog' -or $checks -contains 'cost')
    if ($debounceOnly) {
        # Debounce alone, both switches on (clicks 25 ms, keys 20 ms). CHIPPI_DEBOUNCE_INJECTED lets it act on SendInput.
        @'
{"version":1,
 "app":{"islandEnabled":false},
 "modules":{"debounce":{"clickEnabled":true,"clickMs":25,"keyEnabled":true,"keyMs":20}},
 "features":{"preset":"custom","installed":["debounce"],"capabilitiesUsed":[]}}
'@ | Set-Content -LiteralPath (Join-Path $profileDir 'settings.json') -Encoding ASCII
        $env:CHIPPI_DEBOUNCE_INJECTED = '1'
    }
    $env:CHIPPI_INPUT_TIMINGS = "$results\hook-timings.csv"
    if ($checks -contains 'watchdog') { $env:CHIPPI_INPUT_DROP_HOOKS = '45' }
    if ($checks -contains 'pause') { $env:CHIPPI_INPUT_PROBE_FILTER = '1' }
    $chippi = if ($checks -contains 'pause') { StartChippi -radial ',"openOverFullscreen":false' } else { StartChippi }

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

    if ($checks -contains 'pause') {
        # A Debug build's probe filter pauses and keeps F24 (CHIPPI_INPUT_PROBE_FILTER). The "game" is a borderless window
        # covering the screen; target.ps1 logs to fullscreen.log there.
        function F24 { [Probe]::Key(0x87, $true); Start-Sleep -Milliseconds 80; [Probe]::Key(0x87, $false); Start-Sleep -Milliseconds 500 }
        function GameLog { if (Test-Path "$results\fullscreen.log") { @(Get-Content -LiteralPath "$results\fullscreen.log") } else { @() } }
        function StartGame {
            Remove-Item -LiteralPath "$results\fullscreen.log" -ErrorAction SilentlyContinue
            $game = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\target.ps1 -Fullscreen' -PassThru
            Start-Sleep -Seconds 4
            [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false) # the game in front
            Start-Sleep -Milliseconds 800
            Remove-Item -LiteralPath "$results\fullscreen.log" -ErrorAction SilentlyContinue
            return $game
        }
        function EndGame($game) {
            Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false) # the windowed target in front again
            Start-Sleep -Milliseconds 800
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        }
        $pause = [ordered]@{}
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

        # Always on off (the default), Open over fullscreen apps off.
        F24
        $pause.windowedF24 = 'app heard: ' + ((TargetLog) -join '; ')
        $game = StartGame
        F24
        $pause.fullscreenF24 = 'game heard: ' + ((GameLog) -join '; ')
        Remove-Item -LiteralPath "$results\fullscreen.log" -ErrorAction SilentlyContinue
        [Probe]::Middle($true); Start-Sleep -Milliseconds 400; [Probe]::Glide($x, $y, $x, $y - 150, 120); [Probe]::Middle($false)
        Start-Sleep -Milliseconds 1500
        [Probe]::MoveTo($x, $y)
        $pause.radialOffOverGame = 'game heard: ' + ((GameLog) -join '; ')
        EndGame $game
        $pause.radialOffWindowed = Flick 'middle' 0 -150
        # F24 held while the game comes to the front: its up is still kept, so the game never sees an up without a down.
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        [Probe]::Key(0x87, $true)
        Start-Sleep -Milliseconds 300
        $game = StartGame
        [Probe]::Key(0x87, $false)
        Start-Sleep -Milliseconds 500
        $pause.heldAcrossThePause = 'game heard: ' + ((GameLog) -join '; ') + ' | app heard: ' + ((TargetLog) -join '; ')
        EndGame $game
        Stop-Process -Id $chippi.Id -Force
        Start-Sleep -Seconds 2

        # Always on.
        $chippi = StartChippi -app ',"inputAlwaysOn":true'
        $game = StartGame
        F24
        $pause.alwaysOnFullscreenF24 = 'game heard: ' + ((GameLog) -join '; ')
        EndGame $game
        Stop-Process -Id $chippi.Id -Force
        Start-Sleep -Seconds 2

        # Pause in these apps: the windowed target (powershell.exe) counts as fullscreen.
        $chippi = StartChippi -app ',"pauseApps":["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"]'
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        F24
        $pause.listedWindowedF24 = 'app heard: ' + ((TargetLog) -join '; ')
        Stop-Process -Id $chippi.Id -Force
        Start-Sleep -Seconds 2

        # No Radial menu: only the probe filter needs the keyboard, so its hook comes off for the game and back after it
        # (the hub's log says so).
        $chippi = StartChippi -installed ''
        $game = StartGame
        F24
        $pause.noRadialFullscreenF24 = 'game heard: ' + ((GameLog) -join '; ')
        EndGame $game
        F24
        $pause.noRadialWindowedF24 = 'app heard: ' + ((TargetLog) -join '; ')
        $found.pause = $pause
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

    if ($checks -contains 'timestamps') {
        # The hook's own timestamp beside the Stopwatch, for 400 moves 2 ms apart: how often each ticks.
        $rows = [Probe]::HookClock(400, 2.0)
        Set-Content -LiteralPath "$results\hook-clock.csv" -Value (@('hookMs,stopwatchMs') + $rows) -Encoding ASCII
        $hook = $rows | ForEach-Object { [double]($_ -split ',')[0] }
        $watch = $rows | ForEach-Object { [double]($_ -split ',')[1] }
        $hookSteps = for ($i = 1; $i -lt $hook.Count; $i++) { $hook[$i] - $hook[$i - 1] }
        $watchSteps = for ($i = 1; $i -lt $watch.Count; $i++) { $watch[$i] - $watch[$i - 1] }
        $distinct = ($hookSteps | Where-Object { $_ -gt 0 } | Sort-Object -Unique | Select-Object -First 6) -join ' '
        $found.timestamps = [ordered]@{
            events = $rows.Count
            hookStepsZeroOrNot = "$(@($hookSteps | Where-Object { $_ -eq 0 }).Count) of $($hookSteps.Count) steps are 0 ms"
            hookSmallestNonZeroStepsMs = $distinct
            hookMaxStepMs = ($hookSteps | Measure-Object -Maximum).Maximum
            stopwatchMedianStepMs = [math]::Round((($watchSteps | Sort-Object)[[int]($watchSteps.Count / 2)]), 3)
        }
    }

    if ($checks -contains 'debounce') {
        $d = [ordered]@{}
        function Run([string]$name, [scriptblock]$act) {
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
            $before = (Text).Length
            & $act
            Start-Sleep -Milliseconds 700
            $typed = (Text).Substring($before)
            $d[$name] = [ordered]@{ target = ((TargetLog) -join '; '); typed = $typed }
            Note "debounce $name : $($d[$name].target) typed '$typed'"
            Start-Sleep -Milliseconds 300
        }
        [Probe]::MoveTo($x, $y)
        Run 'click2msGap' { [Probe]::Click(2) }
        Run 'click60msGap' { [Probe]::Click(60) }
        Run 'clickHeldThenSecond' { [Probe]::Left($true); [Probe]::Spin(300); [Probe]::Left($false); [Probe]::Spin(100); [Probe]::Left($true); [Probe]::Left($false) }
        Run 'key2msGap' { [Probe]::TapTwice(0x45, 2) }
        Run 'key60msGap' { [Probe]::TapTwice(0x45, 60) }
        Run 'otherKeysFastStillBoth' { [Probe]::Key(0x41, $true); [Probe]::Key(0x41, $false); [Probe]::Spin(2); [Probe]::Key(0x42, $true); [Probe]::Key(0x42, $false) }
        Run 'keyHeldRepeats' { [Probe]::Key(0x45, $true); [Probe]::Spin(300); [Probe]::Key(0x45, $true); [Probe]::Spin(33); [Probe]::Key(0x45, $true); [Probe]::Key(0x45, $false) }
        Run 'fastTypingBurst' { for ($i = 0; $i -lt 20; $i++) { [Probe]::TapTwice(0x46, 40) } }
        [Probe]::Circle($x, $y, 150, 8000) | Out-Null
        # Enough key events for a p99 that isn't the first call's JIT: 4,000 (hook-timings.csv, keyboard row).
        Run 'keyBurst4000Events' { for ($i = 0; $i -lt 2000; $i++) { [Probe]::Key([uint16](0x41 + $i % 26), $true); [Probe]::Key([uint16](0x41 + $i % 26), $false); [Probe]::Spin(6) } }
        $d.keyBurst4000Events.target = "$((($d.keyBurst4000Events.typed).Length)) letters typed of 2000"
        $d.keyBurst4000Events.typed = ''
        $found.debounce = $d
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
