# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The capture layer's check (docs/phase-7/capture-layer.md, Tests), run by probe.ps1 inside the Sandbox with [Probe]
# loaded. A Debug Chippi with CHIPPI_CAPTURE_PROBE gives each mode a shortcut (Ctrl+Alt+1 Region ... 7 Pick a point,
# 8 Region after 3 s, 9 Region as a swallowed press, Print Screen Region) and logs what the layer did. Chippi runs in
# the Sandbox's own normal profile, so Windows' Print Screen setting there is the real one. Returns what it found.

$ErrorActionPreference = 'Stop'
$results = 'C:\results'
$log = "$results\capture-probe.log"
$found = [ordered]@{}
function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value ('{0:HH:mm:ss.fff} {1}' -f (Get-Date), $text) }
function Events { if (Test-Path $log) { @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() } }
function Setting { $v = Get-ItemProperty -Path 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -ErrorAction SilentlyContinue; if ($v) { [string]$v.PrintScreenKeyForSnippingEnabled } else { 'unset' } }
# Waits for the next event named $name after $from events; returns it, or $null after $ms.
function WaitEvent([string]$name, [int]$from, [int]$ms = 4000) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $ms) {
        $e = Events | Select-Object -Skip $from | Where-Object { $_.event -eq $name } | Select-Object -First 1
        if ($e) { return $e }
        Start-Sleep -Milliseconds 50
    }
    return $null
}
function Tap([uint16]$vk) { [Probe]::Key($vk, $true); [Probe]::Spin(25); [Probe]::Key($vk, $false); [Probe]::Spin(25) }
function PrintScreen { [Probe]::ExtendedKey(0x2C, 0x37, $true); [Probe]::Spin(25); [Probe]::ExtendedKey(0x2C, 0x37, $false); [Probe]::Spin(25) }
function Chord([uint16[]]$keys) { foreach ($k in $keys) { [Probe]::Key($k, $true); [Probe]::Spin(15) }; [array]::Reverse($keys); foreach ($k in $keys) { [Probe]::Key($k, $false); [Probe]::Spin(15) } }
function Starter([int]$digit) { Chord @(0xA2, 0xA4, [uint16](0x30 + $digit)) }   # Ctrl+Alt+digit
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [switch]$Ctrl) {
    [Probe]::MoveTo($x1, $y1); Start-Sleep -Milliseconds 120
    if ($Ctrl) { [Probe]::Key(0xA2, $true) }
    [Probe]::Left($true); Start-Sleep -Milliseconds 60
    [Probe]::Glide($x1, $y1, $x2, $y2, 250); Start-Sleep -Milliseconds 60
    [Probe]::Left($false)
    if ($Ctrl) { [Probe]::Key(0xA2, $false) }
}
function Click([int]$x, [int]$y) { [Probe]::MoveTo($x, $y); Start-Sleep -Milliseconds 120; [Probe]::Left($true); [Probe]::Spin(40); [Probe]::Left($false) }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
function Shot([string]$name) {
    $v = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $b = New-Object System.Drawing.Bitmap $v.Width, $v.Height
    $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($v.X, $v.Y, 0, 0, $b.Size); $g.Dispose()
    $b.Save("$results\capture-$name.png"); $b.Dispose()
}
# One scenario: $start opens the overlay, $act runs on it; returns the events it logged and where the keyboard ended up.
function Scenario([string]$name, [scriptblock]$start, [scriptblock]$act, [string]$shot = '') {
    $from = @(Events).Count
    Click 700 520   # the fixture in front first, so the hand-back has somewhere to go
    Start-Sleep -Milliseconds 300
    & $start
    $frozen = WaitEvent 'frozen' $from 6000
    Start-Sleep -Milliseconds 200
    & $act
    if ($shot) { Start-Sleep -Milliseconds 300; Shot $shot }
    Start-Sleep -Milliseconds 200
    $after = WaitEvent 'after' $from 3000
    if (-not $after) { Tap 0x1B; Tap 0x1B; $after = WaitEvent 'after' $from 3000 }   # leave nothing open for the next one
    $events = @(Events | Select-Object -Skip $from)
    $r = [ordered]@{ frozen = [bool]$frozen; events = @($events | ForEach-Object { $_ | ConvertTo-Json -Compress }) }
    Note "capture $name : $($r | ConvertTo-Json -Compress -Depth 4)"
    Start-Sleep -Milliseconds 300
    return $r
}

$found.settingBefore = Setting
$fixture = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\capture-fixture.ps1' -PassThru
Start-Sleep -Seconds 5

# Only the shell: no feature installed, the island off.
$settingsDir = Join-Path $env:APPDATA 'Chippi'
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
Set-Content -LiteralPath (Join-Path $settingsDir 'settings.json') -Encoding ASCII -Value '{"version":1,"app":{"islandEnabled":false},"modules":{},"features":{"preset":"custom","installed":[],"capabilitiesUsed":[]}}'
$env:CHIPPI_CAPTURE_PROBE = $log
$env:CHIPPI_CAPTURE_TIMINGS = "$results\capture-timings.txt"
$env:CHIPPI_CAPTURE_PROBE_POINTS = '300,300;650,300;100,100'
$chippi = Start-Process -FilePath 'C:\chippi\Chippi.exe' -PassThru
Start-Sleep -Seconds 20
$chippi.Refresh()
if ($chippi.HasExited) { throw "Chippi exited ($($chippi.ExitCode))" }
$found.ready = @(Events | Where-Object { $_.event -in 'ready', 'refused' } | ForEach-Object { $_ | ConvertTo-Json -Compress })
$found.settingWhileHeld = Setting   # 0: Chippi turned Windows' Print Screen off

$s = [ordered]@{}
# Region: a drag over both halves of the fixture, then Enter; the frozen pixels match the fixture's.
$s.regionDrag = Scenario 'regionDrag' { Starter 1 } { Drag 260 250 700 450; Start-Sleep -Milliseconds 400; Tap 0x0D } 'region-released'
# Region: a click takes the window under it.
$s.regionClick = Scenario 'regionClick' { Starter 1 } { Click 500 400; Start-Sleep -Milliseconds 300; Tap 0x0D }
# Esc while choosing cancels; the fixture gets the keyboard back.
$s.esc = Scenario 'esc' { Starter 1 } { Tap 0x1B }
# Ctrl at the drag's start: the fast path.
$s.ctrlDrag = Scenario 'ctrlDrag' { Starter 1 } { Drag 300 300 600 400 -Ctrl; Start-Sleep -Milliseconds 300; Tap 0x1B }
# After release: Right moves a pixel, Shift+Down grows a pixel, Ctrl+A takes the display.
$s.keys = Scenario 'keys' { Starter 1 } { Drag 300 300 500 400; Start-Sleep -Milliseconds 300; Tap 0x27; Chord @(0xA0, 0x28); Chord @(0xA2, 0x41); Start-Sleep -Milliseconds 300; Tap 0x0D } 'ctrl-a'
# Window mode: a drag does nothing, a click takes the window.
$s.window = Scenario 'window' { Starter 2 } { Drag 300 300 600 400; Start-Sleep -Milliseconds 300; Click 500 400; Start-Sleep -Milliseconds 300; Tap 0x0D }
$s.screen = Scenario 'screen' { Starter 3 } { Start-Sleep -Milliseconds 300; Tap 0x0D } 'screen'
$s.allDisplays = Scenario 'allDisplays' { Starter 4 } { Start-Sleep -Milliseconds 300; Tap 0x0D }
$s.freeform = Scenario 'freeform' { Starter 5 } {
    [Probe]::MoveTo(400, 300); Start-Sleep -Milliseconds 100; [Probe]::Left($true)
    $px = 400; $py = 300
    foreach ($p in @(@(600, 300), @(650, 450), @(450, 500), @(350, 400), @(400, 300))) { [Probe]::Glide($px, $py, $p[0], $p[1], 80); $px = $p[0]; $py = $p[1] }
    [Probe]::Left($false); Start-Sleep -Milliseconds 300; Tap 0x0D } 'freeform'
$s.lastRegion = Scenario 'lastRegion' { Starter 6 } { Start-Sleep -Milliseconds 300; Tap 0x0D }
$s.pick = Scenario 'pick' { Starter 7 } { [Probe]::MoveTo(300, 300); Start-Sleep -Milliseconds 200; Tap 0x27; Click 300 300 } 'pick'
$s.pickRightCancel = Scenario 'pickRightCancel' { Starter 7 } { [Probe]::MoveTo(300, 300); Start-Sleep -Milliseconds 200; [Probe]::Right($true); [Probe]::Right($false) }
$s.swallowed = Scenario 'swallowed' { Starter 9 } { Start-Sleep -Milliseconds 300; Tap 0x1B }
# The delay: counts 3 s with the fixture keeping the keyboard, then freezes; and cancelled by the same starter again.
$s.delay = Scenario 'delay' { Starter 8; Start-Sleep -Milliseconds 1200; $s.delayForegroundDuringCount = [Probe]::Foreground() } { Start-Sleep -Milliseconds 300; Tap 0x1B }
$s.delayCancel = Scenario 'delayCancel' { Starter 8; Start-Sleep -Milliseconds 1000; Starter 8; Start-Sleep -Milliseconds 3500 } { }
# Print Screen with another app in front, Windows' setting off by Chippi.
$s.printScreen = Scenario 'printScreen' { PrintScreen } { Start-Sleep -Milliseconds 300; Tap 0x1B }
$s.clipperAfterPrintScreen = @(Get-Process -Name ScreenClippingHost, SnippingTool -ErrorAction SilentlyContinue).Count

# Timings: 10 more opens by shortcut (Esc each), then 30 releases.
$opens = @()
foreach ($i in 1..10) { $o = Scenario "open$i" { Starter 1 } { Start-Sleep -Milliseconds 200; Tap 0x1B }; $opens += $o.frozen }
$s.timedOpensFrozen = @($opens | Where-Object { $_ }).Count
$releases = Scenario 'releases' { Starter 1 } {
    Drag 300 300 500 400
    foreach ($i in 1..30) { Start-Sleep -Milliseconds 150; Drag 500 400 (520 + $i) (420 + $i) }   # each a handle drag and release
    Start-Sleep -Milliseconds 300; Tap 0x1B }
$found.scenarios = $s

# Windows' switch turned back on from outside: Chippi says "Used by Windows" and leaves it; Print Screen goes to Windows.
Set-ItemProperty -Path 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -Value 1 -Type DWord
Start-Sleep -Seconds 2
$found.usedByWindows = @(Events | Where-Object { $_.event -eq 'shortcuts' } | Select-Object -Last 1 | ForEach-Object { $_.entries })
$found.settingAfterTurnedOn = Setting
$from = @(Events).Count
Click 700 520; Start-Sleep -Milliseconds 300
PrintScreen
Start-Sleep -Seconds 2
$found.printScreenWithWindowsOn = [ordered]@{
    chippiFroze = [bool](WaitEvent 'frozen' $from 500)
    windowsClipper = @(Get-Process -Name ScreenClippingHost, SnippingTool -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
}
Shot 'windows-clip'
Tap 0x1B; Start-Sleep -Milliseconds 500
Get-Process -Name ScreenClippingHost, SnippingTool -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path "$results\capture-timings.txt") { $found.timings = @(Get-Content -LiteralPath "$results\capture-timings.txt") }
$logs = Join-Path $env:LOCALAPPDATA 'Chippi\logs'
if (Test-Path $logs) { Copy-Item -Path $logs -Destination "$results\chippi-logs" -Recurse -ErrorAction SilentlyContinue }
Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $fixture.Id -Force -ErrorAction SilentlyContinue
return $found
